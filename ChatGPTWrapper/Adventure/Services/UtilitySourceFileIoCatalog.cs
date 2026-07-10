using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Jobs that use the utility source-reference file I/O methodology (publish → pointer → scrape → delete).
/// </summary>
public static class UtilitySourceFileIoCatalog
{
    public static IReadOnlyList<string> SourceFileIoJobIds { get; } =
    [
        GenerationJobId.ExtractEntities,
        GenerationJobId.ExpandEntity,
        GenerationJobId.UpdateState,
        GenerationJobId.ProposeEntityState,
        GenerationJobId.ProposeEntitiesFile,
        GenerationJobId.ProposeSourceEdits,
    ];

    public static bool UsesSourceFileIo(string? jobId) =>
        !string.IsNullOrWhiteSpace(jobId)
        && SourceFileIoJobIds.Contains(jobId, StringComparer.OrdinalIgnoreCase);

    public static bool RequiresEphemeralUtilityChat(string? jobId) =>
        UsesSourceFileIo(jobId);
}
