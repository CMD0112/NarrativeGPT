using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.Adventure.Stores;

/// <summary>
/// Owns utility transport / lane policy settings. Write-through from AI Tools;
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
    }

    public static void ApplyToBundle(AdventureBundle target, AdventureSettings source) =>
        ApplyToSettings(target.Metadata.Settings, source);

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
