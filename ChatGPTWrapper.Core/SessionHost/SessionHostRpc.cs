namespace ChatGPTWrapper.Core.SessionHost;

public static class SessionHostRpcMethods
{
    public const string EnsureReady = "EnsureReady";
    public const string SendMessage = "SendMessage";
    public const string Regenerate = "Regenerate";
    public const string CaptureAssistant = "CaptureAssistant";
    public const string DiscoverProjects = "DiscoverProjects";
    public const string SyncSources = "SyncSources";
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
