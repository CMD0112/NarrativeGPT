using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class AttachmentSendPolicyTests
{
    [Fact]
    public void Classify_text_only_when_no_attachments()
    {
        Assert.Equal(AttachmentSendMode.TextOnly, AttachmentSendPolicy.Classify("hello", null));
    }

    [Fact]
    public void Classify_image_primary_for_attachment_only_image()
    {
        var ctx = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "map.png", MimeType = "image/png" },
        ]);

        Assert.Equal(AttachmentSendMode.ImagePrimary, AttachmentSendPolicy.Classify("", ctx));
    }

    [Fact]
    public void ResolveDisplayPlayerLine_uses_placeholder_when_unnamed()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.AttachmentOnlyPlaceholder = "[Scene photo]";

        var ctx = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "", MimeType = "image/png" },
        ]);

        Assert.Equal("[Scene photo]", AttachmentSendPolicy.ResolveDisplayPlayerLine(bundle, "", ctx));
    }

    [Fact]
    public void BuildAttachmentManifestSection_lists_staged_files()
    {
        var ctx = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "map.png", MimeType = "image/png" },
            new ComposerAttachmentMeta { Name = "notes.pdf", MimeType = "application/pdf" },
        ]);

        var text = AttachmentSendPolicy.BuildAttachmentManifestSection(ctx);

        Assert.NotNull(text);
        Assert.Contains("=== ATTACHMENTS (staged with this turn) ===", text);
        Assert.Contains("map.png (image, image/png)", text);
        Assert.Contains("notes.pdf (file, application/pdf)", text);
    }
}
