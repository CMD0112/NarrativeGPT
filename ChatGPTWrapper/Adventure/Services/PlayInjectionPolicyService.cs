using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class PlayInjectionPolicyService
{
    public const int DefaultThinTranscriptTurns = 6;
    public const int DefaultFatTranscriptTurns = 12;

    public static void EnsureDefaults(AdventureMetadata metadata) =>
        EnsureDefaults(metadata.Settings);

    public static void EnsureDefaults(AdventureSettings settings)
    {
        settings.InjectionPolicy ??= new PlayInjectionPolicy();
        if (string.IsNullOrWhiteSpace(settings.InjectionPolicy.InjectionPresetId))
            settings.InjectionPolicy.InjectionPresetId = InjectionPresetIds.Standard;
    }

    public static PlayInjectionPolicy Resolve(AdventureSettings settings) =>
        settings.InjectionPolicy ?? new PlayInjectionPolicy();

    internal static int ResolveTranscriptMaxTurns(AdventureSettings settings, PacketMode mode)
    {
        var policy = Resolve(settings);
        if (policy.TranscriptMaxTurns > 0)
            return policy.TranscriptMaxTurns;

        return mode == PacketMode.Thin ? DefaultThinTranscriptTurns : DefaultFatTranscriptTurns;
    }

    public static void ApplyPreset(AdventureSettings settings, string presetId)
    {
        EnsureDefaults(settings);
        var preset = InjectionPresetLibrary.Find(presetId);
        if (preset is null)
            return;

        settings.MaxPacketChars = preset.MaxPacketChars;
        settings.AttachmentContextMode = preset.AttachmentContextMode;
        settings.InjectionPolicy.InjectionPresetId = preset.Id;
        settings.InjectionPolicy.IncludeSummary = preset.IncludeSummary;
        settings.InjectionPolicy.IncludePinnedMemory = preset.IncludePinnedMemory;
        settings.InjectionPolicy.IncludeTranscript = preset.IncludeTranscript;
        settings.InjectionPolicy.IncludeTriggeredCards = preset.IncludeTriggeredCards;
        settings.InjectionPolicy.TranscriptMaxTurns = preset.TranscriptMaxTurns;
        settings.InjectionPolicy.IncludeState = true;
        settings.InjectionPolicy.IncludeSourcesPointers = true;
    }

    public static void MarkCustom(AdventureSettings settings)
    {
        EnsureDefaults(settings);
        settings.InjectionPolicy.InjectionPresetId = InjectionPresetIds.Custom;
    }

    /// <summary>Returns whether the UI should block disabling this section in thin delegated mode.</summary>
    public static bool IsMandatorySection(string sectionId, bool thinDelegated) =>
        sectionId switch
        {
            "player" => true,
            "meta" => true,
            "state" when thinDelegated => true,
            "sources" when thinDelegated => true,
            _ => false,
        };

    public static bool CanDisableSection(string sectionId, bool thinDelegated) =>
        !IsMandatorySection(sectionId, thinDelegated);
}
