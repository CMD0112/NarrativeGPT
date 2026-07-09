using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Shared kernel for utility jobs that publish inputs to Project sources, reference them by filename,
/// and scrape delimited file output from assistant replies (CMD-441 / CMD-443).
/// </summary>
public static class UtilitySourceFileIoService
{
    public const string DiagnosticJobId = "source-io-e2e";

    public static string BuildDiagnosticRemotePath(string runToken) =>
        UtilitySourceFileNaming.BuildDiagnosticInputRemotePath(DiagnosticJobId, runToken, "diagnostic.md");

    private static readonly Regex DelimitedFileBlockRegex = new(
        @"---\s*begin\s+(.+?)\s*---\s*(?:\r?\n)?([\s\S]*?)(?:\r?\n)?---\s*end\s+\1\s*---",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string BuildSourceRetrieveLine(string remoteSourcesPath) =>
        $"Retrieve from `{NormalizeSourcesPath(remoteSourcesPath)}` before applying this job.";

    public static string BuildTaskScopedPointerLine(string remoteSourcesPath, string? taskHint = null)
    {
        var path = NormalizeSourcesPath(remoteSourcesPath);
        return string.IsNullOrWhiteSpace(taskHint)
            ? $"- Retrieve from {path}"
            : $"- Retrieve from {path} — {taskHint}";
    }

    public static string BuildDelimitedOutputDeliveryBlock(
        string outputFileName,
        string? remoteMirrorPath = null,
        string? schemaNotes = null)
    {
        var mirrorNote = string.IsNullOrWhiteSpace(remoteMirrorPath)
            ? ""
            : $"\n(Project source mirror name is `{NormalizeSourcesPath(remoteMirrorPath)}`.)";

        var notes = string.IsNullOrWhiteSpace(schemaNotes)
            ? ""
            : $"\n\n**Schema notes:**\n{schemaNotes.Trim()}";

        return $"""
            === DELIVERABLE — {outputFileName} ===
            Produce one complete file after applying this job.

            **Filename rule (strict):** inline block must use `{outputFileName}` exactly.{mirrorNote}

            **Required response shape:**
            --- begin {outputFileName} ---
            (full file text)
            --- end {outputFileName} ---

            **CRITICAL:** The wrapper reads your reply text, not attachments alone.
            You MUST include the inline begin/end block in the message body.
            Downloadable exports are optional; the inline block is mandatory.{notes}
            """;
    }

    public static bool HasCompleteDelimitedDelivery(string responseText, string fileName) =>
        !string.IsNullOrWhiteSpace(TryExtractDelimitedBlock(responseText, fileName));

    public static string? TryExtractDelimitedBlock(string responseText, string fileName)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        foreach (Match match in DelimitedFileBlockRegex.Matches(responseText))
        {
            var blockName = match.Groups[1].Value.Trim();
            if (!BlockNameMatchesFile(blockName, fileName))
                continue;

            var content = StripOptionalCodeFence(match.Groups[2].Value);
            if (!string.IsNullOrWhiteSpace(content))
                return content;
        }

        return null;
    }

    public static IReadOnlyList<DelimitedFileBlock> ExtractAllDelimitedBlocks(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return [];

        var blocks = new List<DelimitedFileBlock>();
        foreach (Match match in DelimitedFileBlockRegex.Matches(responseText))
        {
            var fileName = match.Groups[1].Value.Trim();
            var content = StripOptionalCodeFence(match.Groups[2].Value);
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(content))
                continue;

            blocks.Add(new DelimitedFileBlock(fileName, content));
        }

        return blocks;
    }

    public static async Task<UtilitySourcePublishResult> PublishBytesToProjectAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        string gizmoId,
        string remoteSourcesPath,
        byte[] content,
        string mimeType = "text/markdown",
        AdventureBundle? bundle = null,
        ProjectSourceUploadMethod? uploadMethodOverride = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gizmoId))
            return UtilitySourcePublishResult.Failed("missing_gizmo_id");

        if (content is not { Length: > 0 })
            return UtilitySourcePublishResult.Failed("empty_content");

        remoteSourcesPath = ProjectSourceUploadService.NormalizeRemoteFileName(remoteSourcesPath);
        var uploadMethod = ProjectSourceUploadMethodResolver.Resolve(bundle, uploadMethodOverride);
        progress?.Report(uploadMethod switch
        {
            ProjectSourceUploadMethod.PureApi => $"Publishing {remoteSourcesPath} via pure API…",
            _ => $"Publishing {remoteSourcesPath} via headless Chrome…",
        });

        try
        {
            var publish = await api.SourcePublication.PublishAsync(
                core,
                new ProjectSourcePublicationRequest
                {
                    GizmoId = gizmoId,
                    RemoteFileName = remoteSourcesPath,
                    Content = content,
                    MimeType = mimeType,
                    AdventureId = bundle?.Metadata.Id,
                    UploadMethod = uploadMethod,
                },
                progress,
                cancellationToken);

            return publish.Run?.Outcome == ProjectPublicationOutcome.Verified
                ? UtilitySourcePublishResult.Verified(publish.File, publish.VerifiedByteCount)
                : UtilitySourcePublishResult.Failed(
                    $"source_publish_{publish.Run?.Outcome.ToString().ToLowerInvariant() ?? "unknown"}");
        }
        catch (Exception ex)
        {
            return UtilitySourcePublishResult.Failed(ex.Message);
        }
    }

    public static string ComputeContentSha256(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<UtilitySourcePublishResult> PublishBytesUtilityFastAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        string gizmoId,
        string remoteSourcesPath,
        byte[] content,
        string mimeType = "text/markdown",
        AdventureBundle? bundle = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gizmoId))
            return UtilitySourcePublishResult.Failed("missing_gizmo_id");

        if (content is not { Length: > 0 })
            return UtilitySourcePublishResult.Failed("empty_content");

        remoteSourcesPath = ProjectSourceUploadService.NormalizeRemoteFileName(remoteSourcesPath);
        progress?.Report($"Publishing {remoteSourcesPath} via utility fast path…");

        try
        {
            var publish = await api.SourcePublication.PublishUtilityFastAsync(
                core,
                new ProjectSourcePublicationRequest
                {
                    GizmoId = gizmoId,
                    RemoteFileName = remoteSourcesPath,
                    Content = content,
                    MimeType = mimeType,
                    AdventureId = bundle?.Metadata.Id,
                    UploadMethod = ProjectSourceUploadMethod.PureApi,
                },
                progress,
                cancellationToken);

            return publish.Run?.Outcome == ProjectPublicationOutcome.Verified
                ? UtilitySourcePublishResult.Verified(publish.File, publish.VerifiedByteCount)
                : UtilitySourcePublishResult.Failed(
                    $"source_publish_{publish.Run?.Outcome.ToString().ToLowerInvariant() ?? "unknown"}");
        }
        catch (Exception ex)
        {
            return UtilitySourcePublishResult.Failed(ex.Message);
        }
    }

    public static async Task<(bool Success, string? Error, IReadOnlyList<UtilitySourcePublishResult> Results)> PublishUtilityFastBatchAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        string gizmoId,
        IReadOnlyList<(string RemotePath, byte[] Content, string MimeType)> files,
        AdventureBundle? bundle = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gizmoId))
            return (false, "missing_gizmo_id", []);

        if (files.Count == 0)
            return (false, "no_files", []);

        try
        {
            var requests = files.Select(file => new ProjectSourcePublicationRequest
            {
                GizmoId = gizmoId,
                RemoteFileName = ProjectSourceUploadService.NormalizeRemoteFileName(file.RemotePath),
                Content = file.Content,
                MimeType = file.MimeType,
                AdventureId = bundle?.Metadata.Id,
                UploadMethod = ProjectSourceUploadMethod.PureApi,
            }).ToList();

            var published = await api.SourcePublication.PublishUtilityFastBatchAsync(
                core,
                requests,
                progress,
                cancellationToken);

            var results = published
                .Select(p => p.Run?.Outcome == ProjectPublicationOutcome.Verified
                    ? UtilitySourcePublishResult.Verified(p.File, p.VerifiedByteCount)
                    : UtilitySourcePublishResult.Failed(
                        $"source_publish_{p.Run?.Outcome.ToString().ToLowerInvariant() ?? "unknown"}"))
                .ToList();

            if (results.Any(r => !r.Success))
                return (false, results.First(r => !r.Success).Error ?? "source_publish_failed", results);

            return (true, null, results);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, []);
        }
    }

    public static async Task<UtilitySourcePublishResult> PublishLocalFileToProjectAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        string gizmoId,
        string remoteSourcesPath,
        string localFilePath,
        AdventureBundle? bundle = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localFilePath))
            return UtilitySourcePublishResult.Failed("local_file_missing");

        var bytes = await File.ReadAllBytesAsync(localFilePath, cancellationToken);
        var mimeType = ProjectSourceUploadService.ResolveMimeType(remoteSourcesPath);
        return await PublishBytesToProjectAsync(
            api,
            core,
            gizmoId,
            remoteSourcesPath,
            bytes,
            mimeType,
            bundle,
            uploadMethodOverride: null,
            progress,
            cancellationToken);
    }

    public static bool BlockNameMatchesFile(string blockName, string fileName) =>
        string.Equals(blockName, fileName, StringComparison.OrdinalIgnoreCase)
        || blockName.EndsWith('/' + fileName, StringComparison.OrdinalIgnoreCase)
        || blockName.EndsWith('\\' + fileName, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeSourcesPath(string relativePath) =>
        relativePath.Replace('\\', '/').Trim().TrimStart('/');

    public static string StripOptionalCodeFence(string content)
    {
        var text = content.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var fenceMatch = Regex.Match(
            text,
            @"```(?:\w+)?\s*([\s\S]*?)\s*```",
            RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
            text = fenceMatch.Groups[1].Value.Trim();

        return text;
    }

    public static byte[] BuildDiagnosticPayload(string runToken)
    {
        var body = $"""
            # CGW utility source I/O diagnostic

            Run token: `{runToken}`
            Published via UtilitySourceFileIoService (CMD-442).

            Edit this file in a utility job reply using a delimited output block.
            """;
        return Encoding.UTF8.GetBytes(body);
    }

    public const string E2eJobId = "source_io_e2e";

    public static string BuildUtilitySourcesBlock(
        string gizmoId,
        IReadOnlyList<(string RemotePath, string? TaskHint)> taskScopedPointers)
    {
        var bullets = taskScopedPointers.Count == 0
            ? "- (none)"
            : string.Join(
                Environment.NewLine,
                taskScopedPointers.Select(p => BuildTaskScopedPointerLine(p.RemotePath, p.TaskHint)));

        return $"""
            [[cgw:sources v="2" mode="utility-worker"]]
            Project: {gizmoId}

            CANON CORE:
            - (none — retrieve task-scoped sections below)

            TASK-SCOPED:
            {bullets}

            [[/cgw:sources]]
            """;
    }

    public static string BuildE2eOutputFileName(string runToken) =>
        $"cgw-utility-io-out-{runToken}.md";

    public static string BuildE2eSourcesBlock(string gizmoId, string remoteSourcesPath) =>
        BuildUtilitySourcesBlock(
            gizmoId,
            [(remoteSourcesPath, "CGW utility source I/O E2E diagnostic input")]);

    public static string BuildE2eJobBody(string gizmoId, string remoteSourcesPath, string runToken)
    {
        var outputFile = BuildE2eOutputFileName(runToken);
        var confirmLine = $"E2E confirmed: {runToken}";
        return $"""
            {BuildE2eSourcesBlock(gizmoId, remoteSourcesPath)}

            === UTILITY SOURCE I/O E2E JOB ===
            {BuildSourceRetrieveLine(remoteSourcesPath)}

            After reading the Project source file, produce a revised copy that preserves the original content and adds this exact final line:
            {confirmLine}

            {BuildDelimitedOutputDeliveryBlock(outputFile, remoteSourcesPath)}
            """;
    }

    public static string BuildE2eJobPacket(string gizmoId, string remoteSourcesPath, string runToken, Guid runId)
    {
        var withContract = ContextTagFormat.AppendInlineUtilityResponseContract(
            BuildE2eJobBody(gizmoId, remoteSourcesPath, runToken),
            E2eJobId,
            expectsJsonArray: false);
        return ContextTagFormat.WrapUtilityJob(E2eJobId, withContract, "worker", runId);
    }

    public static string? TryExtractE2eOutput(string responseText, string runToken)
    {
        var outputFile = BuildE2eOutputFileName(runToken);
        var unwrapped = ContextTagFormat.UnwrapUtilityJobResponse(responseText);
        return TryExtractDelimitedBlock(unwrapped, outputFile)
               ?? TryExtractDelimitedBlock(responseText, outputFile);
    }

    public static bool E2eOutputContainsToken(string? extractedContent, string runToken) =>
        !string.IsNullOrWhiteSpace(extractedContent)
        && extractedContent.Contains($"E2E confirmed: {runToken}", StringComparison.Ordinal);
}

public readonly record struct DelimitedFileBlock(string FileName, string Content);

public sealed class UtilitySourcePublishResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public GizmoFileRef? File { get; init; }

    public int VerifiedByteCount { get; init; }

    public static UtilitySourcePublishResult Verified(GizmoFileRef file, int verifiedByteCount) =>
        new()
        {
            Success = true,
            File = file,
            VerifiedByteCount = verifiedByteCount,
        };

    public static UtilitySourcePublishResult Failed(string error) =>
        new() { Success = false, Error = error };
}
