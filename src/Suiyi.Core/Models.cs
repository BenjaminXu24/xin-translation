namespace Suiyi.Core;

public enum Language { Auto, Chinese, English, Filipino }
public enum Tone { Chat, Formal }

public sealed record GlossaryEntry(string Source, string Target);
public sealed record TranslationRequest(string Text, Language Source, Language Target, Tone Tone,
    IReadOnlyList<GlossaryEntry> Glossary);

public sealed record TranslationResult(Language Source, Language Target, string? OptimizedChinese,
    IReadOnlyList<string> Segments, bool IsMock)
{
    public string Text => string.Join("\n\n", Segments);
}

public sealed record ServiceOptions(string Endpoint, string Model, string ApiKey, TimeSpan? Timeout = null);

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}

public enum TranslationErrorKind
{
    InvalidInput, Configuration, Network, Timeout, Authentication, RateLimit, Service, InvalidResponse, Integrity
}

public sealed class TranslationException : Exception
{
    public TranslationErrorKind Kind { get; }

    public TranslationException(TranslationErrorKind kind, string message) : base(message) => Kind = kind;
}
