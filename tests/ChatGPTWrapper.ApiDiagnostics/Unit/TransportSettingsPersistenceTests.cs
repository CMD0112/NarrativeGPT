using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class TransportSettingsPersistenceTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public TransportSettingsPersistenceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-TransportSettings-" + Guid.NewGuid().ToString("N"));
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
    public void Commit_persists_lane_policy_without_play_settings_save()
    {
        var bundle = AdventureStore.CreateNew("Transport write-through");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, reloaded.Metadata.Settings.UtilityExecutionPolicy);
    }

    [Fact]
    public void Full_save_with_stale_bundle_does_not_clobber_transport_on_disk()
    {
        var bundle = AdventureStore.CreateNew("Transport clobber guard");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.PlayInjectionPreferred;
        stale.Memory.Entries.Add(new MemoryEntry { Text = "A fact." });

        AdventureStore.Save(stale, AdventureSaveScope.Memory);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, reloaded.Metadata.Settings.UtilityExecutionPolicy);
        Assert.Single(reloaded.Memory.Entries);
    }

    [Fact]
    public void Memory_only_save_from_dialog_path_does_not_reset_lane_policy()
    {
        var bundle = AdventureStore.CreateNew("Memory save scope");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerPreferred;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var dialogBundle = AdventureStore.Load(bundle.Metadata.Id)!;
        dialogBundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.PlayInjectionPreferred;
        dialogBundle.Memory.ReviewQueue.Add(new MemoryEntry { Text = "Proposed memory." });
        dialogBundle.Memory.ReviewQueue.RemoveAt(0);

        AdventureStore.Save(dialogBundle, AdventureSaveScope.Memory);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerPreferred, reloaded.Metadata.Settings.UtilityExecutionPolicy);
    }

    [Fact]
    public void ApplyToBundle_syncs_stale_play_view_bundle()
    {
        var bundle = AdventureStore.CreateNew("Play view sync");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var playViewBundle = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, playViewBundle.Metadata.Settings.UtilityExecutionPolicy);

        playViewBundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.PlayInjectionPreferred;
        playViewBundle.Entities.ReviewQueue.Add(new EntityReviewItem { EntityType = "character" });

        AdventureStore.Save(playViewBundle, AdventureSaveScope.Entities);

        var meta = AdventureStore.ReadMetadataFromDisk(bundle.Metadata.Id)!;
        TransportSettingsStore.ApplyToBundle(playViewBundle, meta.Settings);

        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, playViewBundle.Metadata.Settings.UtilityExecutionPolicy);
        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, meta.Settings.UtilityExecutionPolicy);
    }

    [Fact]
    public void Full_metadata_save_with_stale_bundle_preserves_transport_via_merge()
    {
        var bundle = AdventureStore.CreateNew("Metadata merge guard");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.PlayInjectionPreferred;
        stale.Cards.Cards.Add(new StoryCard { Name = "Hook", Content = "Rumor." });

        AdventureStore.Save(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, reloaded.Metadata.Settings.UtilityExecutionPolicy);
        Assert.Single(reloaded.Cards.Cards);
    }

    [Fact]
    public void Commit_persists_local_utility_inference_settings()
    {
        var bundle = AdventureStore.CreateNew("Local inference transport");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.LocalUtilityInference.Enabled = true;
        bundle.Metadata.Settings.LocalUtilityInference.DualRun = true;
        bundle.Metadata.Settings.LocalUtilityInference.BaseUrl = "http://127.0.0.1:11434";
        bundle.Metadata.Settings.LocalUtilityInference.Model = "qwen2.5:7b-instruct";
        TransportSettingsStore.Commit(bundle, caller: "test");

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Metadata.Settings.LocalUtilityInference.Enabled);
        Assert.True(reloaded.Metadata.Settings.LocalUtilityInference.DualRun);
        Assert.Equal("http://127.0.0.1:11434", reloaded.Metadata.Settings.LocalUtilityInference.BaseUrl);
        Assert.Equal("qwen2.5:7b-instruct", reloaded.Metadata.Settings.LocalUtilityInference.Model);
    }

    [Fact]
    public void Commit_persists_MaxParallelUtilityWorkerJobs()
    {
        var bundle = AdventureStore.CreateNew("Parallel worker setting");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.MaxParallelUtilityWorkerJobs = 3;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Metadata.Settings.UseEphemeralUtilityWorkerChat);
        Assert.Equal(3, reloaded.Metadata.Settings.MaxParallelUtilityWorkerJobs);
    }

    [Fact]
    public void Commit_persists_UseEphemeralUtilityWorkerChat()
    {
        var bundle = AdventureStore.CreateNew("Ephemeral worker setting");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Metadata.Settings.UseEphemeralUtilityWorkerChat);
    }

    [Fact]
    public void Commit_persists_ForceUtilityWorkerDomAttach()
    {
        var bundle = AdventureStore.CreateNew("Force DOM attach setting");
        AdventureStore.Save(bundle);

        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat = true;
        bundle.Metadata.Settings.ForceUtilityWorkerDomAttach = true;
        TransportSettingsStore.Commit(bundle, caller: "test");

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Metadata.Settings.UseEphemeralUtilityWorkerChat);
        Assert.True(reloaded.Metadata.Settings.ForceUtilityWorkerDomAttach);
    }

    [Fact]
    public void PlaySettingsStore_commit_still_updates_transport_fields()
    {
        var bundle = AdventureStore.CreateNew("Play settings transport");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        ui.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        ui.Metadata.Settings.AutoSpillToWorker = false;

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, reloaded.Metadata.Settings.UtilityExecutionPolicy);
        Assert.False(reloaded.Metadata.Settings.AutoSpillToWorker);
    }
}
