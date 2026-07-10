using System.Linq;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class GenerationJobGuideTests
{
    [Fact]
    public void ResolveInstructionBody_returns_builtin_default_when_no_override()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();

        var body = GenerationJobGuideService.ResolveInstructionBody(bundle, GenerationJobId.ProposeMemories);

        Assert.Contains("discrete story events", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(GenerationJobGuideService.IsUsingDefaultInstruction(bundle, GenerationJobId.ProposeMemories));
    }

    [Fact]
    public void SetInstructionOverride_stores_custom_body_and_changes_seed_version()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var custom = "Custom memory extraction rules.";

        GenerationJobGuideService.SetInstructionOverride(bundle, GenerationJobId.ProposeMemories, custom);

        Assert.Equal(custom, GenerationJobGuideService.ResolveInstructionBody(bundle, GenerationJobId.ProposeMemories));
        Assert.False(GenerationJobGuideService.IsUsingDefaultInstruction(bundle, GenerationJobId.ProposeMemories));
        Assert.NotEqual(
            GenerationJobGuideService.GetSeedVersion(GenerationJobId.ProposeMemories),
            GenerationJobGuideService.GetEffectiveSeedVersion(bundle, GenerationJobId.ProposeMemories));
    }

    [Fact]
    public void ResetInstructionOverride_restores_default()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        GenerationJobGuideService.SetInstructionOverride(bundle, GenerationJobId.ProposeMemories, "Custom rules.");

        GenerationJobGuideService.ResetInstructionOverride(bundle, GenerationJobId.ProposeMemories);

        Assert.True(GenerationJobGuideService.IsUsingDefaultInstruction(bundle, GenerationJobId.ProposeMemories));
        Assert.Equal(
            GenerationJobGuideService.GetSeedVersion(GenerationJobId.ProposeMemories),
            GenerationJobGuideService.GetEffectiveSeedVersion(bundle, GenerationJobId.ProposeMemories));
    }

    [Fact]
    public void SetInstructionOverride_removes_entry_when_matching_default()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var defaultBody = GenerationJobGuideService.BuildDefaultInstructionBody(GenerationJobId.UpdateSummary);

        GenerationJobGuideService.SetInstructionOverride(bundle, GenerationJobId.UpdateSummary, "Temporary custom.");
        GenerationJobGuideService.SetInstructionOverride(bundle, GenerationJobId.UpdateSummary, defaultBody);

        Assert.True(GenerationJobGuideService.IsUsingDefaultInstruction(bundle, GenerationJobId.UpdateSummary));
        Assert.Empty(bundle.Metadata.UtilityJobGuideOverrides);
    }

    [Fact]
    public void ExpandStoryCard_shares_bootstrap_lore_override_key()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string custom = "Shared lore card rules.";

        GenerationJobGuideService.SetInstructionOverride(bundle, GenerationJobId.BootstrapLore, custom);

        Assert.Equal(custom, GenerationJobGuideService.ResolveInstructionBody(bundle, GenerationJobId.ExpandStoryCard));
    }

    [Theory]
    [InlineData(GenerationJobId.ExtractEntities)]
    [InlineData(GenerationJobId.ProposeMemories)]
    [InlineData(GenerationJobId.UpdateState)]
    [InlineData(GenerationJobId.UpdateSummary)]
    [InlineData(GenerationJobId.ContinuityCheck)]
    [InlineData(GenerationJobId.ProcessTurn)]
    [InlineData(GenerationJobId.DesignAdventure)]
    [InlineData(GenerationJobId.ProposeSourceEdits)]
    public void EditableUtilityJobIds_have_non_empty_defaults(string jobId)
    {
        Assert.False(string.IsNullOrWhiteSpace(GenerationJobGuideService.BuildDefaultInstructionBody(jobId)));
        Assert.Contains(jobId, GenerationJobGuideService.EditableUtilityJobIds);
    }

    [Fact]
    public void EditableUtilityJobIds_have_catalog_metadata()
    {
        foreach (var jobId in GenerationJobGuideService.EditablePlayUtilityJobIds.Concat(GenerationJobGuideService.EditableDesignUtilityJobIds))
        {
            Assert.False(string.IsNullOrWhiteSpace(GenerationJobGuideService.GetCatalogCategory(jobId)));
            Assert.False(string.IsNullOrWhiteSpace(GenerationJobGuideService.GetCatalogDescription(jobId)));
        }
    }

    [Fact]
    public void GetEffectiveSeedVersion_matches_builtin_for_defaults()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();

        foreach (var jobId in GenerationJobGuideService.EditableUtilityJobIds)
        {
            Assert.Equal(
                GenerationJobGuideService.GetSeedVersion(jobId),
                GenerationJobGuideService.GetEffectiveSeedVersion(bundle, jobId));
            Assert.Equal(
                GenerationJobGuideService.GetEffectiveSeedVersion(bundle, jobId),
                GenerationUtilitySessionService.GetSeedVersion(bundle, jobId));
        }
    }
}
