namespace ChatGPTWrapper.Adventure.Models;

public sealed class ContextIndexDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<ContextIndexEntry> Entries { get; set; } = [];
}

public sealed class ContextIndexEntry
{
    public string Id { get; set; } = "";

    /// <summary>Machine target, e.g. plot.md#mysteries/basement-whispers.</summary>
    public string Target { get; set; } = "";

    public string Kind { get; set; } = "concept";

    public List<string> Triggers { get; set; } = [];
}
