namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

public sealed class ProjectPublicationAttempt
{
    public required ProjectPublicationLaneId Lane { get; init; }

    public required ProjectSourcePublicationPhase Phase { get; init; }

    public string? FileId { get; init; }

    public required ProjectPublicationAttemptOutcome Outcome { get; set; }

    public long LatencyMs { get; init; }

    public string? Error { get; set; }

    public bool? ListConfirmObserved { get; set; }
}

public sealed class ProjectFilePublicationRun
{
    public required Guid RunId { get; init; }

    public required string GizmoId { get; init; }

    public required string RemoteFileName { get; init; }

    public required string LocalSha256 { get; init; }

    public required HashSet<string> BaselineRemoteIds { get; init; }

    public required List<ProjectPublicationAttempt> Attempts { get; init; }

    public required ProjectPublicationOutcome Outcome { get; set; }

    public ProjectPublicationProfile Profile { get; init; }

    public List<string> DeferredGhostFileIds { get; init; } = [];

    /// <summary>
    /// API uploads that completed attach/bind; never ghost-deleted even when byte verify is pending.
    /// </summary>
    public HashSet<string> ProtectedUploadFileIds { get; init; } =
        new(StringComparer.Ordinal);
}
