namespace ChatGPTWrapper.Core.SessionHost;

public static class SessionHostRpcMethods
{
    public const string EnsureReady = "EnsureReady";
    public const string SendMessage = "SendMessage";
    public const string Regenerate = "Regenerate";
    public const string CaptureAssistant = "CaptureAssistant";
    public const string DiscoverProjects = "DiscoverProjects";
    public const string SyncSources = "SyncSources";

    /// <summary>Host-owned play packet send (orchestrator RPC boundary).</summary>
    public const string PlaySend = "PlaySend";
}

public sealed class SessionHostRequest
{
    public string Method { get; init; } = "";

    public string? Id { get; init; }

    public Dictionary<string, object?>? Params { get; init; }
}

public sealed class SessionHostResponse
{
    public string? Id { get; init; }

    public bool Ok { get; init; }

    public object? Result { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// RPC payload for <see cref="SessionHostRpcMethods.PlaySend"/> (Phase 8 migration).
/// </summary>
public sealed class PlaySendHostRequest
{
    public Guid AdventureId { get; init; }

    public string? ComposeText { get; init; }

    public string? ArtifactHash { get; init; }
}

public sealed class PlaySendHostResponse
{
    public string Outcome { get; init; } = "";

    public string? ReasonCode { get; init; }

    public string? ConversationId { get; init; }

    public string? VerificationChannel { get; init; }
}
