namespace Suiyi.Core;

/// <summary>Fixed demonstrations only; not a translation model and never claims actual quality acceptance.</summary>
public sealed class MockTranslationService : ITranslationService
{
    private readonly TimeSpan _delay;
    private const string ChinesePayment = "我们还没收到款，先别开通 API，等财务确认后再开。";
    private const string EnglishPayment = "Hi Maria, we haven’t received the PHP 12,500 payment for INV-1042.";
    private const string EnglishCallback = "Could you check the API callback?";
    private const string FilipinoPayment = "Pakicheck po kung pumasok na ang payment.";

    public MockTranslationService(TimeSpan? delay = null) => _delay = delay ?? TimeSpan.FromMilliseconds(500);

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = TranslationRules.Resolve(request);
        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        string key = resolved.Text.Trim().Replace("haven't", "haven’t", StringComparison.Ordinal);
        string? optimized = resolved.Source == Language.Chinese ? resolved.Text.Trim() : null;
        string? text = (resolved.Source, resolved.Target, key) switch
        {
            (Language.English, Language.Chinese, EnglishPayment) => "嗨 Maria，我们还没有收到 INV-1042 对应的 PHP 12,500 付款。",
            (Language.Chinese, Language.English, ChinesePayment) => "We haven’t received the payment yet. Please hold off on enabling the API until Finance confirms it.",
            (Language.English, Language.Chinese, EnglishCallback) => "你能检查一下 API 回调吗？",
            (Language.Filipino, Language.Chinese, FilipinoPayment) => "请检查一下款项是否已到账。",
            (Language.Chinese, Language.Filipino, ChinesePayment) => "Hindi pa natin natatanggap ang bayad. Huwag munang i-enable ang API; hintayin muna ang kumpirmasyon ng Finance.",
            (Language.English, Language.Filipino, EnglishCallback) => "Puwede mo bang i-check ang API callback?",
            (Language.English, Language.Filipino, EnglishPayment) => "Hi Maria, hindi pa natin natatanggap ang PHP 12,500 na bayad para sa INV-1042.",
            (Language.Filipino, Language.English, FilipinoPayment) => "Please check whether the payment has come through.",
            (Language.Chinese, Language.Chinese, ChinesePayment) => "我们尚未收到款项，请先不要开通 API，等财务确认后再开通。",
            _ => null
        };
        if (key == ChinesePayment && resolved.Source == Language.Chinese)
            optimized = "我们尚未收到款项，请先不要开通 API，等财务确认后再开通。";
        if (text is null)
            return new(resolved.Source, resolved.Target, optimized,
                new[] { "【模拟模式】此输入没有固定样例；请在设置中配置 API 服务与密钥后获取真实翻译。" }, true);
        return new(resolved.Source, resolved.Target, optimized, SentenceSegmenter.Split(text), true);
    }
}
