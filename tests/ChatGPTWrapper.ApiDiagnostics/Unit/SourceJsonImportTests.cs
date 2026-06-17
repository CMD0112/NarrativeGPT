using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

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
        Assert.Contains("PROJECT SOURCE REFERENCES", prompt, StringComparison.Ordinal);
        Assert.Contains("sourceRef:", prompt, StringComparison.Ordinal);
        Assert.Contains("sourceRef values", prompt, StringComparison.Ordinal);
        Assert.Contains("plotEssentials:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("=== world.md", prompt, StringComparison.Ordinal);
        Assert.Contains("=== scenario.md", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("[CGW:design]", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Job: design_adventure", prompt, StringComparison.Ordinal);
        Assert.Contains("DELIVERABLE — canonical JSON files", prompt, StringComparison.Ordinal);
        Assert.Contains("scenario.json", prompt, StringComparison.Ordinal);
        Assert.Contains("entities.json", prompt, StringComparison.Ordinal);
        Assert.Contains("ONLY `scenario.json` and `entities.json`", prompt, StringComparison.Ordinal);
        Assert.Contains("--- begin scenario.json ---", prompt, StringComparison.Ordinal);
        Assert.Contains("Import proposal object", prompt, StringComparison.Ordinal);
        Assert.Contains("CRITICAL:", prompt, StringComparison.Ordinal);
        Assert.Contains("Downloadable files alone are NOT enough", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSourceReferencesBlock_lists_section_source_refs_from_manifest()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        AdventureTestData.WriteLocalSources(bundle);

        var block = SourceJsonImportService.BuildSourceReferencesBlock(bundle);

        Assert.Contains("sourceRef:", block, StringComparison.Ordinal);
        Assert.Contains("Retrieve:", block, StringComparison.Ordinal);
        Assert.Matches(@"sourceRef: ""[^""]+\#[^""]+""", block);
    }

    [Fact]
    public void ParseAndEnqueue_extracts_proposal_after_inline_file_blocks()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string response = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Kaed" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [] }
            --- end entities.json ---
            {
              "scenarioFields": [
                { "field": "plotEssentials", "value": "New plot from AI.", "rationale": "plot.md#essentials" }
              ],
              "entities": []
            }
            """;

        var count = SourceJsonImportService.ParseAndEnqueue(bundle, response);

        Assert.Equal(1, count);
        var item = Assert.Single(bundle.Scenario.JsonImportReviewQueue);
        Assert.Equal("plotEssentials", item.Field);
    }

    [Fact]
    public void ParseAndEnqueue_diffs_from_inline_json_blocks_when_proposal_missing()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var beforeSetting = bundle.Scenario.Setting;
        const string response = """
            --- begin scenario.json ---
            {
              "schemaVersion": 1,
              "setting": "Larkhollow, a green valley village.",
              "playerRole": "",
              "genre": "",
              "tone": "",
              "openingSituation": "",
              "majorConflicts": "",
              "startingConstraints": "",
              "plotEssentials": "",
              "worldRules": "",
              "authorsNote": "",
              "lexiconRules": "",
              "lexiconPools": "",
              "lexiconAvoid": ""
            }
            --- end scenario.json ---
            --- begin entities.json ---
            {
              "schemaVersion": 1,
              "characters": [
                { "name": "Nell Arven", "description": "Rescued child." }
              ],
              "party": [],
              "locations": [],
              "factions": [],
              "concepts": []
            }
            --- end entities.json ---
            """;

        var count = SourceJsonImportService.ParseAndEnqueue(bundle, response);

        Assert.True(count >= 2);
        Assert.Contains(
            bundle.Scenario.JsonImportReviewQueue,
            q => q.Kind == SourceJsonImportService.KindScenarioField
                 && q.Field == "setting"
                 && q.Value.Contains("Larkhollow", StringComparison.Ordinal));
        Assert.Contains(
            bundle.Scenario.JsonImportReviewQueue,
            q => q.Kind == SourceJsonImportService.KindEntity
                 && q.Name == "Nell Arven"
                 && q.Action == "add");
        Assert.Equal(beforeSetting, bundle.Scenario.Setting);
    }

    [Fact]
    public void IsSettledResponse_accepts_inline_blocks_when_stream_complete()
    {
        const string response = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Kaed" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [] }
            --- end entities.json ---
            """;

        Assert.True(SourceJsonImportService.IsSettledResponse(response, streamComplete: true));
        Assert.True(SourceJsonImportService.IsSettledResponse(response, streamComplete: false));
    }

    [Fact]
    public void IsParseableResponse_accepts_inline_blocks_without_proposal()
    {
        const string response = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Kaed" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [] }
            --- end entities.json ---
            """;

        Assert.True(SourceJsonImportService.IsParseableResponse(response));
        Assert.True(SourceJsonImportService.IsSettledResponse(response, streamComplete: true));
    }

    [Fact]
    public void ParseAndEnqueue_diffs_from_fenced_inline_json_blocks()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string response = """
            --- begin scenario.json ---
            ```json
            {
              "schemaVersion": 1,
              "setting": "Larkhollow, a green valley village.",
              "playerRole": "",
              "genre": "",
              "tone": "",
              "openingSituation": "",
              "majorConflicts": "",
              "startingConstraints": "",
              "plotEssentials": "",
              "worldRules": "",
              "authorsNote": "",
              "lexiconRules": "",
              "lexiconPools": "",
              "lexiconAvoid": ""
            }
            ```
            --- end scenario.json ---
            --- begin entities.json ---
            ```json
            { "schemaVersion": 1, "characters": [], "party": [], "locations": [], "factions": [], "concepts": [] }
            ```
            --- end entities.json ---
            """;

        var count = SourceJsonImportService.ParseAndEnqueue(bundle, response);

        Assert.True(count >= 1);
        Assert.Contains(
            bundle.Scenario.JsonImportReviewQueue,
            q => q.Kind == SourceJsonImportService.KindScenarioField && q.Field == "setting");
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
    public void HasCompleteJsonImportDelivery_requires_entities_end_marker()
    {
        const string incomplete = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Kaed" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [] }
            """;

        const string complete = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Kaed" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [] }
            --- end entities.json ---
            """;

        Assert.False(SourceJsonImportService.HasCompleteJsonImportDelivery(incomplete));
        Assert.True(SourceJsonImportService.HasCompleteJsonImportDelivery(complete));
    }

    [Fact]
    public void ParseAndEnqueue_diffs_party_description_from_noncanonical_entities_json()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string response = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Same", "playerRole": "", "genre": "", "tone": "", "openingSituation": "", "majorConflicts": "", "startingConstraints": "", "plotEssentials": "", "worldRules": "", "authorsNote": "", "lexiconRules": "", "lexiconPools": "", "lexiconAvoid": "" }
            --- end scenario.json ---
            --- begin entities.json ---
            {
              "schemaVersion": 1,
              "characters": [],
              "party": [
                {
                  "name": "Nell Arven",
                  "entityType": "person",
                  "description": "Rescued child from the Sallow Road."
                }
              ],
              "locations": [],
              "factions": [],
              "concepts": []
            }
            --- end entities.json ---
            """;

        var count = SourceJsonImportService.ParseAndEnqueue(bundle, response);

        Assert.True(count >= 1);
        Assert.Contains(
            bundle.Scenario.JsonImportReviewQueue,
            q => q.Kind == SourceJsonImportService.KindEntity
                 && q.Name == "Nell Arven"
                 && q.Action == "add");
    }

    [Fact]
    public void IsParseableJobResponse_accepts_json_import_inline_blocks()
    {
        const string response = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Kaed" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [] }
            --- end entities.json ---
            """;

        Assert.True(GenerationJobHandlers.IsParseableJobResponse(
            GenerationJobId.ProposeJsonImport,
            response));
    }

    [Fact]
    public void ParseAndEnqueue_diffs_scenario_with_unescaped_inner_quotes()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string response = """
            --- begin scenario.json ---
            {
            "schemaVersion": 1,
            "setting": "Larkhollow, a green valley village.",
            "playerRole": "",
            "genre": "",
            "tone": "",
            "openingSituation": "",
            "majorConflicts": "",
            "startingConstraints": "",
            "plotEssentials": "",
            "worldRules": "- The Seed-Tithe marks victims to be erased and "planted" into the bell.",
            "authorsNote": "",
            "lexiconRules": "- Do not reuse "green," "winter," or "bell" in every new name.",
            "lexiconPools": "",
            "lexiconAvoid": ""
            }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [], "party": [], "locations": [], "factions": [], "concepts": [] }
            --- end entities.json ---
            """;

        var count = SourceJsonImportService.ParseAndEnqueue(bundle, response);

        Assert.True(count >= 2);
        Assert.Contains(
            bundle.Scenario.JsonImportReviewQueue,
            q => q.Kind == SourceJsonImportService.KindScenarioField
                 && q.Field == "setting"
                 && q.Value.Contains("Larkhollow", StringComparison.Ordinal));
        Assert.Contains(
            bundle.Scenario.JsonImportReviewQueue,
            q => q.Kind == SourceJsonImportService.KindScenarioField
                 && q.Field == "lexiconRules"
                 && q.Value.Contains("green,", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseAndEnqueue_saves_proposed_json_snapshot_from_inline_blocks()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string response = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Larkhollow" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [{ "id": "mira", "name": "Mira Thorn" }] }
            --- end entities.json ---
            {
              "scenarioFields": [
                { "field": "plotEssentials", "value": "New plot.", "rationale": "plot.md#essentials" }
              ],
              "entities": []
            }
            """;

        SourceJsonImportService.ParseAndEnqueue(bundle, response);

        Assert.True(SourceJsonImportService.HasProposedJsonSnapshot(bundle.Scenario));
        var snap = bundle.Scenario.JsonImportProposedSnapshot!;
        Assert.Contains("Larkhollow", snap.ScenarioJson, StringComparison.Ordinal);
        Assert.Contains("Mira Thorn", snap.EntitiesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildProposedJsonSnapshot_normalizes_json()
    {
        const string response = """
            --- begin scenario.json ---
            {"schemaVersion":1,"setting":"Kaed"}
            --- end scenario.json ---
            --- begin entities.json ---
            {"schemaVersion":1,"characters":[]}
            --- end entities.json ---
            """;

        var snap = SourceJsonImportService.TryBuildProposedJsonSnapshot(response);

        Assert.NotNull(snap);
        Assert.Contains("\"setting\": \"Kaed\"", snap!.ScenarioJson, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatJsonImportFileDiff_shows_unified_diff()
    {
        var diff = SourceJsonImportService.FormatJsonImportFileDiff(
            """{ "setting": "Kaed" }""",
            """{ "setting": "Larkhollow" }""",
            "Current scenario.json",
            "Proposed scenario.json");

        Assert.Contains("Larkhollow", diff, StringComparison.Ordinal);
        Assert.Contains("Kaed", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void CountProposalsDryRun_does_not_save_proposed_snapshot()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string response = """
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Larkhollow" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "schemaVersion": 1, "characters": [] }
            --- end entities.json ---
            {
              "scenarioFields": [
                { "field": "plotEssentials", "value": "New plot.", "rationale": "plot.md#essentials" }
              ],
              "entities": []
            }
            """;

        SourceJsonImportService.CountProposalsDryRun(bundle, response);

        Assert.Null(bundle.Scenario.JsonImportProposedSnapshot);
    }

    [Fact]
    public void FindNonCanonicalJsonImportFilenames_flags_wrong_block_names()
    {
        const string response = """
            --- begin adventure.json ---
            { "schemaVersion": 1 }
            --- end adventure.json ---
            --- begin scenario.json ---
            { "schemaVersion": 1, "setting": "Kaed" }
            --- end scenario.json ---
            """;

        var invalid = SourceJsonImportService.FindNonCanonicalJsonImportFilenames(response);

        Assert.Equal(["adventure.json"], invalid);
    }

    [Fact]
    public void TryBuildProposedJsonSnapshot_warns_on_missing_schemaVersion()
    {
        const string response = """
            --- begin scenario.json ---
            { "setting": "Kaed" }
            --- end scenario.json ---
            --- begin entities.json ---
            { "characters": [] }
            --- end entities.json ---
            """;

        var snap = SourceJsonImportService.TryBuildProposedJsonSnapshot(response);

        Assert.NotNull(snap);
        Assert.Contains(snap!.PreviewWarnings, w => w.Contains("scenario.json", StringComparison.Ordinal));
        Assert.Contains(snap.PreviewWarnings, w => w.Contains("entities.json", StringComparison.Ordinal));
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

    [Fact]
    public void JsonImportConflict_classifies_supported_proposal_from_cited_source()
    {
        var bundle = AdventureStore.CreateNew("Conflict Supported", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);
            bundle = AdventureStore.Load(bundle.Metadata.Id)!;

            var item = new JsonImportReviewItem
            {
                Kind = SourceJsonImportService.KindScenarioField,
                Field = "plotEssentials",
                Value = "The lord vanished three nights ago.",
                PriorValue = "",
                Rationale = "plot.md#essentials",
            };

            var analysis = JsonImportConflictService.Analyze(bundle, item);

            Assert.Equal(JsonImportConflictSeverity.Supported, analysis.Severity);
            Assert.Equal("plot.md#essentials", analysis.SourceRef);
            Assert.Contains("vanished", analysis.SourceExcerpt!, StringComparison.OrdinalIgnoreCase);
            Assert.True(analysis.WarnStaleSourcesOnAccept);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void JsonImportConflict_classifies_drift_when_proposal_differs_from_deterministic_import()
    {
        var bundle = AdventureStore.CreateNew("Conflict Drift", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var scenarioPath = Path.Combine(
                ProjectSourceExportService.SourcesDirectory(bundle),
                SectionSchema.ScenarioFile);
            var scenarioMarkdown = File.ReadAllText(scenarioPath);
            scenarioMarkdown = scenarioMarkdown.Replace(
                "**Setting:** A haunted castle on the moor",
                "**Setting:** A fogbound lighthouse on the coast",
                StringComparison.Ordinal);
            File.WriteAllText(scenarioPath, scenarioMarkdown);
            ProjectSourceImportService.RefreshManifestSectionsFromMarkdown(
                bundle,
                SectionSchema.ScenarioFile,
                scenarioMarkdown);
            AdventureStore.Save(bundle);
            bundle = AdventureStore.Load(bundle.Metadata.Id)!;

            var item = new JsonImportReviewItem
            {
                Kind = SourceJsonImportService.KindScenarioField,
                Field = "setting",
                Value = "A fogbound lighthouse",
                PriorValue = "A haunted castle on the moor",
                Rationale = "scenario.md",
            };

            var analysis = JsonImportConflictService.Analyze(bundle, item);

            Assert.Equal(JsonImportConflictSeverity.Drift, analysis.Severity);
            Assert.Contains("fogbound lighthouse on the coast", analysis.DeterministicValue!, StringComparison.OrdinalIgnoreCase);
            Assert.True(analysis.WarnStaleSourcesOnAccept);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void JsonImportConflict_classifies_unsupported_when_cited_source_omits_claim()
    {
        var bundle = AdventureStore.CreateNew("Conflict Unsupported", AdventureTestData.CreatePopulatedScenario());
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);
            bundle = AdventureStore.Load(bundle.Metadata.Id)!;

            var item = new JsonImportReviewItem
            {
                Kind = SourceJsonImportService.KindScenarioField,
                Field = "plotEssentials",
                Value = "A completely invented plot line with no basis in sources.",
                PriorValue = bundle.Scenario.PlotEssentials,
                Rationale = "plot.md#essentials",
            };

            var analysis = JsonImportConflictService.Analyze(bundle, item);

            Assert.Equal(JsonImportConflictSeverity.Unsupported, analysis.Severity);
            Assert.True(analysis.WarnStaleSourcesOnAccept);
            Assert.Contains("Unsupported", analysis.DisplaySummary, StringComparison.Ordinal);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void JsonImportConflict_warns_stale_sources_on_accept_for_json_changes()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var item = new JsonImportReviewItem
        {
            Kind = SourceJsonImportService.KindScenarioField,
            Field = "tone",
            Value = "Grim",
            PriorValue = "Brooding and uncanny",
        };

        var analysis = JsonImportConflictService.Analyze(bundle, item);

        Assert.True(analysis.WarnStaleSourcesOnAccept);
        Assert.Contains("Stale sources", analysis.DisplaySummary, StringComparison.Ordinal);
        Assert.Contains("newer than local markdown", JsonImportConflictService.BuildAcceptWarningMessage(analysis), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonImportConflict_entity_add_shows_duplicate_hint_when_manifest_section_exists()
    {
        var bundle = AdventureStore.CreateNew("Conflict Entity", AdventureTestData.CreatePopulatedScenario());
        try
        {
            bundle.Entities.Characters.Add(new CharacterEntry
            {
                Name = "Mara Voss",
                Description = "The household steward.",
            });
            AdventureTestData.WriteLocalSources(bundle);
            AdventureStore.Save(bundle);
            bundle = AdventureStore.Load(bundle.Metadata.Id)!;

            var item = new JsonImportReviewItem
            {
                Kind = SourceJsonImportService.KindEntity,
                EntityType = "person",
                Name = "Mara Voss",
                Action = "add",
                Value = "Duplicate NPC description.",
                Rationale = "cast.md",
            };

            var analysis = JsonImportConflictService.Analyze(bundle, item);

            Assert.Contains("Duplicate hint", analysis.EntityLinkageHint!, StringComparison.Ordinal);
            Assert.Contains("cast.md", analysis.EntityLinkageHint!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}
