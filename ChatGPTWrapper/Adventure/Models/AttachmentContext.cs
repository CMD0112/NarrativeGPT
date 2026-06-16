namespace ChatGPTWrapper.Adventure.Models;

public sealed class ComposerAttachmentMeta
{
    public string Name { get; set; } = "";

    public string? MimeType { get; set; }

    public long? SizeBytes { get; set; }

    public bool IsImage =>
        !string.IsNullOrWhiteSpace(MimeType)
        && MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public enum AttachmentSendMode
{
    TextOnly,
    TextWithImage,
    ImagePrimary,
    TextWithDocument,
    DocumentPrimary,
}

public sealed class AttachmentContext
{
    public IReadOnlyList<ComposerAttachmentMeta> Attachments { get; init; } = [];

    public bool HasAttachments => Attachments.Count > 0;

    public bool HasImages => Attachments.Any(a => a.IsImage);

    public bool HasDocuments => Attachments.Any(a => !a.IsImage);

    public bool IsAttachmentOnly(string? playerLine) =>
        string.IsNullOrWhiteSpace(playerLine) && HasAttachments;

    public static AttachmentContext FromPending(
        IEnumerable<PlayComposePendingAttachmentRef> attachments) =>
        new()
        {
            Attachments = attachments
                .Select(a => new ComposerAttachmentMeta
                {
                    Name = a.Name,
                    MimeType = a.MimeType,
                    SizeBytes = a.SizeBytes,
                })
                .ToList(),
        };

    public static AttachmentContext FromMeta(IEnumerable<ComposerAttachmentMeta> meta) =>
        new() { Attachments = meta.ToList() };
}

/// <summary>Lightweight attachment ref for adventure services (avoids WebView layer types).</summary>
public sealed class PlayComposePendingAttachmentRef
{
    public string Name { get; init; } = "";

    public string? MimeType { get; init; }

    public long? SizeBytes { get; init; }
}
