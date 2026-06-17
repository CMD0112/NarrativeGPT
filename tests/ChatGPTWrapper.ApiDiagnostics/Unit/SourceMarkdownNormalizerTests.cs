using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class SourceMarkdownNormalizerTests
{
    [Fact]
    public void Normalize_cast_repairs_missing_headers_and_player_fields()
    {
        const string input = """
            Cast
            player

            Name: Rowan Vale

            Background:

            Former road-surveyor.

            party
            Calder Renn

            Id: calder-renn
            Aliases: Cal, Old Renn
            Role: Old friend
            Flavor: "You always did read a road better than a room, Rowan."

            npcs
            Liora Vale

            Id: liora-vale
            Role: Rowan's spouse
            Flavor: "Do not trust the clean copy."
            """;

        var normalized = SourceMarkdownNormalizer.Normalize(SectionSchema.CastFile, input);

        Assert.StartsWith("# Cast", normalized, StringComparison.Ordinal);
        Assert.Contains("## player", normalized, StringComparison.Ordinal);
        Assert.Contains("**Name:** Rowan Vale", normalized, StringComparison.Ordinal);
        Assert.Contains("**Background:**", normalized, StringComparison.Ordinal);
        Assert.Contains("## party", normalized, StringComparison.Ordinal);
        Assert.Contains("### Calder Renn", normalized, StringComparison.Ordinal);
        Assert.Contains("## npcs", normalized, StringComparison.Ordinal);
        Assert.Contains("### Liora Vale", normalized, StringComparison.Ordinal);
        Assert.Contains("> Flavor: You always did read a road better than a room, Rowan.", normalized, StringComparison.Ordinal);
        Assert.Contains("> Flavor: Do not trust the clean copy.", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_cast_leaves_canonical_markdown_unchanged()
    {
        const string input = """
            # Cast

            ## player

            **Name:** Alex

            ## npcs

            ### Mara Voss
            Id: mara-voss
            Role: Guide

            > Flavor: Keep to the lit roads.
            """;

        var normalized = SourceMarkdownNormalizer.Normalize(SectionSchema.CastFile, input);

        Assert.Contains("# Cast", normalized, StringComparison.Ordinal);
        Assert.Contains("## player", normalized, StringComparison.Ordinal);
        Assert.Contains("**Name:** Alex", normalized, StringComparison.Ordinal);
        Assert.Contains("### Mara Voss", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Cast\nplayer", normalized.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void TryWrite_cast_applies_normalizer_before_persisting()
    {
        var bundle = AdventureStore.CreateNew("Normalizer write test");
        const string sloppy = """
            Cast
            player

            Name: Alex
            """;

        Assert.True(AdventureSourceFileService.TryWrite(bundle, SectionSchema.CastFile, sloppy, "test"));

        var saved = File.ReadAllText(
            AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.CastFile));
        Assert.Contains("# Cast", saved, StringComparison.Ordinal);
        Assert.Contains("## player", saved, StringComparison.Ordinal);
        Assert.Contains("**Name:** Alex", saved, StringComparison.Ordinal);
    }
}
