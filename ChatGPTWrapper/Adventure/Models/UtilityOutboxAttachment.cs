namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Staged file for a utility worker outbox job (bytes on disk under the adventure folder).</summary>
public sealed class UtilityOutboxAttachment
{
    /// <summary>Path relative to the adventure directory.</summary>
    public string RelativePath { get; set; } = "";

    public string Name { get; set; } = "";

    public string MimeType { get; set; } = "application/octet-stream";
}
