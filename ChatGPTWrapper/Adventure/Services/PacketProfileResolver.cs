using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum PacketProfile
{
    /// <summary>Linked Project with published sources — pointer-first delegation.</summary>
    SourceDelegated,

    /// <summary>No linked Project — opening + session deltas only.</summary>
    MinimalLocal,

    /// <summary>Full inline lore — explicit escape hatch or user chose to proceed unpublished.</summary>
    InlineFallback,
}

internal static class PacketProfileResolver
{
    public static PacketProfile Resolve(AdventureBundle bundle, bool userChoseInlineFallback = false)
    {
        if (bundle.Metadata.Settings.ForceInlineLore)
            return PacketProfile.InlineFallback;

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        if (readiness.CanDelegateStaticContent)
            return PacketProfile.SourceDelegated;

        if (userChoseInlineFallback && readiness.HasLinkedProject)
            return PacketProfile.InlineFallback;

        if (!readiness.HasLinkedProject)
            return PacketProfile.MinimalLocal;

        // Linked but sources not published — pointer packet with "sources not ready" notice.
        return PacketProfile.SourceDelegated;
    }

    public static PacketMode ToPacketMode(PacketProfile profile) =>
        profile == PacketProfile.InlineFallback ? PacketMode.Fat : PacketMode.Thin;

    public static string ProfileMetaMode(PacketProfile profile) =>
        profile switch
        {
            PacketProfile.SourceDelegated => "delegated",
            PacketProfile.MinimalLocal => "minimal",
            PacketProfile.InlineFallback => "inline",
            _ => "delegated",
        };

    public static string DisplayLabel(PacketProfile profile, ProjectSourceReadiness readiness) =>
        profile switch
        {
            PacketProfile.SourceDelegated when readiness.CanDelegateStaticContent => "Source-delegated",
            PacketProfile.SourceDelegated => "Publish sources to enable delegation",
            PacketProfile.MinimalLocal => "Minimal local",
            PacketProfile.InlineFallback => "Inline fallback",
            _ => profile.ToString(),
        };

    public static int ResolveMaxChars(PacketProfile profile, AdventureSettings settings) =>
        profile == PacketProfile.InlineFallback
            ? Math.Max(4000, settings.MaxPacketChars)
            : Math.Max(4000, Math.Min(settings.MaxPacketChars, 8000));
}
