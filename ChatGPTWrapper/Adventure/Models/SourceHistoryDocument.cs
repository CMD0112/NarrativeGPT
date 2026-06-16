namespace ChatGPTWrapper.Adventure.Models;

public sealed class SourceHistoryDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<SourceFileHistoryEntry> Entries { get; set; } = [];
}

public sealed class SourceFileHistoryEntry
{
    public string RelativePath { get; set; } = "";

    public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Sha256 { get; set; } = "";

    /// <summary>Path relative to sources/ (e.g. .history/scenario.md/20260605T120000Z-abc12345.md).</summary>
    public string ArchiveRelativePath { get; set; } = "";

    public string Reason { get; set; } = "export";
}

public enum RemoteProbeMatch
{
    Unknown,
    Match,
    Differ,
    MissingOnProject,
    NotDownloadable,
}
