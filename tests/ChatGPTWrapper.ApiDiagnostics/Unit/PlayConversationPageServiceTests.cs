using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayConversationPageServiceTests
{
    [Fact]
    public void TryAdoptBrowserConversation_updates_stale_binding_from_plain_conversation_url()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
                LinkedConversationId = "stale-thread",
            },
        };

        var url = ChatGptUrls.BuildConversationUrl("live-thread");

        Assert.True(PlayConversationPageService.TryAdoptBrowserConversation(bundle, url));
        Assert.Equal("live-thread", bundle.Metadata.LinkedConversationId);
    }

    [Fact]
    public void TryAdoptBrowserConversation_noop_when_already_bound_to_browser_thread()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                LinkedProjectId = "g-p-test",
                LinkedConversationId = "thread-1",
            },
        };

        var url = ChatGptUrls.BuildProjectConversationUrl("thread-1", "g-p-test");

        Assert.False(PlayConversationPageService.TryAdoptBrowserConversation(bundle, url));
        Assert.Equal("thread-1", bundle.Metadata.LinkedConversationId);
    }

    [Fact]
    public void IsAdoptablePlayConversationUrl_accepts_plain_conversation_for_linked_project()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { LinkedProjectId = "g-p-test" },
        };

        Assert.True(
            PlayConversationPageService.IsAdoptablePlayConversationUrl(
                bundle,
                ChatGptUrls.BuildConversationUrl("abc"),
                "abc"));
    }

    [Fact]
    public void PreparePrebuiltPacket_passes_start_packet_through_without_rebuild()
    {
        const string packet =
            "[[cgw:meta mode=\"thin\" turn=\"1\"]] [[/cgw:meta]]"
            + "[[cgw:sources v=\"2\" mode=\"thin\"]]Project: g-p-test[[/cgw:sources]]"
            + "Begin the adventure.";

        var prepared = PromptInjectionService.PreparePrebuiltPacket(packet);

        Assert.Equal(packet, prepared.MergedText);
        Assert.Equal(packet, prepared.UserText);
        Assert.False(string.IsNullOrWhiteSpace(prepared.Hash));
    }
}
