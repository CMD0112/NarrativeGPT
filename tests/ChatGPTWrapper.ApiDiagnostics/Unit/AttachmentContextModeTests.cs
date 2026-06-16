using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class AttachmentContextModeTests
{
    [Fact]
    public void Minimal_mode_omits_transcript_when_image_attached()
    {
        var ctx = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "map.png", MimeType = "image/png" },
        ]);

        Assert.True(AttachmentSendPolicy.ShouldOmitTranscript(AttachmentContextMode.Minimal, ctx));
        Assert.False(AttachmentSendPolicy.ShouldOmitTranscript(AttachmentContextMode.Full, ctx));
    }

    [Fact]
    public void Full_mode_skips_packet_trim()
    {
        Assert.True(AttachmentSendPolicy.ShouldSkipTrim(AttachmentContextMode.Full));
        Assert.False(AttachmentSendPolicy.ShouldSkipTrim(AttachmentContextMode.Minimal));
    }

    [Fact]
    public void PrepareSend_minimal_caps_lore_cards_with_image()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Metadata.Settings.AttachmentContextMode = AttachmentContextMode.Minimal;
        for (var i = 0; i < 5; i++)
        {
            bundle.Cards.Cards.Add(new StoryCard
            {
                Name = $"Card{i}",
                Enabled = true,
                Triggers = ["dragon"],
                Content = $"Lore {i}",
            });
        }

        var attachment = AttachmentContext.FromMeta(
        [
            new ComposerAttachmentMeta { Name = "scene.png", MimeType = "image/png" },
        ]);

        var prepared = PromptInjectionService.PrepareSend(bundle, "face the dragon", attachment);
        Assert.DoesNotContain("=== RECENT TRANSCRIPT ===", prepared.MergedText);
        Assert.Contains("dragon", prepared.MergedText, StringComparison.OrdinalIgnoreCase);
    }
}
