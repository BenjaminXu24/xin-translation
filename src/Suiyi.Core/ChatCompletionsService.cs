using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Suiyi.Core;

/// <summary>Implements HTTPS Bearer-authenticated Chat Completions with explicit OpenAI and DeepSeek request profiles.</summary>
public sealed class ChatCompletionsService : ITranslationService, IDisposable
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private readonly ServiceOptions _options;
    private readonly Uri _endpoint;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _timeout;
    private bool IsDeepSeek => _endpoint.IdnHost.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase) && _endpoint.Port == 443;

    public ChatCompletionsService(ServiceOptions options)
        : this(options, CreateClient(), true) { }

    // A supplied client is intended for protocol tests and remains owned by the caller.
    public ChatCompletionsService(ServiceOptions options, HttpClient httpClient)
        : this(options, httpClient, false) { }

    private ChatCompletionsService(ServiceOptions options, HttpClient client, bool ownsClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            if (_ownsClient) _client.Dispose();
            throw new TranslationException(TranslationErrorKind.Configuration, "服务地址必须是没有用户名、密码或片段的完整 HTTPS Chat Completions 地址。");
        }
        if (string.IsNullOrWhiteSpace(options.Model) || options.Model.Length > 200 ||
            string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Any(char.IsControl))
        {
            if (_ownsClient) _client.Dispose();
            throw new TranslationException(TranslationErrorKind.Configuration, "请在本地设置中填写有效的模型名与 API Key。");
        }
        _endpoint = endpoint;
        if (IsDeepSeek && options.Model.Trim().Equals("deepseek", StringComparison.OrdinalIgnoreCase))
        {
            if (_ownsClient) _client.Dispose();
            throw new TranslationException(TranslationErrorKind.Configuration,
                "DeepSeek 需要具体模型名，不能只填 deepseek。请在设置中改为 deepseek-flash，或服务商提供的其他具体模型名。");
        }
        _timeout = options.Timeout ?? TimeSpan.FromSeconds(45);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(3))
        {
            if (_ownsClient) _client.Dispose();
            throw new TranslationException(TranslationErrorKind.Configuration, "网络超时必须大于 0 且不超过 180 秒。");
        }
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = TranslationRules.Resolve(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        var payload = new Dictionary<string, object>
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new[]
            {
                new { role = "system", content = TranslationPrompt.SystemInstructions },
                new { role = "user", content = TranslationPrompt.CreateUserPayload(resolved) }
            }
        };
        if (IsDeepSeek)
        {
            // DeepSeek Chat Completions documents thinking, but does not document OpenAI's store field.
            // A translation request needs the final text only; select its non-thinking JSON mode explicitly.
            payload["thinking"] = new { type = "disabled" };
        }
        else payload["store"] = false;
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
        try
        {
            using var response = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                    throw await ReadParameterErrorAsync(response, timeoutSource.Token).ConfigureAwait(false);
                ThrowHttpError(response.StatusCode);
            }
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                throw InvalidResponse("服务返回内容过大，已停止处理。请缩短输入后重试。");
            using var bodyStream = await response.Content.ReadAsStreamAsync(timeoutSource.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] bytes = new byte[8192];
            while (true)
            {
                int count = await bodyStream.ReadAsync(bytes, timeoutSource.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (buffer.Length + count > MaxResponseBytes)
                    throw InvalidResponse("服务返回内容过大，已停止处理。请缩短输入后重试。");
                await buffer.WriteAsync(bytes.AsMemory(0, count), timeoutSource.Token).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return ParseResponse(buffer.ToArray(), resolved);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            throw new TranslationException(TranslationErrorKind.Timeout, "翻译请求已超时。请检查网络或在设置中增加超时后重试。");
        }
        catch (HttpRequestException)
        {
            throw new TranslationException(TranslationErrorKind.Network, "无法连接翻译服务。请检查网络、服务地址和代理后重试。");
        }
        catch (IOException)
        {
            throw new TranslationException(TranslationErrorKind.Network, "接收翻译响应失败。请检查网络后重试。");
        }
        catch (JsonException)
        {
            throw InvalidResponse("服务返回的 JSON 格式无效。请检查服务是否支持所需协议后重试。");
        }
    }

    private static TranslationResult ParseResponse(byte[] bytes, TranslationRequest request)
    {
        using var outer = JsonDocument.Parse(bytes);
        if (outer.RootElement.ValueKind != JsonValueKind.Object ||
            !outer.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
            throw InvalidResponse("服务没有返回单条有效译文。请检查模型与协议后重试。");
        var choice = choices[0];
        if (choice.ValueKind != JsonValueKind.Object)
            throw InvalidResponse("服务返回的译文对象无效。请检查模型与协议后重试。");
        if (!choice.TryGetProperty("finish_reason", out var finish) || finish.ValueKind != JsonValueKind.String || finish.GetString() != "stop")
            throw InvalidResponse("服务返回的译文未完整结束或受到限制，已阻止展示。请缩短输入或更换模型后重试。");
        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(content.GetString()))
            throw InvalidResponse("服务返回了空内容或非文本内容。请检查模型与协议后重试。");
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null &&
            !(refusal.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(refusal.GetString())))
            throw InvalidResponse("模型未返回完整翻译，已阻止展示。请调整输入或模型后重试。");

        using var inner = JsonDocument.Parse(content.GetString()!);
        var root = inner.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("source_language", out var source) || source.ValueKind != JsonValueKind.String ||
            source.GetString() != TranslationRules.LanguageName(request.Source))
            throw InvalidResponse("译文的源语言标记与请求不一致。请手动指定语言后重试。");
        if (!root.TryGetProperty("optimized_chinese", out var optimized))
            throw InvalidResponse("服务缺少中文优化字段。请检查模型与协议后重试。");
        string? optimizedText = null;
        if (request.Source == Language.Chinese)
        {
            if (optimized.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(optimized.GetString()))
                throw InvalidResponse("服务未提供完整中文优化结果。请重试。");
            optimizedText = NormalizeBody(optimized.GetString()!);
            ProtectedTokenValidator.EnsurePreserved(request.Text, optimizedText);
        }
        else if (optimized.ValueKind != JsonValueKind.Null)
            throw InvalidResponse("服务返回了不适用的中文优化结果。请重试。");
        if (!root.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array)
            throw InvalidResponse("服务缺少有效的分段译文。请检查模型与协议后重试。");

        var sourceSegments = SentenceSegmenter.Split(request.Text);
        if (segments.GetArrayLength() != sourceSegments.Count)
            throw InvalidResponse("译文段数与输入不一致，可能存在遗漏或重复，已阻止展示。请重试。");
        var translated = new List<string>();
        for (int i = 0; i < sourceSegments.Count; i++)
        {
            var segment = segments[i];
            if (segment.ValueKind != JsonValueKind.Object || !segment.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int value) || value != i + 1 ||
                !segment.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(text.GetString()))
                throw InvalidResponse("译文段落编号、顺序或内容无效，已阻止展示。请重试。");
            string segmentText = NormalizeBody(text.GetString()!);
            ProtectedTokenValidator.EnsurePreserved(sourceSegments[i], segmentText);
            translated.AddRange(SentenceSegmenter.Split(segmentText));
        }
        return new(request.Source, request.Target, optimizedText, translated.AsReadOnly(), false);
    }

    private static string NormalizeBody(string text) => text.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
    private static TranslationException InvalidResponse(string message) => new(TranslationErrorKind.InvalidResponse, message);

    private static async Task<TranslationException> ReadParameterErrorAsync(HttpResponseMessage response, CancellationToken token)
    {
        string fallback = $"翻译服务返回 HTTP {(int)response.StatusCode}，请求参数被拒绝。请核对模型名及接口格式后重试。";
        // The provider may echo submitted text or credentials. Read a bounded body into memory only,
        // then map allowlisted fields/patterns to fixed messages. Never surface any provider text.
        const int limit = 8192;
        if (response.Content.Headers.ContentLength > limit) return ParameterError(fallback);
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] bytes = new byte[1024];
        while (true)
        {
            int read = await stream.ReadAsync(bytes, token).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > limit) return ParameterError(fallback);
            await buffer.WriteAsync(bytes.AsMemory(0, read), token).ConfigureAwait(false);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return ParameterError(fallback);
            string Field(string name) => error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()!.ToLowerInvariant() : "";
            string parameter = Field("param");
            string detail = parameter switch
            {
                "model" => "服务拒绝了模型名。请按服务商控制台填写可用的模型名。",
                "response_format" => "服务拒绝了 JSON 输出参数。请确认该模型支持 JSON object 输出。",
                "store" => "服务不接受 store 参数，请使用针对该服务的客户端版本。",
                "thinking" or "reasoning_effort" => "服务拒绝了思考模式参数。请确认模型与接口匹配。",
                "max_tokens" => "服务拒绝了输出长度参数。请检查模型的输出长度限制。",
                "messages" => "服务拒绝了消息格式。请确认地址为 Chat Completions 接口。",
                "stream" or "stream_options" => "服务拒绝了流式参数。请确认使用非流式 Chat Completions 接口。",
                _ => ""
            };
            string code = Field("code"), message = Field("message");
            if (detail.Length == 0)
            {
                if (code is "model_not_found" or "invalid_model" or "unknown_model" ||
                    (message.Contains("model", StringComparison.Ordinal) &&
                    (message.Contains("not found", StringComparison.Ordinal) || message.Contains("does not exist", StringComparison.Ordinal) || message.Contains("not exist", StringComparison.Ordinal))))
                    detail = "服务未识别模型名，或当前密钥没有该模型权限。请按服务商控制台核对模型名。";
                else if ((message.Contains("context", StringComparison.Ordinal) || message.Contains("token", StringComparison.Ordinal)) &&
                    (message.Contains("exceed", StringComparison.Ordinal) || message.Contains("too long", StringComparison.Ordinal)))
                    detail = "服务拒绝了输入长度。请缩短文本或使用上下文长度更大的模型。";
                else if (message.Contains("unsupported", StringComparison.Ordinal) || message.Contains("not support", StringComparison.Ordinal) || message.Contains("unknown parameter", StringComparison.Ordinal))
                {
                    if (message.Contains("response_format", StringComparison.Ordinal) || message.Contains("json_object", StringComparison.Ordinal)) detail = "服务不支持所需的 JSON 输出格式。请检查模型及接口。";
                    else if (message.Contains("store", StringComparison.Ordinal)) detail = "服务不接受 store 参数，请使用针对该服务的客户端版本。";
                    else if (message.Contains("thinking", StringComparison.Ordinal) || message.Contains("reasoning_effort", StringComparison.Ordinal)) detail = "服务不支持请求中的思考模式参数。请确认模型与接口匹配。";
                }
            }
            return ParameterError(detail.Length > 0 ? $"HTTP {(int)response.StatusCode}：{detail}" : fallback);
        }
        catch (JsonException) { return ParameterError(fallback); }
    }

    private static TranslationException ParameterError(string message) => new(TranslationErrorKind.Service, message);

    private static void ThrowHttpError(HttpStatusCode code)
    {
        throw code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new TranslationException(TranslationErrorKind.Authentication, "API Key 无效、已过期或没有该模型的权限。请在本地设置中检查密钥和模型。"),
            HttpStatusCode.TooManyRequests =>
                new TranslationException(TranslationErrorKind.RateLimit, "服务请求过于频繁或额度不足。请稍后重试并检查服务额度。"),
            HttpStatusCode.PaymentRequired =>
                new TranslationException(TranslationErrorKind.Service, "服务返回 HTTP 402，账户余额不足或计费未启用。请检查服务商账户余额。"),
            _ => new TranslationException(TranslationErrorKind.Service, $"翻译服务返回 HTTP {(int)code}。请检查服务地址、模型及所需 Chat Completions JSON 协议。")
        };
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
