namespace ChatGPTWrapper.Adventure.Models;

/// <summary>
/// Per-adventure routing of play utility jobs to a local OpenAI-compatible server (Ollama, LM Studio).
/// Narrator / play send remain on ChatGPT.
/// </summary>
public sealed class LocalUtilityInferenceSettings
{
    /// <summary>When true, eligible utility jobs use local inference instead of ChatGPT utility lanes.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When true with <see cref="Enabled"/>, also send eligible jobs to the utility worker lane (ChatGPT)
    /// and collect both local LLM and worker proposals for review.
    /// </summary>
    public bool DualRun { get; set; }

    /// <summary>Override <see cref="ChatGPTWrapper.Core.LocalInference.LocalInferenceOptions.DefaultBaseUrl"/>; null uses env/default.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Override default model; null uses env/default.</summary>
    public string? Model { get; set; }
}
