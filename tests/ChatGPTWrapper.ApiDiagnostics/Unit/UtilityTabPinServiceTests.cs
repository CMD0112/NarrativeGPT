using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityTabPinServiceTests
{
    private const string ProjectId = "g-p-6a220fab2eb48191a75b9d88d85a3d91";
    private const string PlayConvId = "6a24cb8f-e1c4-83ea-993a-de18c5e5a371";
    private const string UtilityConvId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    [Fact]
    public void TryResolveUtilityConversationFromSource_parses_project_conversation_url()
    {
        var bundle = CreateBundle();
        var url = $"https://chatgpt.com/g/{ProjectId}/c/{UtilityConvId}";

        var ok = PlayTabPinService.TryResolveUtilityConversationFromSource(
            bundle,
            url,
            out var conversationId,
            out var error);

        Assert.True(ok);
        Assert.Equal(UtilityConvId, conversationId);
        Assert.Null(error);
    }

    [Fact]
    public void TryResolveUtilityConversationFromSource_rejects_play_thread_id()
    {
        var bundle = CreateBundle(playConversationId: PlayConvId);
        var url = $"https://chatgpt.com/g/{ProjectId}/c/{PlayConvId}";

        var ok = PlayTabPinService.TryResolveUtilityConversationFromSource(
            bundle,
            url,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Equal("utility_same_as_play_thread", error);
    }

    [Fact]
    public void TryResolveUtilityConversationFromSource_rejects_non_conversation_url()
    {
        var bundle = CreateBundle();
        var url = $"https://chatgpt.com/g/{ProjectId}/project";

        var ok = PlayTabPinService.TryResolveUtilityConversationFromSource(
            bundle,
            url,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Equal("utility_tab_not_on_conversation", error);
    }

    [Fact]
    public void IsAcceptableUtilityConversationId_rejects_play_thread()
    {
        var bundle = CreateBundle(playConversationId: PlayConvId);
        Assert.False(PlayTabPinService.IsAcceptableUtilityConversationId(bundle, PlayConvId));
    }

    [Fact]
    public void HasUtilityPin_detects_metadata_key()
    {
        var bundle = CreateBundle();
        Assert.False(PlayTabPinService.HasUtilityPin(bundle));

        bundle.Metadata.PinnedUtilityTabKey = "tab-1";
        Assert.True(PlayTabPinService.HasUtilityPin(bundle));
    }

    [Fact]
    public void FormatUtilityStatus_shows_pinned_prefix_when_utility_pin_set()
    {
        var bundle = CreateBundle();
        bundle.Metadata.PinnedUtilityTabKey = "tab-utility";
        bundle.Metadata.UtilitySessions[GenerationJobId.ProposeMemories] = new GenerationUtilitySession
        {
            ConversationId = UtilityConvId,
            JobCount = 2,
        };

        var status = GenerationUtilitySessionService.FormatUtilityStatus(
            bundle,
            GenerationJobId.ProposeMemories);

        Assert.Contains("utility tab pinned", status, StringComparison.Ordinal);
        Assert.Contains("2 job(s)", status, StringComparison.Ordinal);
    }

    private static AdventureBundle CreateBundle(string? playConversationId = null) =>
        new()
        {
            Metadata = new AdventureMetadata
            {
                Id = Guid.NewGuid(),
                Title = "Test Adventure",
                LinkedProjectId = ProjectId,
                LinkedConversationId = playConversationId,
            },
        };
}
