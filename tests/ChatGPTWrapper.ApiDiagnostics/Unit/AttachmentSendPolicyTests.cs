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

    [Fact]
    public void FilenameSearchTokens_joins_stem_names()
    {
        var ctx = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "tavern-map.png", MimeType = "image/png" },
            new ComposerAttachmentMeta { Name = "handout.pdf", MimeType = "application/pdf" },
        ]);

        var tokens = AttachmentSendPolicy.FilenameSearchTokens(ctx);

        Assert.Contains("tavern-map", tokens);
        Assert.Contains("handout", tokens);
    }

    [Fact]
    public void PrepareSend_image_primary_includes_guidance_manifest_and_display_line()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.InjectAttachmentGuidance = true;
        var attachment = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "scene-sketch.png", MimeType = "image/png" },
        ]);

        var prepared = PromptInjectionService.PrepareSend(bundle, "", attachment);

        Assert.Equal(AttachmentSendMode.ImagePrimary, prepared.AttachmentSendMode);
        Assert.Equal("scene-sketch.png", prepared.UserText);
        Assert.Contains("=== ATTACHMENT GUIDANCE ===", prepared.MergedText);
        Assert.Contains("=== ATTACHMENTS (staged with this turn) ===", prepared.MergedText);
        Assert.Contains(prepared.Sections, s => s.Id == "attachment-guidance" && s.Included);
        Assert.Contains(prepared.Sections, s => s.Id == "attachment-manifest" && s.Included);
    }

    [Fact]
    public void PrepareSend_attachment_filename_enriches_card_trigger()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.UseSectionInjection = false;
        bundle.Metadata.Settings.ForceInlineLore = true;
        AdventureTestData.WriteLocalSources(bundle);
        try
        {
            bundle.Cards.Cards.Add(new StoryCard
            {
                Name = "DragonLore",
                Enabled = true,
                Triggers = ["dragon"],
                Content = "Ancient wyrm lore.",
            });

            var attachment = AttachmentContext.FromMeta(
            [
                new ComposerAttachmentMeta { Name = "dragon-sketch.png", MimeType = "image/png" },
            ]);

            var prepared = PromptInjectionService.PrepareSend(bundle, "", attachment);

            Assert.Contains("DragonLore", prepared.MergedText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}
