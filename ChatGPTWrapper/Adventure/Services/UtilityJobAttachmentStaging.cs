using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Persists utility job attachment bytes for durable outbox replay.</summary>
internal static class UtilityJobAttachmentStaging
{
    public static string StagingDirectory(Guid adventureId, Guid runId) =>
        Path.Combine(
            AppDirectories.AdventureDirectory(adventureId),
            "utility-attach-staging",
            runId.ToString("N"));

    public static IReadOnlyList<UtilityOutboxAttachment> Stage(
        Guid adventureId,
        Guid runId,
        IReadOnlyList<DomAttachmentPayload> attachments)
    {
        if (attachments is not { Count: > 0 })
            return [];

        var dir = StagingDirectory(adventureId, runId);
        Directory.CreateDirectory(dir);
        var adventureRoot = AppDirectories.AdventureDirectory(adventureId);
        var staged = new List<UtilityOutboxAttachment>(attachments.Count);

        foreach (var attachment in attachments)
        {
            var safeName = SanitizeFileName(attachment.Name);
            var filePath = Path.Combine(dir, $"{Guid.NewGuid():N}-{safeName}");
            File.WriteAllBytes(filePath, attachment.Content);
            staged.Add(new UtilityOutboxAttachment
            {
                RelativePath = Path.GetRelativePath(adventureRoot, filePath),
                Name = attachment.Name,
                MimeType = attachment.MimeType,
            });
        }

        return staged;
    }

    public static IReadOnlyList<DomAttachmentPayload> LoadDomPayloads(
        Guid adventureId,
        IReadOnlyList<UtilityOutboxAttachment>? attachments)
    {
        if (attachments is not { Count: > 0 })
            return [];

        var adventureRoot = AppDirectories.AdventureDirectory(adventureId);
        var payloads = new List<DomAttachmentPayload>(attachments.Count);
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.RelativePath))
                continue;

            var fullPath = Path.GetFullPath(Path.Combine(adventureRoot, attachment.RelativePath));
            if (!fullPath.StartsWith(adventureRoot, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
            {
                continue;
            }

            payloads.Add(new DomAttachmentPayload
            {
                Name = attachment.Name,
                MimeType = attachment.MimeType,
                Content = File.ReadAllBytes(fullPath),
            });
        }

        return payloads;
    }

    public static AttachmentContext ToAttachmentContext(
        Guid adventureId,
        IReadOnlyList<UtilityOutboxAttachment>? attachments)
    {
        if (attachments is not { Count: > 0 })
            return new AttachmentContext();

        var adventureRoot = AppDirectories.AdventureDirectory(adventureId);
        return AttachmentContext.FromMeta(attachments.Select(a =>
        {
            long? sizeBytes = null;
            if (!string.IsNullOrWhiteSpace(a.RelativePath))
            {
                var fullPath = Path.GetFullPath(Path.Combine(adventureRoot, a.RelativePath));
                if (fullPath.StartsWith(adventureRoot, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(fullPath))
                {
                    sizeBytes = new FileInfo(fullPath).Length;
                }
            }

            return new ComposerAttachmentMeta
            {
                Name = a.Name,
                MimeType = a.MimeType,
                SizeBytes = sizeBytes,
            };
        }));
    }

    public static void Cleanup(Guid adventureId, Guid runId)
    {
        var dir = StagingDirectory(adventureId, runId);
        if (!Directory.Exists(dir))
            return;

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup after job completion.
        }
    }

    public static IReadOnlyList<DomAttachmentPayload> LoadFromPaths(IEnumerable<string> paths)
    {
        var payloads = new List<DomAttachmentPayload>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            var bytes = File.ReadAllBytes(path);
            var name = Path.GetFileName(path);
            payloads.Add(new DomAttachmentPayload
            {
                Name = name,
                MimeType = GuessMimeType(name),
                Content = bytes,
            });
        }

        return payloads;
    }

    private static string SanitizeFileName(string name)
    {
        var fileName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(fileName))
            return "attachment.bin";

        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        return fileName;
    }

    internal static string GuessMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream",
        };
    }
}
