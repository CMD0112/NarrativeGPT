using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Stores;

internal static class AdventureStore
{
    private static string MetadataPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "adventure.json");

    private static string ScenarioPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "scenario.json");

    private static string LogPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "log.json");

    private static string SummaryPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "summary.json");

    private static string StatePath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "state.json");

    private static string MemoryPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "memory.json");

    private static string EntitiesPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "entities.json");

    private static string CardsPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "cards.json");

    private static string ContinuityPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "continuity.json");

    private static string PromptHistoryPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "prompt-history.json");

    private static string UtilityExchangesPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "utility-exchanges.json");

    private static string ThreadMetadataPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "thread-metadata.json");

    private static string NotesPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "notes.txt");

    private static string SourceManifestPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "source-manifest.json");

    private static string DesignWorkspacePath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "design-workspace.json");

    private static string ContextIndexPath(Guid id) =>
        Path.Combine(AppDirectories.AdventureDirectory(id), "context-index.json");

    public static List<AdventureMetadata> ListIndex()
    {
        AppDirectories.EnsureCreated();
        var list = new List<AdventureMetadata>();
        var seen = new HashSet<Guid>();

        if (Directory.Exists(AppDirectories.AdventuresDirectory))
        {
            foreach (var dir in Directory.EnumerateDirectories(AppDirectories.AdventuresDirectory))
            {
                var meta = TryLoadMetadataFromDirectory(dir);
                if (meta is null)
                    continue;

                list.Add(meta);
                seen.Add(meta.Id);
            }
        }

        foreach (var (id, path) in AdventureLocationStore.All.ToList())
        {
            if (seen.Contains(id))
                continue;

            var meta = TryLoadMetadataFromDirectory(path);
            if (meta is null)
                continue;

            if (meta.Id != id)
                meta.Id = id;

            list.Add(meta);
            seen.Add(id);
        }

        return list.OrderByDescending(a => a.LastPlayedAt).ToList();
    }

    private static AdventureMetadata? TryLoadMetadataFromDirectory(string dir)
    {
        var metaPath = Path.Combine(dir, "adventure.json");
        if (!File.Exists(metaPath))
            return null;

        return LoadJson<AdventureMetadata>(metaPath);
    }

    public static AdventureBundle CreateNew(string title, ScenarioDocument? scenario = null, bool designing = false)
    {
        var meta = new AdventureMetadata
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled adventure" : title.Trim(),
            Genre = scenario?.Genre ?? "",
            ScenarioSummary = scenario?.OpeningSituation ?? "",
            Status = designing ? AdventureStatus.Designing : AdventureStatus.Active,
        };

        var bundle = new AdventureBundle
        {
            Metadata = meta,
            Scenario = scenario ?? new ScenarioDocument(),
            DesignWorkspace = designing ? AdventureDesignService.CreateInitialWorkspace() : new(),
        };

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.Genre))
            bundle.Metadata.Genre = bundle.Scenario.Genre;

        AdventureSourceFileService.EnsureLayout(bundle);
        try
        {
            ProjectSourceExportService.ExportForce(bundle);
        }
        catch
        {
            /* export may fail on empty scenario; manifest still valid */
        }

        Save(bundle);
        return bundle;
    }

    public static AdventureBundle? Load(Guid id)
    {
        var dir = AppDirectories.AdventureDirectory(id);
        if (!Directory.Exists(dir))
            return null;

        var meta = LoadJson<AdventureMetadata>(MetadataPath(id));
        if (meta is null)
            return null;

        AdventureMetadataMigration.MigrateUtilitySessions(meta);
        AdventureMetadataMigration.EnsureSettingsDefaults(meta);
        var deprecatedPlayMigrated = AdventureMetadataMigration.MigrateDeprecatedPlaySettings(meta);
        var linkMigrated = AdventureMetadataMigration.MigrateProjectLinkFields(meta);

        var manifest = LoadSourceManifest(id);
        AdventureMetadataMigration.MigrateSourcePublishMode(meta, manifest);
        var deliveryMigrated = AdventureMetadataMigration.MigrateUtilityDeliveryMode(meta);

        var threadRegistryMigrated = AdventureMetadataMigration.MigrateThreadRegistry(meta);
        var bindingRetired = AdventureMetadataMigration.MigrateThreadBindingRetirement(meta);

        if (linkMigrated || threadRegistryMigrated || deliveryMigrated || deprecatedPlayMigrated || bindingRetired)
            WriteJson(MetadataPath(id), meta);

        var entities = LoadJson<EntitiesDocument>(EntitiesPath(id)) ?? new();
        var entitiesMigrated = EntitiesDocumentMigration.Migrate(entities);
        var canonSchemaMigrated = CanonSchemaMigrationService.Migrate(meta);

        var bundle = new AdventureBundle
        {
            Metadata = meta,
            Scenario = LoadJson<ScenarioDocument>(ScenarioPath(id)) ?? new(),
            Log = LoadJson<LogDocument>(LogPath(id)) ?? new(),
            Summary = LoadJson<SummaryDocument>(SummaryPath(id)) ?? new(),
            State = LoadJson<StateDocument>(StatePath(id)) ?? new(),
            Memory = LoadJson<MemoryDocument>(MemoryPath(id)) ?? new(),
            Entities = entities,
            Cards = LoadJson<CardsDocument>(CardsPath(id)) ?? new(),
            Continuity = LoadJson<ContinuityDocument>(ContinuityPath(id)) ?? new(),
            PromptHistory = LoadJson<PromptHistoryDocument>(PromptHistoryPath(id)) ?? new(),
            UtilityExchanges = LoadJson<UtilityExchangesDocument>(UtilityExchangesPath(id)) ?? new(),
            ThreadMetadata = LoadJson<ThreadMetadataDocument>(ThreadMetadataPath(id)) ?? new(),
            Notes = File.Exists(NotesPath(id)) ? File.ReadAllText(NotesPath(id)) : "",
            SourceManifest = manifest,
            ContextIndex = LoadJson<ContextIndexDocument>(ContextIndexPath(id)) ?? new(),
            DesignWorkspace = LoadJson<AdventureDesignWorkspace>(DesignWorkspacePath(id))
                ?? new AdventureDesignWorkspace(),
        };

        var bindingTrustMigrated = AdventureMetadataMigration.MigratePlayThreadBindingTrust(bundle);
        SectionInjectionMigrationService.TryMigrateIfNeeded(bundle);
        var sourcesBootstrapped = AdventureSourceFileService.TryBootstrapLocalSourcesFromDesignWorkspace(bundle);
        var manifestReconciled = AdventureSourceFileService.ReconcileManifest(bundle);
        var loreMaterialized = AdventureProjectBindingService.HasLinkedProject(bundle)
            && ProjectSourceInjectionService.EnsureLoreSourcesMaterialized(bundle);
        var sourcesPushed = CanonReconciliationService.TryAutoPushSourcesFromJsonOnLoad(bundle);
        var queuePruned = ProjectSourceImportService.PruneStaleImportRemovalProposals(bundle);
        if (queuePruned > 0)
            ProjectSourceImportService.DeduplicateSourceEditReviewQueue(bundle);
        AdventureSessionService.RestoreActiveSessionOnLoad(bundle);
        PlaySettingsStore.HydrateContinuationQueue(bundle);
        SummaryReviewService.EnsureRevisionFields(bundle.Summary);
        SummaryReviewService.Normalize(bundle.Summary);
        var workerPinReconciled = UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle);
        var needsPersist = entitiesMigrated || canonSchemaMigrated || sourcesBootstrapped > 0 || manifestReconciled
            || loreMaterialized || sourcesPushed || queuePruned > 0 || bindingTrustMigrated || workerPinReconciled;
        if (needsPersist)
        {
            var scope = entitiesMigrated || canonSchemaMigrated || sourcesBootstrapped > 0 || manifestReconciled
                || loreMaterialized || sourcesPushed || queuePruned > 0 || bindingTrustMigrated
                ? AdventureSaveScope.All
                : AdventureSaveScope.Metadata;
            Save(bundle, scope);
        }
        return bundle;
    }

    /// <summary>
    /// Refreshes an in-memory bundle from disk without replacing the object reference
    /// (keeps shared narrator sessions and cockpit/dialog bundle pointers stable).
    /// </summary>
    public static bool ReloadInto(AdventureBundle target, bool preserveNarratorSettings = false)
    {
        var fresh = Load(target.Metadata.Id);
        if (fresh is null)
            return false;

        CopyBundleState(fresh, target, preserveNarratorSettings);
        return true;
    }

    private static void CopyBundleState(AdventureBundle from, AdventureBundle to, bool preserveNarratorSettings)
    {
        AdventureSettings? narratorSnapshot = null;
        if (preserveNarratorSettings)
            narratorSnapshot = NarratorSettingsSession.CaptureNarratorBaseline(to.Metadata.Settings);

        to.Scenario = CloneJson(from.Scenario);
        to.Log = CloneJson(from.Log);
        to.Summary = CloneJson(from.Summary);
        to.State = CloneJson(from.State);
        to.Memory = CloneJson(from.Memory);
        to.Entities = CloneJson(from.Entities);
        to.Cards = CloneJson(from.Cards);
        to.Continuity = CloneJson(from.Continuity);
        to.PromptHistory = CloneJson(from.PromptHistory);
        to.UtilityExchanges = CloneJson(from.UtilityExchanges);
        to.ThreadMetadata = CloneJson(from.ThreadMetadata);
        to.Notes = from.Notes;
        to.SourceManifest = CloneJson(from.SourceManifest);
        to.ContextIndex = CloneJson(from.ContextIndex);
        to.DesignWorkspace = CloneJson(from.DesignWorkspace);
        to.ContinuationQueue = from.ContinuationQueue.ToList();
        to.CurrentSessionId = from.CurrentSessionId;

        to.Metadata.Settings = CloneJson(from.Metadata.Settings);
        to.Metadata.UtilityJobGuideOverrides = CloneJson(
            from.Metadata.UtilityJobGuideOverrides
            ?? new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase));

        if (from.Metadata.UtilityWorkerCapabilities is not null)
            to.Metadata.UtilityWorkerCapabilities = CloneJson(from.Metadata.UtilityWorkerCapabilities);

        if (preserveNarratorSettings && narratorSnapshot is not null)
            NarratorSettingsSession.ApplyNarratorBaseline(to.Metadata.Settings, narratorSnapshot);
    }

    public static SourceManifest LoadSourceManifest(Guid id)
    {
        var manifest = LoadJson<SourceManifest>(SourceManifestPath(id)) ?? new();
        SourceManifestHelper.MigrateManifest(manifest);
        return manifest;
    }

    public static void SaveSourceManifest(Guid id, SourceManifest manifest)
    {
        WriteJson(SourceManifestPath(id), manifest);
    }

    public static void Save(AdventureBundle bundle, bool allowLinkMetadataOverwrite = false) =>
        Save(bundle, AdventureSaveScope.All, allowLinkMetadataOverwrite);

    public static void Save(
        AdventureBundle bundle,
        AdventureSaveScope scope,
        bool allowLinkMetadataOverwrite = false)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (scope == AdventureSaveScope.None)
            return;

        var id = bundle.Metadata.Id;
        var dir = AppDirectories.AdventureDirectory(id);
        Directory.CreateDirectory(dir);

        bundle.Metadata.LastPlayedAt = DateTimeOffset.UtcNow;
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        PreserveThreadRegistryFromDisk(bundle.Metadata, id);
        PreserveUtilityWorkerBindingFromDisk(bundle.Metadata, id);
        if (!allowLinkMetadataOverwrite)
            PreserveLinkMetadataFromDisk(bundle.Metadata, id);

        if (bundle.Metadata.SchemaVersion >= 6)
            AdventureMetadataMigration.StripLegacyThreadBindingFields(bundle.Metadata);

        AdventureMetadataMigration.MigrateThreadBindingRetirement(bundle.Metadata);

        if (scope.HasFlag(AdventureSaveScope.Metadata))
        {
            SettingsMergeService.MergeTransportOnMetadataSave(
                bundle.Metadata.Settings,
                id,
                caller: scope == AdventureSaveScope.Metadata ? "MetadataOnly" : scope.ToString());
            MergeUtilityWorkerCapabilitiesForSave(bundle.Metadata, id);
            WriteJson(MetadataPath(id), bundle.Metadata);
        }
        if (scope.HasFlag(AdventureSaveScope.Scenario))
            WriteJson(ScenarioPath(id), bundle.Scenario);
        if (scope.HasFlag(AdventureSaveScope.Log))
            WriteJson(LogPath(id), bundle.Log);
        if (scope.HasFlag(AdventureSaveScope.Summary))
        {
            SummaryReviewService.EnsureRevisionFields(bundle.Summary);
            SummaryReviewService.Normalize(bundle.Summary);
            if (scope != AdventureSaveScope.Summary)
                SummaryReviewService.PreserveOnFullSave(bundle.Summary, id);
            WriteJson(SummaryPath(id), bundle.Summary);
        }
        if (scope.HasFlag(AdventureSaveScope.State))
        {
            PlaySettingsStore.MirrorContinuationQueue(bundle);
            WriteJson(StatePath(id), bundle.State);
        }
        if (scope.HasFlag(AdventureSaveScope.Memory))
            WriteJson(MemoryPath(id), bundle.Memory);
        if (scope.HasFlag(AdventureSaveScope.Entities))
            WriteJson(EntitiesPath(id), bundle.Entities);
        if (scope.HasFlag(AdventureSaveScope.Cards))
            WriteJson(CardsPath(id), bundle.Cards);
        if (scope.HasFlag(AdventureSaveScope.Continuity))
            WriteJson(ContinuityPath(id), bundle.Continuity);
        if (scope.HasFlag(AdventureSaveScope.PromptHistory))
            WriteJson(PromptHistoryPath(id), bundle.PromptHistory);
        if (scope.HasFlag(AdventureSaveScope.UtilityExchanges))
            WriteJson(UtilityExchangesPath(id), bundle.UtilityExchanges);
        if (scope.HasFlag(AdventureSaveScope.ThreadMetadata))
            WriteJson(ThreadMetadataPath(id), bundle.ThreadMetadata);
        if (scope.HasFlag(AdventureSaveScope.Notes))
            File.WriteAllText(NotesPath(id), bundle.Notes ?? "");
        if (scope.HasFlag(AdventureSaveScope.SourceManifest))
            SaveSourceManifest(id, bundle.SourceManifest);
        if (scope.HasFlag(AdventureSaveScope.ContextIndex))
            WriteJson(ContextIndexPath(id), bundle.ContextIndex);
        if (scope.HasFlag(AdventureSaveScope.DesignWorkspace))
            WriteJson(DesignWorkspacePath(id), bundle.DesignWorkspace);
    }

    public static void SaveSourceManifestOnly(AdventureBundle bundle) =>
        Save(bundle, AdventureSaveScope.SourceManifest);

    /// <summary>
    /// Persists play-settings UI changes without overwriting structured canon
    /// (<c>entities.json</c> / most of <c>scenario.json</c>) that may have been updated elsewhere.
    /// </summary>
    public static void SavePlaySettingsFromDialog(AdventureBundle ui) =>
        PlaySettingsStore.Commit(ui);

    /// <summary>
    /// Copies utility-job review proposals from disk into a dialog bundle when the UI opened before they landed.
    /// </summary>
    internal static void SyncReviewDomainsFromDisk(AdventureBundle target)
    {
        var disk = ReadBundleDocumentsFromDisk(target.Metadata.Id);
        if (disk is null)
            return;

        SummaryReviewService.SyncFromDisk(target.Summary, disk.Summary);

        if (disk.Memory.ReviewQueue.Count > 0 && target.Memory.ReviewQueue.Count == 0)
            target.Memory.ReviewQueue = CloneJson(disk.Memory.ReviewQueue);

        if (disk.Cards.ReviewQueue.Count > 0 && target.Cards.ReviewQueue.Count == 0)
            target.Cards.ReviewQueue = CloneJson(disk.Cards.ReviewQueue);

        SyncUtilityWorkerCapabilitiesFromDisk(target);
    }

    /// <summary>
    /// Refreshes utility worker probe results when another surface verified while this bundle was open.
    /// </summary>
    internal static void SyncUtilityWorkerCapabilitiesFromDisk(AdventureBundle target)
    {
        var disk = ReadBundleDocumentsFromDisk(target.Metadata.Id);
        if (disk?.Metadata.UtilityWorkerCapabilities is null)
            return;

        var diskCaps = disk.Metadata.UtilityWorkerCapabilities;
        var targetCaps = target.Metadata.UtilityWorkerCapabilities;

        if (targetCaps is null)
        {
            target.Metadata.UtilityWorkerCapabilities = CloneJson(diskCaps);
            return;
        }

        var diskAt = diskCaps.LastProbedAt ?? DateTimeOffset.MinValue;
        var targetAt = targetCaps.LastProbedAt ?? DateTimeOffset.MinValue;

        if (diskAt >= targetAt)
            target.Metadata.UtilityWorkerCapabilities = CloneJson(diskCaps);

        UtilityWorkerPinService.TryReconcilePinFromCapabilities(target);
    }

    /// <summary>
    /// Keeps the newest utility worker probe when concurrent saves race (e.g. verify vs play settings).
    /// </summary>
    private static void MergeUtilityWorkerCapabilitiesForSave(AdventureMetadata incoming, Guid id)
    {
        var onDisk = LoadJson<AdventureMetadata>(MetadataPath(id));
        if (onDisk?.UtilityWorkerCapabilities is null)
            return;

        var incomingCaps = incoming.UtilityWorkerCapabilities;
        var diskCaps = onDisk.UtilityWorkerCapabilities;

        if (incomingCaps is null)
        {
            incoming.UtilityWorkerCapabilities = diskCaps;
            return;
        }

        var incomingAt = incomingCaps.LastProbedAt ?? DateTimeOffset.MinValue;
        var diskAt = diskCaps.LastProbedAt ?? DateTimeOffset.MinValue;

        if (diskAt > incomingAt)
            incoming.UtilityWorkerCapabilities = diskCaps;
    }

    internal static SummaryDocument? ReadSummaryFromDisk(Guid id) =>
        LoadJson<SummaryDocument>(SummaryPath(id));

    internal static AdventureMetadata? ReadMetadataFromDisk(Guid id) =>
        LoadJson<AdventureMetadata>(MetadataPath(id));

    internal static AdventureBundle? ReadBundleDocumentsFromDisk(Guid id)
    {
        var dir = AppDirectories.AdventureDirectory(id);
        if (!Directory.Exists(dir))
            return null;

        var meta = LoadJson<AdventureMetadata>(MetadataPath(id));
        if (meta is null)
            return null;

        var manifest = LoadSourceManifest(id);

        return new AdventureBundle
        {
            Metadata = meta,
            Scenario = LoadJson<ScenarioDocument>(ScenarioPath(id)) ?? new(),
            Log = LoadJson<LogDocument>(LogPath(id)) ?? new(),
            Summary = LoadJson<SummaryDocument>(SummaryPath(id)) ?? new(),
            State = LoadJson<StateDocument>(StatePath(id)) ?? new(),
            Memory = LoadJson<MemoryDocument>(MemoryPath(id)) ?? new(),
            Entities = LoadJson<EntitiesDocument>(EntitiesPath(id)) ?? new(),
            Cards = LoadJson<CardsDocument>(CardsPath(id)) ?? new(),
            Continuity = LoadJson<ContinuityDocument>(ContinuityPath(id)) ?? new(),
            PromptHistory = LoadJson<PromptHistoryDocument>(PromptHistoryPath(id)) ?? new(),
            UtilityExchanges = LoadJson<UtilityExchangesDocument>(UtilityExchangesPath(id)) ?? new(),
            ThreadMetadata = LoadJson<ThreadMetadataDocument>(ThreadMetadataPath(id)) ?? new(),
            Notes = File.Exists(NotesPath(id)) ? File.ReadAllText(NotesPath(id)) : "",
            SourceManifest = manifest,
            ContextIndex = LoadJson<ContextIndexDocument>(ContextIndexPath(id)) ?? new(),
            DesignWorkspace = LoadJson<AdventureDesignWorkspace>(DesignWorkspacePath(id))
                ?? new AdventureDesignWorkspace(),
        };
    }

    private static T CloneJson<T>(T value) where T : class, new() =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, AdventureJson.Options), AdventureJson.Options)
        ?? new();

    public static void Delete(Guid id)
    {
        var dir = AppDirectories.AdventureDirectory(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        AdventureLocationStore.Remove(id);
    }

    public static int DeleteMany(IEnumerable<Guid> ids)
    {
        var count = 0;
        foreach (var id in ids.Distinct())
        {
            Delete(id);
            count++;
        }

        return count;
    }

    public static int SetArchivedMany(IEnumerable<Guid> ids, bool archived)
    {
        var count = 0;
        foreach (var id in ids.Distinct())
        {
            var bundle = Load(id);
            if (bundle is null)
                continue;

            if (bundle.Metadata.Archived == archived)
                continue;

            bundle.Metadata.Archived = archived;
            Save(bundle);
            count++;
        }

        return count;
    }

    public static AdventureMetadata? PeekMetadataFromDirectory(string sourceDir)
    {
        var path = Path.Combine(sourceDir, "adventure.json");
        return LoadJson<AdventureMetadata>(path);
    }

    public static AdventureBundle ImportFromDirectory(string sourceDir, AdventureImportOptions? options = null)
    {
        options ??= new AdventureImportOptions();
        sourceDir = Path.GetFullPath(sourceDir);

        if (!AdventureDirectoryService.DirectoryHasAdventureMetadata(sourceDir))
            throw new InvalidDataException("Missing adventure.json");

        var meta = LoadJson<AdventureMetadata>(Path.Combine(sourceDir, "adventure.json"))
                   ?? throw new InvalidDataException("Missing adventure.json");

        var id = options.NewId ?? meta.Id;

        if (options.Mode == AdventureImportMode.RegisterInPlace)
        {
            meta.Id = id;
            meta.CreatedAt = DateTimeOffset.UtcNow;
            meta.LastPlayedAt = DateTimeOffset.UtcNow;
            WriteJson(Path.Combine(sourceDir, "adventure.json"), meta);
            AdventureLocationStore.Set(id, sourceDir);
            return Load(id) ?? throw new InvalidOperationException("Failed to load registered adventure.");
        }

        meta.Id = id;
        meta.CreatedAt = DateTimeOffset.UtcNow;
        meta.LastPlayedAt = DateTimeOffset.UtcNow;

        var destDir = Path.Combine(AppDirectories.AdventuresDirectory, id.ToString("D"));
        AdventureDirectoryService.CopyDirectory(sourceDir, destDir);
        WriteJson(Path.Combine(destDir, "adventure.json"), meta);
        AdventureLocationStore.Remove(id);

        var bundle = Load(id) ?? throw new InvalidOperationException("Failed to load imported adventure.");
        return bundle;
    }

    public static bool MaterializeDirectory(Guid id)
    {
        var bundle = Load(id);
        if (bundle is null)
            return false;

        var currentDir = Path.GetFullPath(bundle.DirectoryPath);
        var standardDir = Path.GetFullPath(Path.Combine(AppDirectories.AdventuresDirectory, id.ToString("D")));

        if (!string.Equals(currentDir, standardDir, StringComparison.OrdinalIgnoreCase))
        {
            AdventureDirectoryService.CopyDirectory(currentDir, standardDir);
            AdventureLocationStore.Remove(id);
            bundle = Load(id);
            if (bundle is null)
                return false;
        }

        AdventureSourceFileService.EnsureLayout(bundle);
        Save(bundle);
        return true;
    }

    public static AdventureBundle ImportFromDirectory(string sourceDir, Guid? newId) =>
        ImportFromDirectory(sourceDir, new AdventureImportOptions { NewId = newId });

    private static T? LoadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<T>(json, AdventureJson.Options);
        }
        catch
        {
            return null;
        }
    }

    private static void PreserveThreadRegistryFromDisk(AdventureMetadata incoming, Guid id)
    {
        var existing = LoadJson<AdventureMetadata>(MetadataPath(id));
        if (existing is null)
            return;

        if (incoming.ThreadRegistry is null || incoming.ThreadRegistry.Count == 0)
        {
            if (existing.ThreadRegistry is { Count: > 0 })
            {
                incoming.ThreadRegistry = existing.ThreadRegistry;
                incoming.ActiveThreadIds = existing.ActiveThreadIds;
                incoming.ThreadRegistryMigratedAt ??= existing.ThreadRegistryMigratedAt;
            }

            return;
        }

        if (existing.ThreadRegistry is not { Count: > 0 })
            return;

        PreserveThreadRegistryKindFromDisk(incoming, existing, AdventureThreadKind.UtilityWorker);
        PreserveThreadRegistryKindFromDisk(incoming, existing, AdventureThreadKind.Design);
        PreserveThreadRegistryKindFromDisk(incoming, existing, AdventureThreadKind.Play);
    }

    private static void PreserveThreadRegistryKindFromDisk(
        AdventureMetadata incoming,
        AdventureMetadata existing,
        AdventureThreadKind kind)
    {
        incoming.ThreadRegistry ??= [];
        incoming.ActiveThreadIds ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var existingEntry = ResolveThreadRegistryEntryForPreserve(existing, kind);
        if (existingEntry is null || string.IsNullOrWhiteSpace(existingEntry.ConversationId))
            return;

        var incomingEntry = incoming.ThreadRegistry.FirstOrDefault(e => e.Kind == kind);
        if (incomingEntry is null)
        {
            incoming.ThreadRegistry.Add(CloneJson(existingEntry));
            if (existing.ActiveThreadIds?.TryGetValue(
                    AdventureThreadRegistryService.KindKey(kind),
                    out var activeId) == true)
            {
                incoming.ActiveThreadIds[AdventureThreadRegistryService.KindKey(kind)] = activeId;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(incomingEntry.ConversationId))
            incomingEntry.ConversationId = existingEntry.ConversationId;

        if (string.IsNullOrWhiteSpace(incomingEntry.PinnedTabKey)
            && !string.IsNullOrWhiteSpace(existingEntry.PinnedTabKey))
        {
            incomingEntry.PinnedTabKey = existingEntry.PinnedTabKey;
        }

        if (string.IsNullOrWhiteSpace(incomingEntry.PinnedTabTitle)
            && !string.IsNullOrWhiteSpace(existingEntry.PinnedTabTitle))
        {
            incomingEntry.PinnedTabTitle = existingEntry.PinnedTabTitle;
        }

        if (string.IsNullOrWhiteSpace(incomingEntry.PinnedTabUrl)
            && !string.IsNullOrWhiteSpace(existingEntry.PinnedTabUrl))
        {
            incomingEntry.PinnedTabUrl = existingEntry.PinnedTabUrl;
        }
    }

    private static AdventureThreadEntry? ResolveThreadRegistryEntryForPreserve(
        AdventureMetadata metadata,
        AdventureThreadKind kind)
    {
        if (metadata.ThreadRegistry is not { Count: > 0 })
            return null;

        if (metadata.ActiveThreadIds?.TryGetValue(AdventureThreadRegistryService.KindKey(kind), out var activeId) == true)
        {
            var active = metadata.ThreadRegistry.FirstOrDefault(e => e.Id == activeId);
            if (active is not null)
                return active;
        }

        return metadata.ThreadRegistry.FirstOrDefault(e =>
            e.Kind == kind && e.Status == AdventureThreadStatus.Active)
               ?? metadata.ThreadRegistry.FirstOrDefault(e => e.Kind == kind);
    }

    private static void PreserveUtilityWorkerBindingFromDisk(AdventureMetadata incoming, Guid id)
    {
        var existing = LoadJson<AdventureMetadata>(MetadataPath(id));
        if (existing is null)
            return;

        incoming.UtilitySessions ??= new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);
        if (!incoming.UtilitySessions.ContainsKey(UtilityWorkerSessionService.SessionJobId)
            && existing.UtilitySessions?.TryGetValue(
                UtilityWorkerSessionService.SessionJobId,
                out var workerSession) == true
            && workerSession is not null)
        {
            incoming.UtilitySessions[UtilityWorkerSessionService.SessionJobId] = CloneJson(workerSession);
        }
    }

    private static void PreserveLinkMetadataFromDisk(AdventureMetadata incoming, Guid id)
    {
        var existing = LoadJson<AdventureMetadata>(MetadataPath(id));
        if (existing is null)
            return;

        if (string.IsNullOrWhiteSpace(incoming.LinkedProjectId)
            && !string.IsNullOrWhiteSpace(existing.LinkedProjectId))
        {
            incoming.LinkedProjectId = existing.LinkedProjectId;
            incoming.LinkedProjectHint = existing.LinkedProjectHint ?? incoming.LinkedProjectHint;
        }

        incoming.ProjectLink ??= existing.ProjectLink;

        if (existing.SchemaVersion >= 7
            && incoming.ThreadRegistry is { Count: > 0 }
            && existing.ThreadRegistry is { Count: > 0 })
        {
            var incomingPlay = incoming.ThreadRegistry.FirstOrDefault(e => e.Kind == AdventureThreadKind.Play);
            var existingPlay = existing.ThreadRegistry.FirstOrDefault(e => e.Kind == AdventureThreadKind.Play);
            if (incomingPlay is not null
                && existingPlay is not null
                && string.IsNullOrWhiteSpace(incomingPlay.PinnedTabUrl)
                && !string.IsNullOrWhiteSpace(existingPlay.PinnedTabUrl))
            {
                incomingPlay.PinnedTabUrl = existingPlay.PinnedTabUrl;
            }
        }

        if (existing.SchemaVersion >= 6)
            return;

        incoming.LinkedConversationId ??= existing.LinkedConversationId;
        incoming.PinnedPlayTabKey ??= existing.PinnedPlayTabKey;
        incoming.PinnedPlayTabTitle ??= existing.PinnedPlayTabTitle;
        incoming.PinnedPlayTabUrl ??= existing.PinnedPlayTabUrl;
        incoming.PinnedDesignTabKey ??= existing.PinnedDesignTabKey;
        incoming.PinnedDesignTabTitle ??= existing.PinnedDesignTabTitle;
        incoming.PinnedDesignTabUrl ??= existing.PinnedDesignTabUrl;

        if (incoming.UtilitySessions is null || incoming.UtilitySessions.Count == 0)
        {
            if (existing.UtilitySessions is { Count: > 0 })
                incoming.UtilitySessions = existing.UtilitySessions;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(value, AdventureJson.Options));
    }
}
