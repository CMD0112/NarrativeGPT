using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Internal worker-lane options for ephemeral utility jobs (CMD-424).</summary>
internal sealed class EphemeralUtilityRunOptions
{
    public IReadOnlyList<DomAttachmentPayload>? DomAttachments { get; init; }

    public string? JobId { get; init; }

    public IUtilityWorkerHost? WorkerHost { get; init; }

    /// <summary>Pinned utility worker conversation for attach-worker / pinned DOM fallback.</summary>
    public string? FallbackConversationId { get; init; }

    public AdventureBundle? Bundle { get; init; }
}

public enum EphemeralProjectChatPhase
{
    Create,
    Navigate,
    Send,
    Capture,
    Delete,
}

public sealed class EphemeralProjectChatRequest
{
    public required CoreWebView2 Core { get; init; }

    public required string GizmoId { get; init; }

    public required string MessageText { get; init; }

    /// <summary>Clicks New chat in project when API create paths are skipped or fail.</summary>
    public Func<CoreWebView2, CancellationToken, Task<string?>>? TryUiCreate { get; init; }

    /// <summary>When true, only the UI create path is attempted (no ConversationInit / legacy POST).</summary>
    public bool UiCreateOnly { get; init; }

    /// <summary>Skip create; composer already open on project home (caller ran UI setup).</summary>
    public bool ComposerAlreadyOpen { get; init; }

    /// <summary>DOM composer submit on project home (required when UI opens composer without a /c/ URL).</summary>
    public AdventureTurnService? TurnService { get; init; }

    /// <summary>When true, PATCH is_visible:false after capture (best-effort).</summary>
    public bool DeleteAfterCapture { get; init; } = true;

    /// <summary>Return success before hide completes (hide still runs best-effort).</summary>
    public bool DeleteInBackground { get; init; } = true;

    /// <summary>Skip session fetch when WebView is already on a signed-in project page.</summary>
    public bool WarmSession { get; init; }

    /// <summary>DOM send wait cap; API send ignores this. Defaults to ephemeral-scaled timeout.</summary>
    public int? SendTimeoutMs { get; init; }

    /// <summary>Composer polling cap before DOM send when caller already verified composer.</summary>
    public int? MaxComposerWaitSeconds { get; init; }

    public int CaptureMaxAttempts { get; init; } = 6;

    public TimeSpan CapturePollDelay { get; init; } = TimeSpan.FromSeconds(1);
}

public sealed class EphemeralProjectChatResult
{
    public bool Success { get; init; }

    public string? ResponseText { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }

    public EphemeralProjectChatPhase? FailedPhase { get; init; }

    public bool Deleted { get; init; }

    public string? DeleteError { get; init; }

    public bool StreamComplete { get; init; }

    public bool DomComposerReady { get; init; }
}

public sealed class EphemeralProvisionResult
{
    public bool Success { get; init; }

    public string? ConversationId { get; init; }

    public bool DomComposerReady { get; init; }

    public string? Error { get; init; }

    public EphemeralProjectChatPhase? FailedPhase { get; init; }
}
