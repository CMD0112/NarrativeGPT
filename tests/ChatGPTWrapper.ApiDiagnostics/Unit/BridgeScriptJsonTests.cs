using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class BridgeScriptJsonTests
{
    public static TheoryData<string, bool, string?> PingCases
    {
        get
        {
            var pongJson = """{"type":"pong","ok":true}""";
            var apiResultJson = """{"type":"apiResult","ok":true}""";
            var data = new TheoryData<string, bool, string?>();
            data.Add(pongJson, true, "pong");
            data.Add(JsonSerializer.Serialize(pongJson), true, "pong");
            data.Add(JsonSerializer.Serialize(JsonSerializer.Serialize(pongJson)), true, "pong");
            data.Add(apiResultJson, true, "apiResult");
            data.Add(JsonSerializer.Serialize(apiResultJson), true, "apiResult");
            data.Add("""{"type":"apiError","ok":false,"error":"bridge_not_injected"}""", false, "apiError");
            data.Add("{}", false, null);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PingCases))]
    public void Normalize_and_IsBridgeSuccess_handles_webview_shapes(
        string raw,
        bool expectSuccess,
        string? expectType)
    {
        var json = BridgeScriptJson.Normalize(raw);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var msg = new ApiBridgeMessage(json);
        Assert.Equal(expectType, msg.Type);
        Assert.Equal(expectSuccess, BridgeScriptJson.IsBridgeSuccess(msg));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    public void Normalize_returns_empty_for_nullish_webview_results(string raw)
    {
        var normalized = BridgeScriptJson.Normalize(raw);
        Assert.True(string.IsNullOrEmpty(normalized));
    }

    [Fact]
    public void Triple_wrapped_string_unwraps_to_pong()
    {
        var inner = """{"type":"pong","ok":true}""";
        var doubleWrapped = JsonSerializer.Serialize(inner);
        var tripleWrapped = JsonSerializer.Serialize(doubleWrapped);

        var json = BridgeScriptJson.Normalize(tripleWrapped);
        var msg = new ApiBridgeMessage(json);

        Assert.Equal("pong", msg.Type);
        Assert.True(BridgeScriptJson.IsBridgeSuccess(msg));
    }

    [Fact]
    public void Normalize_script_stringify_round_trip_from_webview()
    {
        var inner = """{"type":"pong","ok":true}""";
        var webViewRaw = JsonSerializer.Serialize(inner);

        var json = BridgeScriptJson.Normalize(webViewRaw);
        var msg = new ApiBridgeMessage(json);

        Assert.Equal("pong", msg.Type);
        Assert.True(BridgeScriptJson.IsBridgeSuccess(msg));
    }

    [Fact]
    public void Truncate_limits_length()
    {
        Assert.Equal("abc", BridgeScriptJson.Truncate("abcdef", 3));
        Assert.Equal("", BridgeScriptJson.Truncate(null, 10));
    }
}
