using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Views;

/// <summary>
/// Host callbacks and events wired by <see cref="WinUiPlaySettingsBridge"/> for play settings UI
/// (native WinUI workbench or legacy WPF dialog).
/// </summary>
public interface IPlaySettingsHost
{
    Func<string?>? ResolvePreviewComposerText { get; set; }

    Func<AttachmentContext?>? ResolvePreviewAttachmentContext { get; set; }

    Func<Task<int>>? ResolveThreadUserTurnCountAsync { get; set; }

    Func<Task>? SyncSourcesAsync { get; set; }

    Func<Task>? RefreshSourcesStatusAsync { get; set; }

    Func<Task>? ReconcileDuplicatesAsync { get; set; }

    Action? OpenThreadsHub { get; set; }

    event EventHandler? PinPlayTabRequested;

    event EventHandler? OpenPinnedPlayTabRequested;

    event EventHandler? ClearPlayTabPinRequested;

    event EventHandler? ReviewQueueChanged;

    Func<PlayThreadStartRequest?, Task>? StartNewPlayThreadAsync { get; set; }

    Action? OpenPlayHandoffDialog { get; set; }

    Action<ProposalReviewCategory?>? OpenProposalReviewHub { get; set; }

    Func<Task>? DraftNewProjectChatAsync { get; set; }

    Action? CancelProjectChatDraft { get; set; }

    Func<string, Task<UtilityStoryContextBuildResult>>? PreviewLiveStoryContextAsync { get; set; }

    Func<string, IReadOnlyList<DomAttachmentPayload>?, string?, Task>? RunSourceEditJobAsync { get; set; }

    Func<string, Task>? RunUtilityJobWithAttachmentsAsync { get; set; }

    Func<Task<IReadOnlyList<ConversationFileRef>>>? ListThreadFilesAsync { get; set; }

    Func<ConversationFileRef, Task<byte[]>>? DownloadThreadFileAsync { get; set; }

    Func<Task>? OpenProjectSettingsAsync { get; set; }

    Func<Task>? PushInstructionsNowAsync { get; set; }

    Func<Task>? RefreshSummaryAsync { get; set; }

    Func<Task>? SuggestMemoriesAsync { get; set; }

    Func<Task>? GenerateCardsAsync { get; set; }

    Func<Guid, Task>? ExpandStoryCardAsync { get; set; }

    Func<Task>? SyncInstructionsAsync { get; set; }

    event EventHandler? TransportSettingsCommitted;

    Func<Task>? ProbeSourcesAsync { get; set; }

    Func<Task>? OpenApiSyncDiagnosticsAsync { get; set; }

    Func<string, Task>? ProbeSourceFileAsync { get; set; }

    Func<string, string, Task<string?>>? SynthesizeSourceAsync { get; set; }

    Func<Task>? PromptThreadLogSyncAsync { get; set; }

    Func<Task>? PromptThreadLogSnapshotAsync { get; set; }

    Func<Task>? PromptThreadLogDumpAsync { get; set; }

    /// <summary>Call after host callbacks are wired — sources tab probes API sync availability.</summary>
    void RefreshHostDelegates();
}
