using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class AdventurePlayContextTests
{
    [Fact]
    public void BuildProjectConversationUrl_uses_path_style_for_gizmo_ids()
    {
        var url = ChatGptUrls.BuildProjectConversationUrl("conv-abc", "g-p-test");

        Assert.Equal("https://chatgpt.com/g/g-p-test/c/conv-abc", url);
    }

    [Fact]
    public void ResolveProjectConversationUrl_honors_pinned_path_style_hint()
    {
        var hint = "https://chatgpt.com/g/g-p-test/c/old-thread";
        var url = ChatGptUrls.ResolveProjectConversationUrl("new-thread", "g-p-test", hint);

        Assert.Equal("https://chatgpt.com/g/g-p-test/c/new-thread", url);
    }

    [Fact]
    public void ConversationBelongsToProject_accepts_list_member()
    {
        var convs = new List<GizmoConversationRef>
        {
            new() { Id = "thread-1" },
            new() { Id = "thread-2" },
        };

        Assert.True(AdventurePlayContextService.ConversationBelongsToProject("thread-2", convs));
        Assert.False(AdventurePlayContextService.ConversationBelongsToProject("other", convs));
    }

    [Fact]
    public void ShouldAcceptLinkedConversationId_rejects_stray_id_when_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-abc",
                LinkedConversationId = "stored-thread",
            },
        };

        var convs = new List<GizmoConversationRef> { new() { Id = "stored-thread" } };

        Assert.True(AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, "stored-thread", convs));
        Assert.True(AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, "stored-thread"));
        Assert.False(AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, "random-thread", convs));
    }

    [Fact]
    public void ShouldAcceptLinkedConversationId_allows_any_id_when_not_linked()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata(),
        };

        Assert.True(AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, "any-thread"));
    }

    [Fact]
    public void PreferAdventureWebViewForLinkedProject_requires_play_mode_and_pinned_tab()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = "g-p-x",
                PinnedPlayTabKey = "tab-key",
            },
        };

        Assert.True(AdventurePlayContextService.PreferAdventureWebViewForLinkedProject(true, bundle));
        Assert.False(AdventurePlayContextService.PreferAdventureWebViewForLinkedProject(false, bundle));
        Assert.False(AdventurePlayContextService.PreferAdventureWebViewForLinkedProject(true, null));
        Assert.False(AdventurePlayContextService.PreferAdventureWebViewForLinkedProject(
            true,
            new AdventureBundle { Metadata = new AdventureMetadata { LinkedProjectId = "g-p-x" } }));
    }

    [Fact]
    public void BuildProjectConversationUrl_parses_back_to_conversation_and_project()
    {
        var url = ChatGptUrls.BuildProjectConversationUrl("thread-1", "g-p-test");
        var uri = new Uri(url);

        Assert.True(ChatGptUrls.TryParseConversationId(uri, out var convId));
        Assert.Equal("thread-1", convId);
        Assert.True(ChatGptUrls.TryParseGizmoId(uri, out var gizmoId));
        Assert.True(ChatGptUrls.GizmoIdsEqual(gizmoId, "g-p-test"));
    }

    [Fact]
    public void TryGetLinkedProjectConversationFromUrl_reads_project_path_url()
    {
        var url = ChatGptUrls.BuildProjectConversationUrl("thread-1", "g-p-test");

        Assert.True(
            AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(url, "g-p-test", out var convId));
        Assert.Equal("thread-1", convId);
    }

    [Fact]
    public void IsOnPlayConversationPage_accepts_conversation_url_without_project_query()
    {
        const string convId = "abc-123";
        var withProject = ChatGptUrls.BuildProjectConversationUrl(convId, "g-p-test");
        var withoutProject = ChatGptUrls.BuildConversationUrl(convId);

        Assert.True(AdventurePlayContextService.IsOnPlayConversationPage(withProject, convId, "g-p-test"));
        Assert.True(AdventurePlayContextService.IsOnPlayConversationPage(withoutProject, convId, "g-p-test"));
        Assert.False(AdventurePlayContextService.IsOnPlayConversationPage(withoutProject, "other-id", "g-p-test"));
    }

    [Fact]
    public void IsOnPlayConversationPage_rejects_wrong_project_with_same_conversation_id()
    {
        const string convId = "abc-123";
        const string expectedProject = "g-p-test";
        const string otherProject = "g-p-other";
        var wrongProjectUrl = ChatGptUrls.BuildProjectConversationUrl(convId, otherProject);

        Assert.False(AdventurePlayContextService.IsOnPlayConversationPage(
            wrongProjectUrl,
            convId,
            expectedProject));
    }

    [Fact]
    public void TryParseGizmoId_reads_g_p_segment_from_project_path_url()
    {
        const string projectId = "g-p-6a220fab2eb48191a75b9d88d85a3d91";
        const string convId = "3819a292-37f5-4e50-a91f-a6208ff4a699";
        var url = $"https://chatgpt.com/g/g-p-6a220fab2eb48191a75b9d88d85a3d91/c/{convId}";

        Assert.True(ChatGptUrls.TryParseGizmoId(new Uri(url), out var gizmoId));
        Assert.True(ChatGptUrls.GizmoIdsEqual(gizmoId, projectId));
        Assert.True(AdventurePlayContextService.IsOnProjectConversationPage(url, convId, projectId));
    }
}
