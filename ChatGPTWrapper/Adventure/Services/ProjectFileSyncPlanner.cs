using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class SyncPlanBuildOptions
{
    public bool ExportSources { get; init; } = true;

    public SourceExportMode ExportMode { get; init; } = SourceExportMode.IfStale;

    public bool EnsureProjectPage { get; init; } = true;

    public IReadOnlyList<GizmoFileRef>? CachedRemoteFiles { get; init; }
}

internal static class ProjectFileSyncPlanner
{
    private const int MaxParallelRemoteDownloads = 3;

    public static string SyncTempDirectory(AdventureBundle bundle) =>
        Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), ".sync-tmp");

    public static async Task<SourceSyncPlan> BuildPlanAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool ensureProjectPage = true,
        IReadOnlyList<GizmoFileRef>? cachedRemoteFiles = null,
        bool exportSources = true,
        SourceExportMode exportMode = SourceExportMode.IfStale) =>
        await BuildPlanAsync(
            core,
            bundle,
            api,
            new SyncPlanBuildOptions
            {
                ExportSources = exportSources,
                ExportMode = exportMode,
                EnsureProjectPage = ensureProjectPage,
                CachedRemoteFiles = cachedRemoteFiles,
            },
            progress,
            cancellationToken);

    public static async Task<SourceSyncPlan> BuildPlanAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        SyncPlanBuildOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId))
            throw new InvalidOperationException("No linked ChatGPT project.");

        SourceManifestHelper.MigrateManifest(bundle.SourceManifest);

        if (options.ExportSources)
        {
            progress?.Report("Checking local sources…");
            ProjectSourceExportService.Export(bundle, options.ExportMode);
        }

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        foreach (var entry in bundle.SourceManifest.Entries)
        {
            var localPath = Path.Combine(dir, entry.RelativePath);
            if (File.Exists(localPath))
            {
                entry.LocalSha256 = ProjectSourceExportService.ComputeSha256(localPath);
                entry.Sha256 = entry.LocalSha256;
            }
        }

        progress?.Report(options.CachedRemoteFiles is { Count: > 0 }
            ? "Checking sync status…"
            : "Fetching project file list…");

        IReadOnlyList<GizmoFileRef> remoteFiles;
        if (options.CachedRemoteFiles is { Count: > 0 })
        {
            remoteFiles = options.CachedRemoteFiles;
        }
        else if (ProjectRemoteListCache.TryGet(gizmoId, out var cached))
        {
            remoteFiles = cached;
        }
        else
        {
            remoteFiles = await api.GetProjectFilesDirectAsync(
                core,
                gizmoId,
                cancellationToken,
                options.EnsureProjectPage);
            ProjectRemoteListCache.Set(gizmoId, remoteFiles);
        }

        var plan = new SourceSyncPlan
        {
            DetectedRemoteFiles = remoteFiles.ToList(),
        };

        var matchedRemoteIds = new HashSet<string>(StringComparer.Ordinal);
        var compareItems = new List<CompareWorkItem>();

        foreach (var entry in bundle.SourceManifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localPath = Path.Combine(dir, entry.RelativePath);
            var hasLocal = File.Exists(localPath);
            var localHash = hasLocal ? entry.LocalSha256 : "";

            if (PruneStaleRemoteBinding(entry, remoteFiles))
                plan.StaleBindingsCleared++;

            var remote = TryMatchRemoteFile(entry, remoteFiles);
            if (remote is not null)
            {
                matchedRemoteIds.Add(remote.FileId);
                entry.RemoteFileId = remote.FileId;
                entry.RemoteFileName = remote.Name;
            }

            if (remote is null || string.IsNullOrWhiteSpace(remote.FileId))
            {
                ApplyState(entry, SourceSyncState.LocalOnly, SourceSyncAction.PushReplace, localHash, "", entry.BaselineSha256);
                plan.Items.Add(new SourceSyncPlanItem { Entry = entry });
                continue;
            }

            if (!hasLocal)
            {
                ApplyState(entry, SourceSyncState.MissingRemote, SourceSyncAction.Skip, "", "", entry.BaselineSha256);
                plan.Items.Add(new SourceSyncPlanItem { Entry = entry });
                continue;
            }

            if (!string.IsNullOrEmpty(localHash)
                && !string.IsNullOrEmpty(entry.RemoteSha256)
                && string.Equals(localHash, entry.RemoteSha256, StringComparison.OrdinalIgnoreCase))
            {
                ClassifyThreeWay(entry, localHash, entry.RemoteSha256, entry.RemoteSha256, hasRemoteMatch: true);
                plan.Items.Add(new SourceSyncPlanItem { Entry = entry });
                continue;
            }

            if (ShouldSkipRemoteDownloadWhenBaselineMatchesLocal(localHash, entry.BaselineSha256))
            {
                var baseline = entry.BaselineSha256;
                ClassifyThreeWay(entry, localHash, baseline, baseline, hasRemoteMatch: true);
                plan.Items.Add(new SourceSyncPlanItem { Entry = entry });
                continue;
            }

            compareItems.Add(new CompareWorkItem(entry, remote, localHash, localPath));
        }

        if (compareItems.Count > 0)
        {
            progress?.Report($"Comparing file contents ({compareItems.Count})…");
            await CompareRemoteHashesParallelAsync(
                core,
                bundle,
                api,
                compareItems,
                plan,
                progress,
                cancellationToken);
        }

        foreach (var remote in remoteFiles)
        {
            if (string.IsNullOrWhiteSpace(remote.FileId) || matchedRemoteIds.Contains(remote.FileId))
                continue;

            plan.UnmatchedRemoteFiles.Add(remote);
            var remoteOnlyEntry = new SourceManifestEntry
            {
                RelativePath = remote.Name ?? remote.FileId,
                RemoteFileId = remote.FileId,
                RemoteFileName = remote.Name,
                SyncState = SourceSyncState.RemoteOnly,
                PlannedAction = SourceSyncAction.Skip,
            };
            plan.Items.Add(new SourceSyncPlanItem { Entry = remoteOnlyEntry });
        }

        ReconcileDuplicateRows(plan, dir);
        bundle.SourceManifest.LastKnownDuplicateRemotes = GetOrphanDuplicates(plan).Count;
        bundle.SourceManifest.RefreshSyncedFlag();
        return plan;
    }

    private sealed class CompareWorkItem(
        SourceManifestEntry entry,
        GizmoFileRef remote,
        string localHash,
        string localPath)
    {
        public SourceManifestEntry Entry { get; } = entry;

        public GizmoFileRef Remote { get; } = remote;

        public string LocalHash { get; } = localHash;

        public string LocalPath { get; } = localPath;
    }

    private static async Task CompareRemoteHashesParallelAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        List<CompareWorkItem> items,
        SourceSyncPlan plan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxParallelRemoteDownloads);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                progress?.Report($"Comparing {item.Entry.RelativePath}…");
                var baseline = item.Entry.BaselineSha256;
                if (string.IsNullOrEmpty(baseline))
                {
                    if (!string.IsNullOrEmpty(item.Entry.RemoteSha256))
                        baseline = item.Entry.RemoteSha256;
                    else if (!string.IsNullOrEmpty(item.LocalHash))
                        baseline = item.LocalHash;
                }

                try
                {
                    var tempDir = SyncTempDirectory(bundle);
                    Directory.CreateDirectory(tempDir);
                    var tempPath = Path.Combine(tempDir, item.Entry.RelativePath + ".remote");
                    var failFast = string.Equals(item.Remote.Location, "fs", StringComparison.OrdinalIgnoreCase);
                    await api.DownloadFileToPathAsync(
                        core,
                        item.Remote.FileId,
                        tempPath,
                        cancellationToken,
                        bundle.Metadata.LinkedProjectId,
                        item.Remote.Location,
                        failFast);
                    item.Entry.RemoteSha256 = ProjectSourceExportService.ComputeSha256(tempPath);
                    try { File.Delete(tempPath); } catch { /* ignore */ }
                }
                catch (ChatGptApiException ex) when (ChatGptProjectApiService.IsRemoteFileDownloadUnavailable(ex))
                {
                    item.Entry.RemoteSha256 = "";
                    MarkListedNotDownloadable(plan, item.Remote);
                    ProjectLinkDiagnostics.Log(
                        $"listed_not_downloadable file={item.Remote.FileId} name={item.Remote.Name}");
                }
                catch (Exception ex)
                {
                    item.Entry.RemoteSha256 = "";
                    MarkListedNotDownloadable(plan, item.Remote);
                    ProjectLinkDiagnostics.Log(
                        $"listed_not_downloadable file={item.Remote.FileId} name={item.Remote.Name} error={ex.Message}");
                }

                if (string.IsNullOrEmpty(baseline))
                {
                    if (!string.IsNullOrEmpty(item.Entry.RemoteSha256))
                        baseline = item.Entry.RemoteSha256;
                    else if (!string.IsNullOrEmpty(item.LocalHash))
                        baseline = item.LocalHash;
                }

                ClassifyThreeWay(
                    item.Entry,
                    item.LocalHash,
                    item.Entry.RemoteSha256,
                    baseline,
                    hasRemoteMatch: true);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        foreach (var item in items)
            plan.Items.Add(new SourceSyncPlanItem { Entry = item.Entry });
    }

    internal static void ReconcileDuplicateRows(SourceSyncPlan plan, string sourcesDir)
    {
        var remoteOnlyByBase = plan.Items
            .Where(i => i.Entry.SyncState == SourceSyncState.RemoteOnly)
            .GroupBy(i => NormalizeBaseName(i.Entry.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var toRemove = new List<SourceSyncPlanItem>();

        foreach (var item in plan.Items.ToList())
        {
            if (item.Entry.SyncState != SourceSyncState.LocalOnly)
                continue;

            var baseName = NormalizeBaseName(item.Entry.RelativePath);
            if (!remoteOnlyByBase.TryGetValue(baseName, out var remoteRows) || remoteRows.Count == 0)
                continue;

            var remoteRow = remoteRows[0];
            var remote = plan.DetectedRemoteFiles.FirstOrDefault(f =>
                string.Equals(f.FileId, remoteRow.Entry.RemoteFileId, StringComparison.Ordinal));

            if (remote is null)
                continue;

            item.Entry.RemoteFileId = remote.FileId;
            item.Entry.RemoteFileName = remote.Name;
            plan.UnmatchedRemoteFiles.RemoveAll(f =>
                string.Equals(f.FileId, remote.FileId, StringComparison.Ordinal));

            var localPath = Path.Combine(sourcesDir, item.Entry.RelativePath);
            var localHash = File.Exists(localPath)
                ? ProjectSourceExportService.ComputeSha256(localPath)
                : item.Entry.LocalSha256;

            if (!string.IsNullOrEmpty(localHash)
                && !string.IsNullOrEmpty(item.Entry.RemoteSha256)
                && string.Equals(localHash, item.Entry.RemoteSha256, StringComparison.OrdinalIgnoreCase))
            {
                ClassifyThreeWay(item.Entry, localHash, item.Entry.RemoteSha256, localHash, hasRemoteMatch: true);
            }
            else if (string.IsNullOrEmpty(item.Entry.RemoteSha256) && !string.IsNullOrEmpty(localHash))
            {
                ApplyState(
                    item.Entry,
                    SourceSyncState.LocalNewer,
                    SourceSyncAction.PushReplace,
                    localHash,
                    "",
                    item.Entry.BaselineSha256);
            }
            else
            {
                ClassifyThreeWay(
                    item.Entry,
                    localHash,
                    item.Entry.RemoteSha256,
                    item.Entry.BaselineSha256,
                    hasRemoteMatch: true);
            }

            toRemove.Add(remoteRow);
            remoteRows.RemoveAt(0);
            if (remoteRows.Count == 0)
                remoteOnlyByBase.Remove(baseName);
        }

        foreach (var row in toRemove)
            plan.Items.Remove(row);
    }

    internal static IReadOnlyList<GizmoFileRef> GetOrphanDuplicates(SourceSyncPlan plan)
    {
        var boundBaseNames = plan.Items
            .Where(i => i.Entry.SyncState != SourceSyncState.RemoteOnly)
            .Select(i => NormalizeBaseName(i.Entry.RelativePath))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return plan.UnmatchedRemoteFiles
            .Where(r =>
            {
                var baseName = NormalizeBaseName(r.Name ?? r.FileId);
                return boundBaseNames.Contains(baseName);
            })
            .ToList();
    }

    internal static string NormalizeBaseName(string path) =>
        Path.GetFileName(path.Trim()).Trim();

    /// <summary>
    /// Clears stored remote binding when the file id is no longer listed on the project (e.g. browser delete).
    /// </summary>
    private static void MarkListedNotDownloadable(SourceSyncPlan plan, GizmoFileRef remote)
    {
        if (string.IsNullOrWhiteSpace(remote.FileId))
            return;

        if (plan.ListedNotDownloadableFiles.Any(f =>
                string.Equals(f.FileId, remote.FileId, StringComparison.Ordinal)))
        {
            return;
        }

        plan.ListedNotDownloadableFiles.Add(remote);
    }

    internal static bool PruneStaleRemoteBinding(
        SourceManifestEntry entry,
        IReadOnlyList<GizmoFileRef> remoteFiles)
    {
        if (string.IsNullOrWhiteSpace(entry.RemoteFileId))
            return false;

        var stillListed = remoteFiles.Any(f =>
            string.Equals(f.FileId, entry.RemoteFileId, StringComparison.Ordinal));
        if (stillListed)
            return false;

        var oldId = entry.RemoteFileId;
        SourceManifestHelper.ClearEntryRemoteBinding(entry);
        ProjectLinkDiagnostics.Log(
            $"remote_binding_cleared path={entry.RelativePath} old_id={oldId}");
        return true;
    }

    internal static GizmoFileRef? TryMatchRemoteFile(
        SourceManifestEntry entry,
        IReadOnlyList<GizmoFileRef> remoteFiles)
    {
        var candidates = CollectMatchCandidates(entry, remoteFiles);
        return candidates.Count == 0 ? null : SelectBestMatch(entry, candidates);
    }

    internal static List<GizmoFileRef> CollectMatchCandidates(
        SourceManifestEntry entry,
        IReadOnlyList<GizmoFileRef> remoteFiles)
    {
        if (remoteFiles.Count == 0)
            return [];

        var candidates = new List<GizmoFileRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(GizmoFileRef? remote)
        {
            if (remote is null || string.IsNullOrWhiteSpace(remote.FileId) || !seen.Add(remote.FileId))
                return;

            candidates.Add(remote);
        }

        if (!string.IsNullOrWhiteSpace(entry.RemoteFileId))
        {
            Add(remoteFiles.FirstOrDefault(f =>
                string.Equals(f.FileId, entry.RemoteFileId, StringComparison.Ordinal)));
        }

        foreach (var remote in remoteFiles)
        {
            if (string.IsNullOrWhiteSpace(remote.Name))
                continue;

            if (string.Equals(remote.Name.Trim(), entry.RelativePath.Trim(), StringComparison.OrdinalIgnoreCase))
                Add(remote);
        }

        var entryBaseName = NormalizeBaseName(entry.RelativePath);
        if (!string.IsNullOrWhiteSpace(entryBaseName))
        {
            foreach (var remote in remoteFiles)
            {
                if (string.IsNullOrWhiteSpace(remote.Name))
                    continue;

                if (string.Equals(NormalizeBaseName(remote.Name), entryBaseName, StringComparison.OrdinalIgnoreCase))
                    Add(remote);
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.RemoteFileName))
        {
            foreach (var remote in remoteFiles)
            {
                if (string.IsNullOrWhiteSpace(remote.Name))
                    continue;

                if (string.Equals(remote.Name.Trim(), entry.RemoteFileName.Trim(), StringComparison.OrdinalIgnoreCase))
                    Add(remote);
            }
        }

        return candidates;
    }

    internal static GizmoFileRef SelectBestMatch(
        SourceManifestEntry entry,
        IReadOnlyList<GizmoFileRef> candidates)
    {
        if (candidates.Count == 1)
            return candidates[0];

        if (!string.IsNullOrWhiteSpace(entry.RemoteFileId))
        {
            var byId = candidates.FirstOrDefault(c =>
                string.Equals(c.FileId, entry.RemoteFileId, StringComparison.Ordinal));
            if (byId is not null)
                return byId;
        }

        if (!string.IsNullOrEmpty(entry.RemoteSha256))
        {
            // Prefer candidate already bound in manifest metadata when hashes were known.
        }

        return candidates[0];
    }

    internal static bool ShouldSkipRemoteDownloadWhenBaselineMatchesLocal(
        string? localHash,
        string? baselineSha256) =>
        !string.IsNullOrEmpty(localHash)
        && !string.IsNullOrEmpty(baselineSha256)
        && string.Equals(localHash, baselineSha256, StringComparison.Ordinal);

    internal static void ClassifyThreeWay(
        SourceManifestEntry entry,
        string localHash,
        string remoteHash,
        string baseline,
        bool hasRemoteMatch = false)
    {
        if (!string.IsNullOrEmpty(localHash) && localHash == remoteHash)
        {
            ApplyState(entry, SourceSyncState.InSync, SourceSyncAction.Skip, localHash, remoteHash, localHash);
            return;
        }

        if (string.IsNullOrEmpty(remoteHash) && !string.IsNullOrEmpty(localHash))
        {
            if (hasRemoteMatch || !string.IsNullOrWhiteSpace(entry.RemoteFileId))
            {
                // Remote is listed but content could not be fetched — do not schedule Pull.
                if (!string.IsNullOrEmpty(baseline) && localHash == baseline)
                {
                    ApplyState(entry, SourceSyncState.InSync, SourceSyncAction.Skip, localHash, remoteHash, baseline);
                    return;
                }

                ApplyState(entry, SourceSyncState.LocalNewer, SourceSyncAction.PushReplace, localHash, remoteHash, baseline);
                return;
            }

            ApplyState(entry, SourceSyncState.LocalOnly, SourceSyncAction.PushReplace, localHash, remoteHash, baseline);
            return;
        }

        if (string.IsNullOrEmpty(localHash) && !string.IsNullOrEmpty(remoteHash))
        {
            ApplyState(entry, SourceSyncState.RemoteNewer, SourceSyncAction.Pull, localHash, remoteHash, baseline);
            return;
        }

        var localEqBaseline = string.IsNullOrEmpty(baseline) || localHash == baseline;
        var remoteEqBaseline = string.IsNullOrEmpty(baseline) || remoteHash == baseline;

        if (localEqBaseline && !remoteEqBaseline && !string.IsNullOrEmpty(remoteHash))
        {
            ApplyState(entry, SourceSyncState.RemoteNewer, SourceSyncAction.Pull, localHash, remoteHash, baseline);
            return;
        }

        if (!localEqBaseline && remoteEqBaseline && !string.IsNullOrEmpty(localHash))
        {
            ApplyState(entry, SourceSyncState.LocalNewer, SourceSyncAction.PushReplace, localHash, remoteHash, baseline);
            return;
        }

        if (!localEqBaseline && !remoteEqBaseline && localHash != remoteHash)
        {
            ApplyState(entry, SourceSyncState.Conflict, SourceSyncAction.NeedsResolution, localHash, remoteHash, baseline);
            return;
        }

        ApplyState(entry, SourceSyncState.InSync, SourceSyncAction.Skip, localHash, remoteHash,
            string.IsNullOrEmpty(baseline) ? localHash : baseline);
    }

    private static void ApplyState(
        SourceManifestEntry entry,
        SourceSyncState state,
        SourceSyncAction action,
        string localHash,
        string remoteHash,
        string baseline)
    {
        entry.SyncState = state;
        entry.PlannedAction = action;
        if (!string.IsNullOrEmpty(localHash))
            entry.LocalSha256 = localHash;
        entry.RemoteSha256 = remoteHash;
        if (state == SourceSyncState.InSync && !string.IsNullOrEmpty(localHash))
            entry.BaselineSha256 = localHash;
        else if (string.IsNullOrEmpty(entry.BaselineSha256) && !string.IsNullOrEmpty(baseline))
            entry.BaselineSha256 = baseline;
    }

    public static SourceSyncAction ResolveAction(SourceSyncPlanItem item)
    {
        if (item.Entry.SyncState == SourceSyncState.Conflict)
        {
            return item.Resolution switch
            {
                SourceConflictResolution.KeepLocal => SourceSyncAction.PushReplace,
                SourceConflictResolution.KeepRemote => SourceSyncAction.Pull,
                SourceConflictResolution.Skip => SourceSyncAction.Skip,
                _ => SourceSyncAction.NeedsResolution,
            };
        }

        return item.Entry.PlannedAction;
    }

    public static bool IsAutoSafe(SourceSyncPlanItem item)
    {
        var action = ResolveAction(item);
        return action is SourceSyncAction.Pull or SourceSyncAction.PushReplace;
    }

    internal static IReadOnlyList<SourceSyncAction> GetAvailableActions(SourceSyncPlanItem item) =>
        item.Entry.SyncState switch
        {
            SourceSyncState.InSync => [SourceSyncAction.Skip, SourceSyncAction.Pull, SourceSyncAction.PushReplace],
            SourceSyncState.LocalNewer or SourceSyncState.LocalOnly =>
                [SourceSyncAction.Skip, SourceSyncAction.PushReplace],
            SourceSyncState.RemoteNewer => [SourceSyncAction.Skip, SourceSyncAction.Pull],
            SourceSyncState.Conflict =>
                [SourceSyncAction.Skip, SourceSyncAction.Pull, SourceSyncAction.PushReplace],
            SourceSyncState.RemoteOnly => [SourceSyncAction.Skip, SourceSyncAction.Pull],
            SourceSyncState.MissingRemote => [SourceSyncAction.Skip],
            _ => [SourceSyncAction.Skip],
        };

    internal static bool ApplyUserAction(SourceSyncPlanItem item, SourceSyncAction action)
    {
        if (item.Entry.SyncState == SourceSyncState.Conflict)
        {
            var resolution = action switch
            {
                SourceSyncAction.PushReplace => SourceConflictResolution.KeepLocal,
                SourceSyncAction.Pull => SourceConflictResolution.KeepRemote,
                SourceSyncAction.Skip => SourceConflictResolution.Skip,
                _ => SourceConflictResolution.None,
            };

            if (item.Resolution == resolution)
                return false;

            item.Resolution = resolution;
            return true;
        }

        if (!GetAvailableActions(item).Contains(action) || item.Entry.PlannedAction == action)
            return false;

        item.Resolution = SourceConflictResolution.None;
        item.Entry.PlannedAction = action;
        return true;
    }
}
