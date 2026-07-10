using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class ThreadManagerCopy
{
    public const string DialogTitle = "Manage adventure threads";

    public const string IntroText =
        "One active pin per thread kind. Create thread slots, pin browser tabs to a selected row, "
        + "and switch active to change which conversation jobs and navigation use. "
        + "Archive retires a slot without deleting local log data; Remove deletes archived rows from this list.";

    public const string NewThreadSlotButton = "New thread slot…";
    public const string PinCurrentToSelectedButton = "Pin current tab to selected";
    public const string PickTabToPinButton = "Pick browser tab…";
    public const string ClearPinButton = "Clear pin";
    public const string RemoveButton = "Remove";
    public const string ShowArchivedCheck = "Show archived threads";
    public const string StartNewDesignThreadButton = "Start new design thread…";

    public static string NewThreadSlotPrompt(AdventureThreadKind kind) => kind switch
    {
        AdventureThreadKind.Play =>
            "Enter a label for the new play thread slot (e.g. Chapter 3). "
            + "After creating, open a Project chat in a browser tab and pin it to this row.",
        AdventureThreadKind.Design =>
            "Enter a label for the new design thread slot (e.g. Cast, Framework). "
            + "After creating, open a Project chat in a browser tab and pin it to this row.",
        _ => "Enter a label for the new thread slot.",
    };

    public const string NewThreadSlotDefaultLabel = "";

    public static string RemoveConfirmBody(string label) =>
        $"Permanently remove \"{label}\" from the thread list?\n\n"
        + "Local adventure log data is kept. This cannot be undone.";

    public static string ClearPinConfirmBody(string label) =>
        $"Clear the pinned browser tab for \"{label}\"?\n\n"
        + "The conversation id is kept; navigation will use the conversation URL when available.";
}
