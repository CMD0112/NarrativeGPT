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
        Assert.Contains("inspect narrator-scales.md", prepared.MergedText);
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
        Assert.Contains("Combat difficulty: hard", prepared.MergedText);
        Assert.Contains("inspect narrator-scales.md", prepared.MergedText);
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
        Assert.Contains("inspect narrator-scales.md", prepared.MergedText);
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

    [Fact]
    public void PrepareSend_omits_override_when_baseline_tone_from_scenario()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.SourcePublishMode = SourcePublishMode.ApiSync;
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Metadata.Settings.Tone = null!;
        bundle.Scenario.Tone = "Brooding and uncanny";
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            Tone = null,
        };

        var prepared = PromptInjectionService.PrepareSend(bundle, "Step into the fog.");

        Assert.DoesNotContain("=== TURN OVERRIDES ===", prepared.MergedText);
    }

    [Fact]
    public void PrepareSend_omits_response_length_when_normal()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            ResponseLength = "normal",
            DetailLevel = "medium",
        };
        bundle.Metadata.Settings.DetailLevel = "medium";

        var prepared = PromptInjectionService.PrepareSend(bundle, "Scan the horizon.");

        Assert.DoesNotContain("=== TURN OVERRIDES ===", prepared.MergedText);
    }

    [Fact]
    public void PrepareSend_appends_violence_pacing_and_consequence_overrides()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.ViolenceLevel = "moderate";
        bundle.Metadata.Settings.NarrativePacing = "balanced";
        bundle.Metadata.Settings.ConsequenceWeight = "balanced";
        bundle.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            ViolenceLevel = "mild",
            NarrativePacing = "brisk",
            ConsequenceWeight = "forgiving",
        };

        var prepared = PromptInjectionService.PrepareSend(bundle, "Sprint through the alley.");

        Assert.Contains("Violence level: mild", prepared.MergedText);
        Assert.Contains("Narrative pacing: brisk", prepared.MergedText);
        Assert.Contains("Consequence weight: forgiving", prepared.MergedText);
    }

    [Fact]
    public void ResolveViolenceLevel_coalesces_turn_session_adventure()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.ViolenceLevel = "moderate";
        var session = AdventureSessionService.EnsureSession(bundle);
        bundle.Metadata.Settings.SessionNarratorOverrides[session.Id.ToString()] = new PlaySessionNarratorOverrides
        {
            ViolenceLevel = "intense",
        };
        bundle.Metadata.Settings.PlayTurnOverrides.ViolenceLevel = "mild";

        Assert.Equal("mild", NarratorOverrideResolver.ResolveViolenceLevel(bundle));

        bundle.Metadata.Settings.PlayTurnOverrides.ViolenceLevel = null;
        Assert.Equal("intense", NarratorOverrideResolver.ResolveViolenceLevel(bundle));

        bundle.Metadata.Settings.SessionNarratorOverrides.Clear();
        Assert.Equal("moderate", NarratorOverrideResolver.ResolveViolenceLevel(bundle));
    }

    [Fact]
    public void SetAdventureBaseline_persists_balanced_catalog_defaults()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "proj");
        bundle.Metadata.Settings.NarrativePacing = "brisk";
        bundle.Metadata.Settings.ConsequenceWeight = "harsh";

        NarratorOverrideResolver.SetAdventureBaseline(bundle, NarratorParameter.NarrativePacing, "balanced");
        NarratorOverrideResolver.SetAdventureBaseline(bundle, NarratorParameter.ConsequenceWeight, "balanced");

        Assert.Equal("balanced", bundle.Metadata.Settings.NarrativePacing);
        Assert.Equal("balanced", bundle.Metadata.Settings.ConsequenceWeight);
    }
}
