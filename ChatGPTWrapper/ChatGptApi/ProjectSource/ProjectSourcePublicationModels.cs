namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

public sealed class ProjectSourcePublicationRequest
{
    public required string GizmoId { get; init; }

    public required string RemoteFileName { get; init; }

    public required byte[] Content { get; init; }

    public required string MimeType { get; init; }

    public Guid? AdventureId { get; init; }
}

public sealed class ProjectSourcePublicationResult
{
    public required GizmoFileRef File { get; init; }

    public required ProjectSourceBindingStrategy BindingStrategy { get; init; }

    public required int VerifiedByteCount { get; init; }
}

public enum ProjectSourceBindingStrategy
{
    /// <summary>Snorlax bind via POST project-files attach (sync attach only; not used for publication).</summary>
    SnorlaxProjectFilesApi,

    /// <summary>Snorlax publication bind via incremental detail upsert (primary).</summary>
    SnorlaxDetailUpsert,

    /// <summary>Obsolete alias kept for log/test compatibility.</summary>
    [Obsolete("Use SnorlaxDetailUpsert.")]
    SnorlaxDetailUpsertFallback = SnorlaxDetailUpsert,

    /// <summary>Publication escalated to browser library upload after register+upsert verify failed.</summary>
    SnorlaxLibraryEscalation,

    /// <summary>Publication escalated to project knowledge DOM/CDP upload after API lanes failed.</summary>
    SnorlaxDomEscalation,

    /// <summary>Pre-Snorlax project merge upsert attach.</summary>
    LegacyUpsert,
}

public enum ProjectSourceFileKind
{
    Markdown,
    PlainText,
    Json,
    Pdf,
    Image,
    Binary,
}
