using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record InjectionPresetSpec(
    string Id,
    string DisplayName,
    string Description,
    int MaxPacketChars,
    int TranscriptMaxTurns,
    bool IncludeSummary,
    bool IncludePinnedMemory,
    bool IncludeTranscript,
    bool IncludeTriggeredCards,
    AttachmentContextMode AttachmentContextMode);

public static class InjectionPresetLibrary
{
    public static IReadOnlyList<InjectionPresetSpec> All { get; } =
    [
        new(
            InjectionPresetIds.Compact,
            "Compact",
            "Smaller packets — short transcript tail, reduced lore fan-out.",
            MaxPacketChars: 12000,
            TranscriptMaxTurns: 2,
            IncludeSummary: true,
            IncludePinnedMemory: true,
            IncludeTranscript: true,
            IncludeTriggeredCards: true,
            AttachmentContextMode: AttachmentContextMode.Auto),
        new(
            InjectionPresetIds.Standard,
            "Standard",
            "Balanced defaults for most adventures.",
            MaxPacketChars: 28000,
            TranscriptMaxTurns: 0,
            IncludeSummary: true,
            IncludePinnedMemory: true,
            IncludeTranscript: true,
            IncludeTriggeredCards: true,
            AttachmentContextMode: AttachmentContextMode.Auto),
        new(
            InjectionPresetIds.Full,
            "Full",
            "Maximum context — longer transcript and higher char budget.",
            MaxPacketChars: 40000,
            TranscriptMaxTurns: 12,
            IncludeSummary: true,
            IncludePinnedMemory: true,
            IncludeTranscript: true,
            IncludeTriggeredCards: true,
            AttachmentContextMode: AttachmentContextMode.Full),
    ];

    public static InjectionPresetSpec? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static InjectionPresetSpec Standard =>
        Find(InjectionPresetIds.Standard)!;
}
