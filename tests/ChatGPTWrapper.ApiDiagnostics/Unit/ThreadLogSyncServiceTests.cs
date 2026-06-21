using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ThreadLogSyncServiceTests
{
    [Fact]
    public void Analyze_no_drift_when_thread_matches_log()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.AcceptTurn(turn, "Dark room.");

        var threadPairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "look around", NarratorText = "Dark room." },
        };

        var analysis = ThreadLogSyncService.Analyze(bundle, threadPairs);

        Assert.False(analysis.HasDrift);
        Assert.Equal(1, analysis.ThreadTurnCount);
        Assert.Equal(1, analysis.LogTurnCount);
    }

    [Fact]
    public void FilterThreadPairsForSync_strips_utility_pairs()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var pairs = new List<TranscriptTurnPair>
        {
            new()
            {
                PlayerText = ContextTagFormat.WrapUtilityJob("propose_memories", "job packet"),
                NarratorText = ContextTagFormat.WrapUtilityResponse("propose_memories", "done"),
            },
            new() { PlayerText = "next", NarratorText = "ok" },
        };

        var filtered = ThreadLogSyncService.FilterThreadPairsForSync(pairs, bundle);

        Assert.Single(filtered);
        Assert.Equal("next", filtered[0].PlayerText);
    }

    [Fact]
    public void Analyze_no_drift_when_thread_has_handoff_prefix()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.AcceptTurn(turn, "Dark room.");

        var threadPairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "[[cgw:handoff]]summary[[/cgw:handoff]]", NarratorText = "Ready." },
            new() { PlayerText = "look around", NarratorText = "Dark room." },
        };

        var analysis = ThreadLogSyncService.Analyze(bundle, threadPairs);

        Assert.False(analysis.HasDrift);
        Assert.Equal(2, analysis.ThreadTurnCount);
        Assert.Equal(1, analysis.LogTurnCount);
    }

    [Fact]
    public void Analyze_no_drift_when_narrator_text_differs_only_by_sanitizer()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.AcceptTurn(turn, "Dark room.");

        var threadPairs = new List<TranscriptTurnPair>
        {
            new()
            {
                PlayerText = "look around",
                NarratorText = "Dark room. Show moreShow less",
            },
        };

        var analysis = ThreadLogSyncService.Analyze(bundle, threadPairs);

        Assert.False(analysis.HasDrift);
    }

    [Fact]
    public void Analyze_no_drift_when_log_has_prior_thread_history()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        for (var i = 0; i < 3; i++)
        {
            var turn = TurnTimelineService.CreateTurn(bundle, $"action {i}");
            TurnTimelineService.AcceptTurn(turn, $"response {i}");
        }

        var threadPairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "action 2", NarratorText = "response 2" },
        };

        var analysis = ThreadLogSyncService.Analyze(bundle, threadPairs);

        Assert.False(analysis.HasDrift);
        Assert.Equal(1, analysis.ThreadTurnCount);
        Assert.Equal(3, analysis.LogTurnCount);
        Assert.Equal(1, analysis.ComparedTurnCount);
    }

    [Fact]
    public void Analyze_no_drift_when_narrator_capture_is_truncated_prefix()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.AcceptTurn(turn, "Dark room with a long description that continues.");

        var threadPairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "look around", NarratorText = "Dark room with a long" },
        };

        var analysis = ThreadLogSyncService.Analyze(bundle, threadPairs);

        Assert.False(analysis.HasDrift);
    }

    [Fact]
    public void Analyze_detects_real_suffix_mismatch()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "look around");
        TurnTimelineService.AcceptTurn(turn, "Dark room.");

        var threadPairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "bootstrap", NarratorText = "ok" },
            new() { PlayerText = "look around", NarratorText = "Different ending." },
        };

        var analysis = ThreadLogSyncService.Analyze(bundle, threadPairs);

        Assert.True(analysis.HasDrift);
    }

    [Fact]
    public void ApplyFromThread_rebuilds_accepted_turns()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        var turn = TurnTimelineService.CreateTurn(bundle, "old");
        TurnTimelineService.AcceptTurn(turn, "stale");

        var threadPairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "fresh", NarratorText = "new scene" },
            new() { PlayerText = "continue", NarratorText = "and on" },
        };

        ThreadLogSyncService.ApplyFromThread(bundle, threadPairs);

        var accepted = bundle.Log.Turns.Where(t => t.Status == TurnStatus.Accepted).OrderBy(t => t.Index).ToList();
        Assert.Equal(2, accepted.Count);
        Assert.Equal("fresh", accepted[0].PlayerText);
        Assert.Equal("new scene", accepted[0].NarratorText);
        Assert.Equal("continue", accepted[1].PlayerText);
        Assert.Equal(4, bundle.ThreadMetadata.Messages.Count(m => !m.IsUtility && m.LinkedTurnId is not null));
    }
}

[Trait("Category", "Unit")]
public sealed class AdventureRandomTablesStoreTests
{
    [Fact]
    public void Save_and_load_round_trip()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        try
        {
            var doc = new RandomTablesDocument
            {
                Tables = new Dictionary<string, List<string>>
                {
                    ["mood"] = ["tense", "calm"],
                },
            };

            AdventureRandomTablesStore.Save(bundle, doc);
            var loaded = AdventureRandomTablesStore.Load(bundle);

            Assert.True(loaded.Tables.TryGetValue("mood", out var entries));
            Assert.Equal(["tense", "calm"], entries);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}
