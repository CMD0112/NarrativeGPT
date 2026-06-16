using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityExtractionServiceTests
{
    [Fact]
    public void BuildExtractionPrompt_includes_turn_player_and_narrator_text()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var turn = new TurnRecord
        {
            Index = 1,
            PlayerText = "I search the room.",
            NarratorText = "Dust motes swirl in the lantern light.",
        };

        var prompt = EntityExtractionService.BuildExtractionPrompt(bundle, turn);

        Assert.Contains("I search the room.", prompt);
        Assert.Contains("Dust motes swirl in the lantern light.", prompt);
        Assert.Contains("=== EXCHANGE ===", prompt);
        Assert.Contains("=== EXTRACTION JOB ===", prompt);
    }

    [Fact]
    public void BuildSeedPrompt_includes_title_prefix_and_seed_version()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var prompt = EntityExtractionService.BuildSeedPrompt(bundle, 2);

        Assert.Contains(GenerationUtilitySessionService.GetTitlePrefix(GenerationJobId.ExtractEntities), prompt);
        Assert.Contains(bundle.Metadata.Id.ToString("N"), prompt);
        Assert.Contains("#2", prompt);
    }

    [Theory]
    [InlineData("""{"memories":[{"text":"A clue","tags":[],"pinned":false}]}""", 1)]
    [InlineData("""{"text":"Single memory","tags":["lore"],"pinned":true}""", 1)]
    public void TryNormalizeJsonArrayResponse_unwraps_object_envelopes(string json, int expectedCount)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(json);

        Assert.NotNull(normalized);
        Assert.True(EntityExtractionService.IsValidJsonArray(normalized));
        using var doc = System.Text.Json.JsonDocument.Parse(normalized!);
        Assert.Equal(expectedCount, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void IsSettledJobResponse_rejects_empty_array_until_stream_complete()
    {
        Assert.False(GenerationJobHandlers.IsSettledJobResponse(
            GenerationJobId.ProposeMemories,
            "[]",
            streamComplete: false));
        Assert.True(GenerationJobHandlers.IsSettledJobResponse(
            GenerationJobId.ProposeMemories,
            "[]",
            streamComplete: true));
    }

    [Fact]
    public void HasActionableJobProposals_detects_memory_text()
    {
        const string json = """[{"text":"A room","tags":[],"pinned":false}]""";
        Assert.True(GenerationJobHandlers.HasActionableJobProposals(GenerationJobId.ProposeMemories, json));
        Assert.False(GenerationJobHandlers.HasActionableJobProposals(GenerationJobId.ProposeMemories, "[]"));
    }

    [Fact]
    public void ApplyAcceptedReviewItem_applies_concept()
    {
        var entities = new EntitiesDocument();
        var item = new EntityReviewItem
        {
            EntityType = "concept",
            ProposedChange = """{"entityType":"concept","name":"Blood debt","description":"Owed favors.","category":"culture","action":"create"}""",
        };

        Assert.True(EntityExtractionService.ApplyAcceptedReviewItem(entities, item));
        Assert.Single(entities.Concepts);
        Assert.Equal("Blood debt", entities.Concepts[0].Name);
    }

    [Fact]
    public void UpdateSummary_rejects_story_card_json()
    {
        const string cards = """
            [{"name":"The Room","type":"Place","triggers":["room"],"content":"A plain room.","enabled":true}]
            """;
        var result = GenerationJobHandlers.ApplyResponse(
            AdventureTestData.CreateLinkedBundle(),
            GenerationJobId.UpdateSummary,
            cards);

        Assert.Equal(0, result.ProposalCount);
        Assert.Equal("wrong_response_format", result.Error);
    }

    [Fact]
    public void IsParseableJobResponse_accepts_wrapped_memory_array()
    {
        const string response = """
            ```json
            {"memories":[{"text":"The lord vanished","tags":["mystery"],"pinned":false}]}
            ```
            """;

        Assert.True(GenerationJobHandlers.IsParseableJobResponse(GenerationJobId.ProposeMemories, response));
    }

    [Fact]
    public void TryNormalizeJsonResponse_strips_markdown_fence()
    {
        const string fenced = """
            Here is the JSON:
            ```json
            [{"entityType":"character","name":"Mira"}]
            ```
            """;

        var normalized = EntityExtractionService.TryNormalizeJsonResponse(fenced);

        Assert.NotNull(normalized);
        Assert.StartsWith("[", normalized);
        Assert.EndsWith("]", normalized);
    }

    [Fact]
    public void ParseExtractionResponse_skips_noop_actions()
    {
        const string json = """
            [
              {"entityType":"character","name":"Mira","action":"noop"},
              {"entityType":"location","name":"Tower","action":"create"}
            ]
            """;

        var items = EntityExtractionService.ParseExtractionResponse(json);

        Assert.Single(items);
        Assert.Equal("place", items[0].EntityType);
    }

    [Fact]
    public void ApplyAcceptedReviewItem_adds_character_from_proposal()
    {
        var entities = new EntitiesDocument();
        var item = new EntityReviewItem
        {
            EntityType = "character",
            ProposedChange = """{"entityType":"character","name":"Mira","description":"The innkeeper","roleOrStatus":"ally"}""",
        };

        Assert.True(EntityExtractionService.ApplyAcceptedReviewItem(entities, item));
        var character = Assert.Single(entities.Characters);
        Assert.Equal("Mira", character.Name);
    }

    [Fact]
    public void EnqueueProposals_appends_to_review_queue()
    {
        var entities = new EntitiesDocument();
        var proposals = new[]
        {
            new EntityReviewItem { EntityType = "location", ProposedChange = """{"name":"Tower"}""" },
            new EntityReviewItem { EntityType = "quest", ProposedChange = """{"name":"Find the key"}""" },
        };

        EntityExtractionService.EnqueueProposals(entities, proposals);

        Assert.Equal(2, entities.ReviewQueue.Count);
    }
}

[Trait("Category", "Unit")]
public sealed class GenerationUtilitySessionServiceTests
{
    [Fact]
    public void ShouldRotateSession_when_thresholds_exceeded()
    {
        var session = new GenerationUtilitySession
        {
            JobCount = GenerationUtilitySessionService.MaxJobsPerSession,
            SeedVersion = EntityExtractionService.SeedVersion,
        };
        var bundle = AdventureTestData.CreateLinkedBundle();
        Assert.True(GenerationUtilitySessionService.ShouldRotateSession(
            bundle, session, GenerationJobId.ExtractEntities));
    }

    [Fact]
    public void TryReconcileSession_picks_matching_conversation()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var title = GenerationUtilitySessionService.BuildUtilityTitleLine(bundle, GenerationJobId.ExtractEntities, 1);
        var conversations = new List<GizmoConversationRef>
        {
            new() { Id = "utility-1", Title = title, UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(5) },
        };

        var session = GenerationUtilitySessionService.TryReconcileSession(
            bundle, GenerationJobId.ExtractEntities, conversations);

        Assert.NotNull(session);
        Assert.Equal("utility-1", session.ConversationId);
    }
}

[Trait("Category", "Unit")]
public sealed class GenerationJobHandlersTests
{
    [Fact]
    public void ApplyUpdateSummary_sets_pending_review()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var result = GenerationJobHandlers.ApplyResponse(bundle, GenerationJobId.UpdateSummary, "A new summary text.");

        Assert.True(result.Success);
        Assert.Equal(1, result.ProposalCount);
        Assert.True(bundle.Summary.PendingReview);
        Assert.Equal("A new summary text.", bundle.Summary.ProposedSummary);
    }

    [Fact]
    public void ApplyProposeMemories_enqueues_review_items()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """[{"text":"The lord is missing","tags":["mystery"],"pinned":true}]""";
        var result = GenerationJobHandlers.ApplyResponse(bundle, GenerationJobId.ProposeMemories, json);

        Assert.Equal(1, result.ProposalCount);
        Assert.Single(bundle.Memory.ReviewQueue);
    }

    [Fact]
    public void ApplyProposeMemories_unwraps_memories_envelope()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """{"memories":[{"text":"Hidden door","tags":[],"pinned":false}]}""";
        var result = GenerationJobHandlers.ApplyResponse(bundle, GenerationJobId.ProposeMemories, json);

        Assert.Equal(1, result.ProposalCount);
        Assert.Single(bundle.Memory.ReviewQueue);
    }

    [Fact]
    public void ApplyProposeMemories_skips_null_array_entries()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """[null, {"text":"Valid memory","tags":[],"pinned":false}]""";
        var result = GenerationJobHandlers.ApplyResponse(bundle, GenerationJobId.ProposeMemories, json);

        Assert.Equal(1, result.ProposalCount);
        Assert.Single(bundle.Memory.ReviewQueue);
        Assert.Equal("Valid memory", bundle.Memory.ReviewQueue[0].Text);
    }

    [Fact]
    public void ApplyProposeMemories_unwraps_envelope_with_null_entries()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """{"memories":[null,{"text":"Hidden door","tags":[],"pinned":false}]}""";
        var result = GenerationJobHandlers.ApplyResponse(bundle, GenerationJobId.ProposeMemories, json);

        Assert.Equal(1, result.ProposalCount);
        Assert.Single(bundle.Memory.ReviewQueue);
    }

    [Fact]
    public void TryNormalizeJsonArrayResponse_filters_null_envelope_entries()
    {
        const string json = """{"memories":[null,{"text":"A clue","tags":[],"pinned":false}]}""";
        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(json);

        Assert.NotNull(normalized);
        using var doc = System.Text.Json.JsonDocument.Parse(normalized!);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void GenerationJobScheduler_queues_auto_jobs()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.Settings.AutoExtractEntities = true;
        bundle.Metadata.Settings.AutoProposeMemories = true;
        bundle.Metadata.Settings.AutoUpdateSummary = true;
        bundle.Metadata.Settings.SummaryUpdateIntervalTurns = 5;

        var turn = new TurnRecord { Index = 5, Status = TurnStatus.Accepted, NarratorText = "ok" };
        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);

        Assert.Contains(GenerationJobId.ExtractEntities, jobs);
        Assert.Contains(GenerationJobId.ProposeMemories, jobs);
        Assert.Contains(GenerationJobId.UpdateSummary, jobs);
    }
}
