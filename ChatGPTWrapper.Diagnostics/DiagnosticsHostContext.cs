namespace ChatGPTWrapper.Diagnostics;

/// <summary>
/// Host-specific metadata and optional legacy log paths (WPF registers; WinUI may omit).
/// </summary>
internal static class DiagnosticsHostContext
{
    public static Func<string>? GetAppVersion { get; set; }

    public static DiagnosticsLegacyLogs? LegacyLogs { get; set; }

    public static Action<string>? MirrorToLinkProjectLog { get; set; }
}

internal sealed class DiagnosticsLegacyLogs
{
    public string? LinkProject { get; init; }

    public string? SyncTrace { get; init; }

    public string? DiscoveryTrace { get; init; }
}

internal sealed record PlaySendRunContext(Guid? RunId, string? RunIdShort, Guid? AdventureId);

internal static class DiagnosticsPlaySendContext
{
    public static Func<PlaySendRunContext?>? GetActiveRun { get; set; }
}
