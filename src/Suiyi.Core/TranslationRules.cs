using System.Text.RegularExpressions;

namespace Suiyi.Core;

public static partial class LanguageDetector
{
    private static readonly HashSet<string> FilipinoWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pakicheck", "pakisuri", "paki", "kumusta", "salamat", "pumasok", "bayad", "kabayaran",
        "kung", "ang", "ng", "mga", "wala", "hindi", "pa", "na", "po", "natin", "namin", "inyong",
        "tayo", "ito", "iyon", "magandang", "umaga", "bukas", "kailangan", "huwag", "antayin"
    };

    public static Language Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Language.Auto;
        if (ChineseCharacters().IsMatch(text)) return Language.Chinese;
        var words = Words().Matches(text).Select(m => m.Value).ToArray();
        int hits = words.Count(FilipinoWords.Contains);
        return hits >= 2 || words.Any(w => w.Equals("pakicheck", StringComparison.OrdinalIgnoreCase) ||
            w.Equals("kumusta", StringComparison.OrdinalIgnoreCase) || w.Equals("salamat", StringComparison.OrdinalIgnoreCase))
            ? Language.Filipino : Language.English;
    }

    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}]")]
    private static partial Regex ChineseCharacters();
    [GeneratedRegex(@"[A-Za-z]+")]
    private static partial Regex Words();
}

public static class TranslationRules
{
    public const int MaxInputCharacters = 20_000;

    public static TranslationRequest Resolve(TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new TranslationException(TranslationErrorKind.InvalidInput, "请先输入要翻译的文本。");
        if (request.Text.Length > MaxInputCharacters)
            throw new TranslationException(TranslationErrorKind.InvalidInput, $"文本最多支持 {MaxInputCharacters:N0} 个字符，请拆分后再试。");
        if (!Enum.IsDefined(request.Source) || !Enum.IsDefined(request.Target) || !Enum.IsDefined(request.Tone))
            throw new TranslationException(TranslationErrorKind.InvalidInput, "语言或风格选项无效。");
        if (request.Glossary is null || request.Glossary.Count > 100 ||
            request.Glossary.Any(g => g is null || string.IsNullOrWhiteSpace(g.Source) ||
            string.IsNullOrWhiteSpace(g.Target) || g.Source.Length > 100 || g.Target.Length > 100))
            throw new TranslationException(TranslationErrorKind.InvalidInput, "术语表最多 100 项，每项内容不能为空且不超过 100 个字符。");

        var source = request.Source == Language.Auto ? LanguageDetector.Detect(request.Text) : request.Source;
        var target = request.Target == Language.Auto ? (source == Language.Chinese ? Language.English : Language.Chinese) : request.Target;
        return request with { Source = source, Target = target };
    }

    public static string LanguageName(Language language) => language switch
    {
        Language.Chinese => "chinese",
        Language.English => "english",
        Language.Filipino => "filipino",
        _ => "auto"
    };
}
