using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Single entry point for play-thread rotation packets (fresh start + handoff).
/// Always reloads on-disk JSON and reconciles source manifest sections before building.
/// </summary>
internal static class PlayThreadPacketService
{
    internal sealed class RotationPacketResult
    {
        public required string Packet { get; init; }

        public PlayHandoffCheckpoint? Checkpoint { get; init; }
    }

    /// <summary>Reload adventure JSON and reconcile <c>sources/*</c> into the manifest.</summary>
    public static AdventureBundle? ReloadFresh(Guid adventureId) =>
        AdventureStore.Load(adventureId);

    /// <summary>Build a start packet from the latest on-disk adventure state.</summary>
    public static string BuildStartPacket(Guid adventureId)
    {
        var bundle = ReloadFresh(adventureId);
        return bundle is null ? "" : AdventureBootstrapService.BuildStartPacket(bundle);
    }

    /// <summary>Build a start packet from the latest on-disk adventure state.</summary>
    public static string BuildStartPacket(AdventureBundle bundle) =>
        BuildStartPacket(bundle.Metadata.Id);

    /// <summary>Build a handoff packet using a fresh snapshot and current on-disk sources/JSON.</summary>
    public static string BuildHandoffPacket(AdventureBundle bundle, PlayHandoffOptions? options = null)
    {
        var fresh = ReloadFresh(bundle.Metadata.Id);
        if (fresh is null)
            return "";

        var snapshot = PlayHandoffService.CaptureSnapshot(fresh);
        return PlayHandoffService.BuildHandoffPacket(
            fresh,
            snapshot,
            options ?? new PlayHandoffOptions());
    }

    /// <summary>
    /// Builds the clipboard packet for play-thread rotation. Captures handoff snapshot at call time
    /// (before <see cref="PlayThreadRotationService.ReleasePlayThread"/>).
    /// </summary>
    public static RotationPacketResult BuildRotationPacket(
        AdventureBundle bundle,
        PlayThreadStartRequest? request,
        PlayThreadStartKind kind)
    {
        var fresh = ReloadFresh(bundle.Metadata.Id) ?? bundle;
        request ??= new PlayThreadStartRequest();

        if (kind == PlayThreadStartKind.FreshStart)
        {
            return new RotationPacketResult
            {
                Packet = AdventureBootstrapService.BuildStartPacket(fresh),
            };
        }

        var options = request.HandoffOptions ?? new PlayHandoffOptions();
        var snapshot = PlayHandoffService.CaptureSnapshot(fresh);
        var packet = PlayHandoffService.BuildHandoffPacket(fresh, snapshot, options);
        var checkpoint = PlayHandoffService.BuildCheckpoint(fresh, snapshot, options);
        checkpoint.HandoffPacket = packet;

        return new RotationPacketResult
        {
            Packet = packet,
            Checkpoint = checkpoint,
        };
    }
}
