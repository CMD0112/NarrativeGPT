using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayComposeUiStateTests
{
    private static readonly JsonSerializerOptions ComposeJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Serialize_omits_null_fields()
    {
        var json = JsonSerializer.Serialize(
            new PlayComposeUiState { Busy = false, Focus = true },
            ComposeJsonOptions);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("busy", out var busy) && busy.GetBoolean() == false);
        Assert.True(root.TryGetProperty("focus", out var focus) && focus.GetBoolean());
        Assert.False(root.TryGetProperty("text", out _));
        Assert.False(root.TryGetProperty("clear", out _));
    }

    [Fact]
    public void Serialize_success_idle_patch_matches_intended_send_completion_contract()
    {
        var json = JsonSerializer.Serialize(
            new PlayComposeUiState
            {
                Busy = false,
                Focus = true,
                Status = "Sent.",
            },
            ComposeJsonOptions);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("clear", out _), "Success should not re-clear composer (native parity).");
        Assert.True(root.GetProperty("focus").GetBoolean());
        Assert.False(root.TryGetProperty("acceptEnabled", out _));
    }

    [Fact]
    public void Serialize_restore_patch_includes_text_and_focus()
    {
        var json = JsonSerializer.Serialize(
            new PlayComposeUiState
            {
                Text = "try again",
                Busy = false,
                Focus = true,
            },
            ComposeJsonOptions);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("try again", root.GetProperty("text").GetString());
        Assert.True(root.GetProperty("focus").GetBoolean());
    }

    [Fact]
    public void ApplyState_script_shape_matches_injection()
    {
        var state = new PlayComposeUiState { Busy = true, Status = "Sending to ChatGPT…" };
        var json = JsonSerializer.Serialize(state, ComposeJsonOptions);
        var script =
            $"(function(){{var fn=globalThis.__cgwPlayComposeApplyState;if(typeof fn==='function')fn({json});}})()";

        Assert.Contains("Sending to ChatGPT", script);
        Assert.Contains("__cgwPlayComposeApplyState", script);
    }
}
