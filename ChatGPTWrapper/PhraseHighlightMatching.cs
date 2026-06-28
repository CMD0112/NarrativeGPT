using System.Text.RegularExpressions;

namespace ChatGPTWrapper;

public enum PhraseHighlightProfile
{
    Single,
    ProperName,
    TitledName,
    Descriptive,
    SlashVariants,
}

internal static class PhraseHighlightMatching
{
    private static readonly HashSet<string> FirstNameAliasStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an",
        "mother", "father", "captain", "king", "queen", "lord", "lady", "sir", "dame",
        "examiner", "priest", "sister", "brother", "uncle", "aunt",
        "old", "young", "lost", "true", "red", "black",
    };

    internal readonly record struct MatchNeedle(string Text, int Tier = 1);

    internal readonly record struct CompiledPhraseRule(
        string Phrase,
        PhraseHighlightProfile Profile,
        IReadOnlyList<MatchNeedle> Needles);

    internal static bool PhraseEndsWithPossessive(string phrase) =>
        Regex.IsMatch(phrase.Trim(), @"['\u2019]s?$", RegexOptions.IgnoreCase);

    internal static CompiledPhraseRule CompileRule(string phrase, Guid? entityId = null) =>
        new(phrase, ClassifyProfile(phrase, entityId), BuildNeedles(phrase, entityId));

    internal static PhraseHighlightProfile ClassifyProfile(string phrase, Guid? entityId = null)
    {
        var trimmed = phrase.Trim();
        if (trimmed.Contains('/'))
            return PhraseHighlightProfile.SlashVariants;

        var words = SplitWords(trimmed);
        if (words.Count <= 1)
        {
            if (words.Count == 1 && words[0].Contains('-'))
                return PhraseHighlightProfile.Descriptive;
            return PhraseHighlightProfile.Single;
        }

        if (entityId is not null && !StartsWithArticle(trimmed))
            return PhraseHighlightProfile.ProperName;

        if (StartsWithArticle(trimmed) || IsAllLowerWords(words))
            return PhraseHighlightProfile.Descriptive;

        if (words.Count >= 2 && IsCapitalizedWord(words[0]))
            return PhraseHighlightProfile.ProperName;

        if (IsCapitalizedWord(words[^1]))
            return PhraseHighlightProfile.TitledName;

        return PhraseHighlightProfile.Descriptive;
    }

    internal static string? TryGetFirstNameAlias(string phrase, Guid? entityId)
    {
        if (entityId is null)
            return null;

        var trimmed = phrase.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains('/'))
            return null;

        var words = SplitWords(trimmed);
        if (words.Count < 2)
            return null;

        if (ClassifyProfile(trimmed, entityId) != PhraseHighlightProfile.ProperName)
            return null;

        var first = words[0];
        if (!IsCapitalizedWord(first) || FirstNameAliasStopwords.Contains(first))
            return null;

        return first;
    }

    internal static IReadOnlyList<MatchNeedle> GetMatchNeedles(string phrase, Guid? entityId = null) =>
        CompileRule(phrase, entityId).Needles;

    internal static IReadOnlyList<MatchNeedle> BuildNeedles(string phrase, Guid? entityId = null)
    {
        var profile = ClassifyProfile(phrase, entityId);
        var variants = profile == PhraseHighlightProfile.SlashVariants
            ? ExpandSlashVariants(phrase)
            : [phrase];

        var needles = new List<MatchNeedle>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variant in variants)
            AddNeedle(needles, seen, variant, tier: 1);

        return needles;
    }

    internal static IReadOnlyList<(int Start, int End)> FindMatches(string text, CompiledPhraseRule rule)
    {
        var matches = new List<(int Start, int End)>();

        foreach (var needle in rule.Needles)
        {
            var idx = 0;
            while (idx < text.Length)
            {
                var found = text.IndexOf(needle.Text, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    break;

                var end = found + needle.Text.Length;
                if (!IsWordBoundary(text, found, end))
                {
                    idx = found + 1;
                    continue;
                }

                if (!PhraseEndsWithPossessive(rule.Phrase))
                    end = ExtendMatchForPossessive(text, end);

                matches.Add((found, end));
                idx = found + 1;
            }
        }

        matches.Sort(static (a, b) => a.Start.CompareTo(b.Start) != 0
            ? a.Start.CompareTo(b.Start)
            : b.End.CompareTo(a.End));

        var filtered = new List<(int Start, int End)>();
        var cursor = 0;
        foreach (var match in matches)
        {
            if (match.Start < cursor)
                continue;

            filtered.Add(match);
            cursor = match.End;
        }

        return filtered;
    }

    internal static bool IsWordBoundary(string text, int start, int end)
    {
        if (start > 0)
        {
            var before = text[start - 1];
            if (char.IsLetterOrDigit(before))
                return false;
            if (before == '-')
                return false;
        }

        if (end < text.Length && char.IsLetterOrDigit(text[end]))
            return false;

        return true;
    }

    internal static int ExtendMatchForPossessive(string text, int end)
    {
        if (end >= text.Length)
            return end;

        var ch = text[end];
        if (ch != '\'' && ch != '\u2019')
            return end;

        if (end + 1 < text.Length && char.ToLowerInvariant(text[end + 1]) == 's')
            return end + 2;

        return end + 1;
    }

    private static void AddNeedle(List<MatchNeedle> needles, HashSet<string> seen, string text, int tier)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!seen.Add(text))
            return;

        needles.Add(new MatchNeedle(text, tier));
    }

    private static List<string> SplitWords(string phrase) =>
        phrase.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool StartsWithArticle(string phrase) =>
        Regex.IsMatch(phrase.Trim(), @"^(the|a|an)(\s+|$)", RegexOptions.IgnoreCase);

    private static bool IsAllLowerWords(IReadOnlyList<string> words) =>
        words.All(word =>
        {
            var stripped = Regex.Replace(word, @"['\u2019]s?$", "", RegexOptions.IgnoreCase);
            return stripped.Equals(stripped.ToLowerInvariant(), StringComparison.Ordinal);
        });

    private static bool IsCapitalizedWord(string word)
    {
        if (string.IsNullOrEmpty(word))
            return false;

        var ch = word[0];
        return char.IsUpper(ch) && ch != char.ToLowerInvariant(ch);
    }

    private static IReadOnlyList<string> ExpandSlashVariants(string phrase) =>
        phrase.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
