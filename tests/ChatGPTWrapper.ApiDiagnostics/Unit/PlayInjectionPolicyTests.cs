using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class PlayInjectionPolicyTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public PlayInjectionPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-InjectionPolicy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        AppDirectories.TestRootOverride = _tempRoot;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }
    [Fact]
    public void ApplyPreset_compact_sets_expected_fields()
    {
        var settings = new AdventureSettings();
        PlayInjectionPolicyService.ApplyPreset(settings, InjectionPresetIds.Compact);

        Assert.Equal(12000, settings.MaxPacketChars);
        Assert.Equal(InjectionPresetIds.Compact, settings.InjectionPolicy.InjectionPresetId);
        Assert.Equal(2, settings.InjectionPolicy.TranscriptMaxTurns);
        Assert.True(settings.InjectionPolicy.IncludeSummary);
        Assert.True(settings.InjectionPolicy.IncludeTranscript);
    }

    [Fact]
    public void PrepareSend_omits_transcript_when_policy_disabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-policy-tx");
        bundle.Metadata.Settings.UseSectionInjection = false;
        bundle.Metadata.Settings.InjectionPolicy.IncludeTranscript = false;
        bundle.Log.Turns.Add(new TurnRecord
        {
            Index = 1,
            PlayerText = "Hello",
            NarratorText = "Hi there.",
            Status = TurnStatus.Accepted,
        });

        var prepared = PromptInjectionService.PrepareSend(bundle, "Next line.");

        Assert.DoesNotContain("=== RECENT TRANSCRIPT ===", prepared.MergedText);
        Assert.Contains(prepared.Sections, s => s.Id == "transcript" && !s.Included);
    }

    [Fact]
    public void PrepareSend_omits_summary_when_policy_disabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-policy-sum");
        bundle.Metadata.Settings.UseSectionInjection = false;
        bundle.Metadata.Settings.InjectionPolicy.IncludeSummary = false;
        bundle.Summary.RollingSummary = "The party reached the mill.";

        var prepared = PromptInjectionService.PrepareSend(bundle, "Look around.");

        Assert.DoesNotContain("=== STORY SO FAR", prepared.MergedText);
    }

    [Fact]
    public void EnforceMandatorySections_restores_sources_in_thin_delegated_mode()
    {
        var settings = new AdventureSettings();
        settings.InjectionPolicy.IncludeSourcesPointers = false;
        settings.InjectionPolicy.IncludeState = false;

        InjectionPolicyGuard.EnforceMandatorySections(settings, thinDelegated: true);

        Assert.True(settings.InjectionPolicy.IncludeSourcesPointers);
        Assert.True(settings.InjectionPolicy.IncludeState);
    }

    [Fact]
    public void ResolveTranscriptMaxTurns_uses_preset_default_for_thin()
    {
        var settings = new AdventureSettings();
        settings.InjectionPolicy.TranscriptMaxTurns = 0;

        var turns = PlayInjectionPolicyService.ResolveTranscriptMaxTurns(settings, PacketMode.Thin);

        Assert.Equal(PlayInjectionPolicyService.DefaultThinTranscriptTurns, turns);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_persists_injection_policy()
    {
        var bundle = AdventureStore.CreateNew("Injection policy save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        PlayInjectionPolicyService.ApplyPreset(ui.Metadata.Settings, InjectionPresetIds.Compact);
        ui.Metadata.Settings.InjectionPolicy.IncludeTranscript = false;
        ui.Metadata.Settings.UseContextTags = false;
        ui.Metadata.Settings.UseSectionInjection = true;

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(InjectionPresetIds.Compact, reloaded.Metadata.Settings.InjectionPolicy.InjectionPresetId);
        Assert.False(reloaded.Metadata.Settings.InjectionPolicy.IncludeTranscript);
        Assert.False(reloaded.Metadata.Settings.UseContextTags);
        Assert.True(reloaded.Metadata.Settings.UseSectionInjection);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_persists_utility_job_guide_overrides()
    {
        var bundle = AdventureStore.CreateNew("Job guide save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        GenerationJobGuideService.SetInstructionOverride(
            ui,
            GenerationJobId.ProposeMemories,
            "Custom memory guide body");

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(
            "Custom memory guide body",
            GenerationJobGuideService.ResolveInstructionBody(reloaded, GenerationJobId.ProposeMemories));
    }

    [Fact]
    public void SavePlaySettingsFromDialog_does_not_rewrite_narrator_scales_on_settings_change()
    {
        var bundle = AdventureStore.CreateNew("Narrator scales stable");
        AdventureStore.Save(bundle);

        var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.NarratorScalesFile);
        var before = File.ReadAllText(path);
        var beforeHash = ProjectSourceExportService.ComputeSha256Bytes(
            System.Text.Encoding.UTF8.GetBytes(before));

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        ui.Metadata.Settings.DetailLevel = "deep";
        ui.Metadata.Settings.NarrativePacing = "brisk";
        AdventureStore.SavePlaySettingsFromDialog(ui);

        var after = File.ReadAllText(path);
        var afterHash = ProjectSourceExportService.ComputeSha256Bytes(
            System.Text.Encoding.UTF8.GetBytes(after));
        Assert.Equal(beforeHash, afterHash);

        var prepared = PromptInjectionService.PrepareSend(ui, "Look around.");
        Assert.Contains("=== ACTIVE NARRATOR SCALES ===", prepared.MergedText);
        Assert.Contains("deep", prepared.MergedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_persists_narrator_scope_and_adventure_baselines()
    {
        var bundle = AdventureStore.CreateNew("Narrator scope save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        NarratorOverrideResolver.SetAdventureBaseline(ui, NarratorParameter.DetailLevel, "deep");
        NarratorOverrideResolver.SetAdventureBaseline(ui, NarratorParameter.Difficulty, "hard");
        NarratorOverrideResolver.PersistScope(ui.Metadata.Settings, NarratorOverrideScope.Adventure);

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(NarratorOverrideScope.Adventure, NarratorOverrideResolver.ReadPersistedScope(reloaded.Metadata.Settings));
        Assert.Equal("deep", reloaded.Metadata.Settings.DetailLevel);
        Assert.Equal("hard", reloaded.Metadata.Settings.Difficulty);
    }
}
