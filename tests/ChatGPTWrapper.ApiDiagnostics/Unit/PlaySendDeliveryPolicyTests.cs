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
    public void Default_prefer_api_for_new_settings()
    {
        var settings = new AdventureSettings();
        Assert.False(settings.PreferDomPlaySend);
    }

    [Fact]
    public void PreferDom_setting_retired_always_api_canonical()
    {
        var bundle = Bundle(preferDom: true);

        Assert.False(PlaySendDeliveryPolicy.PreferDom(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiTextPlaySend(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiCapture(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiRegenerate(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldPrefetchApiWarmup(bundle));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiUtilitySend(
            bundle,
            UtilityConversationReadinessLevel.Registered));
        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiWorkerLaneSend(
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
    public void Api_attachment_refs_do_not_use_api_send()
    {
        var bundle = Bundle(preferDom: true);
        var refs = new List<ChatAttachmentRef>
        {
            new() { FileId = "file-1", FileName = "a.png", MimeType = "image/png" },
        };

        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiPlaySend(bundle, refs, domAttachments: null));
        Assert.False(PlaySendDeliveryPolicy.ShouldUseApiPlaySend(
            bundle,
            refs,
            domAttachments: [new DomAttachmentPayload { Name = "a.png", MimeType = "image/png", Content = [1] }]));
    }

    [Fact]
    public void Play_attachments_require_dom_composer_staging()
    {
        var refs = new List<ChatAttachmentRef>
        {
            new() { FileId = "file-1", FileName = "a.png", MimeType = "image/png" },
        };

        Assert.True(PlaySendDeliveryPolicy.RequiresDomComposerForAttachments(
            refs,
            domAttachments: null,
            attachmentsPreStaged: false));
    }
}
