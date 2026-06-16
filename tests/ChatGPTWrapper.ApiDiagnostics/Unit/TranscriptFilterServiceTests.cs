using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class TranscriptFilterServiceTests
{
    private static List<TranscriptTurnPair> MakePairs(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new TranscriptTurnPair
            {
                PlayerText = $"p{i}",
                NarratorText = $"n{i}",
                TurnIndex = i,
            })
            .ToList();

    [Fact]
    public void ApplyLookbackAndFilter_skip_newest_and_max_pairs()
    {
        var settings = new UtilityStoryContextSettings
        {
            MaxTurnPairs = 3,
            SkipNewestTurnPairs = 2,
        };

        var result = TranscriptFilterService.ApplyLookbackAndFilter(MakePairs(10), settings);

        Assert.Equal(3, result.Count);
        Assert.Equal("p6", result[0].PlayerText);
        Assert.Equal("n8", result[^1].NarratorText);
    }

    [Fact]
    public void ApplyLookbackAndFilter_min_pairs_gate_returns_empty()
    {
        var settings = new UtilityStoryContextSettings
        {
            MaxTurnPairs = 10,
            MinTurnPairs = 5,
        };

        var result = TranscriptFilterService.ApplyLookbackAndFilter(MakePairs(2), settings);

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyLookbackAndFilter_drops_pairs_with_wrapped_utility_response()
    {
        var pairs = new List<TranscriptTurnPair>
        {
            new()
            {
                PlayerText = "look around",
                NarratorText = ContextTagFormat.WrapUtilityResponse("generate_recap", "Recap text."),
            },
            new()
            {
                PlayerText = "next",
                NarratorText = "ok",
            },
        };

        var result = TranscriptFilterService.ApplyLookbackAndFilter(pairs, new UtilityStoryContextSettings());

        Assert.Single(result);
        Assert.Equal("next", result[0].PlayerText);
    }

    [Fact]
    public void ApplyLookbackAndFilter_drops_inline_utility_pairs()
    {
        var pairs = new List<TranscriptTurnPair>
        {
            new()
            {
                PlayerText = ContextTagFormat.WrapUtilityJob("propose_memories", "job packet"),
                NarratorText = "[]",
            },
            new()
            {
                PlayerText = "look around",
                NarratorText = "Dust swirls.",
            },
        };
        var settings = new UtilityStoryContextSettings();

        var result = TranscriptFilterService.ApplyLookbackAndFilter(pairs, settings, isLiveSource: true);

        Assert.Single(result);
        Assert.Equal("look around", result[0].PlayerText);
        Assert.Equal("Dust swirls.", result[0].NarratorText);
    }

    [Fact]
    public void ApplyLookbackAndFilter_strips_injected_context_player_text_on_live()
    {
        var pairs = new List<TranscriptTurnPair>
        {
            new()
            {
                PlayerText = "[[cgw:sources]]project lore[[/cgw:sources]]",
                NarratorText = "Opening.",
            },
            new()
            {
                PlayerText = "[[cgw:meta]] [[/cgw:meta]]\n\nlook around",
                NarratorText = "Dust swirls.",
            },
        };
        var settings = new UtilityStoryContextSettings();

        var result = TranscriptFilterService.ApplyLookbackAndFilter(pairs, settings, isLiveSource: true);

        Assert.Equal("", result[0].PlayerText);
        Assert.Equal("Opening.", result[0].NarratorText);
        Assert.Equal("look around", result[1].PlayerText);
    }

    [Fact]
    public void ApplyLookbackAndFilter_strips_narratorless_pairs_on_live()
    {
        var pairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "six", NarratorText = "" },
            new() { PlayerText = "seven", NarratorText = "" },
            new() { PlayerText = "eight", NarratorText = "Eight." },
        };
        var settings = new UtilityStoryContextSettings();

        var result = TranscriptFilterService.ApplyLookbackAndFilter(pairs, settings, isLiveSource: true);

        Assert.Single(result);
        Assert.Equal("eight", result[0].PlayerText);
        Assert.Equal("Eight.", result[0].NarratorText);
    }

    [Fact]
    public void ApplyLookbackAndFilter_excludes_incomplete_trailing_pair_on_live()
    {
        var pairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "a", NarratorText = "b" },
            new() { PlayerText = "c", NarratorText = "" },
        };
        var settings = new UtilityStoryContextSettings { ExcludeIncompleteTrailingPair = true };

        var result = TranscriptFilterService.ApplyLookbackAndFilter(pairs, settings, isLiveSource: true);

        Assert.Single(result);
        Assert.Equal("a", result[0].PlayerText);
    }

    [Fact]
    public void ApplyLookbackAndFilter_since_last_accepted_turn_local()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Log.Turns.Add(new TurnRecord { Index = 1, Status = TurnStatus.Accepted, PlayerText = "old", NarratorText = "old n" });
        bundle.Log.Turns.Add(new TurnRecord { Index = 2, Status = TurnStatus.Accepted, PlayerText = "mid", NarratorText = "mid n" });
        bundle.Log.Turns.Add(new TurnRecord { Index = 3, Status = TurnStatus.Pending, PlayerText = "new", NarratorText = "new n" });

        var pairs = new List<TranscriptTurnPair>
        {
            new() { PlayerText = "old", NarratorText = "old n", TurnIndex = 1 },
            new() { PlayerText = "mid", NarratorText = "mid n", TurnIndex = 2 },
            new() { PlayerText = "new", NarratorText = "new n", TurnIndex = 3 },
        };

        var settings = new UtilityStoryContextSettings
        {
            LookbackAnchor = UtilityLookbackAnchor.SinceLastAcceptedTurn,
            MaxTurnPairs = 12,
        };

        var result = TranscriptFilterService.ApplyLookbackAndFilter(pairs, settings, bundle, isLiveSource: false);

        Assert.Single(result);
        Assert.Equal("new", result[0].PlayerText);
    }
}
