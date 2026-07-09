using System.IO;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Ephemeral utility job: publish adventure source files to Project sources, run source-edit context,
/// capture proposals via delimited inline JSON output.
/// </summary>
internal static class SourceFileRevisionService
{
    public const string OutputFileName = "source-edits.json";

    public const string UtilityTitlePrefix = "[CGW:source-edits]";

    public static IReadOnlyList<string> PublishableSourceFileNames { get; } =
        SectionSchema.CoreLoreFiles.ToList();

    public static string BuildCanonicalInputRemotePath(
        AdventureBundle bundle,
        Guid runId,
        string fileName) =>
        UtilitySourceFileNaming.BuildInputRemotePath(
            bundle.Metadata.Id,
            GenerationJobId.ProposeSourceEdits,
            runId,
            fileName);

    public static string LocalSourcePath(AdventureBundle bundle, string fileName) =>
        Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), fileName);

    public static async Task<(bool Success, string? Error, IReadOnlyList<string> RemotePaths)> PublishSourceFilesToProjectAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        AdventureBundle bundle,
        Guid runId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        await UtilityPublishSession.PublishJobInputsAsync(
            api,
            core,
            bundle,
            GenerationJobId.ProposeSourceEdits,
            runId,
            progress,
            cancellationToken);

    public static string BuildRevisionPrompt(
        AdventureBundle bundle,
        string userPrompt,
        Guid runId,
        string? gizmoId = null)
    {
        gizmoId ??= AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var published = new List<(string RemotePath, string? TaskHint)>();
        foreach (var fileName in PublishableSourceFileNames)
        {
            var localPath = LocalSourcePath(bundle, fileName);
            if (!File.Exists(localPath))
                continue;

            published.Add((
                BuildCanonicalInputRemotePath(bundle, runId, fileName),
                $"Current {fileName} input for this source-edit job"));
        }

        var sourcesBlock = string.IsNullOrWhiteSpace(gizmoId) || published.Count == 0
            ? ""
            : $"""
              {UtilitySourceFileIoService.BuildUtilitySourcesBlock(gizmoId, published)}

              """;

        var retrieveLines = published.Count == 0
            ? ""
            : string.Join(
                Environment.NewLine,
                published.Select(p => UtilitySourceFileIoService.BuildSourceRetrieveLine(p.RemotePath)));

        var formatReference = CanonFormatReferenceService.BuildPromptBlock(bundle);
        var formatHints = ProjectSourceFileTemplates.BuildInlineFormatsSection(
            published.Select(p => Path.GetFileName(p.RemotePath)).ToList());
        var formatsBlock = string.IsNullOrWhiteSpace(formatHints)
            ? ""
            : $"""

              === SOURCE FILE FORMATS (summary) ===
              {formatHints}
              """;

        var instructions = InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle);

        return $"""
            {sourcesBlock}=== SOURCE EDIT JOB ===
            {userPrompt.Trim()}

            {retrieveLines}

            {BuildSourceEditsDeliveryBlock()}

            === CURRENT INSTRUCTIONS (instruction-domain) ===
            {instructions}
            {formatReference}
            {formatsBlock}
            """;
    }

    private static string BuildSourceEditsDeliveryBlock() =>
        UtilitySourceFileIoService.BuildDelimitedOutputDeliveryBlock(
            OutputFileName,
            schemaNotes: """
            - Output must be a JSON array of proposal objects.
            - Each object: `{ "targetFile", "operation", "content", "rationale" }`.
            - `operation` is one of: replace, append, remove.
            - `targetFile` uses adventure source basenames (world.md, plot.md, scenario.md, cast.md, instructions-snippet.md).
            """);

    public static string? TryExtractProposalsJson(string responseText) =>
        UtilitySourceFileIoService.TryExtractDelimitedBlock(responseText, OutputFileName)
        ?? EntityExtractionService.TryNormalizeJsonResponse(responseText);

    public static bool HasCompleteSourceEditsDelivery(string responseText) =>
        !string.IsNullOrWhiteSpace(TryExtractProposalsJson(responseText));

    public static bool IsSettledResponse(string responseText, bool streamComplete)
    {
        if (HasCompleteSourceEditsDelivery(responseText))
            return true;

        if (streamComplete && !string.IsNullOrWhiteSpace(EntityExtractionService.TryNormalizeJsonResponse(responseText)))
            return true;

        return false;
    }
}
