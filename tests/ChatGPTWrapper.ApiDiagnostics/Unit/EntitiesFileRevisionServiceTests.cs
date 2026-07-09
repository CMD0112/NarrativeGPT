using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntitiesFileRevisionServiceTests
{
    [Fact]
    public void BuildRemoteTrackableSourcesPath_uses_canonical_naming()
    {
        var bundle = TestBundle("Snorlax Saga");
        var runId = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        Assert.Equal(
            $"sources/cgw-utility-io/{UtilitySourceFileNaming.BuildAdventureKey(bundle.Metadata.Id)}/propose-entities-file/{UtilitySourceFileNaming.BuildRunKey(runId)}/in/entities.json",
            EntitiesFileRevisionService.BuildCanonicalInputRemotePath(bundle, runId));
    }

    [Fact]
    public void BuildRevisionPrompt_includes_scope_sources_block_and_delivery_block()
    {
        var bundle = TestBundle("Test");
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Aldric", Description = "Guide" });
        var runId = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        var prompt = EntitiesFileRevisionService.BuildRevisionPrompt(
            bundle,
            new UtilityTranscriptScope
            {
                TargetPair = new TranscriptTurnPair
                {
                    TurnIndex = 1,
                    PlayerText = "Who is Aldric?",
                    NarratorText = "A hooded guide watches from the gate.",
                },
            },
            runId,
            "g-p-test");

        Assert.Contains("ENTITIES FILE REVISION JOB", prompt, StringComparison.Ordinal);
        Assert.Contains("[[cgw:sources", prompt, StringComparison.Ordinal);
        Assert.Contains("TASK-SCOPED:", prompt, StringComparison.Ordinal);
        Assert.Contains("propose-entities-file", prompt, StringComparison.Ordinal);
        Assert.Contains("--- begin entities.json ---", prompt, StringComparison.Ordinal);
        Assert.Contains("Aldric", prompt, StringComparison.Ordinal);
        Assert.Contains("hooded guide", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAndEnqueue_extracts_inline_file_and_queues_entity_proposals()
    {
        var bundle = TestBundle("Test");
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Aldric", Description = "Old guide" });

        var response = """
            --- begin entities.json ---
            {
              "schemaVersion": 3,
              "characters": [
                { "name": "Aldric", "description": "Hooded guide at the gate", "role": "Guide" }
              ],
              "party": [],
              "locations": [],
              "inventory": [],
              "quests": [],
              "factions": [],
              "concepts": [],
              "relationships": [],
              "mysteries": [],
              "conflicts": [],
              "consequences": [],
              "customEntries": [],
              "reviewQueue": []
            }
            --- end entities.json ---
            """;

        var count = EntitiesFileRevisionService.ParseAndEnqueue(bundle, response);

        Assert.True(count >= 1);
        Assert.True(EntitiesFileRevisionService.HasProposedSnapshot(bundle.Entities));
        Assert.Contains("Hooded guide", bundle.Entities.ProposedSnapshot!.EntitiesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSettledResponse_accepts_complete_inline_entities_file()
    {
        var response = """
            --- begin entities.json ---
            { "schemaVersion": 3, "characters": [], "party": [], "locations": [], "inventory": [], "quests": [], "factions": [], "concepts": [], "relationships": [], "mysteries": [], "conflicts": [], "consequences": [], "customEntries": [], "reviewQueue": [] }
            --- end entities.json ---
            """;

        Assert.True(EntitiesFileRevisionService.IsSettledResponse(response, streamComplete: true));
    }

    [Fact]
    public void RequiresEphemeralLane_true_for_source_file_io_jobs()
    {
        Assert.True(EntitiesFileRevisionService.RequiresEphemeralLane(GenerationJobId.ProposeEntitiesFile));
        Assert.True(EntitiesFileRevisionService.RequiresEphemeralLane(GenerationJobId.ExtractEntities));
        Assert.True(EntitiesFileRevisionService.RequiresEphemeralLane(GenerationJobId.ExpandEntity));
    }

    private static AdventureBundle TestBundle(string title)
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Title = title;
        return bundle;
    }
}
