using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>CMD-450: worker assembler manifest sections align with ai-tools-context-matrix.</summary>
[Trait("Category", "Unit")]
[Trait("Diagnostics", "Logged")]
public sealed class AutomationJobContextManifestTests
{
    [Theory]
    [InlineData(GenerationJobId.ExtractEntities)]
    [InlineData(GenerationJobId.ProposeMemories)]
    [InlineData(GenerationJobId.ContinuityCheck)]
    public async Task WorkerSolo_manifest_sections_match_context_matrix(string jobId)
    {
        var bundle = CreateWorkerBundle();
        bundle.Metadata.Settings.UseUtilityJobContextAssembler = true;

        var assembler = new UtilityJobContextAssembler();
        var result = await assembler.AssembleAsync(
            bundle,
            jobId,
            new UtilityContextAssemblyRequest { Channel = UtilityExecutionChannel.WorkerBackground });

        Assert.NotNull(result.Manifest);
        Assert.Equal(jobId, result.Manifest!.JobId);
        Assert.Contains("transcript", result.Manifest.SectionsIncluded);

        switch (jobId)
        {
            case GenerationJobId.ExtractEntities:
                Assert.Equal(1, result.Manifest.TurnPairCount);
                Assert.Contains("entity_index", result.Manifest.SectionsIncluded);
                Assert.DoesNotContain("summary", result.Manifest.SectionsIncluded);
                break;

            case GenerationJobId.ProposeMemories:
                Assert.Equal(1, result.Manifest.TurnPairCount);
                Assert.Contains("=== MEMORY BASELINE ===", result.StoryContextBlock);
                Assert.DoesNotContain("entity_index", result.Manifest.SectionsIncluded);
                break;

            case GenerationJobId.ContinuityCheck:
                Assert.True(result.Manifest.TurnPairCount >= 1);
                Assert.Contains("summary", result.Manifest.SectionsIncluded);
                Assert.Contains("entity_index", result.Manifest.SectionsIncluded);
                Assert.Contains("state", result.Manifest.SectionsIncluded);
                break;
        }
    }

    private static AdventureBundle CreateWorkerBundle()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        PlayThreadBindingService.MarkVerified(bundle, "conv-play-manifest");
        bundle.Summary.RollingSummary = "The party entered the crypt.";
        bundle.State.CurrentLocation = "Crypt antechamber";
        bundle.State.OpenObjectives = "Find the key";
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Guide", Role = "NPC" });
        bundle.Memory.Entries.Add(new MemoryEntry
        {
            Text = "Found a rusted key.",
            Tags = ["item"],
            Anchor = new MemoryAnchor { TurnIndex = 1 },
        });
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            Status = TurnStatus.Accepted,
            PlayerText = "Enter the crypt",
            NarratorText = "Darkness swallows the torchlight.",
        });
        return bundle;
    }
}
