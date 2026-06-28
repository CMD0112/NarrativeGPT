using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Adventure.Stores;

/// <summary>
/// Single commit path for Play Settings dialog persistence.
/// Always read-merge-writes from disk so background jobs cannot be clobbered by stale bundles.
/// </summary>
internal static class PlaySettingsStore
{
    [Flags]
    internal enum ExternalSync
    {
        None = 0,
        ReviewProposals = 1 << 0,
        WorkerCapabilities = 1 << 1,
        All = ReviewProposals | WorkerCapabilities,
    }

    /// <summary>Loads a fresh bundle snapshot for editing (play settings domains only).</summary>
    internal static AdventureBundle? LoadForEditor(Guid adventureId) =>
        AdventureStore.Load(adventureId);

    /// <summary>
    /// Persists dialog-owned domains. Never writes entities, log, or source manifest.
    /// </summary>
    internal static void Commit(AdventureBundle workingCopy)
    {
        ArgumentNullException.ThrowIfNull(workingCopy);

        var disk = AdventureStore.ReadBundleDocumentsFromDisk(workingCopy.Metadata.Id);
        if (disk is null)
            return;

        NormalizeBeforeCommit(workingCopy);

        disk.Metadata.Settings = CloneJson(workingCopy.Metadata.Settings);
        disk.Metadata.UtilityJobGuideOverrides = CloneJson(
            workingCopy.Metadata.UtilityJobGuideOverrides
            ?? new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase));

        MergeSummaryForPlaySettingsSave(disk.Summary, workingCopy.Summary);
        disk.State = CloneJson(workingCopy.State);
        disk.State.ContinuationQueue = workingCopy.ContinuationQueue.ToList();
        disk.Scenario.AuthorsNote = workingCopy.Scenario.AuthorsNote;
        MergeCardsForPlaySettingsSave(disk.Cards, workingCopy.Cards);
        MergeMemoryForPlaySettingsSave(disk.Memory, workingCopy.Memory);
        disk.Continuity = CloneJson(workingCopy.Continuity);
        disk.UtilityExchanges = CloneJson(workingCopy.UtilityExchanges);
        disk.ThreadMetadata = CloneJson(workingCopy.ThreadMetadata);

        using (SettingsMergeService.BeginExplicitTransportWrite())
        {
            AdventureStore.Save(disk, AdventureSaveScope.PlaySettingsDialog);
        }

        AdventureSettingsCommitNotifier.Notify(workingCopy.Metadata.Id);
    }

    internal static void SyncExternalDomains(AdventureBundle target, ExternalSync sync)
    {
        if (sync == ExternalSync.None)
            return;

        if (sync.HasFlag(ExternalSync.ReviewProposals))
            AdventureStore.SyncReviewDomainsFromDisk(target);
        else if (sync.HasFlag(ExternalSync.WorkerCapabilities))
            AdventureStore.SyncUtilityWorkerCapabilitiesFromDisk(target);
    }

    internal static void MirrorContinuationQueue(AdventureBundle bundle)
    {
        if (bundle.ContinuationQueue.Count > 0 || bundle.State.ContinuationQueue.Count == 0)
            bundle.State.ContinuationQueue = bundle.ContinuationQueue.ToList();
    }

    internal static void HydrateContinuationQueue(AdventureBundle bundle)
    {
        if (bundle.State.ContinuationQueue.Count > 0 || bundle.ContinuationQueue.Count == 0)
            bundle.ContinuationQueue = bundle.State.ContinuationQueue.ToList();
    }

    private static void NormalizeBeforeCommit(AdventureBundle bundle)
    {
        UtilityStoryContextSettingsService.EnsureDefaults(bundle.Metadata);
        PlayInjectionPolicyService.EnsureDefaults(bundle.Metadata);
        MirrorContinuationQueue(bundle);
    }

    private static void MergeSummaryForPlaySettingsSave(SummaryDocument target, SummaryDocument ui) =>
        SummaryReviewService.MergeForPlaySettingsSave(target, ui);

    private static void MergeMemoryForPlaySettingsSave(MemoryDocument target, MemoryDocument ui)
    {
        target.Entries = CloneJson(ui.Entries);

        if (ui.ReviewQueue.Count > 0)
            target.ReviewQueue = CloneJson(ui.ReviewQueue);
    }

    private static void MergeCardsForPlaySettingsSave(CardsDocument target, CardsDocument ui)
    {
        target.Cards = CloneJson(ui.Cards);

        if (ui.ReviewQueue.Count > 0)
            target.ReviewQueue = CloneJson(ui.ReviewQueue);
    }

    private static T CloneJson<T>(T value) where T : class, new() =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AdventureJson.Options), AdventureJson.Options)
        ?? new();
}
