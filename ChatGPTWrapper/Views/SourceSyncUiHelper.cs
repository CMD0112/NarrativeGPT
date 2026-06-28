using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Views;

internal static class SourceSyncUiHelper
{
    public static int CountOrphanDuplicates(SourceSyncPlan? plan) =>
        plan is null ? 0 : ProjectFileSyncPlanner.GetOrphanDuplicates(plan).Count;

    public static async Task<bool> ConfirmAndReconcileDuplicatesAsync(
        Window owner,
        CoreWebView2 core,
        AdventureBundle bundle,
        SourceSyncPlan plan,
        ProjectSourceSyncService sync,
        ProjectFileSyncOrchestrator? fileSync,
        IProgress<string>? progress = null)
    {
        var orphans = ProjectFileSyncPlanner.GetOrphanDuplicates(plan);
        if (orphans.Count == 0)
        {
            MessageBox.Show(owner, "No duplicate orphan files to remove.", "Reconcile duplicates");
            return false;
        }

        var list = string.Join(
            Environment.NewLine,
            orphans.Select(o => $"- {o.Name ?? o.FileId} ({o.FileId})"));

        if (MessageBox.Show(
                owner,
                $"Remove {orphans.Count} duplicate remote file(s) from the ChatGPT project?\n\n{list}",
                "Reconcile duplicates",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return false;
        }

        var result = await sync.ReconcileDuplicatesAsync(core, bundle, plan, orphans, progress);
        if (!result.Success)
        {
            MessageBox.Show(
                owner,
                result.Error ?? "Reconcile failed.",
                "Reconcile duplicates",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return result.RemovedDuplicates > 0;
        }

        if (fileSync is not null)
        {
            await fileSync.BuildStatusPlanAsync(core, bundle, progress);
            AdventureStore.Save(bundle);
        }

        MessageBox.Show(
            owner,
            $"Removed {result.RemovedDuplicates} duplicate file(s).",
            "Reconcile duplicates");
        return true;
    }

    public static string FormatLinkHealth(AdventureBundle bundle, SourceSyncPlan? plan = null)
    {
        AdventureProjectBindingService.PrepareBundleForProjectLink(bundle);
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var project = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata) ?? "not linked";
        var lastSync = bundle.SourceManifest.LastRemoteSyncAt?.ToLocalTime().ToString("g") ?? "never";
        var duplicates = CountOrphanDuplicates(plan);
        var duplicateText = duplicates > 0 ? $" | {duplicates} duplicate remote(s)" : "";
        return $"Project: {project} | Last sync: {lastSync} | Packets: {readiness.ModeLabel}{duplicateText}";
    }

    public static string FormatRemoteBanner(SourceSyncPlan plan)
    {
        if (plan.SyncBlocked)
            return plan.SyncBlockReason ?? "Sync is blocked until ChatGPT sidebar duplicates are removed.";

        var parts = new List<string>();

        if (plan.DetectedRemoteFiles.Count == 0)
        {
            var pushCount = plan.Items.Count(i =>
                i.Entry.SyncState is not SourceSyncState.RemoteOnly
                && ProjectFileSyncPlanner.ResolveAction(i) == SourceSyncAction.PushReplace);
            if (pushCount > 0)
                parts.Add($"Remote project is empty — {pushCount} local file(s) ready to push.");
        }
        else
        {
            var names = string.Join(
                ", ",
                plan.DetectedRemoteFiles
                    .Select(f => string.IsNullOrWhiteSpace(f.Name) ? f.FileId : f.Name));
            parts.Add($"ChatGPT project has {plan.DetectedRemoteFiles.Count} file(s): {names}.");
            if (plan.UnmatchedRemoteFiles.Count > 0)
                parts.Add($"{plan.UnmatchedRemoteFiles.Count} not matched to local manifest paths.");
        }

        if (plan.StaleBindingsCleared > 0)
            parts.Add($"{plan.StaleBindingsCleared} stale remote binding(s) cleared.");

        if (plan.ListedNotDownloadableFiles.Count > 0)
        {
            parts.Add(
                $"{plan.ListedNotDownloadableFiles.Count} remote file(s) listed but not downloadable — re-push recommended.");
        }

        var orphanCount = CountOrphanDuplicates(plan);
        if (orphanCount > 0)
            parts.Add($"{orphanCount} duplicate orphan(s) can be removed with Reconcile duplicates.");

        return string.Join(" ", parts);
    }

    public static bool ConfirmClearRemoteBindings(Window owner)
    {
        return MessageBox.Show(
                   owner,
                   "Clear all stored remote file bindings for this adventure?\n\n"
                   + "Local sources/ files are unchanged. Use Refresh plan, then Apply all to re-upload.",
                   "Clear remote bindings",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public static void ClearRemoteBindings(AdventureBundle bundle)
    {
        SourceManifestHelper.ClearRemoteBindings(bundle.SourceManifest);
        AdventureStore.Save(bundle);
    }

    public static void ShowApplyWarnings(Window owner, ProjectSourceSyncResult result)
    {
        if (result.Warnings.Count == 0)
            return;

        MessageBox.Show(
            owner,
            string.Join(Environment.NewLine, result.Warnings),
            "Sync warnings",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
