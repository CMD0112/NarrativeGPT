using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlayTurnOverrideTests
{
    [Fact]
    public void PrepareSend_appends_turn_overrides_when_set()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            ResponseLength = "brief",
            DetailLevel = "high",
        };
        bundle.Metadata.Settings.DetailLevel = "medium";

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around the room.");

        Assert.Contains("=== TURN OVERRIDES ===", prepared.MergedText);
        Assert.Contains("Response length: brief", prepared.MergedText);
        Assert.Contains("Detail level: high", prepared.MergedText);
    }

    [Fact]
    public void PrepareSend_appends_tone_and_difficulty_overrides()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.Tone = "neutral";
        bundle.Metadata.Settings.Difficulty = "balanced";
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            Tone = "grim",
            Difficulty = "hard",
        };

        var prepared = PromptInjectionService.PrepareSend(bundle, "Charge the gate.");

        Assert.Contains("Tone: grim", prepared.MergedText);
        Assert.Contains("Difficulty: hard", prepared.MergedText);
    }

    [Fact]
    public void PrepareSend_omits_inherited_fields()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.DetailLevel = "medium";
        bundle.Metadata.Settings.Tone = "neutral";
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            DetailLevel = "medium",
            Tone = null,
        };

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around.");

        Assert.DoesNotContain("=== TURN OVERRIDES ===", prepared.MergedText);
    }

    [Fact]
    public void PrepareSend_merges_session_overrides()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        var session = AdventureSessionService.EnsureSession(bundle);
        bundle.Metadata.Settings.SessionNarratorOverrides[session.Id.ToString()] = new PlaySessionNarratorOverrides
        {
            Tone = "dramatic",
        };
        bundle.Metadata.Settings.Tone = "neutral";

        var prepared = PromptInjectionService.PrepareSend(bundle, "Enter the hall.");

        Assert.Contains("Tone: dramatic", prepared.MergedText);
    }

    [Fact]
    public void PrepareSend_appends_turn_directive_block()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            TurnDirective = "Keep this exchange terse and tactical.",
        };

        var prepared = PromptInjectionService.PrepareSend(bundle, "Roll initiative.");

        Assert.Contains("=== TURN DIRECTIVE ===", prepared.MergedText);
        Assert.Contains("Keep this exchange terse and tactical.", prepared.MergedText);
    }
}
