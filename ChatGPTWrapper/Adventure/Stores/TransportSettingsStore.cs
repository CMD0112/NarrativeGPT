using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.Adventure.Stores;

/// <summary>
/// Owns utility transport / lane policy settings. Write-through from Utility jobs tab;
/// read-merge-write on commit so stale bundles cannot clobber disk.
/// </summary>
internal static class TransportSettingsStore
{
    public static void ApplyToSettings(AdventureSettings target, AdventureSettings source)
    {
        target.HideInlineUtilityDuringPlay = source.HideInlineUtilityDuringPlay;
        target.ShowInlineUtilityTraffic = source.ShowInlineUtilityTraffic;
        target.PlayUtilityInjectionMode = source.PlayUtilityInjectionMode;
        target.MaxUtilitySectionsPerSend = source.MaxUtilitySectionsPerSend;
        target.UtilityExecutionPolicy = source.UtilityExecutionPolicy;
        target.AutoSpillToWorker = source.AutoSpillToWorker;
        target.UseEphemeralUtilityWorkerChat = source.UseEphemeralUtilityWorkerChat;
        target.MaxParallelUtilityWorkerJobs = source.MaxParallelUtilityWorkerJobs;
        target.ForceUtilityWorkerDomAttach = source.ForceUtilityWorkerDomAttach;

        target.LocalUtilityInference ??= new LocalUtilityInferenceSettings();
        var sourceLocal = source.LocalUtilityInference ?? new LocalUtilityInferenceSettings();
        target.LocalUtilityInference.Enabled = sourceLocal.Enabled;
        target.LocalUtilityInference.DualRun = sourceLocal.DualRun;
        target.LocalUtilityInference.BaseUrl = sourceLocal.BaseUrl;
        target.LocalUtilityInference.Model = sourceLocal.Model;
    }

    public static void ApplyToBundle(AdventureBundle target, AdventureSettings source) =>
        ApplyToSettings(target.Metadata.Settings, source);

    /// <summary>Refreshes transport / local-inference fields from disk onto a live bundle.</summary>
    public static void SyncFromDisk(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var meta = AdventureStore.ReadMetadataFromDisk(bundle.Metadata.Id);
        if (meta?.Settings is null)
            return;

        ApplyToBundle(bundle, meta.Settings);
    }

    /// <summary>Persists transport fields from the working bundle to disk.</summary>
    public static void Commit(AdventureBundle workingCopy, string caller = nameof(TransportSettingsStore))
    {
        ArgumentNullException.ThrowIfNull(workingCopy);

        var disk = AdventureStore.ReadBundleDocumentsFromDisk(workingCopy.Metadata.Id);
        if (disk is null)
            return;

        UtilityStoryContextSettingsService.EnsureDefaults(disk.Metadata);
        PlayInjectionPolicyService.EnsureDefaults(disk.Metadata);

        var policyBefore = disk.Metadata.Settings.UtilityExecutionPolicy;
        ApplyToSettings(disk.Metadata.Settings, workingCopy.Metadata.Settings);

        using (SettingsMergeService.BeginExplicitTransportWrite())
        {
            AdventureStore.Save(disk, AdventureSaveScope.Metadata);
        }

        ApplyToSettings(workingCopy.Metadata.Settings, disk.Metadata.Settings);

        LogSettingsWrite(
            workingCopy.Metadata.Id,
            caller,
            policyBefore,
            disk.Metadata.Settings.UtilityExecutionPolicy);

        AdventureSettingsCommitNotifier.Notify(workingCopy.Metadata.Id);
    }

    internal static void LogSettingsWrite(
        Guid adventureId,
        string caller,
        UtilityExecutionPolicy policyBefore,
        UtilityExecutionPolicy policyAfter)
    {
        DiagnosticsLog.Write(
            DiagnosticsChannel.Program,
            DiagnosticsLevel.Info,
            "settings.write",
            $"settings.write caller={caller} policy={policyAfter}",
            adventureId: adventureId,
            category: "settings",
            source: caller,
            data: new
            {
                caller,
                policyBefore = policyBefore.ToString(),
                policyAfter = policyAfter.ToString(),
            });
    }
}
