using System.IO;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Stages project knowledge files on ChatGPT's project file input via CDP (WebView2).
/// Targets inputs outside the chat composer — see bridge <c>prepareProjectKnowledgeUpload</c>.
/// </summary>
internal static class ProjectKnowledgeFileStaging
{
    public static async Task<(bool Success, string? Error)> StageAsync(
        CoreWebView2 core,
        string fileName,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
            return (false, "empty_content");

        DomFileStagingCore.CleanupStagedFiles();
        var stagingDir = DomFileStagingUtilities.GetStagingDirectory(DomFileInputTarget.ProjectKnowledge);
        var sanitized = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitized)))
            sanitized += DomFileStagingUtilities.GuessExtension(mimeType, DomFileInputTarget.ProjectKnowledge);

        var path = Path.Combine(stagingDir, $"cgw-proj-{Guid.NewGuid():N}-{sanitized}");
        try
        {
            await File.WriteAllBytesAsync(path, content, cancellationToken);
            var fullPath = Path.GetFullPath(path);

            DomFileStagingCore.TrackStagingPaths([fullPath]);

            if (!await ProjectKnowledgeFileInputPreparer.HasMarkedInputAsync(core, cancellationToken))
            {
                DomFileStagingCore.CleanupStagedFiles();
                return (false, "project_file_input_not_marked");
            }

            var staged = await DomFileStagingCore.StageMarkedInputAsync(
                core,
                ProjectKnowledgeFileInputPreparer.MarkAttribute,
                [fullPath],
                cancellationToken);
            if (!staged.Success)
            {
                DomFileStagingCore.CleanupStagedFiles();
                return staged;
            }

            ProjectLinkDiagnostics.Log(
                $"Project DOM CDP staged file={sanitized} bytes={content.Length}");
            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DomFileStagingCore.CleanupStagedFiles();
            return (false, ex.Message);
        }
    }

    public static void CleanupStagedFiles() => DomFileStagingCore.CleanupStagedFiles();

    internal static string SanitizeFileName(string? name) =>
        DomFileStagingUtilities.SanitizeFileName(name, "source.md");

    internal static string Basename(string remoteFileName) =>
        SanitizeFileName(remoteFileName);

    internal static bool RemoteFileMatchesName(GizmoFileRef file, string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(file.Name))
            return false;

        var expected = Basename(remoteFileName);
        var actual = Basename(file.Name);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RemoteFileMatchesUploadAlias(GizmoFileRef file, string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(file.Name))
            return false;

        var expected = Basename(remoteFileName);
        var actual = Basename(file.Name);
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            return true;

        if (actual.EndsWith("-" + expected, StringComparison.OrdinalIgnoreCase))
            return true;

        return Regex.IsMatch(
            actual,
            $@"^cgw-(?:auto|proj|ext)-[a-f0-9]{{32}}-{Regex.Escape(expected)}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool RemoteFileMatchesPublicationTarget(GizmoFileRef file, string remoteFileName) =>
        RemoteFileMatchesName(file, remoteFileName)
        || RemoteFileMatchesUploadAlias(file, remoteFileName);
}
