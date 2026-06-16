namespace ChatGPTWrapper.Adventure.Models;

public sealed class SectionManifestEntry
{
    /// <summary>Section path within file, e.g. npcs/mara-voss or opening.</summary>
    public string Id { get; set; } = "";

    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";

    public List<string> Aliases { get; set; } = [];

  public string? ParentId { get; set; }

    /// <summary>Cached body text for inline packet render (excludes file-level headers).</summary>
    public string BodyCache { get; set; } = "";

    /// <summary>First line or phrase for C5 baseline downgrade checks.</summary>
    public string? KeyPhrase { get; set; }

    public string? SourceEntityId { get; set; }

    public bool Pinned { get; set; }

    public string MachineId(string fileName) =>
        $"{fileName}#{Id}";
}
