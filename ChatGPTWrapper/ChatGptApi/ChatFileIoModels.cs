namespace ChatGPTWrapper.ChatGptApi;

/// <summary>Raw file bytes for native-composer DOM submit when API attachment send fails.</summary>
public sealed class DomAttachmentPayload
{
    public required string Name { get; init; }

    public required string MimeType { get; init; }

    public required byte[] Content { get; init; }
}

public sealed class ChatAttachmentRef
{
    public required string FileId { get; init; }

    public required string FileName { get; init; }

    public required string MimeType { get; init; }

    public long SizeBytes { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }
}

public sealed class ChatAttachmentUploadResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public ChatAttachmentRef? Attachment { get; init; }
}

public sealed class ConversationFileRef
{
    public required string FileId { get; init; }

    public string? Name { get; init; }

    public string? MimeType { get; init; }

    public string? Location { get; init; }

    public string? AssetPointer { get; init; }

    public string? MessageId { get; init; }

    public string? AuthorRole { get; init; }

    public string Source { get; init; } = "";
}

public sealed class ComposerFileUiProbe
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<ComposerFileInputProbe> FileInputs { get; init; } = [];

    public IReadOnlyList<ComposerAttachButtonProbe> AttachButtons { get; init; } = [];

    public string? PageHref { get; init; }
}

public sealed class ComposerFileInputProbe
{
    public string Accept { get; init; } = "";

    public bool Multiple { get; init; }

    public bool Hidden { get; init; }

    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string TestId { get; init; } = "";
}

public sealed class ComposerAttachButtonProbe
{
    public string Selector { get; init; } = "";

    public string TestId { get; init; } = "";

    public string AriaLabel { get; init; } = "";

    public string Text { get; init; } = "";
}

public sealed class ChatDownloadEventRecord
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    public string? Uri { get; init; }

    public string? MimeType { get; init; }

    public long TotalBytes { get; init; }

    public string? ResultFilePath { get; init; }

    public bool Handled { get; init; }
}
