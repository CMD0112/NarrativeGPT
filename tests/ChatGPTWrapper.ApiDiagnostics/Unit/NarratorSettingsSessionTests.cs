using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class NarratorSettingsSessionTests
{
    [Fact]
    public void CreatePreviewBundle_reflects_session_narrator_state()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var session = NarratorSettingsSession.Attach(bundle);
        NarratorOverrideResolver.SetAdventureBaseline(bundle, NarratorParameter.DetailLevel, "deep");
        NarratorOverrideResolver.PersistScope(bundle.Metadata.Settings, NarratorOverrideScope.Adventure);

        var preview = session.CreatePreviewBundle();
        var prepared = PromptInjectionService.PrepareSend(preview, "Look around.");

        Assert.Contains("=== ACTIVE NARRATOR SCALES ===", prepared.MergedText);
        Assert.Contains("deep", prepared.MergedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReloadInto_preserves_narrator_settings_when_requested()
    {
        var bundle = AdventureStore.CreateNew("Reload preserve");
        bundle.Metadata.Settings.DetailLevel = "deep";
        bundle.Metadata.Settings.Tone = "grim";
        bundle.Summary.RollingSummary = "On disk summary";
        AdventureStore.Save(bundle);

        bundle.Summary.RollingSummary = "Edited in memory";
        bundle.Metadata.Settings.DetailLevel = "minimal";

        Assert.True(AdventureStore.ReloadInto(bundle, preserveNarratorSettings: true));
        Assert.Equal("On disk summary", bundle.Summary.RollingSummary);
        Assert.Equal("minimal", bundle.Metadata.Settings.DetailLevel);
        Assert.Equal("grim", bundle.Metadata.Settings.Tone);
    }

    [Fact]
    public void ReloadInto_overwrites_narrator_settings_by_default()
    {
        var bundle = AdventureStore.CreateNew("Reload overwrite");
        bundle.Metadata.Settings.DetailLevel = "deep";
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.DetailLevel = "minimal";
        Assert.True(AdventureStore.ReloadInto(bundle));

        Assert.Equal("deep", bundle.Metadata.Settings.DetailLevel);
    }

    [Fact]
    public void CommitPlaySettingsDialog_persists_scope_and_adventure_baselines()
    {
        var bundle = AdventureStore.CreateNew("Session commit");
        AdventureStore.Save(bundle);

        var session = NarratorSettingsSession.Attach(bundle);
        NarratorOverrideResolver.SetAdventureBaseline(bundle, NarratorParameter.DetailLevel, "deep");
        NarratorOverrideResolver.SetAdventureBaseline(bundle, NarratorParameter.NarrativePacing, "balanced");
        NarratorOverrideResolver.PersistScope(bundle.Metadata.Settings, NarratorOverrideScope.Adventure);

        session.CommitPlaySettingsDialog();

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(NarratorOverrideScope.Adventure, NarratorOverrideResolver.ReadPersistedScope(reloaded.Metadata.Settings));
        Assert.Equal("deep", reloaded.Metadata.Settings.DetailLevel);
        Assert.Equal("balanced", reloaded.Metadata.Settings.NarrativePacing);
    }

    [Fact]
    public void RepointWorkingBundle_follows_dialog_bundle_for_play_settings_save()
    {
        var bundle = AdventureStore.CreateNew("Repoint save");
        AdventureStore.Save(bundle);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        var session = NarratorSettingsSession.Attach(stale);

        var working = AdventureStore.Load(bundle.Metadata.Id)!;
        PlayInjectionPolicyService.ApplyPreset(working.Metadata.Settings, InjectionPresetIds.Compact);
        working.Metadata.Settings.InjectionPolicy.IncludeTranscript = false;

        session.RepointWorkingBundle(working);
        session.CommitPlaySettingsDialog();

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(InjectionPresetIds.Compact, reloaded.Metadata.Settings.InjectionPolicy.InjectionPresetId);
        Assert.False(reloaded.Metadata.Settings.InjectionPolicy.IncludeTranscript);
    }

    [Fact]
    public void NarratorSettingsEqual_detects_baseline_changes()
    {
        var left = new AdventureSettings { DetailLevel = "medium", Tone = "neutral" };
        var right = new AdventureSettings { DetailLevel = "medium", Tone = "neutral" };
        var changed = new AdventureSettings { DetailLevel = "deep", Tone = "neutral" };

        Assert.True(NarratorSettingsSession.NarratorSettingsEqual(left, right));
        Assert.False(NarratorSettingsSession.NarratorSettingsEqual(left, changed));
    }
}
