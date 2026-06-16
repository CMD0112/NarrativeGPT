using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ConversationConduitCacheTests
{
    [Fact]
    public void Set_and_TryGet_round_trip()
    {
        ConversationConduitCache.Invalidate("conv-a");
        ConversationConduitCache.Set("conv-a", "token-1");

        Assert.True(ConversationConduitCache.TryGet("conv-a", out var token));
        Assert.Equal("token-1", token);
    }

    [Fact]
    public void Invalidate_removes_entry()
    {
        ConversationConduitCache.Set("conv-b", "token-2");
        ConversationConduitCache.Invalidate("conv-b");

        Assert.False(ConversationConduitCache.TryGet("conv-b", out _));
    }

    [Fact]
    public void TryGetJwtExpiry_reads_exp_claim()
    {
        const string header = "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpXVCJ9";
        const string payload = "eyJleHAiOjE3ODA4MDY4MTN9";
        const string signature = "sig";
        var jwt = $"{header}.{payload}.{signature}";

        var expiry = ConversationConduitCache.TryGetJwtExpiry(jwt);

        Assert.NotNull(expiry);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1780806813), expiry);
    }
}
