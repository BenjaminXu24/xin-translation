using System.Text.RegularExpressions;

namespace Suiyi.Core;

/// <summary>Rejects outputs missing literal identifiers; semantic fidelity still needs real-model validation.</summary>
public static partial class ProtectedTokenValidator
{
    public static IReadOnlyList<string> Extract(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (Regex expression in new[] { Urls(), Emails(), Identifiers(), Fields(), Acronyms(), Numbers(), CurrencyAmounts() })
        {
            foreach (Match match in expression.Matches(text))
            {
                string value = match.Value.TrimEnd('.', ',', ';', '!', '?', ')', ']', '}', '。', '，', '；', '！', '？');
                if (value.Length > 0) tokens.Add(value);
            }
        }
        foreach (Match greeting in GreetingNames().Matches(text)) tokens.Add(greeting.Groups["name"].Value);
        return tokens.ToArray();
    }

    public static void EnsurePreserved(string input, string output)
    {
        var outputUrls = Urls().Matches(output).Select(m => TrimPunctuation(m.Value)).ToHashSet(StringComparer.Ordinal);
        var outputEmails = Emails().Matches(output).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        foreach (string token in Extract(input))
        {
            bool preserved;
            if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                preserved = outputUrls.Contains(token);
            else if (Emails().IsMatch(token))
                preserved = outputEmails.Contains(token);
            else
            {
                // ASCII boundaries permit natural Chinese adjacency while rejecting mutated fields/IDs.
                string before = char.IsAsciiLetterOrDigit(token[0]) ? "(?<![A-Za-z0-9_])" : "";
                string after = char.IsAsciiLetterOrDigit(token[^1]) ? "(?![A-Za-z0-9_])" : "";
                if (NumericToken().IsMatch(token) || CurrencyAmounts().IsMatch(token))
                {
                    before = "(?<![A-Za-z0-9_.,])";
                    after = "(?![A-Za-z0-9_%％]|[.,]\\d)";
                }
                preserved = Regex.IsMatch(output, before + Regex.Escape(token) + after, RegexOptions.CultureInvariant);
            }
            if (!preserved)
                // Do not disclose any source token in exception messages or logs.
                throw new TranslationException(TranslationErrorKind.Integrity,
                    "译文未完整保留金额、币种、编号、URL 或 API 字段，已阻止展示。请重试或调整服务/模型。");
        }
    }

    private static string TrimPunctuation(string token) => token.TrimEnd('.', ',', ';', '!', '?', ')', ']', '}', '。', '，', '；', '！', '？');

    [GeneratedRegex("(?:https?://|www\\.)[^\\s<>\\\"'，。；！？]+", RegexOptions.IgnoreCase)]
    private static partial Regex Urls();
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex Emails();
    [GeneratedRegex(@"\b(?:[A-Za-z][A-Za-z0-9_]{0,24}[-_]\d[A-Za-z0-9_-]*|[a-fA-F0-9]{8}-(?:[a-fA-F0-9]{4}-){3}[a-fA-F0-9]{12})\b")]
    private static partial Regex Identifiers();
    [GeneratedRegex(@"\b(?:[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+|[a-z]+[A-Z][A-Za-z0-9]*)\b")]
    private static partial Regex Fields();
    [GeneratedRegex(@"\b(?:API|HTTP|HTTPS|JSON|XML|SDK|SQL|OAuth|URL|UUID|PHP|USD|CNY|RMB|EUR|GBP|JPY|SGD|HKD|AUD|CAD|MYR|IDR|THB|VND|INR|KRW|AED|BRL)\b")]
    private static partial Regex Acronyms();
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:[$€£₱¥]\s*)?[-+]?\d[\d,]*(?:\.\d+)?(?:%|％)?(?![A-Za-z0-9_])")]
    private static partial Regex Numbers();
    [GeneratedRegex(@"^(?:[$€£₱¥]\s*)?[-+]?\d")]
    private static partial Regex NumericToken();
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:(?:PHP|USD|CNY|RMB|EUR|GBP|JPY|SGD|HKD|AUD|CAD|MYR|IDR|THB|VND|INR|KRW|AED|BRL)\s*[-+]?\d[\d,]*(?:\.\d+)?|[-+]?\d[\d,]*(?:\.\d+)?\s*(?:PHP|USD|CNY|RMB|EUR|GBP|JPY|SGD|HKD|AUD|CAD|MYR|IDR|THB|VND|INR|KRW|AED|BRL))(?![A-Za-z0-9_])")]
    private static partial Regex CurrencyAmounts();
    [GeneratedRegex(@"(?<![A-Za-z])(?i:Hi|Hello|Hey|Dear)\s+(?<name>\p{Lu}[\p{L}\p{M}'-]+(?:\s+\p{Lu}[\p{L}\p{M}'-]+){0,2})[,，]")]
    private static partial Regex GreetingNames();
}
