using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class UtilityJobPacketAttachmentEnricherTests
{
    [Fact]
    public void Append_leaves_text_only_packet_unchanged()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings() },
        };

        var result = UtilityJobPacketAttachmentEnricher.Append(bundle, "=== JOB ===\nDo work.", null);

        Assert.Equal("=== JOB ===\nDo work.", result);
    }

    [Fact]
    public void Append_adds_reference_note_section()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings() },
        };

        var result = UtilityJobPacketAttachmentEnricher.Append(
            bundle,
            "=== JOB ===",
            null,
            "Use entities.json as schema reference.");

        Assert.Contains("=== REFERENCE ATTACHMENT INSTRUCTIONS ===", result);
        Assert.Contains("entities.json", result);
    }

    [Fact]
    public void Append_adds_manifest_and_guidance_for_document_attachment()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { InjectAttachmentGuidance = true },
            },
        };
        var attachment = new AttachmentContext
        {
            Attachments =
            [
                new ComposerAttachmentMeta
                {
                    Name = "handout.pdf",
                    MimeType = "application/pdf",
                },
            ],
        };

        var result = UtilityJobPacketAttachmentEnricher.Append(
            bundle,
            "=== JOB ===",
            attachment,
            deliveryLane: UtilityAttachmentDeliveryLane.DomComposer);

        Assert.Contains("=== REFERENCE FILES (composer attach) ===", result);
        Assert.Contains("handout.pdf", result);
        Assert.Contains("staged in the native ChatGPT composer", result);
    }

    [Fact]
    public void Append_embed_lane_manifest_for_json_attachment()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings() },
        };
        var attachment = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "entities.json", MimeType = "application/json" },
        ]);

        var result = UtilityJobPacketAttachmentEnricher.Append(
            bundle,
            "=== JOB ===",
            attachment,
            deliveryLane: UtilityAttachmentDeliveryLane.PacketEmbed);

        Assert.Contains("=== REFERENCE FILES (embedded in packet) ===", result);
        Assert.Contains("entities.json", result);
    }
}
