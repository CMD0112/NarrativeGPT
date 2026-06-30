namespace ChatGPTWrapper.Core.LocalInference;

public sealed class LocalInferenceLabScenario
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public string SystemPrompt { get; init; } = "";

    public string UserPrompt { get; init; } = "";

    public double Temperature { get; init; } = 0.2;

    public bool JsonObjectResponse { get; init; }
}
