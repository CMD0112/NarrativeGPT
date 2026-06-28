namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Play settings packet tab — labels and help for play-turn injection vs new-thread packets.</summary>
internal static class PlayPacketPanelCopy
{
    public const string TabHeader = "Play packet";

    public const string PlayTurnSectionHeader = "Play turn injection";

    public const string PlayTurnSectionHelp =
        "Builds the injected packet for the next Send on the bound play thread. "
        + "Uses your in-page composer first, then the fallback line or continuation queue below. "
        + "Does not apply to pasting into a new ChatGPT thread — use New ChatGPT thread below.";

    public const string SendPrecedenceHint =
        "Send precedence: 1) in-page composer (Play mode), 2) fallback player line, 3) first continuation queue line (consumed on Send).";

    public const string RepairHint =
        "Repair: if a turn reached ChatGPT without injected context, enter the raw player line below and use "
        + "Copy packet for edit repair, then paste into a user-message edit on the play thread.";

    public const string NewThreadSectionHeader = "New ChatGPT thread";

    public const string NewThreadSectionHelp =
        "Self-contained packets for pasting into a new ChatGPT conversation (New chat → paste → Send). "
        + "These ignore the composer, queue, and fallback line above. "
        + "To release the current binding and navigate automatically, use Play → Threads… or "
        + "Start narrative from sources… / Hand off to new chat…";

    public const string NewThreadRotationHint =
        "Preview or copy here for inspection. Session rotation actions also copy the packet and open your Project.";

    public const string CopyNarrativeStartButton = "Copy narrative start packet";

    public const string CopyHandoffButton = "Copy handoff packet";

    public const string CopyPlayTurnButton = "Copy play-turn packet";

    public const string PreviewPlayTurnButton = "Refresh play-turn preview";
}
