using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityJobContextPreviewServiceTests
{
    [Fact]
    public void BuildLocal_worker_lane_includes_manifest_and_deduped_job_core()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = true;
        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        bundle.Summary.RollingSummary = "The party entered the crypt.";
        bundle.State.CurrentLocation = "Crypt";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Enter",
            NarratorText = "Darkness.",
        });
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Guide", Role = "NPC" });

        var preview = UtilityJobContextPreviewService.BuildLocal(bundle, GenerationJobId.ContinuityCheck);

        Assert.NotNull(preview.Manifest);
        Assert.Equal(nameof(UtilityExecutionChannel.WorkerBackground), preview.Manifest!.Lane);
        Assert.Contains("worker solo", preview.Manifest.FormatSummary());
        Assert.Contains("=== STORY BLOCK ===", preview.FormatPreviewBody());
        Assert.Contains("=== JOB CORE (deduped) ===", preview.FormatPreviewBody());
        Assert.DoesNotContain("=== RECENT TURNS ===", preview.JobCorePreview ?? "");
    }

    [Fact]
    public void BuildLocal_bundled_lane_omits_story_block_when_play_snapshot_overlaps()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = true;
        bundle.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.InjectionFirst;
        bundle.Summary.RollingSummary = "Rolling summary text.";
        bundle.State.CurrentLocation = "Harbor";
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Look around",
            NarratorText = "Salt air.",
        });

        var preview = UtilityJobContextPreviewService.BuildLocal(bundle, GenerationJobId.ProposeMemories);

        Assert.NotNull(preview.Manifest);
        Assert.Equal(nameof(UtilityExecutionChannel.AutoBackground), preview.Manifest!.Lane);
        Assert.Empty(preview.Text);
        Assert.Contains("play bundled", preview.Manifest.FormatSummary());
    }

    [Fact]
    public void UtilityJobResultStore_persists_context_manifest()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var pending = new PendingUtilityInjection
        {
            JobId = GenerationJobId.ProposeMemories,
            Channel = UtilityExecutionChannel.WorkerBackground,
            ContextManifest = new UtilityContextManifestRecord
            {
                Lane = nameof(UtilityExecutionChannel.WorkerBackground),
                JobId = GenerationJobId.ProposeMemories,
                SectionsIncluded = ["transcript", "summary"],
                SectionsOmitted = ["transcript:deduped"],
                TranscriptSource = nameof(StoryContextSourceUsed.LocalLog),
                TurnPairCount = 2,
                TotalCharCount = 1200,
            },
        };

        UtilityJobResultStore.Save(
            bundle,
            pending,
            rawResponse: "[]",
            validation: new UtilitySchemaValidation { Ok = true, Payload = "[]" },
            applyResult: new GenerationJobResult { Success = true, ProposalCount = 0 });

        var record = UtilityJobResultStore.LoadRun(bundle.Metadata.Id, pending.RunId);
        Assert.NotNull(record);
        Assert.NotNull(record!.ContextManifest);
        Assert.Equal(1200, record.ContextManifest!.TotalCharCount);
        Assert.Contains("transcript", record.ContextManifest.SectionsIncluded);

        AdventureTestData.DeleteBundle(bundle);
    }
}
