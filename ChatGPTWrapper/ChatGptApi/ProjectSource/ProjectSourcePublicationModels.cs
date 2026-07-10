using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

public sealed class ProjectSourcePublicationRequest
{
    public required string GizmoId { get; init; }

    public required string RemoteFileName { get; init; }

    public required byte[] Content { get; init; }

    public required string MimeType { get; init; }

    public Guid? AdventureId { get; init; }

    public ProjectSourceUploadMethod UploadMethod { get; init; } = ProjectSourceUploadMethod.HeadlessBrowser;
}

public sealed class ProjectSourcePublicationResult
{
    public required GizmoFileRef File { get; init; }

    public required ProjectSourceBindingStrategy BindingStrategy { get; init; }

    public required int VerifiedByteCount { get; init; }

    public ProjectFilePublicationRun? Run { get; init; }
}

public enum ProjectSourceBindingStrategy
{
    /// <summary>Snorlax bind via POST project-files attach (publication last-resort API lane; sync attach primary).</summary>
    SnorlaxProjectFilesApi,

    /// <summary>Snorlax batch sync attach via incremental detail upsert fallback — never used for publication lab.</summary>
    SnorlaxDetailUpsert,

    /// <summary>Obsolete alias kept for log/test compatibility.</summary>
    [Obsolete("Use SnorlaxDetailUpsert.")]
    SnorlaxDetailUpsertFallback = SnorlaxDetailUpsert,

    /// <summary>Publication via browser library upload lane.</summary>
    SnorlaxLibraryEscalation,

    /// <summary>Publication via project knowledge DOM/CDP lane (browser-native).</summary>
    SnorlaxDomEscalation,

    /// <summary>Publication via ChatGPT backend-api Sources upload (HAR-aligned).</summary>
    SnorlaxPureApi,

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
