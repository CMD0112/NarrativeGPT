namespace ChatGPTWrapper.Adventure.Services;

/// <summary>User-facing labels for the two distinct play-thread rotation workflows.</summary>
internal static class PlayThreadRotationCopy
{
    public const string NarrativeFromSourcesTitle = "Start narrative from sources";
    public const string HandoffToNewChatTitle = "Hand off to new chat";

    public const string NarrativeFromSourcesButton = "Start narrative from sources…";
    public const string HandoffToNewChatButton = "Hand off to new chat…";
    public const string PreviewNarrativePacketButton = "Preview narrative start packet";
    public const string PreviewHandoffPacketButton = "Preview handoff packet";

    public const string SessionHelpText =
        "Start narrative from sources… opens a new ChatGPT thread using your source files and adventure JSON only — "
        + "no story summary or transcript from prior play. "
        + "Hand off to new chat… continues an in-progress story in a new thread (summary + optional transcript). "
        + "Draft new project chat… keeps your current play thread bound while you open the Project page.";

    public static string NarrativeFromSourcesConfirmBody(bool hasPlayHistory) =>
        (hasPlayHistory
            ? "This adventure already has play history. Starting a narrative from sources uses only your "
              + "source files and adventure JSON — it does not carry forward your story summary or transcript.\n\n"
            : "")
        + "This will release the current play thread binding (conversation id and pinned tab) "
        + "while keeping your linked Project and local adventure log.\n\n"
        + "A narrative start packet will be copied to your clipboard and your Play tab "
        + "will navigate to your Project.\n\n"
        + "Click New chat in ChatGPT, paste (Ctrl+V), and press Send.";

    public const string HandoffConfirmBody =
        "This will release the current play thread binding while keeping your linked Project "
        + "and adventure log.\n\n"
        + "A handoff packet (carry-forward summary + continuation context) will be copied "
        + "to your clipboard and your Play tab will navigate to your Project.\n\n"
        + "Click New chat in ChatGPT, paste (Ctrl+V), and press Send.";

    public const string LinkProjectFirstMessage = "Link a ChatGPT Project first.";
}
