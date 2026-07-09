using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class PlaySettingsEditorBaselineTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public PlaySettingsEditorBaselineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-PlaySettingsBaseline-" + Guid.NewGuid().ToString("N"));
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
    public void Diff_is_empty_for_unchanged_snapshot()
    {
        var bundle = AdventureStore.CreateNew("Baseline round-trip");
        var chrome = UiChromeStore.Load();
        var baseline = PlaySettingsEditorBaseline.Capture(bundle, chrome, "", bundle.Metadata.Settings);

        Assert.Empty(baseline.Diff(bundle, chrome, "", bundle.Metadata.Settings));
    }

    [Fact]
    public void Diff_detects_automation_toggle()
    {
        var bundle = AdventureStore.CreateNew("Automation diff");
        var chrome = UiChromeStore.Load();
        var baseline = PlaySettingsEditorBaseline.Capture(bundle, chrome, "", bundle.Metadata.Settings);

        bundle.Metadata.Settings.AutoUpdateSummary = true;
        bundle.Metadata.Settings.SummaryUpdateIntervalTurns = 3;

        var hints = baseline.Diff(bundle, chrome, "", bundle.Metadata.Settings);
        Assert.Contains("AI automation", hints);
    }

    [Fact]
    public void Diff_detects_job_guide_override()
    {
        var bundle = AdventureStore.CreateNew("Guide diff");
        var chrome = UiChromeStore.Load();
        var baseline = PlaySettingsEditorBaseline.Capture(bundle, chrome, "", bundle.Metadata.Settings);

        GenerationJobGuideService.SetInstructionOverride(
            bundle,
            GenerationJobId.ProposeMemories,
            "Custom memory rules.");

        var hints = baseline.Diff(bundle, chrome, "", bundle.Metadata.Settings);
        Assert.Contains("job guides", hints);
    }

    [Fact]
    public void WithPersistedSettings_clears_transport_diff_after_write_through()
    {
        var bundle = AdventureStore.CreateNew("Transport baseline");
        AdventureStore.Save(bundle);
        var chrome = UiChromeStore.Load();
        var baseline = PlaySettingsEditorBaseline.Capture(bundle, chrome, "", bundle.Metadata.Settings);

        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var meta = AdventureStore.ReadMetadataFromDisk(bundle.Metadata.Id)!;
        var refreshed = baseline.WithPersistedSettings(meta.Settings);

        Assert.Empty(refreshed.Diff(bundle, chrome, "", bundle.Metadata.Settings));
    }
}
