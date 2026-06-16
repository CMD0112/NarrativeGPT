namespace ChatGPTWrapper.ChatGptApi;

public sealed class ProjectSessionStatus
{
    public bool IsReady { get; init; }

    public bool IsAuthenticated { get; init; }

    public bool HasDeviceId { get; init; }

    public bool HasAccountId { get; init; }

    public string? UserId { get; init; }

    public string? Email { get; init; }

    public string? WebViewSource { get; init; }

    public string? Error { get; init; }
}

public sealed class ProjectDiscoveryResult
{
    public IReadOnlyList<GizmoSummary> Projects { get; init; } = [];

    public IReadOnlyList<string> StrategiesUsed { get; init; } = [];

    public string? Diagnostics { get; init; }

    public int RawItemCount { get; init; }
}

public sealed class ApiProbeResult
{
    public bool Ok { get; init; }

    public int? Status { get; init; }

    public int? ItemCount { get; init; }

    public IReadOnlyList<string> JsonKeys { get; init; } = [];

    public bool HasDeviceId { get; init; }

    public bool HasAccountId { get; init; }

    public bool Authenticated { get; init; }

    public string? Error { get; init; }
}
