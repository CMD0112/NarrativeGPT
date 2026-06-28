using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.Adventure.Services;

public enum PhraseHighlightFontWeightMode
{
    MatchRole,
    Bolder,
    Absolute,
}

/// <summary>
/// Maps phrase-highlight weight storage (<see cref="PhraseHighlightRule.FontWeight"/> + <see cref="PhraseHighlightRule.Bold"/>)
/// to a single intuitive editor choice.
/// </summary>
public static class PhraseHighlightFontWeightChoice
{
    public const string MatchRoleTag = "match";
    public const string BolderTag = "bolder";
    public const string CustomTag = "custom";

    public static PhraseHighlightFontWeightMode DecodeMode(PhraseHighlightRule rule)
    {
        if (rule.FontWeight is not null)
            return PhraseHighlightFontWeightMode.Absolute;

        return rule.Bold
            ? PhraseHighlightFontWeightMode.Bolder
            : PhraseHighlightFontWeightMode.MatchRole;
    }

    public static int? DecodeAbsoluteWeight(PhraseHighlightRule rule) => rule.FontWeight;

    public static bool IsNamedStep(int weight) =>
        FormatFontWeights.NamedSteps.Any(step => step.Value == weight);

    public static void Apply(
        PhraseHighlightRule rule,
        PhraseHighlightFontWeightMode mode,
        int? absoluteWeight = null)
    {
        switch (mode)
        {
            case PhraseHighlightFontWeightMode.MatchRole:
                rule.FontWeight = null;
                rule.Bold = false;
                break;
            case PhraseHighlightFontWeightMode.Bolder:
                rule.FontWeight = null;
                rule.Bold = true;
                break;
            case PhraseHighlightFontWeightMode.Absolute:
                rule.FontWeight = absoluteWeight is null
                    ? null
                    : FormatHighlightComposition.ClampWeight(absoluteWeight.Value);
                rule.Bold = false;
                break;
        }
    }

    public static string DescribeForSummary(PhraseHighlightRule rule)
    {
        return DecodeMode(rule) switch
        {
            PhraseHighlightFontWeightMode.MatchRole => "",
            PhraseHighlightFontWeightMode.Bolder => "Bolder",
            PhraseHighlightFontWeightMode.Absolute when rule.FontWeight is int weight
                => FormatFontWeights.FormatLabel(weight),
            _ => "",
        };
    }

    public static string DescribeResolvedHint(PhraseHighlightRule rule, int roleBaseWeight)
    {
        var resolved = PhraseHighlightStyleResolver.ResolveFontWeight(rule, roleBaseWeight);
        return DecodeMode(rule) switch
        {
            PhraseHighlightFontWeightMode.MatchRole =>
                $"Uses message text weight ({resolved})",
            PhraseHighlightFontWeightMode.Bolder =>
                $"Bolder than message text ({roleBaseWeight} + {FormatHighlightComposition.BoldWeightDelta} → {resolved})",
            PhraseHighlightFontWeightMode.Absolute =>
                $"Fixed weight {resolved}",
            _ => $"Weight {resolved}",
        };
    }

    public static string? TryResolveComboTag(PhraseHighlightRule rule)
    {
        return DecodeMode(rule) switch
        {
            PhraseHighlightFontWeightMode.MatchRole => MatchRoleTag,
            PhraseHighlightFontWeightMode.Bolder => BolderTag,
            PhraseHighlightFontWeightMode.Absolute when rule.FontWeight is int weight =>
                IsNamedStep(weight) ? weight.ToString() : CustomTag,
            _ => MatchRoleTag,
        };
    }
}
