namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Display-hide hint pushed to the play WebView for revision artifacts (CMD-354).</summary>
public sealed class RevisionHideEntry
{
    public string MessageId { get; set; } = "";

    public string? MessageKind { get; set; }

    public string? PromptPrefix { get; set; }

    public string? AssistantDomTurnId { get; set; }
}
