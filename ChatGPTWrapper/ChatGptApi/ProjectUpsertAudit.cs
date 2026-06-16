using System.IO;
using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

internal enum ProjectUpsertIntent
{
    Create,
    Update,
    AttachFiles,
}

internal enum ProjectUpsertOutcome
{
    Created,
    Updated,
    IdMismatch,
    Failed,
    Unresolved,
}

internal sealed class AttachUpsertAuditContext
{
    public string? Location { get; init; }

    public bool DetailBody { get; init; }

    public int? MergedCount { get; init; }

    public string? AttachFileName { get; init; }

    public string? RequestBody { get; init; }
}

internal static class ProjectAttachAudit
{
    private static readonly object Gate = new();

    public static string AuditPath => Path.Combine(AppDirectories.Root, "project-attach-audit.jsonl");

    public static void RecordProjectFilesAttachAttempt(
        string gizmoId,
        string fileName,
        string? fileId,
        object requestBody,
        int responseStatus,
        string? responseBody)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var requestJson = JsonSerializer.Serialize(requestBody);
            var runId = ProjectSyncTrace.ActiveRunId;
            var line = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                runId,
                runIdShort = runId?.ToString("N")[..8],
                path = "project_files_attach",
                gizmoId,
                fileName,
                fileId,
                requestBody = ProjectUpsertAudit.Truncate(requestJson, 800),
                responseStatus,
                responseBody = ProjectUpsertAudit.Truncate(responseBody, 800),
            });

            lock (Gate)
                File.AppendAllText(AuditPath, line + Environment.NewLine);

            ProjectLinkDiagnostics.Log(
                $"ProjectFilesAttach audit file={fileName} status={responseStatus} gizmo={gizmoId} "
                + $"response={ProjectUpsertAudit.Truncate(responseBody, 200) ?? "(empty)"}");

            if (responseStatus is >= 400 and < 600)
            {
                ProjectSyncTrace.Event(
                    ProjectSyncTraceEvents.ProjectFilesAttachFailed,
                    SyncTraceCategory.ProjectFiles,
                    SyncTraceLevel.Error,
                    $"ProjectFiles attach failed file={fileName} status={responseStatus}",
                    phase: SyncTracePhase.Attach,
                    data: new
                    {
                        fileName,
                        fileId,
                        httpStatus = responseStatus,
                        responseBody = ProjectUpsertAudit.Truncate(responseBody, 800),
                        attachAuditPath = AuditPath,
                    });
            }
        }
        catch
        {
            /* ignore */
        }
    }
}

internal static class ProjectUpsertAudit
{
    private static readonly object Gate = new();

    public static string AuditPath => Path.Combine(AppDirectories.Root, "project-upsert-audit.jsonl");

    public static ProjectUpsertOutcome ClassifyOutcome(
        ProjectUpsertIntent intent,
        string? requestGizmoId,
        string? responseGizmoId)
    {
        if (string.IsNullOrWhiteSpace(responseGizmoId))
            return ProjectUpsertOutcome.Unresolved;

        if (intent == ProjectUpsertIntent.Create)
        {
            return string.IsNullOrWhiteSpace(requestGizmoId)
                ? ProjectUpsertOutcome.Created
                : ProjectUpsertOutcome.IdMismatch;
        }

        if (string.IsNullOrWhiteSpace(requestGizmoId))
            return ProjectUpsertOutcome.Created;

        return ChatGptUrls.GizmoIdsEqual(requestGizmoId, responseGizmoId)
            ? ProjectUpsertOutcome.Updated
            : ProjectUpsertOutcome.IdMismatch;
    }

    public static void Record(
        ProjectUpsertIntent intent,
        string caller,
        Guid? adventureId,
        string? requestGizmoId,
        string? requestTitle,
        int fileCount,
        int? responseStatus,
        string? responseGizmoId,
        string? responseTitle,
        ProjectUpsertOutcome outcome,
        string? responseBody = null,
        AttachUpsertAuditContext? attach = null)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var runId = ProjectSyncTrace.ActiveRunId;
            var line = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                runId,
                runIdShort = runId?.ToString("N")[..8],
                intent = intent.ToString().ToLowerInvariant(),
                caller,
                adventureId = adventureId?.ToString(),
                requestGizmoId,
                requestHasId = !string.IsNullOrWhiteSpace(requestGizmoId),
                requestTitle,
                fileCount,
                responseStatus,
                responseGizmoId,
                responseTitle,
                outcome = outcome.ToString().ToLowerInvariant(),
                responseBody = Truncate(responseBody),
                requestBody = Truncate(attach?.RequestBody, 1200),
                attachLocation = attach?.Location,
                detailBody = attach?.DetailBody,
                mergedCount = attach?.MergedCount,
                attachFileName = attach?.AttachFileName,
            });

            lock (Gate)
                File.AppendAllText(AuditPath, line + Environment.NewLine);

            var attachSuffix = attach is null
                ? ""
                : $" location={attach.Location ?? "(none)"} detailBody={attach.DetailBody} merged={attach.MergedCount?.ToString() ?? "(none)"}";
            ProjectLinkDiagnostics.Log(
                $"Upsert audit intent={intent.ToString().ToLowerInvariant()} caller={caller} "
                + $"requestId={requestGizmoId ?? "(none)"} responseId={responseGizmoId ?? "(none)"} "
                + $"outcome={outcome.ToString().ToLowerInvariant()} files={fileCount}{attachSuffix}");

            if (outcome == ProjectUpsertOutcome.IdMismatch)
            {
                ProjectSyncTrace.Event(
                    ProjectSyncTraceEvents.UpsertIdMismatch,
                    SyncTraceCategory.Upsert,
                    SyncTraceLevel.Error,
                    $"Upsert id mismatch for {attach?.AttachFileName ?? "(batch)"}",
                    phase: SyncTracePhase.Attach,
                    data: new
                    {
                        fileName = attach?.AttachFileName,
                        fileCount,
                        requestGizmoId,
                        responseGizmoId,
                        httpStatus = responseStatus,
                        attachLocation = attach?.Location,
                        mergedCount = attach?.MergedCount,
                        detailBody = attach?.DetailBody,
                        requestBody = Truncate(attach?.RequestBody, 300),
                        responseBody = Truncate(responseBody, 300),
                        upsertAuditPath = AuditPath,
                    });
            }
            else if (outcome == ProjectUpsertOutcome.Updated && intent == ProjectUpsertIntent.AttachFiles)
            {
                ProjectSyncTrace.Event(
                    ProjectSyncTraceEvents.UpsertUpdated,
                    SyncTraceCategory.Upsert,
                    SyncTraceLevel.Info,
                    $"Upsert attach updated for {attach?.AttachFileName ?? "(batch)"}",
                    phase: SyncTracePhase.Attach,
                    data: new
                    {
                        fileName = attach?.AttachFileName,
                        requestGizmoId,
                        responseGizmoId,
                        attachLocation = attach?.Location,
                        mergedCount = attach?.MergedCount,
                        detailBody = attach?.DetailBody,
                    });
            }
        }
        catch
        {
            /* ignore */
        }
    }

    internal static bool UpsertBodyIncludesId(string? gizmoId) =>
        !string.IsNullOrWhiteSpace(gizmoId);

    internal static string? Truncate(string? text, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    internal static IReadOnlyList<string> ReadRecentIdMismatchForkIds(string linkedGizmoId, TimeSpan? within = null)
    {
        within ??= TimeSpan.FromHours(24);
        return ReadRecentIdMismatchForkIdsFromFile(AuditPath, linkedGizmoId, DateTimeOffset.UtcNow - within.Value);
    }

    internal static IReadOnlyList<string> ReadRecentIdMismatchForkIdsFromFile(
        string auditPath,
        string linkedGizmoId,
        DateTimeOffset cutoff)
    {
        var forkIds = new List<string>();

        if (!File.Exists(auditPath))
            return forkIds;

        try
        {
            foreach (var line in File.ReadLines(auditPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("at", out var atEl)
                    || !DateTimeOffset.TryParse(atEl.GetString(), out var at)
                    || at < cutoff)
                {
                    continue;
                }

                if (!root.TryGetProperty("outcome", out var outcomeEl)
                    || !string.Equals(outcomeEl.GetString(), "idmismatch", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!root.TryGetProperty("requestGizmoId", out var requestEl))
                    continue;

                var requestId = requestEl.GetString();
                if (string.IsNullOrWhiteSpace(requestId)
                    || !ChatGptUrls.GizmoIdsEqual(requestId, linkedGizmoId))
                {
                    continue;
                }

                if (root.TryGetProperty("responseGizmoId", out var responseEl))
                {
                    var responseId = responseEl.GetString();
                    if (!string.IsNullOrWhiteSpace(responseId)
                        && !ChatGptUrls.GizmoIdsEqual(responseId, linkedGizmoId))
                    {
                        forkIds.Add(responseId);
                    }
                }
            }
        }
        catch
        {
            /* ignore corrupt audit lines */
        }

        return forkIds.Distinct(StringComparer.Ordinal).ToList();
    }
}
