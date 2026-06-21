using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class CanonFormatReferenceServiceTests
{
    [Fact]
    public void BuildPromptBlock_inlines_canon_format_content()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Format injection test");
        AdventureSourceFileService.EnsureLayout(bundle);

        var block = CanonFormatReferenceService.BuildPromptBlock(bundle);

        Assert.Contains("=== CANON FORMAT REFERENCE (canon-format.md) ===", block, StringComparison.Ordinal);
        Assert.Contains("# Canon format reference", block, StringComparison.Ordinal);
        Assert.Contains("## Quick rules", block, StringComparison.Ordinal);
        Assert.Contains("Party entry (correct)", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSpecificationCitation_embeds_full_reference_in_fence()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Spec citation test");
        AdventureSourceFileService.EnsureLayout(bundle);

        var citation = CanonFormatReferenceService.BuildSpecificationCitation(bundle);

        Assert.Contains("**Format reference (canon-format.md)**", citation, StringComparison.Ordinal);
        Assert.Contains("```", citation, StringComparison.Ordinal);
        Assert.Contains("## Entry templates by kind", citation, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPrompt_cast_spec_includes_inlined_canon_format()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Cast format test");
        AdventureSourceFileService.EnsureLayout(bundle);

        var prompt = AdventureDesignSourcePromptService.BuildPrompt(bundle, SectionSchema.CastFile);

        Assert.Contains("**Format reference (canon-format.md)**", prompt, StringComparison.Ordinal);
        Assert.Contains("Party entry (correct)", prompt, StringComparison.Ordinal);
    }
}
