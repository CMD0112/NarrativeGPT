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
    bool WasTrimmed)
{
    public bool IsStale(string currentFingerprint) =>
        !string.Equals(SettingsFingerprint, currentFingerprint, StringComparison.Ordinal);
}
