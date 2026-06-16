using System.Text.Json;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ChatGptProjectApiParsingTests
{
    [Fact]
    public void ParseConversations_accepts_numeric_conversation_id()
    {
        const string json = """
            {
              "items": [
                {
                  "id": 6789012345678901234,
                  "title": "Play thread",
                  "update_time": "1717700000"
                }
              ],
              "total": "25"
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var list = ChatGptProjectApiService.ParseConversationsForTests(doc.RootElement);

        Assert.Single(list);
        Assert.Equal("6789012345678901234", list[0].Id);
        Assert.Equal("Play thread", list[0].Title);
        Assert.NotNull(list[0].UpdatedAt);
    }

    [Fact]
    public void TryGetNextOffset_accepts_string_total()
    {
        const string json = """{"total":"40","has_more":true}""";
        using var doc = JsonDocument.Parse(json);

        var hasMore = ChatGptProjectApiService.TryGetNextOffsetForTests(doc.RootElement, batchCount: 20, currentOffset: 0, out var next);

        Assert.True(hasMore);
        Assert.Equal(20, next);
    }

    [Fact]
    public void TryReadConversationId_accepts_numeric_id()
    {
        const string json = """{"conversation_id":1234567890123456789}""";
        using var doc = JsonDocument.Parse(json);

        var id = ChatGptProjectApiService.TryReadConversationIdForTests(doc.RootElement);

        Assert.Equal("1234567890123456789", id);
    }

    [Fact]
    public void TryReadConversationId_reads_nested_conversation_object()
    {
        const string json = """{"conversation":{"conversation_id":"nested-conv-1"}}""";
        using var doc = JsonDocument.Parse(json);

        var id = ChatGptProjectApiService.TryReadConversationIdForTests(doc.RootElement);

        Assert.Equal("nested-conv-1", id);
    }
}
