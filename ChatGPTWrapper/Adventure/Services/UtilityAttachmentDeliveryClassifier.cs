using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Splits utility worker attachments into packet-embed vs DOM composer lanes.
/// </summary>
internal static class UtilityAttachmentDeliveryClassifier
{
    public static UtilityAttachmentDeliveryLane ResolveLane(IReadOnlyList<DomAttachmentPayload>? attachments)
    {
        if (attachments is not { Count: > 0 })
            return UtilityAttachmentDeliveryLane.None;

        if (UtilityReferenceAttachmentPolicy.CanEmbedInPacket(attachments, out _))
            return UtilityAttachmentDeliveryLane.PacketEmbed;

        Partition(attachments, out var embeddable, out var domRequired);
        if (embeddable.Count > 0 && domRequired.Count > 0)
            return UtilityAttachmentDeliveryLane.Mixed;

        return UtilityAttachmentDeliveryLane.DomComposer;
    }

    public static UtilityAttachmentDeliveryLane ResolveLaneFromMeta(AttachmentContext? attachment)
    {
        if (attachment is not { HasAttachments: true })
            return UtilityAttachmentDeliveryLane.None;

        var hasBinary = attachment.Attachments.Any(a =>
            a.IsImage
            || (a.MimeType ?? "").StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase));

        return hasBinary
            ? UtilityAttachmentDeliveryLane.DomComposer
            : UtilityAttachmentDeliveryLane.PacketEmbed;
    }

    public static string FormatLaneLabel(UtilityAttachmentDeliveryLane lane) =>
        lane switch
        {
            UtilityAttachmentDeliveryLane.PacketEmbed => "packet embed",
            UtilityAttachmentDeliveryLane.DomComposer => "DOM composer",
            UtilityAttachmentDeliveryLane.Mixed => "mixed embed + DOM",
            UtilityAttachmentDeliveryLane.AttachWorker => "attach worker",
            _ => "none",
        };

    public static bool ShouldUseDomAttachLane(IReadOnlyList<DomAttachmentPayload> attachments) =>
        attachments is { Count: > 0 }
        && !UtilityReferenceAttachmentPolicy.CanEmbedInPacket(attachments, out _);

    public static void Partition(
        IReadOnlyList<DomAttachmentPayload> attachments,
        out List<DomAttachmentPayload> embeddable,
        out List<DomAttachmentPayload> domRequired)
    {
        embeddable = [];
        domRequired = [];
        if (attachments is not { Count: > 0 })
            return;

        long embedTotal = 0;
        foreach (var attachment in attachments)
        {
            if (!UtilityReferenceAttachmentPolicy.IsEmbeddableTextFile(attachment))
            {
                domRequired.Add(attachment);
                continue;
            }

            if (attachment.Content.Length > UtilityReferenceAttachmentPolicy.MaxEmbedBytesPerFile)
            {
                domRequired.Add(attachment);
                continue;
            }

            if (embedTotal + attachment.Content.Length > UtilityReferenceAttachmentPolicy.MaxEmbedTotalBytes)
            {
                domRequired.Add(attachment);
                continue;
            }

            embeddable.Add(attachment);
            embedTotal += attachment.Content.Length;
        }
    }
}
