using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Prevents stale in-memory bundles from clobbering transport / lane policy on metadata saves.
/// Explicit transport and play-settings commits bypass the merge.
/// </summary>
internal static class SettingsMergeService
{
    [ThreadStatic]
    private static bool _explicitTransportWrite;

    public static IDisposable BeginExplicitTransportWrite()
    {
        _explicitTransportWrite = true;
        return new ResetOnDispose(() => _explicitTransportWrite = false);
    }

    public static bool IsExplicitTransportWrite => _explicitTransportWrite;

    /// <summary>
    /// When saving metadata from a stale bundle, preserve transport fields already on disk.
    /// </summary>
    public static void MergeTransportOnMetadataSave(AdventureSettings incoming, Guid adventureId, string caller)
    {
        if (_explicitTransportWrite)
            return;

        var onDisk = AdventureStore.ReadMetadataFromDisk(adventureId);
        if (onDisk?.Settings is null)
            return;

        var disk = onDisk.Settings;
        if (TransportSettingsEqual(incoming, disk))
            return;

        var incomingPolicy = incoming.UtilityExecutionPolicy;
        var diskPolicy = disk.UtilityExecutionPolicy;

        DiagnosticsLog.Write(
            DiagnosticsChannel.Program,
            DiagnosticsLevel.Info,
            "settings.clobber_prevented",
            $"settings.clobber_prevented caller={caller} incoming={incomingPolicy} disk={diskPolicy}",
            adventureId: adventureId,
            category: "settings",
            source: caller,
            data: new
            {
                caller,
                incomingPolicy = incomingPolicy.ToString(),
                diskPolicy = diskPolicy.ToString(),
            });

        TransportSettingsStore.ApplyToSettings(incoming, disk);
    }

    private static bool TransportSettingsEqual(AdventureSettings left, AdventureSettings right) =>
        left.HideInlineUtilityDuringPlay == right.HideInlineUtilityDuringPlay
        && left.ShowInlineUtilityTraffic == right.ShowInlineUtilityTraffic
        && left.PlayUtilityInjectionMode == right.PlayUtilityInjectionMode
        && left.MaxUtilitySectionsPerSend == right.MaxUtilitySectionsPerSend
        && left.UtilityExecutionPolicy == right.UtilityExecutionPolicy
        && left.AutoSpillToWorker == right.AutoSpillToWorker;

    private sealed class ResetOnDispose(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
