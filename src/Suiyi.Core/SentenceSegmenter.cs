using System.Text;
using System.Text.RegularExpressions;

namespace Suiyi.Core;

/// <summary>Groups complete sentences; an unusually long single sentence is never split in half.</summary>
public static class SentenceSegmenter
{
    private static readonly Regex GreetingOnly = new(
        @"^(?:(?:Hi|Hello|Hey|Dear)[,，]?|(?:Hi|Hello|Hey|Dear)[ \t]+[\p{L}\p{M}.'-]+(?:[ \t]+[\p{L}\p{M}.'-]+){0,2}[,，])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Split(string text, int maxChars = 400)
    {
        if (maxChars < 20) throw new ArgumentOutOfRangeException(nameof(maxChars));
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var chunks = new List<string>();
        var current = new StringBuilder();
        var sentence = new StringBuilder();

        void AddSentence()
        {
            string value = sentence.ToString().Trim();
            sentence.Clear();
            if (value.Length == 0) return;
            if (current.Length > 0 && current.Length + value.Length + 1 > maxChars && !GreetingOnly.IsMatch(current.ToString()))
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(value);
        }

        void EndParagraph(bool carryGreeting = false)
        {
            AddSentence();
            if (current.Length == 0) return;
            if (carryGreeting && GreetingOnly.IsMatch(current.ToString())) return;
            chunks.Add(current.ToString());
            current.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                // Only blank lines force a paragraph: "Hello Maria,\n..." remains together.
                bool blankLine = i + 1 < text.Length && text[i + 1] == '\n';
                if (blankLine)
                {
                    EndParagraph(carryGreeting: true);
                    while (i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else sentence.Append(' ');
                continue;
            }
            sentence.Append(c);
            bool fullWidthEnd = c is '。' or '！' or '？' or '；';
            bool latinEnd = c is '.' or '!' or '?' &&
                (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]));
            // Decimal amounts and dots inside URLs/API field names do not end sentences.
            if (fullWidthEnd || latinEnd) AddSentence();
        }
        EndParagraph();
        return chunks.AsReadOnly();
    }
}
