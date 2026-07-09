using System.Text.Json;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ConversationParentCacheTests
{
    [Fact]
    public void Set_and_TryGet_round_trip()
    {
        ConversationParentCache.Invalidate("conv-a");
        ConversationParentCache.Set("conv-a", "parent-1");

        Assert.True(ConversationParentCache.TryGet("conv-a", out var parent));
        Assert.Equal("parent-1", parent);
    }

    [Fact]
    public void Invalidate_removes_entry()
    {
        ConversationParentCache.Set("conv-b", "parent-2");
        ConversationParentCache.Invalidate("conv-b");

        Assert.False(ConversationParentCache.TryGet("conv-b", out _));
    }
}

[Trait("Category", "Unit")]
public sealed class ChatGptConversationSendServiceTests
{
    [Fact]
    public void BuildSendBody_includes_required_fields()
    {
        var body = ChatGptConversationSendService.BuildSendBody(
            "conv-123",
            "parent-456",
            "g-p-test",
            "hello world");

        var dict = Assert.IsType<Dictionary<string, object?>>(body);
        Assert.Equal("next", dict["action"]);
        Assert.Equal("conv-123", dict["conversation_id"]);
        Assert.Equal("parent-456", dict["parent_message_id"]);
        Assert.Equal("g-p-test", dict["gizmo_id"]);
        var mode = Assert.IsType<Dictionary<string, object?>>(dict["conversation_mode"]);
        Assert.Equal("gizmo_interaction", mode["kind"]);
        Assert.Equal("g-p-test", mode["gizmo_id"]);
        Assert.Equal("sent", dict["client_prepare_state"]);
        Assert.NotNull(dict["model"]);

        var messages = Assert.IsType<object[]>(dict["messages"]);
        Assert.Single(messages);
        var message = Assert.IsType<Dictionary<string, object?>>(messages[0]);
        var content = Assert.IsType<Dictionary<string, object?>>(message["content"]!);
        var parts = Assert.IsType<string[]>(content["parts"]!);
        Assert.Equal("hello world", parts[0]);
    }

    [Fact]
    public void ExtractCurrentNode_reads_current_node_or_mapping_leaf()
    {
        const string withCurrent = """{"current_node":"node-a","mapping":{"node-a":{},"node-b":{}}}""";
        using (var doc = JsonDocument.Parse(withCurrent))
        {
            Assert.Equal("node-a", ChatGptConversationSendService.ExtractCurrentNode(doc.RootElement));
        }

        const string mappingOnly = """{"mapping":{"root-node":{},"leaf-node":{}}}""";
        using (var doc = JsonDocument.Parse(mappingOnly))
        {
            Assert.Equal("leaf-node", ChatGptConversationSendService.ExtractCurrentNode(doc.RootElement));
        }

        const string tree = """
            {
              "mapping": {
                "root": { "parent": null, "children": ["mid"] },
                "mid": { "parent": "root", "children": ["leaf"] },
                "leaf": { "parent": "mid", "children": [] }
              }
            }
            """;
        using (var doc = JsonDocument.Parse(tree))
        {
            Assert.Equal("leaf", ChatGptConversationSendService.ExtractCurrentNode(doc.RootElement));
        }
    }

    [Theory]
    [InlineData("missing_parent_message_id", true)]
    [InlineData("missing_conduit_token", true)]
    [InlineData("prepare_failed", true)]
    [InlineData("http_422", true)]
    [InlineData("http_401", false)]
    [InlineData("http_403", false)]
    [InlineData("timeout", false)]
    public void IsRetryableSendError_matches_expected_errors(string error, bool expected) =>
        Assert.Equal(expected, ChatGptConversationSendService.IsRetryableSendError(error));

    [Fact]
    public void BuildSendBody_omits_conversation_id_for_client_created_root_first_send()
    {
        var body = ChatGptConversationSendService.BuildSendBody(
            "client-uuid",
            ChatGptConversationSendService.ClientCreatedRootParentId,
            "g-p-test",
            "hello");

        var dict = Assert.IsType<Dictionary<string, object?>>(body);
        Assert.False(dict.ContainsKey("conversation_id"));
        Assert.False(dict.ContainsKey("gizmo_id"));
        Assert.Equal("none", dict["client_prepare_state"]);
        Assert.Equal(ChatGptConversationSendService.ClientCreatedRootParentId, dict["parent_message_id"]);
    }

    [Fact]
    public void BuildPrepareBody_includes_conversation_and_parent()
    {
        var body = ChatGptConversationSendService.BuildPrepareBody(
            "conv-123",
            "parent-456",
            "g-p-test");

        var dict = Assert.IsType<Dictionary<string, object?>>(body);
        Assert.Equal("conv-123", dict["conversation_id"]);
        Assert.Equal("parent-456", dict["parent_message_id"]);
        Assert.Equal("g-p-test", dict["gizmo_id"]);
    }

    [Fact]
    public void ExtractConduitToken_reads_token_field()
    {
        const string json = """{"status":"ok","conduit_token":"ct_test"}""";
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ct_test", ChatGptConversationSendService.ExtractConduitToken(doc.RootElement));
    }

    [Fact]
    public void TrySeedParentCache_sets_parent_from_init_response()
    {
        ConversationParentCache.Invalidate("conv-init");
        const string json = """
            {
              "conversation_id": "conv-init",
              "current_node": "root-node-1"
            }
            """;

        using var doc = JsonDocument.Parse(json);
        ChatGptConversationSendService.TrySeedParentCache("conv-init", doc.RootElement);

        Assert.True(ConversationParentCache.TryGet("conv-init", out var parent));
        Assert.Equal("root-node-1", parent);
    }

    [Fact]
    public void BootstrapNewConversationParent_seeds_client_root_parent()
    {
        ConversationParentCache.Invalidate("conv-new");

        var parent = ChatGptConversationSendService.BootstrapNewConversationParent("conv-new");

        Assert.Equal(ChatGptConversationSendService.ClientCreatedRootParentId, parent);
        Assert.True(ConversationParentCache.TryGet("conv-new", out var cached));
        Assert.Equal(ChatGptConversationSendService.ClientCreatedRootParentId, cached);
    }

    [Fact]
    public void ResolveCurrentNodeOrBootstrap_bootstraps_empty_conversation()
    {
        ConversationParentCache.Invalidate("conv-empty");
        const string json = """{"conversation_id":"conv-empty","mapping":{}}""";

        using var doc = JsonDocument.Parse(json);
        var parent = ChatGptConversationSendService.ResolveCurrentNodeOrBootstrap("conv-empty", doc.RootElement);

        Assert.False(string.IsNullOrWhiteSpace(parent));
        Assert.True(ConversationParentCache.TryGet("conv-empty", out var cached));
        Assert.Equal(parent, cached);
    }

    [Fact]
    public void ResolveCurrentNodeOrBootstrap_uses_existing_node_without_bootstrap()
    {
        ConversationParentCache.Invalidate("conv-has-node");
        const string json = """{"current_node":"existing-node","mapping":{}}""";

        using var doc = JsonDocument.Parse(json);
        var parent = ChatGptConversationSendService.ResolveCurrentNodeOrBootstrap("conv-has-node", doc.RootElement);

        Assert.Equal("existing-node", parent);
        Assert.True(ConversationParentCache.TryGet("conv-has-node", out var cached));
        Assert.Equal("existing-node", cached);
    }
}
