using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class SourceJsonImportTests
{
    [Fact]
    public void BuildImportPrompt_includes_scenario_fields_and_sources()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        AdventureTestData.WriteLocalSources(bundle);

        var prompt = SourceJsonImportService.BuildImportPrompt(bundle);

        Assert.Contains("JSON IMPORT JOB", prompt, StringComparison.Ordinal);
        Assert.Contains("plotEssentials:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("=== world.md ===", prompt, StringComparison.Ordinal);
        Assert.Contains("=== scenario.md ===", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAndEnqueue_queues_scenario_field_proposal()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """
            {
              "scenarioFields": [
                { "field": "plotEssentials", "value": "New plot from AI.", "rationale": "plot.md body" }
              ],
              "entities": []
            }
            """;

        var count = SourceJsonImportService.ParseAndEnqueue(bundle, json);

        Assert.Equal(1, count);
        var item = Assert.Single(bundle.Scenario.JsonImportReviewQueue);
        Assert.Equal(SourceJsonImportService.KindScenarioField, item.Kind);
        Assert.Equal("plotEssentials", item.Field);
        Assert.Equal("New plot from AI.", item.Value);
        Assert.Contains("lord vanished", item.PriorValue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAndEnqueue_queues_entity_add_proposal()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """
            {
              "scenarioFields": [],
              "entities": [
                { "action": "add", "name": "Blackwood", "entityType": "person", "description": "The butler.", "rationale": "cast" }
              ]
            }
            """;

        var count = SourceJsonImportService.ParseAndEnqueue(bundle, json);

        Assert.Equal(1, count);
        var item = Assert.Single(bundle.Scenario.JsonImportReviewQueue);
        Assert.Equal(SourceJsonImportService.KindEntity, item.Kind);
        Assert.Equal("add", item.Action);
        Assert.Equal("Blackwood", item.Name);
        Assert.Equal("person", item.EntityType);
    }

    [Fact]
    public void ParseAndEnqueue_skips_unknown_scenario_field()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """
            {
              "scenarioFields": [
                { "field": "notARealField", "value": "x", "rationale": "" }
              ],
              "entities": []
            }
            """;

        Assert.Equal(0, SourceJsonImportService.ParseAndEnqueue(bundle, json));
        Assert.Empty(bundle.Scenario.JsonImportReviewQueue);
    }

    [Fact]
    public void ApplyResponse_malformed_json_fails_gracefully()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var beforePlot = bundle.Scenario.PlotEssentials;

        var result = GenerationJobHandlers.ApplyResponse(
            bundle,
            GenerationJobId.ProposeJsonImport,
            "not json at all");

        Assert.Equal(0, result.ProposalCount);
        Assert.Equal("no_proposals_parsed", result.Error);
        Assert.Empty(bundle.Scenario.JsonImportReviewQueue);
        Assert.Equal(beforePlot, bundle.Scenario.PlotEssentials);
    }

    [Fact]
    public void ApplyAccepted_merges_scenario_field()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var item = new JsonImportReviewItem
        {
            Kind = SourceJsonImportService.KindScenarioField,
            Field = "worldRules",
            Value = "Magic is forbidden.",
        };

        Assert.True(SourceJsonImportService.ApplyAccepted(bundle, item));
        Assert.Equal("Magic is forbidden.", bundle.Scenario.WorldRules);
    }

    [Fact]
    public void ApplyAccepted_adds_entity()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var item = new JsonImportReviewItem
        {
            Kind = SourceJsonImportService.KindEntity,
            EntityType = "place",
            Name = "Observatory",
            Action = "add",
            Value = "A ruined tower on the hill.",
        };

        Assert.True(SourceJsonImportService.ApplyAccepted(bundle, item));
        Assert.Contains(bundle.Entities.Locations, l => l.Name == "Observatory");
    }

    [Fact]
    public void ApplyAccepted_updates_and_removes_entity()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Elena",
            Description = "Old description.",
        });

        var update = new JsonImportReviewItem
        {
            Kind = SourceJsonImportService.KindEntity,
            EntityType = "person",
            Name = "Elena",
            Action = "update",
            Value = "Updated description.",
        };
        Assert.True(SourceJsonImportService.ApplyAccepted(bundle, update));
        Assert.Equal("Updated description.", bundle.Entities.Characters[0].Description);

        var remove = new JsonImportReviewItem
        {
            Kind = SourceJsonImportService.KindEntity,
            EntityType = "person",
            Name = "Elena",
            Action = "remove",
        };
        Assert.True(SourceJsonImportService.ApplyAccepted(bundle, remove));
        Assert.Empty(bundle.Entities.Characters);
    }

    [Fact]
    public void PendingReviewService_counts_json_import_proposals()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Scenario.JsonImportReviewQueue.Add(new JsonImportReviewItem
        {
            Kind = SourceJsonImportService.KindScenarioField,
            Field = "tone",
            Value = "Grim",
        });

        var counts = PendingReviewService.GetCounts(bundle);

        Assert.Equal(1, counts.JsonImports);
        Assert.Equal(1, counts.Total);
        Assert.Equal("1 proposal awaiting review", PendingReviewService.FormatSummaryLine(counts));
    }
}
