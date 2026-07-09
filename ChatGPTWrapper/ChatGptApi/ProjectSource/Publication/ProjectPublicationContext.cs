using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

public sealed class ProjectPublicationContext
{
    public required CoreWebView2 Core { get; init; }

    public required ProjectSourcePublicationRequest Request { get; init; }

    public required ProjectFilePublicationRun Run { get; init; }

    public required ChatGptProjectApiService Api { get; init; }

    public IProgress<string>? Progress { get; init; }

    public CancellationToken CancellationToken { get; init; }
}

public sealed class LaneAttemptResult
{
    public bool HasCandidate { get; init; }

    public GizmoFileRef? File { get; init; }

    public ProjectSourceBindingStrategy BindingStrategy { get; init; }

    public string? Error { get; init; }

    public bool? ListConfirmObserved { get; init; }

    public static LaneAttemptResult NoCandidate(string? error = null) =>
        new() { HasCandidate = false, Error = error };

    public static LaneAttemptResult Candidate(
        GizmoFileRef file,
        ProjectSourceBindingStrategy strategy,
        bool? listConfirm = null) =>
        new()
        {
            HasCandidate = true,
            File = file,
            BindingStrategy = strategy,
            ListConfirmObserved = listConfirm,
        };
}
