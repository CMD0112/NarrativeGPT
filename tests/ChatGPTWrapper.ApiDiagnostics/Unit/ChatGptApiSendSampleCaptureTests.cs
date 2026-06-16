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
}
