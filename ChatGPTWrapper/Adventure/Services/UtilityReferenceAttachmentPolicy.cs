using System.IO;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Utility worker reference-file delivery on linked project threads.
/// Text/json refs embed in the API packet; binary/large files use shadow-compositor DOM attach.
/// </summary>
internal static class UtilityReferenceAttachmentPolicy
{
    internal const int MaxEmbedBytesPerFile = 512_000;
    internal const int MaxEmbedTotalBytes = 1_000_000;

    public static bool CanEmbedInPacket(
        IReadOnlyList<DomAttachmentPayload> attachments,
        out string? error)
    {
        error = null;
        if (attachments is not { Count: > 0 })
            return true;

        long total = 0;
        foreach (var attachment in attachments)
        {
            if (attachment.Content.Length > MaxEmbedBytesPerFile)
            {
                error = "utility_reference_files_too_large";
                return false;
            }

            total += attachment.Content.Length;
            if (total > MaxEmbedTotalBytes)
            {
                error = "utility_reference_files_too_large";
                return false;
            }

            if (!IsEmbeddableTextFile(attachment))
            {
                error = "utility_reference_files_must_be_text";
                return false;
            }
        }

        return true;
    }

    public static string EmbedInPacket(string jobBody, IReadOnlyList<DomAttachmentPayload> attachments) =>
        UtilityAttachmentTextInlining.AppendInlineContents(jobBody, attachments);

    internal static bool IsEmbeddableTextFile(DomAttachmentPayload attachment)
    {
        var mime = (attachment.MimeType ?? string.Empty).Trim().ToLowerInvariant();
        if (mime.StartsWith("image/", StringComparison.Ordinal)
            || mime == "application/pdf"
            || mime.StartsWith("application/vnd.", StringComparison.Ordinal))
        {
            return false;
        }

        var ext = Path.GetExtension(attachment.Name ?? string.Empty).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".pdf" or ".docx" or ".zip")
            return false;

        return UtilityAttachmentTextInlining.IsMostlyText(attachment.Content);
    }
}
