using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlaySendDeliveryChannelPolicyTests
{
    [Fact]
    public void Api_channel_always_uses_api_text_path()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { PreferDomPlaySend = true },
            },
        };

        Assert.True(PlaySendDeliveryPolicy.ShouldUseApiTextPlaySend(bundle, PlayDeliveryChannel.Api));
    }

    [Fact]
    public void Dom_bootstrap_channel_skips_api_text_path()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { PreferDomPlaySend = false },
            },
        };

        Assert.False(
            PlaySendDeliveryPolicy.ShouldUseApiTextPlaySend(bundle, PlayDeliveryChannel.DomBootstrap));
    }
}

[Trait("Category", "Unit")]
public sealed class PlaySendArmServiceTests
{
    [Fact]
    public void Armed_when_capabilities_allow_and_artifact_fresh()
    {
        var bundle = LinkedBundle("g-p-arm", "conv-play");
        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-arm");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));
        var store = new PreparedSendArtifactStore();
        store.Bind(bundle);
        store.Set(PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = "hello",
            ResolvePlayerLine = (_, _, text) => text ?? "",
        }));

        var arm = PlaySendArmService.Evaluate(caps, store);

        Assert.True(arm.IsArmed);
        Assert.Null(arm.ReasonCode);
    }

    [Fact]
    public void Disarmed_when_stale_artifact()
    {
        var bundle = LinkedBundle("g-p-arm-stale", "conv-play");
        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-arm-stale");
        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab-1"),
            PlayTabSessionFactory.FromBundle(bundle));
        var store = new PreparedSendArtifactStore();
        store.Bind(bundle);
        store.Set(PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = "hello",
            ResolvePlayerLine = (_, _, text) => text ?? "",
        }));
        bundle.Metadata.Settings.MaxPacketChars--;

        var arm = PlaySendArmService.Evaluate(caps, store);

        Assert.False(arm.IsArmed);
        Assert.Equal("stale_preview", arm.ReasonCode);
    }

    private static AdventureBundle LinkedBundle(string projectId, string? conversationId)
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = projectId,
                LinkedConversationId = conversationId,
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "pin-tab-1";
        return bundle;
    }
}
