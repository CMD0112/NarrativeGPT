namespace ChatGPTWrapper.Core.LocalInference;

/// <summary>
/// Connection settings for a local OpenAI-compatible inference server (Ollama, LM Studio, etc.).
/// Isolated from ChatGPT WebView workflows — for lab use and future utility-lane routing (SVA-05).
/// </summary>
public sealed class LocalInferenceOptions
{
    public const string DefaultBaseUrl = "http://127.0.0.1:11434";

    public const string DefaultModel = "qwen2.5:7b-instruct";

    public const string BaseUrlEnvVar = "CGW_OLLAMA_BASE_URL";

    public const string ModelEnvVar = "CGW_OLLAMA_MODEL";

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public string Model { get; init; } = DefaultModel;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public static LocalInferenceOptions FromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable(BaseUrlEnvVar);
        var model = Environment.GetEnvironmentVariable(ModelEnvVar);
        return new LocalInferenceOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(),
        };
    }

    public string NormalizeBaseUrl() => BaseUrl.TrimEnd('/');

    public Uri ChatCompletionsUri() =>
        new($"{NormalizeBaseUrl()}/v1/chat/completions");

    public Uri ModelsUri() =>
        new($"{NormalizeBaseUrl()}/v1/models");

    /// <summary>Ollama-native tags endpoint (not OpenAI-compatible).</summary>
    public Uri OllamaTagsUri() =>
        new($"{NormalizeBaseUrl()}/api/tags");
}
