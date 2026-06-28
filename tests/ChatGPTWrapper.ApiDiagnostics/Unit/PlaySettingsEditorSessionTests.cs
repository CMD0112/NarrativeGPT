using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(nameof(IsolatedAppRootCollection))]
public sealed class PlaySettingsEditorSessionTests : IDisposable
{
    private readonly string _tempRoot;

    public PlaySettingsEditorSessionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-PlaySettingsSession-" + Guid.NewGuid().ToString("N"));
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
    public void SyncFromDisk_when_dirty_does_not_replace_utility_execution_policy()
    {
        var bundle = AdventureStore.CreateNew("Dirty sync guard");
        AdventureStore.Save(bundle);

        var session = PlaySettingsEditorSession.Attach(AdventureStore.Load(bundle.Metadata.Id)!);
        session.IsDirty = () => true;

        session.Bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        session.Bundle.Metadata.Settings.AutoUpdateSummary = true;

        var disk = AdventureStore.Load(bundle.Metadata.Id)!;
        disk.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.PlayInjectionPreferred;
        disk.Metadata.Settings.AutoUpdateSummary = false;
        disk.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            HostReady = true,
            ApiPullOk = true,
            ApiPushOk = true,
            DomRegistrationVerified = true,
            LastProbedAt = DateTimeOffset.UtcNow,
            WorkerConversationId = "worker-conv",
        };
        AdventureStore.Save(disk);

        var reboundPolicy = UtilityExecutionPolicy.PlayInjectionPreferred;
        session.SyncFromDisk(
            preserveNarratorSettings: false,
            rebindAllControls: () => reboundPolicy = session.Bundle.Metadata.Settings.UtilityExecutionPolicy,
            refreshExternalOnly: () => { });

        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, session.Bundle.Metadata.Settings.UtilityExecutionPolicy);
        Assert.True(session.Bundle.Metadata.Settings.AutoUpdateSummary);
        Assert.True(session.Bundle.Metadata.UtilityWorkerCapabilities!.IsGreen);
        Assert.Equal(UtilityExecutionPolicy.PlayInjectionPreferred, reboundPolicy);
    }

    [Fact]
    public void Commit_persists_ai_tools_settings()
    {
        var bundle = AdventureStore.CreateNew("Session commit");
        AdventureStore.Save(bundle);

        var session = PlaySettingsEditorSession.Attach(AdventureStore.Load(bundle.Metadata.Id)!);
        session.Bundle.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerOnly;
        session.Bundle.Metadata.Settings.AutoSpillToWorker = false;
        session.Bundle.Metadata.Settings.AutoUpdateSummary = true;
        session.Bundle.Metadata.Settings.HideInlineUtilityDuringPlay = false;

        session.Commit(
            () => { },
            preserveNarratorSettings: false,
            rebindAllControls: () => { });

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerOnly, reloaded.Metadata.Settings.UtilityExecutionPolicy);
        Assert.False(reloaded.Metadata.Settings.AutoSpillToWorker);
        Assert.True(reloaded.Metadata.Settings.AutoUpdateSummary);
        Assert.False(reloaded.Metadata.Settings.HideInlineUtilityDuringPlay);
    }
}
