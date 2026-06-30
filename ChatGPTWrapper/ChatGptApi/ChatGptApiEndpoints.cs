namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// ChatGPT web backend-api paths (undocumented; may change).
/// </summary>
internal static class ChatGptApiEndpoints
{
    public const string Session = "/api/auth/session";

    public const string ProjectsSidebar = "/backend-api/gizmos/snorlax/sidebar";

    public const string GizmosBootstrap = "/backend-api/gizmos/bootstrap";

    public const string ProjectUpsert = "/backend-api/gizmos/snorlax/upsert";

    public static string ProjectConversations(string gizmoId) =>
        $"/backend-api/gizmos/{Uri.EscapeDataString(gizmoId)}/conversations";

    public static string ProjectFiles(string gizmoId) =>
        $"/backend-api/gizmos/{Uri.EscapeDataString(gizmoId)}/files";

    public static string ProjectFilesList(string gizmoId) =>
        $"/backend-api/projects/{Uri.EscapeDataString(gizmoId)}/files";

    public static string GizmoDetail(string gizmoId) =>
        $"/backend-api/gizmos/{Uri.EscapeDataString(gizmoId)}";

    /// <summary>Snorlax project file attach (observed in ChatGPT web UI).</summary>
    public static string ProjectFilesAttach(string gizmoId) =>
        $"/backend-api/projects/{Uri.EscapeDataString(gizmoId)}/files";

    public const string FilesUpload = "/backend-api/files";

    /// <summary>ChatGPT web UI project source upload (multipart; auto-binds to gizmo).</summary>
    public const string FilesLibraryUpload = "/backend-api/files/library";

    public const string FilesProcessUploadStream = "/backend-api/files/process_upload_stream";

    public static string FileDownload(string fileId) =>
        $"/backend-api/files/{Uri.EscapeDataString(fileId)}";

    public static string FileDownloadWithQuery(string fileId) =>
        $"/backend-api/files/{Uri.EscapeDataString(fileId)}?download=1";

    public static string ProjectFileDownloadWithQuery(string gizmoId, string fileId) =>
        $"/backend-api/projects/{Uri.EscapeDataString(gizmoId)}/files/{Uri.EscapeDataString(fileId)}?download=1";

    public static string ProjectFileDownload(string gizmoId, string fileId) =>
        $"/backend-api/projects/{Uri.EscapeDataString(gizmoId)}/files/{Uri.EscapeDataString(fileId)}";

    public static string GizmoFileDownloadWithQuery(string gizmoId, string fileId) =>
        $"/backend-api/gizmos/{Uri.EscapeDataString(gizmoId)}/files/{Uri.EscapeDataString(fileId)}?download=1";

    public static string GizmoFileDownload(string gizmoId, string fileId) =>
        $"/backend-api/gizmos/{Uri.EscapeDataString(gizmoId)}/files/{Uri.EscapeDataString(fileId)}";

    /// <summary>
    /// Ordered download path candidates. Project-scoped paths come first when gizmoId is set or location is fs.
    /// </summary>
    internal static IReadOnlyList<string> BuildFileDownloadPathCandidates(
        string fileId,
        string? gizmoId = null,
        string? location = null)
    {
        var paths = new List<string>();
        var preferProjectFirst = !string.IsNullOrWhiteSpace(gizmoId)
                                 || string.Equals(location, "fs", StringComparison.OrdinalIgnoreCase);

        if (preferProjectFirst && !string.IsNullOrWhiteSpace(gizmoId))
        {
            paths.Add(ProjectFileDownloadWithQuery(gizmoId, fileId));
            paths.Add(ProjectFileDownload(gizmoId, fileId));
            paths.Add(GizmoFileDownloadWithQuery(gizmoId, fileId));
            paths.Add(GizmoFileDownload(gizmoId, fileId));
        }

        paths.Add(FileDownloadWithQuery(fileId));
        paths.Add(FileDownload(fileId));
        return paths;
    }

    /// <summary>
    /// Project-scoped download paths only (matches ChatGPT project UI).
    /// </summary>
    internal static IReadOnlyList<string> BuildProjectScopedDownloadPathCandidates(
        string fileId,
        string gizmoId) =>
    [
        ProjectFileDownloadWithQuery(gizmoId, fileId),
        ProjectFileDownload(gizmoId, fileId),
        GizmoFileDownloadWithQuery(gizmoId, fileId),
        GizmoFileDownload(gizmoId, fileId),
    ];

    public static string FileDelete(string fileId) =>
        $"/backend-api/files/{Uri.EscapeDataString(fileId)}";

    public static string ProjectFileDelete(string gizmoId, string fileId) =>
        $"/backend-api/gizmos/{Uri.EscapeDataString(gizmoId)}/files/{Uri.EscapeDataString(fileId)}";

    public static string ProjectFilesDelete(string gizmoId) =>
        $"/backend-api/gizmos/{Uri.EscapeDataString(gizmoId)}/files";

    public static string ProjectFilesFileDelete(string gizmoId, string fileId) =>
        $"/backend-api/projects/{Uri.EscapeDataString(gizmoId)}/files/{Uri.EscapeDataString(fileId)}";

    public static string ProjectFilesCollectionDelete(string gizmoId) =>
        $"/backend-api/projects/{Uri.EscapeDataString(gizmoId)}/files";

    /// <summary>Legacy create path; often returns 405.</summary>
    public const string ConversationsCreate = "/backend-api/conversations";

    /// <summary>Session warmup for project chat; does not allocate a conversation id.</summary>
    public const string ConversationInit = "/backend-api/conversation/init";

    public static string ConversationGet(string conversationId) =>
        $"/backend-api/conversation/{Uri.EscapeDataString(conversationId.Trim())}";

    /// <summary>Soft-delete (hide) a conversation — same path as GET, PATCH with is_visible: false.</summary>
    public static string ConversationHide(string conversationId) => ConversationGet(conversationId);

    public const string ConversationPrepare = "/backend-api/f/conversation/prepare";

    public const string ConversationSend = "/backend-api/f/conversation";
}
