using System.Text.Json;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ChatGptApiSendSampleCaptureTests
{
    [Fact]
    public void TryLoadSample_uses_memory_cache_on_second_read()
    {
        ChatGptApiSendSampleCapture.ClearCacheForTests();

        var dir = ChatGptApiSendSampleCapture.SamplesDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "UnitTestSample.json");
        const string json = """{"requestBody":{"action":"next"},"status":200}""";
        File.WriteAllText(path, json);

        try
        {
            Assert.True(ChatGptApiSendSampleCapture.TryLoadSample("UnitTestSample", out var first));
            Assert.True(ChatGptApiSendSampleCapture.TryLoadSample("UnitTestSample", out var second));
            Assert.Equal(first.GetRawText(), second.GetRawText());
        }
        finally
        {
            File.Delete(path);
            ChatGptApiSendSampleCapture.ClearCacheForTests();
        }
    }

    [Fact]
    public void TryLoadSample_rejects_failed_status_samples()
    {
        ChatGptApiSendSampleCapture.ClearCacheForTests();

        var dir = ChatGptApiSendSampleCapture.SamplesDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "FailedSample.json");
        const string json = """{"requestBody":{"action":"next"},"status":403}""";
        File.WriteAllText(path, json);

        try
        {
            Assert.False(ChatGptApiSendSampleCapture.TryLoadSample("FailedSample", out _));
            Assert.False(ChatGptApiSendSampleCapture.TryLoadSuccessfulRequestTemplate("FailedSample", out _));
        }
        finally
        {
            File.Delete(path);
            ChatGptApiSendSampleCapture.ClearCacheForTests();
        }
    }

    [Fact]
    public void ExtractCurrentNode_seeds_parent_cache_from_conversation_json()
    {
        ConversationParentCache.Invalidate("conv-seed");
        const string conversationJson = """
            {
              "conversation_id": "conv-seed",
              "current_node": "leaf-node",
              "mapping": { "leaf-node": {} }
            }
            """;

        using var doc = JsonDocument.Parse(conversationJson);
        var node = ChatGptConversationSendService.ExtractCurrentNode(doc.RootElement);

        Assert.Equal("leaf-node", node);
        ConversationParentCache.Set("conv-seed", node!);
        Assert.True(ConversationParentCache.IsCached("conv-seed"));
    }

    [Fact]
    public void ShouldPersistSample_skips_failed_attach_when_golden_200_exists()
    {
        ChatGptApiSendSampleCapture.ClearCacheForTests();

        var dir = ChatGptApiSendSampleCapture.SamplesDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "POST_backend-api_f_conversation_attachments.json");
        const string golden = """{"status":200,"requestBody":{"messages":[{"metadata":{"attachments":[]}}]}}""";
        File.WriteAllText(path, golden);

        try
        {
            const string attachBody = """{"messages":[{"metadata":{"attachments":[{"id":"f"}]}}]}""";
            Assert.False(ChatGptApiSendSampleCapture.ShouldPersistSampleForTests(
                "POST",
                ChatGptApiEndpoints.ConversationSend,
                403,
                attachBody));
        }
        finally
        {
            File.Delete(path);
            ChatGptApiSendSampleCapture.ClearCacheForTests();
        }
    }

    [Fact]
    public void ResolveSampleKeyForTests_maps_sentinel_chat_requirements()
    {
        Assert.Equal(
            "POST_backend-api_sentinel_chat-requirements_finalize",
            ChatGptApiSendSampleCapture.ResolveSampleKeyForTests(
                "POST",
                "/backend-api/sentinel/chat-requirements/finalize"));
    }

    [Fact]
    public void ResolveSampleKeyForTests_splits_interpreter_and_mapping_paths()
    {
        Assert.Equal(
            "GET_conversation_mapping",
            ChatGptApiSendSampleCapture.ResolveSampleKeyForTests(
                "GET",
                "/backend-api/conversation/conv-1"));

        Assert.Equal(
            "GET_conversation_interpreter_download",
            ChatGptApiSendSampleCapture.ResolveSampleKeyForTests(
                "GET",
                "/backend-api/conversation/conv-1/interpreter/download?message_id=m1&sandbox_path=%2Fmnt%2Fdata%2Fa.md"));
    }

    [Fact]
    public void SanitizeValue_redacts_authorization_and_conduit()
    {
        var auth = ChatGptApiSendHeaderCapture.SanitizeValue("authorization", "Bearer secret.token.here");
        var conduit = ChatGptApiSendHeaderCapture.SanitizeValue("x-conduit-token", "eyJhbGciOiJIUzI1NiJ9.payload.sig");

        Assert.Contains("[REDACTED]", auth);
        Assert.Contains("[REDACTED]", conduit);
        Assert.DoesNotContain("secret.token", auth);
    }

    [Fact]
    public void SummarizeGap_reports_missing_sentinel()
    {
        var golden = new Dictionary<string, string> { ["openai-sentinel"] = "abc" };
        var live = new Dictionary<string, string> { ["x-conduit-token"] = "tok…[REDACTED]" };
        var bridge = new Dictionary<string, string> { ["x-conduit-token"] = "tok…[REDACTED]", ["accept"] = "text/event-stream" };

        var summary = ChatGptApiSendHeaderCapture.SummarizeGap(golden, live, bridge);

        Assert.Contains("missing_vs_golden=[openai-sentinel]", summary);
        Assert.Contains("wire_sentinel=0", summary);
        Assert.Contains("golden_sentinel=1", summary);
        Assert.Contains("bridge_conduit=1", summary);
    }
}
