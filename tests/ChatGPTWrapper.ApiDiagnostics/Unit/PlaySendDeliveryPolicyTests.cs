using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlaySendDeliveryPolicyTests
{
    private static AdventureBundle Bundle(bool preferDom) =>
        new()
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { PreferDomPlaySend = preferDom },
            },
        };

    [Fact]
    public void Default_prefer_dom_for_new_settings()
    {
        var settings = new AdventureSettings();
        Assert.True(settings.PreferDomPlaySend);
    }

    [Fact]
    public void PreferDom_true_skips_api_text_capture_regenerate_utility_and_warmup()
    {
        var bundle = Bundle(preferDom: true);

        Assert.True(PlaySendDeliveryPolicy.PreferDom(bundle));
        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiTextPlaySend(bundle));
        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiCapture(bundle));
        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiRegenerate(bundle));
        Assert.False(PlaySendDeliveryPolicy.ShouldPrefetchApiWarmup(bundle));
        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiUtilitySend(
            bundle,
            UtilityConversationReadinessLevel.Registered));
    }

    [Fact]
    public void PreferDom_false_restores_api_first_for_text_utility_and_warmup()
    {
        var bundle = Bundle(preferDom: false);

        Assert.False(PlaySendDeliveryPolicy.PreferDom(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiTextPlaySend(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiCapture(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiRegenerate(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldPrefetchApiWarmup(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiUtilitySend(
            bundle,
            UtilityConversationReadinessLevel.Registered));
    }

    [Fact]
    public void Api_attachment_refs_still_use_api_when_dom_first_and_no_dom_bytes()
    {
        var bundle = Bundle(preferDom: true);
        var refs = new List<ChatAttachmentRef>
        {
            new() { FileId = "file-1", FileName = "a.png", MimeType = "image/png" },
        };

        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiPlaySend(bundle, refs, domAttachments: null));
        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiPlaySend(
            bundle,
            refs,
            domAttachments: [new DomAttachmentPayload { Name = "a.png", MimeType = "image/png", Content = [1] }]));
    }

    [Fact]
    public void Dom_attachment_bytes_never_use_api_attachment_send_even_when_api_first()
    {
        var bundle = Bundle(preferDom: false);
        var refs = new List<ChatAttachmentRef>
        {
            new() { FileId = "file-1", FileName = "a.png", MimeType = "image/png" },
        };
        var dom = new List<DomAttachmentPayload>
        {
            new() { Name = "a.png", MimeType = "image/png", Content = [1] },
        };

        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiPlaySend(bundle, refs, dom));
    }
}
