namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityWorkerSetupCopy
{
    public const string DialogTitle = "Utility worker";

    public const string TabIntro =
        "Background AI jobs (Extract, Summarize, etc.) run in a dedicated chat inside your "
        + "already-linked ChatGPT Project — not on your play thread. "
        + "This does not create a new Project; it creates a new conversation in the one linked above.";

    /// <summary>First worker chat in the linked Project.</summary>
    public const string SetupButton = "Create worker chat";

    public const string UseCurrentTabButton = "Use current tab as worker";

    public const string VerifyButton = "Verify connection";

    public const string OpenWorkerButton = "Open worker chat";

    /// <summary>Replace worker pin with a new chat in the same linked Project.</summary>
    public const string ReplaceWorkerButton = "New worker chat…";

    public const string LinkProjectFirstMessage =
        "Link this adventure to a ChatGPT Project first (Link project… at the top of this dialog).\n\n"
        + "The utility worker is a conversation inside that Project — the wrapper does not create a new Project for you.";

    public const string SetupInProgressStatus = "Creating worker chat in linked Project…";

    public const string ManualCreateWaitingStatus =
        "Utility worker: click New chat in ChatGPT (Utility worker tab)…";

    public const string ManualCreatePromptMessage =
        "The wrapper couldn't start a new chat automatically.\n\n"
        + "The Utility worker tab is selected on your linked Project. "
        + "In ChatGPT, click New chat in that tab.\n\n"
        + "Setup will continue automatically when the chat opens — "
        + "pinning and verification do not require any further steps.";

    public const string ManualCreateTimeoutMessage =
        "Timed out waiting for a new chat in the Utility worker tab.\n\n"
        + "Open the linked Project, click New chat, then use "
        + "\"Use current tab as worker\" or try Create worker chat again.";

    public const string SetupSuccessMessage =
        "Utility worker chat is ready in your linked Project.\n\n"
        + "Manual AI actions will queue on this chat in the background while you play.";

    public const string RegisteringWorkerStatus =
        "Utility worker: registering and verifying…";

    public const string VerifyInProgressStatus =
        "Utility worker: verifying connection…";

    public const string VerifySuccessStatus =
        "Utility worker: ready.";

    public static string VerifyFailedStatus(string? probeError) =>
        probeError is { Length: > 0 }
            ? $"Utility worker: verify failed ({probeError})"
            : "Utility worker: verify failed.";

    public static string SetupPartialMessage(string? probeError) =>
        "Utility worker chat is pinned, but verification failed"
        + (string.IsNullOrWhiteSpace(probeError) ? "." : $" ({probeError}).")
        + "\n\nClick Verify connection, or try Create worker chat again.";

    public const string SetupReservedThreadMessage =
        "Could not open a new chat in your linked Project for the utility worker.\n\n"
        + "Automatic and manual detection both failed, or the chat was your play/design thread. "
        + "Try Create worker chat again, or open a fresh Project chat and use "
        + "\"Use current tab as worker\".";

    public const string SetupFailedMessage =
        "Could not open a new chat in your linked Project.\n\n"
        + "Make sure ChatGPT is signed in and the Project page loads, then try again.";

    public const string PinCurrentTabFailedMessage =
        "Could not use this tab as the utility worker.\n\n"
        + "Select a chat in your linked Project that is not your play or design thread.";

    public static string ReplaceWorkerConfirmMessage(string? linkedProjectId)
    {
        var project = string.IsNullOrWhiteSpace(linkedProjectId)
            ? "your linked Project"
            : $"Project {linkedProjectId}";
        return $"Open a new chat in {project} and use it as the utility worker?\n\n"
               + "The current worker binding will be replaced. Your play and design threads are unchanged.";
    }

    public static string FormatStepProject(bool linked, string? linkedProjectId) =>
        linked && !string.IsNullOrWhiteSpace(linkedProjectId)
            ? $"1. ChatGPT Project linked ✓ ({linkedProjectId})"
            : linked
                ? "1. ChatGPT Project linked ✓"
                : "1. ChatGPT Project — link one above (Link project…)";

    public static string FormatStepWorkerChat(bool pinned, bool hasLinkedProject) =>
        pinned
            ? "2. Worker chat in Project ✓"
            : hasLinkedProject
                ? "2. Worker chat — Create worker chat (auto; asks you to click New chat only if needed)"
                : "2. Worker chat — link a Project first";

    public static string FormatStepVerified(
        bool green,
        bool pinned,
        bool hostReady,
        bool apiRegistered,
        string? error)
    {
        if (green && pinned)
            return "3. Connection verified ✓";

        if (green && !pinned)
            return "3. Connection verified — pin worker chat (Use current tab as worker)";

        if (!pinned)
            return "3. Connection — pin a worker chat first";

        if (!hostReady)
            return error is { Length: > 0 }
                ? $"3. Connection — page not ready ({error})"
                : "3. Connection — open worker tab in ChatGPT";

        if (!apiRegistered)
            return error is { Length: > 0 }
                ? $"3. Connection — registering failed ({error})"
                : "3. Connection — registering with ChatGPT…";

        return error is { Length: > 0 }
            ? $"3. Connection — verify failed ({error})"
            : "3. Connection — click Verify connection";
    }

    public static UtilityConnectionBannerState ResolveConnectionBannerState(
        bool green,
        bool projectLinked,
        bool pinned,
        bool hostReady,
        string? error)
    {
        if (!projectLinked)
            return UtilityConnectionBannerState.Hidden;

        if (green && pinned)
            return UtilityConnectionBannerState.Ready;

        if (green && !pinned)
            return UtilityConnectionBannerState.InProgress;

        if (!pinned || !hostReady || error is { Length: > 0 })
            return error is { Length: > 0 }
                ? UtilityConnectionBannerState.Error
                : UtilityConnectionBannerState.InProgress;

        return UtilityConnectionBannerState.InProgress;
    }

    public static string FormatConnectionBanner(
        bool green,
        bool projectLinked,
        bool pinned,
        bool hostReady,
        bool apiRegistered,
        string? error)
    {
        if (!projectLinked)
            return "Link a ChatGPT Project on the Play tab first.";

        if (green && pinned)
            return apiRegistered
                ? "Utility worker ready — background AI jobs can use the worker lane."
                : "Utility worker ready (DOM registered) — background AI jobs can use the worker lane.";

        if (green && !pinned)
            return "Worker verified but not pinned — open the worker chat tab, then click Use current tab as worker.";

        if (!pinned)
            return "Pin or create a worker chat in your linked Project.";

        if (!hostReady)
            return error is { Length: > 0 }
                ? $"Worker page not ready ({error}). Open the utility tab in ChatGPT."
                : "Open the utility worker tab in ChatGPT, then verify.";

        if (!apiRegistered)
            return error is { Length: > 0 }
                ? $"Registration incomplete ({error}). Click Verify connection."
                : "Registering worker with ChatGPT… Click Verify connection.";

        return error is { Length: > 0 }
            ? $"Verify failed ({error})."
            : "Click Verify connection.";
    }

    public static string FormatCapabilityDetail(
        bool hostReady,
        bool apiRegistered,
        bool domRegistered,
        string? probeError)
    {
        if (!hostReady)
            return "Capabilities: page not ready";

        var parts = new List<string> { "Host ready" };
        if (apiRegistered)
            parts.Add("API registered");
        else if (domRegistered)
            parts.Add("DOM registered");
        else
            parts.Add("not registered");

        if (probeError is { Length: > 0 })
            parts.Add($"last error: {probeError}");

        return string.Join(" · ", parts);
    }

    public static string FormatWorkerDetail(string? conversationId, string? tabTitle)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return "No worker chat yet — Create worker chat opens a new conversation in your linked Project.";

        var id = conversationId.Length > 14 ? conversationId[..14] + "…" : conversationId;
        var tab = string.IsNullOrWhiteSpace(tabTitle) ? "" : $" · tab \"{tabTitle}\"";
        return $"Worker chat {id}{tab}";
    }
}
