namespace ChatGPTWrapper.Adventure.Models;

public sealed class ProbeMetaDocument
{
    public DateTimeOffset ProbedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ProbeMetaFileEntry> Files { get; set; } = [];
}

public sealed class ProbeMetaFileEntry
{
    public string RelativePath { get; set; } = "";

    public string? FileId { get; set; }

    public string? Sha256 { get; set; }

    public RemoteProbeMatch Match { get; set; } = RemoteProbeMatch.Unknown;
}
