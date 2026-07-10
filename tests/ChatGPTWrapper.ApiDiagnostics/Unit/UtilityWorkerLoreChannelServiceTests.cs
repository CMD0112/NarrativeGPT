using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityWorkerLoreChannelServiceTests
{
    [Fact]
    public void ResolveTaskScoped_minimal_canon_excludes_cast_player_baseline()
    {
        var bundle = CreateLinkedBundleWithSections();
        var signals = new ContextSignalBag { AcceptedTurnCount = 0 };

        var taskRequired = ContextPointerResolver.ResolveTaskScoped(bundle, signals, includeMinimalCanonBaseline: true);

        Assert.Contains(taskRequired.Baseline, p => p.SectionId == "rules");
        Assert.Contains(taskRequired.Baseline, p => p.SectionId == "opening");
        Assert.DoesNotContain(taskRequired.Baseline, p => p.SectionId == "player");
    }

    [Fact]
    public void TryBuild_continuity_includes_utility_worker_sources_block()
    {
        var bundle = CreateLinkedBundleWithSections();
        bundle.Summary.RollingSummary = "Mara guided the party through the harbor.";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "I speak to Mara",
            NarratorText = "She nods.",
        });

        var lore = UtilityWorkerLoreChannelService.TryBuild(
            bundle,
            GenerationJobId.ContinuityCheck,
            new GenerationJobContext());

        Assert.True(lore.HasContent);
        Assert.Contains("mode=\"utility-worker\"", lore.Text);
        Assert.Contains("CANON CORE:", lore.Text);
        Assert.Contains("TASK-SCOPED:", lore.Text);
    }

    [Fact]
    public void AssembleWorkerSoloLocalSync_prepends_lore_before_story_block()
    {
        var bundle = CreateLinkedBundleWithSections();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = true;
        bundle.Summary.RollingSummary = "Harbor fog.";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Hello Mara",
            NarratorText = "She waves.",
        });

        var assembly = UtilityJobContextAssembler.AssembleWorkerSoloLocalSync(
            bundle,
            GenerationJobId.ContinuityCheck);

        Assert.Contains("mode=\"utility-worker\"", assembly.StoryContextBlock);
        Assert.Contains("=== ROLLING SUMMARY ===", assembly.StoryContextBlock);
        Assert.Contains("lore_channel", assembly.Manifest.SectionsIncluded);
    }

    [Fact]
    public void TryBuild_update_summary_job_skips_lore()
    {
        var bundle = CreateLinkedBundleWithSections();
        var lore = UtilityWorkerLoreChannelService.TryBuild(
            bundle,
            GenerationJobId.UpdateSummary,
            new GenerationJobContext());

        Assert.False(lore.HasContent);
    }

    private static AdventureBundle CreateLinkedBundleWithSections() =>
        UtilityWorkerLoreChannelServiceTestsFixtures.CreateLinkedBundleWithSections();
}
