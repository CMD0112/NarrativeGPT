namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Thread message role taxonomy for play edits and utility traffic (CMD-352).</summary>
public static class ThreadMessageKind
{
    public const string PlayUser = "play_user";
    public const string PlayAssistant = "play_assistant";
    public const string NarratorRevisionPrompt = "narrator_revision_prompt";
    public const string NarratorOriginal = "narrator_original";
    public const string NarratorReplacement = "narrator_replacement";

    public static bool IsRevisionArtifact(string? kind) =>
        string.Equals(kind, NarratorRevisionPrompt, StringComparison.Ordinal)
        || string.Equals(kind, NarratorOriginal, StringComparison.Ordinal);
}
