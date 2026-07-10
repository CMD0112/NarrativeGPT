using ChatGPTWrapper.ApiDiagnostics.Live;
using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

[Trait("Category", "Live")]
public sealed class OllamaLiveTests
{
    [Fact]
    public async Task Probe_reaches_local_ollama_and_configured_model()
    {
        if (!OllamaLiveGate.IsEnabled)
            return;

        using var client = new OpenAiCompatibleChatClient();
        var health = await client.ProbeAsync();

        Assert.True(health.Reachable, health.Error);
        Assert.NotEmpty(health.Models);
        Assert.True(
            health.RequestedModelAvailable,
            $"Model '{health.RequestedModel}' not in: {string.Join(", ", health.Models)}");
    }

    [Fact]
    public async Task Chat_returns_non_empty_assistant_text()
    {
        if (!OllamaLiveGate.IsEnabled)
            return;

        using var client = new OpenAiCompatibleChatClient();
        var result = await client.CompleteAsync(new ChatCompletionRequest
        {
            Model = client.Options.Model,
            Messages = [ChatMessage.User("Reply with exactly: pong")],
            Temperature = 0,
            MaxTokens = 32,
        });

        Assert.True(result.Success, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Content));
    }

    [Fact]
    public async Task Entity_demo_returns_json_array_shape()
    {
        if (!OllamaLiveGate.IsEnabled)
            return;

        using var client = new OpenAiCompatibleChatClient();
        var result = await client.CompleteAsync(
            LocalInferenceLabScenarios.EntityExtractionDemo(client.Options.Model));

        Assert.True(result.Success, result.Error);
        var text = result.Content!.Trim();
        Assert.StartsWith("[", text);
        Assert.EndsWith("]", text);
    }
}
