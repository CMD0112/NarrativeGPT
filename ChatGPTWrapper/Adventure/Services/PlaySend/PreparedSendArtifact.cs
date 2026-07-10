using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Immutable merged packet contract for preview and send. See play-send-orchestration-adr.md (I2).
/// </summary>
internal sealed record PreparedSendArtifact(
    string PlayerLine,
    string MergedText,
    string Hash,
    string SettingsFingerprint,
    int PriorThreadUserMessageCount,
    DateTimeOffset PreparedAt,
    bool WasTrimmed,
    PacketProfile Profile,
    PacketDelegationMode DelegationMode,
    AttachmentSendMode AttachmentSendMode,
    IReadOnlyList<InjectionSection> Sections,
    IReadOnlyList<TrimmedSection> Trimmed,
    string ContextText,
    bool HasUtilityInjection,
    int UtilitySectionCount,
    IReadOnlyList<ContextPointer> BaselinePointers,
    IReadOnlyList<ContextPointer> ThisTurnPointers)
{
    public bool IsStale(string currentFingerprint) =>
        !string.Equals(SettingsFingerprint, currentFingerprint, StringComparison.Ordinal);
}
