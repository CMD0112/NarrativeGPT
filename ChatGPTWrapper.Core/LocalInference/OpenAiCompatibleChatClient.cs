using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGPTWrapper.Core.LocalInference;

/// <summary>
/// HTTP client for OpenAI-compatible <c>/v1/chat/completions</c> servers (Ollama, LM Studio).
/// Utility jobs may route here when per-adventure local utility inference is enabled (SVA-05 / SVA-11).
/// </summary>
public sealed class OpenAiCompatibleChatClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly LocalInferenceOptions _options;
    private readonly bool _ownsHttpClient;

    public OpenAiCompatibleChatClient(LocalInferenceOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? LocalInferenceOptions.FromEnvironment();
        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = _options.RequestTimeout };
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
            if (_http.Timeout == TimeSpan.FromSeconds(100))
                _http.Timeout = _options.RequestTimeout;
        }
    }

    public LocalInferenceOptions Options => _options;

    public async Task<LocalInferenceHealthResult> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        var models = await TryListModelsAsync(cancellationToken);
        if (models.Count == 0)
        {
            var tags = await TryOllamaTagsAsync(cancellationToken);
            if (tags.Error is not null && tags.Models.Count == 0)
                return LocalInferenceHealthResult.Unreachable(tags.Error);

            models = tags.Models;
        }

        if (models.Count == 0)
            return LocalInferenceHealthResult.Unreachable("No models reported by server.");

        var requested = _options.Model;
        var available = ModelNameMatches(models, requested);
        return new LocalInferenceHealthResult
        {
            Reachable = true,
            ServerLabel = _options.NormalizeBaseUrl(),
            Models = models,
            RequestedModel = requested,
            RequestedModelAvailable = available,
        };
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Messages.Count == 0)
            return ChatCompletionResult.Fail("messages_required");

        var body = BuildRequestBody(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.ChatCompletionsUri())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ChatCompletionResult.Fail(ex.Message);
        }

        var status = (int)response.StatusCode;
        OpenAiChatCompletionResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<OpenAiChatCompletionResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ChatCompletionResult.Fail($"invalid_response: {ex.Message}", status);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = payload?.Error?.Message ?? $"http_{status}";
            return ChatCompletionResult.Fail(err, status);
        }

        var choice = payload?.Choices?.FirstOrDefault();
        var content = choice?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            return ChatCompletionResult.Fail("empty_completion", status);

        return new ChatCompletionResult
        {
            Success = true,
            Content = content,
            Model = payload?.Model ?? request.Model,
            FinishReason = choice?.FinishReason,
            PromptTokens = payload?.Usage?.PromptTokens,
            CompletionTokens = payload?.Usage?.CompletionTokens,
            HttpStatus = status,
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    private async Task<IReadOnlyList<string>> TryListModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(_options.ModelsUri(), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];

            var payload = await response.Content.ReadFromJsonAsync<OpenAiModelsResponse>(
                JsonOptions,
                cancellationToken);
            return payload?.Data?
                       .Select(m => m.Id)
                       .Where(id => !string.IsNullOrWhiteSpace(id))
                       .Select(id => id!)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList()
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<(IReadOnlyList<string> Models, string? Error)> TryOllamaTagsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(_options.OllamaTagsUri(), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ([], $"tags_http_{(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(
                JsonOptions,
                cancellationToken);
            var names = payload?.Models?
                            .Select(m => !string.IsNullOrWhiteSpace(m.Name) ? m.Name : m.Model)
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .Select(n => n!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        ?? [];
            return (names, names.Count == 0 ? "tags_empty" : null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    private Dictionary<string, object?> BuildRequestBody(ChatCompletionRequest request)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model,
            ["messages"] = request.Messages
                .Select(m => new Dictionary<string, string>
                {
                    ["role"] = RoleToWire(m.Role),
                    ["content"] = m.Content,
                })
                .ToList(),
            ["stream"] = false,
        };

        if (request.Temperature is not null)
            body["temperature"] = request.Temperature;

        if (request.MaxTokens is not null)
            body["max_tokens"] = request.MaxTokens;

        if (request.JsonObjectResponse)
        {
            body["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
        }

        return body;
    }

    private static string RoleToWire(ChatMessageRole role) => role switch
    {
        ChatMessageRole.System => "system",
        ChatMessageRole.Assistant => "assistant",
        _ => "user",
    };

    internal static bool ModelNameMatches(IReadOnlyList<string> available, string requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return false;

        foreach (var name in available)
        {
            if (string.Equals(name, requested, StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.StartsWith(requested, StringComparison.OrdinalIgnoreCase)
                || requested.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
