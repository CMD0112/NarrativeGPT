using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlayTabCapabilityResolverTests
{
    [Fact]
    public void Bound_play_thread_on_pin_tab_is_api_armed()
    {
        var bundle = LinkedBundle("g-p-play", "conv-play");
        var pinKey = "pin-tab-1";
        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-play");
        var session = PlayTabSessionFactory.FromBundle(bundle);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, pinKey),
            session);

        Assert.Equal(PlayAutomationProfile.Full, caps.Profile);
        Assert.True(caps.AcceptPlayDraft);
        Assert.True(caps.AllowSend);
        Assert.False(caps.AllowNativeComposerInput);
        Assert.Equal(PlayDeliveryChannel.Api, caps.DeliveryChannel);
        Assert.True(caps.IsInjectionArmed);
        Assert.False(caps.LegacySuppressPlayAutomation);
        Assert.Equal(PlayDisarmReason.None, caps.DisarmReason);
    }

    [Fact]
    public void Project_landing_with_stored_thread_disarms_send()
    {
        var bundle = LinkedBundle("g-p-util", "conv-play");
        var source = ChatGptUrls.BuildProjectUrl("g-p-util");
        var session = PlayTabSessionFactory.FromBundle(bundle);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-tab"),
            session);

        Assert.False(caps.AllowSend);
        Assert.Equal(PlayDeliveryChannel.None, caps.DeliveryChannel);
        Assert.Equal(PlayDisarmReason.ProjectLanding, caps.DisarmReason);
        Assert.True(caps.LegacySuppressPlayAutomation);
    }

    [Fact]
    public void Play_rotation_draft_on_project_page_allows_dom_bootstrap()
    {
        var bundle = LinkedBundle("g-p-rotate", conversationId: null);
        ProjectChatDraftService.BeginPlayDraft(bundle);

        try
        {
            var source = ChatGptUrls.BuildProjectUrl("g-p-rotate");
            var session = PlayTabSessionFactory.FromBundle(bundle);

            var caps = PlayTabCapabilityResolver.Resolve(
                PlayTabCapabilityContext.FromUrl(
                    bundle,
                    source,
                    draftKind: ProjectChatDraftKind.Play),
                session);

            Assert.Equal(PlayAutomationProfile.DraftProjectOnly, caps.Profile);
            Assert.True(caps.AcceptPlayDraft);
            Assert.True(caps.AllowSend);
            Assert.Equal(PlayDeliveryChannel.DomBootstrap, caps.DeliveryChannel);
            Assert.False(caps.LegacySuppressPlayAutomation);
        }
        finally
        {
            ProjectChatDraftService.Complete(bundle);
        }
    }

    [Fact]
    public void Utility_draft_tab_disables_automation()
    {
        var bundle = LinkedBundle("g-p-util", "conv-play");
        var source = ChatGptUrls.BuildProjectUrl("g-p-util");
        var session = PlayTabSessionFactory.FromBundle(bundle);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(
                bundle,
                source,
                isDraftTab: true,
                draftKind: ProjectChatDraftKind.Utility),
            session);

        Assert.Equal(PlayAutomationProfile.Disabled, caps.Profile);
        Assert.Equal(PlayDisarmReason.DraftTab, caps.DisarmReason);
        Assert.True(caps.LegacySuppressPlayAutomation);
    }

    [Fact]
    public void Play_thread_without_pin_disarms_send()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-play",
                LinkedConversationId = "conv-play",
            },
        };
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-play");
        var session = PlayTabSessionFactory.FromBundle(bundle);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "some-tab"),
            session);

        Assert.Equal(PlayDisarmReason.NoPin, caps.DisarmReason);
        Assert.False(caps.AllowSend);
    }

    [Fact]
    public void Plain_conversation_url_without_project_segment_is_api_armed()
    {
        var bundle = LinkedBundle("g-p-play", "conv-play");
        var pinKey = "pin-tab-1";
        var source = "https://chatgpt.com/c/conv-play";
        var session = PlayTabSessionFactory.FromBundle(bundle);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, pinKey),
            session);

        Assert.True(caps.AllowSend);
        Assert.Equal(PlayDeliveryChannel.Api, caps.DeliveryChannel);
        Assert.Equal(PlayDisarmReason.None, caps.DisarmReason);
        Assert.True(caps.IsInjectionArmed);
    }

    [Fact]
    public void Play_thread_wrong_tab_disarms_send()
    {
        var bundle = LinkedBundle("g-p-play", "conv-play");
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "pin-a";

        var source = ChatGptUrls.BuildProjectConversationUrl("other-conv", "g-p-play");
        var session = PlayTabSessionFactory.FromBundle(bundle);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-b"),
            session);

        Assert.Equal(PlayDisarmReason.ProjectLanding, caps.DisarmReason);
        Assert.False(caps.AllowSend);
    }

    [Fact]
    public void Play_thread_stale_pin_key_on_bound_url_still_arms_send()
    {
        var bundle = LinkedBundle("g-p-play", "conv-play");
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = "pin-a";

        var source = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-play");
        var session = PlayTabSessionFactory.FromBundle(bundle);

        var caps = PlayTabCapabilityResolver.Resolve(
            PlayTabCapabilityContext.FromUrl(bundle, source, "pin-b"),
            session);

        Assert.Equal(PlayDisarmReason.None, caps.DisarmReason);
        Assert.True(caps.AllowSend);
        Assert.Equal(PlayDeliveryChannel.Api, caps.DeliveryChannel);
    }

    [Fact]
    public void Legacy_suppress_matches_ProjectChatDraftService_tests()
    {
        var storedBundle = LinkedBundle("g-p-util", "conv-play");
        var projectSource = ChatGptUrls.BuildProjectUrl("g-p-util");
        Assert.True(
            PlayTabCapabilityResolver.Resolve(
                    PlayTabCapabilityContext.FromUrl(storedBundle, projectSource),
                    PlayTabSessionFactory.FromBundle(storedBundle))
                .LegacySuppressPlayAutomation);

        var playSource = ChatGptUrls.BuildProjectConversationUrl("conv-play", "g-p-util");
        Assert.False(
            PlayTabCapabilityResolver.Resolve(
                    PlayTabCapabilityContext.FromUrl(
                        storedBundle,
                        playSource,
                        PlayTabPinService.GetPlayPinKey(storedBundle)),
                    PlayTabSessionFactory.FromBundle(storedBundle))
                .LegacySuppressPlayAutomation);
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
