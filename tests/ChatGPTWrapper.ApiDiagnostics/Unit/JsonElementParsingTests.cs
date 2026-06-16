using System.Text.Json;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class JsonElementParsingTests
{
    [Fact]
    public void TryGetObjectProperty_returns_false_for_null_property()
    {
        using var doc = JsonDocument.Parse("""{"message":null}""");

        Assert.False(JsonElementParsing.TryGetObjectProperty(doc.RootElement, "message", out _));
    }

    [Fact]
    public void EnumerateObjectElements_skips_null_array_entries()
    {
        using var doc = JsonDocument.Parse("""[null,{"text":"ok"},null]""");

        var objects = JsonElementParsing.EnumerateObjectElements(doc.RootElement).ToList();

        Assert.Single(objects);
        Assert.Equal("ok", JsonElementParsing.GetStringProperty(objects[0], "text"));
    }
}
