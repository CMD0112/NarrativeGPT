using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.NarratorScales;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class NarratorScalesResolverTests
{
    [Fact]
    public void ExpandOverrideLine_includes_summary_and_pointer()
    {
        var line = NarratorScalesResolver.ExpandOverrideLine("Combat difficulty", "hard");

        Assert.StartsWith("Combat difficulty: hard —", line);
        Assert.Contains("inspect narrator-scales.md", line);
        Assert.Contains("combat-difficulty/hard", line);
    }

    [Fact]
    public void BuildQuickReferenceBlock_lists_effective_scales()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.DetailLevel = "high";
        bundle.Metadata.Settings.Tone = "grim";
        bundle.Metadata.Settings.Difficulty = "hard";

        var block = NarratorScalesResolver.BuildQuickReferenceBlock(bundle);

        Assert.Contains("=== ACTIVE NARRATOR SCALES ===", block);
        Assert.Contains("Detail level: high", block);
        Assert.Contains("Tone: grim", block);
        Assert.Contains("Combat difficulty: hard", block);
        Assert.Contains("inspect narrator-scales.md", block);
        Assert.Contains("detail-level/high", block);
    }

    [Fact]
    public void GetEffectiveScales_includes_violence_level()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.ViolenceLevel = "mild";

        var scales = NarratorScalesResolver.GetEffectiveScales(bundle);

        Assert.Contains(scales, s => s.DimensionId == "violence-level" && s.Value == "mild");
    }
}
