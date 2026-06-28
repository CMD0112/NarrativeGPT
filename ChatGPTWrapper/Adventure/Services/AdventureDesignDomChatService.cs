using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureDesignDomChatService
{
    public static bool TryGetDesignConversationId(
        AdventureBundle bundle,
        CoreWebView2 core,
        out string? conversationId,
        out string? error)
    {
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        return DesignTabPinService.TryResolveDesignConversationFromSource(
            bundle,
            core.Source,
            out conversationId,
            out error);
    }

    public static string FormatSendError(string? error) =>
        error switch
        {
            "automation_disabled" =>
                "Adventure automation is disabled — enable it in adventure settings to send design prompts.",
            "composer_not_found" or "utility_page_not_ready" =>
                "ChatGPT composer is not ready — select the design thread tab and wait for the page to finish loading.",
            "timeout" or "capture_timeout" =>
                "Timed out waiting for ChatGPT — try again when the design thread has finished loading.",
            "design_tab_not_on_conversation" =>
                "Open a Project chat in the design browser tab, or pin that tab with “Use this tab as design thread”.",
            "design_same_as_play_thread" =>
                "Create a New chat in the Project for design — not the play thread.",
            "design_no_project" => "Link a ChatGPT Project first.",
            _ when !string.IsNullOrWhiteSpace(error) && error.Contains(' ') => error,
            _ => FormatPinError(error),
        };

    public static string FormatPinError(string? error) =>
        error switch
        {
            "design_tab_not_on_conversation" =>
                "Open a Project chat in the design browser tab, or pin that tab with “Use this tab as design thread”.",
            "design_same_as_play_thread" =>
                "Create a New chat in the Project for design — not the play thread.",
            "design_no_project" => "Link a ChatGPT Project first.",
            _ => "Pin a design thread before sending prompts.",
        };

    public static void PersistDesignSession(AdventureBundle bundle, string conversationId)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var designEntry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)
                            ?? AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design);
        AdventureThreadRegistryService.UpdateConversationId(bundle, designEntry.Id, conversationId);

        var jobId = GenerationJobId.DesignAdventure;
        bundle.Metadata.UtilitySessions ??= new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);
        if (bundle.Metadata.UtilitySessions.TryGetValue(jobId, out var existing))
        {
            existing.ConversationId = conversationId;
            existing.LastUsedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            bundle.Metadata.UtilitySessions[jobId] = new GenerationUtilitySession
            {
                ConversationId = conversationId,
                Sequence = GenerationUtilitySessionService.GetNextSequence(bundle.Metadata, jobId),
                SeedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId),
                CreatedAt = DateTimeOffset.UtcNow,
                LastUsedAt = DateTimeOffset.UtcNow,
            };
        }

        AdventureStore.Save(bundle);
    }

    public static async Task<DesignChatSendResult> SendPromptAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        AdventureTurnService turnService,
        string promptText,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDesignConversationId(bundle, core, out var conversationId, out var pinError)
            || string.IsNullOrWhiteSpace(conversationId))
        {
            return new DesignChatSendResult
            {
                Success = false,
                Error = FormatSendError(pinError),
            };
        }

        PersistDesignSession(bundle, conversationId);

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        await turnService.EnsureUtilityComposerReadyAsync(
            core,
            cancellationToken: cancellationToken,
            conversationId: conversationId,
            gizmoId: gizmoId);

        var priorPlayConversationId = bundle.Metadata.LinkedConversationId;

        var result = await turnService.SendPromptAsync(
            core,
            bundle,
            promptText,
            cancellationToken: cancellationToken);

        if (!string.Equals(
                bundle.Metadata.LinkedConversationId,
                priorPlayConversationId,
                StringComparison.OrdinalIgnoreCase))
        {
            bundle.Metadata.LinkedConversationId = priorPlayConversationId;
            AdventureStore.Save(bundle);
        }

        if (!result.Success)
        {
            return new DesignChatSendResult
            {
                Success = false,
                Error = FormatSendError(result.Error),
            };
        }

        if (string.IsNullOrWhiteSpace(result.NarratorText))
        {
            return new DesignChatSendResult
            {
                Success = true,
                AssistantText = null,
                Error = "sent_no_capture",
            };
        }

        return new DesignChatSendResult
        {
            Success = true,
            AssistantText = result.NarratorText,
        };
    }

    public static async Task<DesignSourcePullResult> PullLatestSourceFilesAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        AdventureTurnService turnService,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDesignConversationId(bundle, core, out var conversationId, out var pinError)
            || string.IsNullOrWhiteSpace(conversationId))
        {
            return new DesignSourcePullResult
            {
                Success = false,
                Error = FormatSendError(pinError),
            };
        }

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);

        var capture = await turnService.CaptureLastAssistantAsync(
            core,
            expectedConversationId: conversationId,
            expectedGizmoId: gizmoId,
            cancellationToken: cancellationToken);

        if (!capture.Success || string.IsNullOrWhiteSpace(capture.Text))
        {
            return new DesignSourcePullResult
            {
                Success = false,
                Error = FormatSendError(capture.Error ?? "capture_failed"),
            };
        }

        var expected = AdventureDesignSourcePromptService.PromptPipelineOrder.ToList();
        var saved = AdventureSourceFileService.TrySaveFromDesignReply(
            bundle,
            capture.Text,
            expected,
            "design-pull");

        if (saved == 0)
        {
            return new DesignSourcePullResult
            {
                Success = false,
                Error = "No source file blocks found in the latest design reply.",
            };
        }

        var paths = AdventureSourceFileService.ExtractFromDesignReply(bundle, capture.Text, expected)
            .Select(e => e.RelativePath)
            .ToList();

        return new DesignSourcePullResult
        {
            Success = true,
            SavedCount = saved,
            SavedPaths = paths,
        };
    }
}
