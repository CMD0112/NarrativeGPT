namespace ChatGPTWrapper.ChatGptApi;

public sealed class ChatGptSessionInfo
{
    public bool IsAuthenticated { get; init; }

    public string? UserId { get; init; }

    public string? Email { get; init; }
}

public sealed class GizmoFileRef
{
    public required string FileId { get; init; }

    public required string Name { get; init; }

    public string? Location { get; init; }

    public long? Size { get; init; }

    /// <summary>True when uploaded via POST /files/library (already bound to the project).</summary>
    public bool FromLibraryUpload { get; init; }

    /// <summary>Token count from upload finalize (browser attach sends include this).</summary>
    public int? FileTokenSize { get; init; }
}

public sealed class GizmoSummary
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Instructions { get; init; }

    public IReadOnlyList<GizmoFileRef> Files { get; init; } = [];
}

public sealed class GizmoConversationRef
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>ChatGPT project settings payload (PATCH /backend-api/projects/{id}).</summary>
public sealed class ProjectSettingsDetail
{
    public required string ProjectId { get; init; }

    public required string Name { get; init; }

    public string Instructions { get; init; } = "";

    public string? Emoji { get; init; }

    public string? Theme { get; init; }
}

public sealed class ProjectSidebarSnapshotResult
{
    public bool LinkedIdFound { get; init; }

    public int LinkedFileCount { get; init; }

    public int SameTitleProjectCount { get; init; }

    public IReadOnlyList<string> SameTitleProjectIds { get; init; } = [];

    public string? Warning { get; init; }
}

public sealed class ProjectSyncPreflightResult
{
    public bool Allowed { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string> SameTitleProjectIds { get; init; } = [];

    public static ProjectSyncPreflightResult Ok() => new() { Allowed = true };

    public static ProjectSyncPreflightResult Blocked(
        string errorCode,
        string message,
        IReadOnlyList<string>? sameTitleIds = null) =>
        new()
        {
            Allowed = false,
            ErrorCode = errorCode,
            Message = message,
            SameTitleProjectIds = sameTitleIds ?? [],
        };
}
