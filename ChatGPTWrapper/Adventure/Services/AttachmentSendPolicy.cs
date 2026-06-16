using System.IO;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AttachmentSendPolicy
{
    public static AttachmentSendMode Classify(string playerLine, AttachmentContext? attachment)
    {
        if (attachment is not { HasAttachments: true })
            return AttachmentSendMode.TextOnly;

        if (attachment.IsAttachmentOnly(playerLine))
            return attachment.HasImages
                ? AttachmentSendMode.ImagePrimary
                : AttachmentSendMode.DocumentPrimary;

        if (attachment.HasImages)
            return AttachmentSendMode.TextWithImage;

        return AttachmentSendMode.TextWithDocument;
    }

    public static string ResolveDisplayPlayerLine(
        AdventureBundle bundle,
        string playerLine,
        AttachmentContext? attachment)
    {
        if (!string.IsNullOrWhiteSpace(playerLine))
            return playerLine.Trim();

        if (attachment is not { HasAttachments: true })
            return playerLine;

        var names = attachment.Attachments
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        if (names.Count > 0)
            return string.Join(", ", names);

        return bundle.Metadata.Settings.AttachmentOnlyPlaceholder;
    }

    public static string BuildAttachmentGuidance(AttachmentSendMode mode)
    {
        return mode switch
        {
            AttachmentSendMode.TextWithImage or AttachmentSendMode.ImagePrimary =>
                """
                === ATTACHMENT GUIDANCE ===
                The player attached an image. Treat it as in-world visual reference (character, location, map, or prop).
                Describe what the characters perceive; do not mention uploads, files, or ChatGPT.
                """,
            AttachmentSendMode.TextWithDocument or AttachmentSendMode.DocumentPrimary =>
                """
                === ATTACHMENT GUIDANCE ===
                The player attached a document. Use its contents as authoritative for this turn if they extend or override typed text.
                Prefer substance from the attachment over repeating long scenario text already in project sources.
                """,
            _ => "",
        };
    }

    public static string? BuildAttachmentManifestSection(AttachmentContext? attachment)
    {
        if (attachment is not { HasAttachments: true })
            return null;

        var lines = attachment.Attachments.Select(a =>
        {
            var kind = a.IsImage ? "image" : "file";
            var mime = string.IsNullOrWhiteSpace(a.MimeType) ? "unknown" : a.MimeType;
            return $"- {a.Name} ({kind}, {mime})";
        });

        return "=== ATTACHMENTS (staged with this turn) ===\n"
               + string.Join("\n", lines)
               + "\n\nThe player also attached the file(s) above in the ChatGPT composer. "
               + "Treat staged content as part of this turn.";
    }

    public static AttachmentContextMode ResolveContextMode(
        AdventureBundle bundle,
        AttachmentSendMode sendMode)
    {
        var setting = bundle.Metadata.Settings.AttachmentContextMode;
        if (setting != AttachmentContextMode.Auto)
            return setting;

        return sendMode is AttachmentSendMode.ImagePrimary or AttachmentSendMode.TextWithImage
            ? AttachmentContextMode.Minimal
            : AttachmentContextMode.Auto;
    }

    public static bool ShouldOmitTranscript(AttachmentContextMode mode, AttachmentContext? attachment) =>
        mode == AttachmentContextMode.Minimal && attachment is { HasImages: true };

    public static int MaxLoreCards(AttachmentContextMode mode, AttachmentContext? attachment) =>
        mode == AttachmentContextMode.Minimal && attachment is { HasImages: true } ? 2 : int.MaxValue;

    public static bool ShouldSkipTrim(AttachmentContextMode mode) =>
        mode == AttachmentContextMode.Full;

    public static IReadOnlyList<string> AttachmentKinds(AttachmentContext? attachment) =>
        attachment?.Attachments
            .Select(a =>
            {
                if (a.IsImage) return "image";
                if (!string.IsNullOrWhiteSpace(a.MimeType)) return a.MimeType!;
                return "file";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    public static string FilenameSearchTokens(AttachmentContext? attachment)
    {
        if (attachment is not { HasAttachments: true })
            return "";

        return string.Join(
            " ",
            attachment.Attachments
                .Select(a => Path.GetFileNameWithoutExtension(a.Name))
                .Where(n => !string.IsNullOrWhiteSpace(n)));
    }
}
