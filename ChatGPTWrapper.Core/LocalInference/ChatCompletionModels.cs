using System.Text.Json.Serialization;

namespace ChatGPTWrapper.Core.LocalInference;

public enum ChatMessageRole
{
    System,
    User,
    Assistant,
}

public sealed class ChatMessage
{
    public ChatMessageRole Role { get; init; }

    public string Content { get; init; } = "";

    public static ChatMessage System(string content) => new() { Role = ChatMessageRole.System, Content = content };

    public static ChatMessage User(string content) => new() { Role = ChatMessageRole.User, Content = content };

    public static ChatMessage Assistant(string content) => new() { Role = ChatMessageRole.Assistant, Content = content };
}

public sealed class ChatCompletionRequest
{
    public string Model { get; init; } = LocalInferenceOptions.DefaultModel;

    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];

    public double? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    /// <summary>When true, asks the server to return JSON (Ollama/OpenAI json_object mode).</summary>
    public bool JsonObjectResponse { get; init; }
}

public sealed class ChatCompletionResult
{
    public bool Success { get; init; }

    public string? Content { get; init; }

    public string? Model { get; init; }

    public string? FinishReason { get; init; }

    public int? PromptTokens { get; init; }

    public int? CompletionTokens { get; init; }

    public string? Error { get; init; }

    public int? HttpStatus { get; init; }

    public static ChatCompletionResult Fail(string error, int? httpStatus = null) =>
        new() { Success = false, Error = error, HttpStatus = httpStatus };
}

public sealed class LocalInferenceHealthResult
{
    public bool Reachable { get; init; }

    public string? ServerLabel { get; init; }

    public IReadOnlyList<string> Models { get; init; } = [];

    public string? RequestedModel { get; init; }

    public bool RequestedModelAvailable { get; init; }

    public string? Error { get; init; }

    public static LocalInferenceHealthResult Unreachable(string error) =>
        new() { Reachable = false, Error = error };
}

internal sealed class OpenAiChatCompletionResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAiChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }

    [JsonPropertyName("error")]
    public OpenAiErrorBody? Error { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class OpenAiMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }
}

internal sealed class OpenAiErrorBody
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class OpenAiModelsResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiModelEntry>? Data { get; set; }
}

internal sealed class OpenAiModelEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaTagModel>? Models { get; set; }
}

internal sealed class OllamaTagModel
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}
