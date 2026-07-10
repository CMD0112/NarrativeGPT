using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightMatchingTests
{
    private static readonly Guid PlayerId = Guid.Parse("00000000-0000-0000-0000-000000000000");

    [Theory]
    [InlineData("Garran Holt", PhraseHighlightProfile.ProperName)]
    [InlineData("the boy left behind", PhraseHighlightProfile.Descriptive)]
    [InlineData("garrison captain", PhraseHighlightProfile.Descriptive)]
    [InlineData("Mother Sella", PhraseHighlightProfile.ProperName)]
    [InlineData("The King in Red / The King in Black", PhraseHighlightProfile.SlashVariants)]
    [InlineData("Anwen", PhraseHighlightProfile.Single)]
    public void ClassifyProfile_groups_phrases_by_matching_strategy(string phrase, PhraseHighlightProfile expected)
    {
        Assert.Equal(expected, PhraseHighlightMatching.ClassifyProfile(phrase));
    }

    [Theory]
    [InlineData("the King in Red", "a faded red cloak", "red", false)]
    [InlineData("the King in Red", "the King in Red appeared", "the King in Red", true)]
    [InlineData("the boy left behind", "he stood behind the door", "behind", false)]
    [InlineData("the boy left behind", "the boy left behind", "the boy left behind", true)]
    [InlineData("the girl who protected them", "she stayed with them", "them", false)]
    [InlineData("The King in Red / The King in Black", "the King in Black arrived", "the King in Black", true)]
    public void FindMatches_use_full_phrase_only_for_descriptive_rules(
        string phrase,
        string text,
        string expectedMatch,
        bool shouldMatch)
    {
        Assert.Equal(shouldMatch, ContainsMatch(text, phrase, null, expectedMatch));
    }

    [Fact]
    public void Entity_linked_name_matches_first_name_alias()
    {
        var compiled = PhraseHighlightMatching.CompileRule("Garran Holt", PlayerId);
        Assert.NotEmpty(PhraseHighlightMatching.FindMatches("Garran Holt nodded", compiled));
        Assert.NotEmpty(PhraseHighlightMatching.FindMatches("Garran nodded", compiled));
        Assert.Empty(PhraseHighlightMatching.FindMatches("holt nodded", compiled));
    }

    [Fact]
    public void Mother_sella_does_not_generate_first_name_alias()
    {
        var entityId = Guid.NewGuid();
        Assert.Null(PhraseHighlightMatching.TryGetFirstNameAlias("Mother Sella", entityId));
        var compiled = PhraseHighlightMatching.CompileRule("Mother Sella", entityId);
        Assert.Empty(PhraseHighlightMatching.FindMatches("Mother waited", compiled));
    }

    [Fact]
    public void Possessive_extension_applies_to_first_name_alias_matches()
    {
        var compiled = PhraseHighlightMatching.CompileRule("Garran Holt", PlayerId);
        Assert.NotEmpty(PhraseHighlightMatching.FindMatches("Garran's blade gleamed", compiled));
    }

    [Fact]
    public void Phrase_highlight_asset_uses_profile_scanner_with_first_name_aliases()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-phrase-highlights.js");
        Assert.Contains("classifyPhraseProfile", js);
        Assert.Contains("compileRuleNeedles", js);
        Assert.Contains("findMatchesInText", js);
        Assert.Contains("getFirstNameAlias", js);
        Assert.DoesNotContain("compileRuleRegex", js);
        Assert.DoesNotContain("getLastWordAlias", js);
    }

    private static bool ContainsMatch(string text, string phrase, Guid? entityId, string expectedMatch)
    {
        var compiled = PhraseHighlightMatching.CompileRule(phrase, entityId);
        return PhraseHighlightMatching.FindMatches(text, compiled)
            .Any(span =>
            {
                var slice = text.Substring(span.Start, span.End - span.Start);
                return slice.Equals(expectedMatch, StringComparison.OrdinalIgnoreCase)
                    || slice.Contains(expectedMatch, StringComparison.OrdinalIgnoreCase);
            });
    }
}
