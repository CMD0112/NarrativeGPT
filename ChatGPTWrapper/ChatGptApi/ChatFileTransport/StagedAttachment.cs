namespace ChatGPTWrapper.ChatGptApi.ChatFileTransport;

public sealed class StagedAttachment
{
    public required string Name { get; init; }

    public required string MimeType { get; init; }

    public StagingSource Source { get; init; }

    public byte[]? Bytes { get; init; }

    public string? FileId { get; init; }

    public long SizeBytes { get; init; }

    public int? FileTokenSize { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public static StagedAttachment FromApi(ChatAttachmentRef attachment) =>
        new()
        {
            Name = attachment.FileName,
            MimeType = attachment.MimeType,
            Source = StagingSource.ApiUpload,
            FileId = attachment.FileId,
            SizeBytes = attachment.SizeBytes,
            FileTokenSize = attachment.FileTokenSize,
            Width = attachment.Width,
            Height = attachment.Height,
        };

    public static StagedAttachment FromDom(DomAttachmentPayload payload) =>
        new()
        {
            Name = payload.Name,
            MimeType = payload.MimeType,
            Source = StagingSource.DomCdp,
            Bytes = payload.Content,
            SizeBytes = payload.Content.Length,
        };

    public ChatAttachmentRef ToApiRef() =>
        new()
        {
            FileId = FileId ?? "",
            FileName = Name,
            MimeType = MimeType,
            SizeBytes = SizeBytes,
            FileTokenSize = FileTokenSize,
            Width = Width,
            Height = Height,
        };

    public DomAttachmentPayload? ToDomPayload()
    {
        if (Bytes is not { Length: > 0 })
            return null;

        return new DomAttachmentPayload
        {
            Name = Name,
            MimeType = MimeType,
            Content = Bytes,
        };
    }
}
