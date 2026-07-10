using System.Net;
using System.Net.Http;
using System.Text;
using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class OpenAiCompatibleChatClientTests
{
    [Fact]
    public void ModelNameMatches_accepts_ollama_prefix_variants()
    {
        var available = new[] { "qwen2.5:7b-instruct", "llama3.2:latest" };

        Assert.True(OpenAiCompatibleChatClient.ModelNameMatches(available, "qwen2.5:7b-instruct"));
        Assert.True(OpenAiCompatibleChatClient.ModelNameMatches(available, "qwen2.5:7b"));
        Assert.False(OpenAiCompatibleChatClient.ModelNameMatches(available, "mistral:7b"));
    }

    [Fact]
    public async Task CompleteAsync_parses_successful_openai_shape_response()
    {
        const string json = """
            {
              "model": "lab-model",
              "choices": [
                {
                  "message": { "role": "assistant", "content": "hello" },
                  "finish_reason": "stop"
                }
              ],
              "usage": { "prompt_tokens": 3, "completion_tokens": 1 }
            }
            """;

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        using var client = new OpenAiCompatibleChatClient(
            new LocalInferenceOptions { BaseUrl = "http://lab.test", Model = "lab-model" },
            http);

        var result = await client.CompleteAsync(new ChatCompletionRequest
        {
            Messages = [ChatMessage.User("hi")],
        });

        Assert.True(result.Success);
        Assert.Equal("hello", result.Content);
        Assert.Equal("lab-model", result.Model);
        Assert.Equal(3, result.PromptTokens);
        Assert.Equal(1, result.CompletionTokens);
        Assert.Equal("stop", result.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_returns_error_on_http_failure()
    {
        const string json = """{ "error": { "message": "model not found" } }""";

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        using var http = new HttpClient(handler);
        using var client = new OpenAiCompatibleChatClient(
            new LocalInferenceOptions { BaseUrl = "http://lab.test", Model = "missing" },
            http);

        var result = await client.CompleteAsync(new ChatCompletionRequest
        {
            Messages = [ChatMessage.User("hi")],
        });

        Assert.False(result.Success);
        Assert.Equal("model not found", result.Error);
        Assert.Equal(404, result.HttpStatus);
    }

    [Fact]
    public async Task ProbeAsync_uses_ollama_tags_when_openai_models_empty()
    {
        const string tagsJson = """
            {
              "models": [
                { "name": "qwen2.5:7b-instruct" },
                { "name": "llama3.2:latest" }
              ]
            }
            """;

        var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("/v1/models", StringComparison.Ordinal) == true)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.RequestUri?.AbsolutePath.Contains("/api/tags", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(tagsJson, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var http = new HttpClient(handler);
        using var client = new OpenAiCompatibleChatClient(
            new LocalInferenceOptions
            {
                BaseUrl = "http://lab.test",
                Model = "qwen2.5:7b-instruct",
            },
            http);

        var health = await client.ProbeAsync();

        Assert.True(health.Reachable);
        Assert.Equal(2, health.Models.Count);
        Assert.True(health.RequestedModelAvailable);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
