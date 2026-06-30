using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Appends attachment manifest and guidance to utility job packet bodies.</summary>
internal static class UtilityJobPacketAttachmentEnricher
{
    public static string Append(
        AdventureBundle bundle,
        string jobBody,
        AttachmentContext? attachment,
        string? referenceNote = null,
        UtilityAttachmentDeliveryLane deliveryLane = UtilityAttachmentDeliveryLane.None)
    {
        var sections = new List<string> { jobBody.TrimEnd() };

        if (attachment is { HasAttachments: true })
        {
            var manifest = BuildManifest(attachment, deliveryLane);
            if (!string.IsNullOrWhiteSpace(manifest))
                sections.Add(manifest.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(referenceNote))
        {
            sections.Add(
                "=== REFERENCE ATTACHMENT INSTRUCTIONS ===\n"
                + referenceNote.Trim()
                + "\n\n"
                + ReferenceGuidance(deliveryLane));
        }

        return sections.Count == 1 ? sections[0] : string.Join("\n\n", sections);
    }

    private static string ReferenceGuidance(UtilityAttachmentDeliveryLane deliveryLane) =>
        deliveryLane switch
        {
            UtilityAttachmentDeliveryLane.DomComposer or UtilityAttachmentDeliveryLane.AttachWorker =>
                "Use the reference files attached to this message in the ChatGPT composer as authoritative for this job.",
            UtilityAttachmentDeliveryLane.Mixed =>
                "Use embedded text sections and composer-attached binary files as authoritative for this job.",
            _ =>
                "Use the reference material embedded in this packet (manifest and file sections below) as authoritative for this job.",
        };

    private static string BuildManifest(AttachmentContext attachment, UtilityAttachmentDeliveryLane deliveryLane)
    {
        var lines = attachment.Attachments.Select(a =>
        {
            var kind = a.IsImage ? "image" : "file";
            var mime = string.IsNullOrWhiteSpace(a.MimeType) ? "unknown" : a.MimeType;
            return $"- {a.Name} ({kind}, {mime})";
        });

        var header = deliveryLane switch
        {
            UtilityAttachmentDeliveryLane.DomComposer or UtilityAttachmentDeliveryLane.AttachWorker =>
                "=== REFERENCE FILES (composer attach) ===",
            UtilityAttachmentDeliveryLane.Mixed =>
                "=== REFERENCE FILES (embed + composer) ===",
            _ =>
                "=== REFERENCE FILES (embedded in packet) ===",
        };

        var footer = deliveryLane switch
        {
            UtilityAttachmentDeliveryLane.DomComposer or UtilityAttachmentDeliveryLane.AttachWorker =>
                "\n\nThese files are staged in the native ChatGPT composer for this send.",
            UtilityAttachmentDeliveryLane.Mixed =>
                "\n\nText files are embedded below; binary files are composer attachments.",
            _ =>
                "\n\nFull file contents appear in dedicated sections below. "
                + "These are included in the API text message — not as ChatGPT composer uploads.",
        };

        return header + "\n" + string.Join("\n", lines) + footer;
    }
}
