using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>Shared play-send helpers used by WPF and WinUI hosts.</summary>
public static class PlaySendHostRuntime
{
    public static string ResolvePlayerInput(
        AdventureBundle bundle,
        bool consumeQueue,
        string? composeText,
        Func<string> getComposerText,
        Func<string>? getPreviewPlayerLine = null,
        Action<AdventureBundle>? onQueueConsumed = null)
    {
        var input = !string.IsNullOrWhiteSpace(composeText)
            ? composeText.Trim()
            : getComposerText();
        if (string.IsNullOrWhiteSpace(input))
            input = getPreviewPlayerLine?.Invoke() ?? "";

        if (!string.IsNullOrWhiteSpace(input))
            return input;

        if (bundle.ContinuationQueue.Count == 0)
            return "";

        var line = bundle.ContinuationQueue[0];
        if (consumeQueue)
        {
            bundle.ContinuationQueue.RemoveAt(0);
            bundle.State.ContinuationQueue = bundle.ContinuationQueue.ToList();
            AdventureStore.Save(bundle);
            onQueueConsumed?.Invoke(bundle);
        }

        return line;
    }

    public static AttachmentContext? BuildAttachmentContext(
        PlayComposeSendEventArgs? sendRequest,
        IReadOnlyList<PlayComposePendingAttachment> pendingAttachments)
    {
        if (sendRequest?.AttachmentMeta is { Count: > 0 } meta)
        {
            return AttachmentContext.FromMeta(meta.Select(m => new ComposerAttachmentMeta
            {
                Name = m.Name,
                MimeType = m.MimeType,
                SizeBytes = m.SizeBytes,
            }));
        }

        if (pendingAttachments.Count > 0)
        {
            return AttachmentContext.FromPending(pendingAttachments.Select(a => new PlayComposePendingAttachmentRef
            {
                Name = a.Name,
                MimeType = a.MimeType,
                SizeBytes = a.Content?.LongLength,
            }));
        }

        if (sendRequest?.AttachmentsPreStaged == true)
        {
            return AttachmentContext.FromMeta(
            [
                new ComposerAttachmentMeta { Name = "attachment", MimeType = null },
            ]);
        }

        return null;
    }

    internal static async Task<PlayContextResult?> RequireLinkedPlayThreadForSendAsync(
        object coreObj,
        AdventureBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
            return null;

        var page = await PlayConversationPageService.EnsureReadyForPlaySendAsync((CoreWebView2)coreObj, bundle);
        var activePlayConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
        if (!page.Success)
        {
            return new PlayContextResult
            {
                Status = string.IsNullOrWhiteSpace(activePlayConversationId)
                    ? PlayContextStatus.NoConversation
                    : PlayContextStatus.NavigationFailed,
                ConversationId = page.ConversationId ?? activePlayConversationId,
                Error = page.Error,
            };
        }

        return new PlayContextResult
        {
            Status = PlayContextStatus.Ready,
            ConversationId = page.ConversationId ?? activePlayConversationId,
        };
    }
}
