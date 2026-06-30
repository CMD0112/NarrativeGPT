using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class PlaySettingsPersistenceTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
    private readonly string _tempRoot;

    public PlaySettingsPersistenceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-PlaySettings-" + Guid.NewGuid().ToString("N"));
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
    public void SavePlaySettingsFromDialog_persists_utility_delivery_settings()
    {
        var bundle = AdventureStore.CreateNew("Utility delivery save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        ui.Metadata.Settings.HideInlineUtilityDuringPlay = false;
        ui.Metadata.Settings.ShowInlineUtilityTraffic = true;
        ui.Metadata.Settings.PlayUtilityInjectionMode = PlayUtilityInjectionMode.InjectionFirst;
        ui.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerPreferred;
        ui.Metadata.Settings.MaxUtilitySectionsPerSend = 4;
        ui.Metadata.Settings.AutoSpillToWorker = false;

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.False(reloaded.Metadata.Settings.HideInlineUtilityDuringPlay);
        Assert.True(reloaded.Metadata.Settings.ShowInlineUtilityTraffic);
        Assert.Equal(PlayUtilityInjectionMode.InjectionFirst, reloaded.Metadata.Settings.PlayUtilityInjectionMode);
        Assert.Equal(UtilityExecutionPolicy.WorkerPreferred, reloaded.Metadata.Settings.UtilityExecutionPolicy);
        Assert.Equal(4, reloaded.Metadata.Settings.MaxUtilitySectionsPerSend);
        Assert.False(reloaded.Metadata.Settings.AutoSpillToWorker);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_persists_automation_toggles()
    {
        var bundle = AdventureStore.CreateNew("Automation save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        ui.Metadata.Settings.AdventureAutomationEnabled = true;
        ui.Metadata.Settings.AutoExtractEntities = true;
        ui.Metadata.Settings.AutoProposeMemories = true;
        ui.Metadata.Settings.AutoUpdateSummary = true;
        ui.Metadata.Settings.SummaryUpdateIntervalTurns = 3;
        ui.Metadata.Settings.AutoContinuityCheck = true;

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Metadata.Settings.AdventureAutomationEnabled);
        Assert.True(reloaded.Metadata.Settings.AutoExtractEntities);
        Assert.True(reloaded.Metadata.Settings.AutoProposeMemories);
        Assert.True(reloaded.Metadata.Settings.AutoUpdateSummary);
        Assert.Equal(3, reloaded.Metadata.Settings.SummaryUpdateIntervalTurns);
        Assert.True(reloaded.Metadata.Settings.AutoContinuityCheck);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_persists_turn_overrides()
    {
        var bundle = AdventureStore.CreateNew("Turn override save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        ui.Metadata.Settings.PlayTurnOverrides = new PlayTurnOverrideSettings
        {
            ResponseLength = "brief",
            DetailLevel = "low",
            Tone = "tense",
            Difficulty = "hard",
        };

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal("brief", reloaded.Metadata.Settings.PlayTurnOverrides.ResponseLength);
        Assert.Equal("low", reloaded.Metadata.Settings.PlayTurnOverrides.DetailLevel);
        Assert.Equal("tense", reloaded.Metadata.Settings.PlayTurnOverrides.Tone);
        Assert.Equal("hard", reloaded.Metadata.Settings.PlayTurnOverrides.Difficulty);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_persists_per_job_story_context_override()
    {
        var bundle = AdventureStore.CreateNew("Story context override save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        UtilityStoryContextSettingsService.SetJobOverride(
            ui,
            GenerationJobId.ProposeMemories,
            new UtilityStoryContextSettings { MaxTurnPairs = 7, IncludeRollingSummary = false });

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        var key = GenerationJobHandlers.GetUtilityJobId(GenerationJobId.ProposeMemories);
        Assert.True(reloaded.Metadata.UtilityJobGuideOverrides.TryGetValue(key, out var over));
        Assert.NotNull(over.Context);
        Assert.Equal(7, over.Context!.MaxTurnPairs);
        Assert.False(over.Context.IncludeRollingSummary);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_preserves_pending_summary_from_disk_when_dialog_stale()
    {
        var bundle = AdventureStore.CreateNew("Summary review preserve");
        AdventureStore.Save(bundle);

        bundle.Summary.ProposedSummary = "Proposed rolling summary from utility job.";
        bundle.Summary.PendingReview = true;
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Summary.PendingReview = false;
        stale.Summary.ProposedSummary = null;
        stale.Metadata.Settings.UtilityExecutionPolicy = UtilityExecutionPolicy.WorkerPreferred;

        AdventureStore.SavePlaySettingsFromDialog(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(UtilityExecutionPolicy.WorkerPreferred, reloaded.Metadata.Settings.UtilityExecutionPolicy);
        Assert.True(reloaded.Summary.PendingReview);
        Assert.Equal("Proposed rolling summary from utility job.", reloaded.Summary.ProposedSummary);
    }

    [Fact]
    public void SyncReviewDomainsFromDisk_loads_pending_summary_into_stale_bundle()
    {
        var bundle = AdventureStore.CreateNew("Sync review domains");
        AdventureStore.Save(bundle);

        bundle.Summary.ProposedSummary = "Queued summary proposal.";
        bundle.Summary.PendingReview = true;
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Summary.PendingReview = false;
        stale.Summary.ProposedSummary = null;

        AdventureStore.SyncReviewDomainsFromDisk(stale);

        Assert.True(stale.Summary.PendingReview);
        Assert.Equal("Queued summary proposal.", stale.Summary.ProposedSummary);
    }

    [Fact]
    public void Full_save_preserves_pending_summary_from_disk_when_bundle_stale()
    {
        var bundle = AdventureStore.CreateNew("Summary clobber guard");
        AdventureStore.Save(bundle);

        bundle.Summary.ProposedSummary = "Proposed rolling summary from utility job.";
        bundle.Summary.PendingReview = true;
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Summary.PendingReview = false;
        stale.Summary.ProposedSummary = null;
        stale.Cards.Cards.Add(new StoryCard { Name = "Side quest hook", Content = "A rumor." });

        AdventureStore.Save(stale);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Summary.PendingReview);
        Assert.Equal("Proposed rolling summary from utility job.", reloaded.Summary.ProposedSummary);
        Assert.Single(reloaded.Cards.Cards);
    }

    [Fact]
    public void Summary_only_save_can_clear_pending_review()
    {
        var bundle = AdventureStore.CreateNew("Summary dismiss");
        AdventureStore.Save(bundle);

        bundle.Summary.ProposedSummary = "Queued summary proposal.";
        bundle.Summary.PendingReview = true;
        AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        SummaryReviewService.DismissProposal(ui);
        AdventureStore.Save(ui, AdventureSaveScope.Summary);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.False(SummaryReviewService.IsPending(reloaded.Summary));
        Assert.Null(reloaded.Summary.ProposedSummary);
    }

    [Fact]
    public void SavePlaySettingsFromDialog_persists_continuation_queue_in_state()
    {
        var bundle = AdventureStore.CreateNew("Continuation queue save");
        AdventureStore.Save(bundle);

        var ui = AdventureStore.Load(bundle.Metadata.Id)!;
        ui.ContinuationQueue = ["Line one", "Line two"];
        PlaySettingsStore.MirrorContinuationQueue(ui);

        AdventureStore.SavePlaySettingsFromDialog(ui);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(["Line one", "Line two"], reloaded.ContinuationQueue);
        Assert.Equal(["Line one", "Line two"], reloaded.State.ContinuationQueue);
    }

    [Fact]
    public void Metadata_save_does_not_clobber_fresher_utility_worker_capabilities()
    {
        var bundle = AdventureStore.CreateNew("Worker cap merge");
        AdventureStore.Save(bundle);

        bundle.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            HostReady = true,
            ApiFetchOk = true,
            ApiPullOk = true,
            DomRegistrationVerified = true,
            ApiPushOk = true,
            LastProbedAt = DateTimeOffset.UtcNow,
            WorkerConversationId = "worker-conv",
        };
        AdventureStore.Save(bundle);

        var stale = AdventureStore.ReadBundleDocumentsFromDisk(bundle.Metadata.Id)!;
        stale.Metadata.UtilityWorkerCapabilities = new UtilityWorkerCapabilities
        {
            HostReady = true,
            LastProbeError = "http_403",
            LastProbedAt = DateTimeOffset.UtcNow.AddHours(-2),
            WorkerConversationId = "worker-conv",
        };

        AdventureStore.Save(stale, AdventureSaveScope.Metadata);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.True(reloaded.Metadata.UtilityWorkerCapabilities!.IsGreen);
        Assert.Null(reloaded.Metadata.UtilityWorkerCapabilities.LastProbeError);
    }

    [Fact]
    public void Metadata_save_preserves_utility_worker_pin_when_stale_bundle_has_play_only_registry()
    {
        var bundle = AdventureStore.CreateNew("Worker pin preserve");
        bundle.Metadata.LinkedProjectId = "g-p-test";
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play).ConversationId = "play-conv";
        var workerEntry = AdventureThreadRegistryService.GetOrCreateActiveEntry(
            bundle,
            AdventureThreadKind.UtilityWorker,
            "Utility worker");
        workerEntry.ConversationId = "worker-conv-preserve";
        bundle.Metadata.UtilitySessions[UtilityWorkerSessionService.SessionJobId] = new GenerationUtilitySession
        {
            ConversationId = "worker-conv-preserve",
            Sequence = 1,
            SeedVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        AdventureStore.Save(bundle);

        var stale = AdventureStore.Load(bundle.Metadata.Id)!;
        stale.Metadata.ThreadRegistry = stale.Metadata.ThreadRegistry
            .Where(e => e.Kind == AdventureThreadKind.Play)
            .ToList();
        stale.Metadata.UtilitySessions = new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);

        AdventureStore.Save(stale, AdventureSaveScope.Metadata);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        Assert.Equal(
            "worker-conv-preserve",
            UtilityWorkerSessionService.GetWorkerConversationId(reloaded));
        Assert.Equal(
            "worker-conv-preserve",
            AdventureThreadRegistryService.GetActiveConversationId(reloaded, AdventureThreadKind.UtilityWorker));
    }
}
