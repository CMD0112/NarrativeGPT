using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class LocalInferenceLabAdventureContextTests
{
    [Fact]
    public void TryLoadAttachments_reads_verbatim_entities_and_memory_json_from_disk()
    {
        var bundle = AdventureStore.CreateNew("Lab file attach");
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Marta", Role = "innkeeper" });
        bundle.Memory.Entries.Add(new MemoryEntry { Text = "Met Marta at the inn." });
        AdventureStore.Save(bundle);

        var attachments = LocalInferenceLabAdventureContextService.TryLoadAttachments(
            bundle.Metadata.Id,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                LocalInferenceLabAdventureFileIds.Entities,
                LocalInferenceLabAdventureFileIds.Memory,
            },
            utilityRunId: null,
            turnIndexForSlice: null);

        Assert.NotNull(attachments);
        Assert.Equal(2, attachments!.Files.Count);

        var entities = attachments.Files.Single(f => f.RelativePath == "entities.json");
        Assert.Contains("\"Marta\"", entities.Content, StringComparison.Ordinal);
        Assert.Contains("reviewQueue", entities.Content, StringComparison.OrdinalIgnoreCase);

        var memory = attachments.Files.Single(f => f.RelativePath == "memory.json");
        Assert.Contains("Met Marta at the inn.", memory.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void TryLoadAttachments_includes_log_turn_slice_and_utility_run_file()
    {
        var bundle = AdventureStore.CreateNew("Lab slice attach");
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 2,
            Status = TurnStatus.Accepted,
            PlayerText = "Hello there.",
            NarratorText = "General Kenobi.",
        });
        AdventureStore.Save(bundle);

        var pending = new PendingUtilityInjection
        {
            JobId = GenerationJobId.ProposeMemories,
            RunId = Guid.NewGuid(),
            TurnIndex = 2,
        };
        UtilityJobResultStore.SaveRun(
            bundle,
            pending,
            rawResponse: "[{\"text\":\"A memory\"}]",
            UtilitySchemaValidation.Success("[{\"text\":\"A memory\"}]"),
            new GenerationJobResult { Success = true, ProposalCount = 1 },
            conversationId: null,
            promptHash: null,
            sentMessageId: null,
            assistantMessageId: null,
            lane: UtilityLane.Worker,
            streamComplete: true,
            pushedAt: null);

        var attachments = LocalInferenceLabAdventureContextService.TryLoadAttachments(
            bundle.Metadata.Id,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            utilityRunId: pending.RunId,
            turnIndexForSlice: 2);

        Assert.NotNull(attachments);
        Assert.Contains(attachments!.Files, f => f.RelativePath == "log.json#turn/2");
        Assert.Contains(attachments.Files, f => f.RelativePath.StartsWith("utility-results/", StringComparison.Ordinal));
        Assert.Equal(GenerationJobId.ProposeMemories, attachments.JobId);
    }

    [Fact]
    public void BuildUserPrompt_embeds_file_sections_verbatim()
    {
        var attachments = new LocalInferenceLabAdventureAttachments
        {
            AdventureId = Guid.NewGuid(),
            AdventureTitle = "Test",
            DirectoryPath = @"C:\adventures\test",
            JobId = "extract_entities",
            Files =
            [
                new LocalInferenceLabFileAttachment
                {
                    RelativePath = "entities.json",
                    Content = "{\"Characters\":[{\"Name\":\"Marta\"}]}",
                    ByteLength = 30,
                },
            ],
        };

        var prompt = LocalInferenceLabDiagnosticPromptComposer.BuildUserPrompt(
            LocalInferenceLabDiagnosticScenarios.DiagEntityProposalsId,
            attachments);

        Assert.Contains("=== FILE: entities.json", prompt, StringComparison.Ordinal);
        Assert.Contains("\"Marta\"", prompt, StringComparison.Ordinal);
        Assert.Contains("exact file contents from disk", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ACCEPTED ENTITY CANON", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendCanonInstructions_references_review_queue_in_json()
    {
        var merged = LocalInferenceLabDiagnosticPromptComposer.AppendCanonInstructions("Base prompt.");
        Assert.Contains("ReviewQueue", merged, StringComparison.Ordinal);
        Assert.Contains("verbatim", merged, StringComparison.OrdinalIgnoreCase);
    }
}
