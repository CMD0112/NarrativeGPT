using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal enum DesignContextStatus
{
    Ready,
    MissingProject,
    NoConversation,
    NavigationFailed,
}

internal sealed class DesignContextResult
{
    public DesignContextStatus Status { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }

    public bool IsReady => Status == DesignContextStatus.Ready;
}

public enum DesignModeEntryIntent
{
    Default,
    LocalSourcesEdit,
}

internal static class AdventureDesignContextService
{
    /// <summary>Design mode open is offline-first; thread navigation runs only on explicit user action.</summary>
    public static bool ShouldEnsureDesignThreadOnOpen => false;

    public static bool CanOpenLocalSourcesEdit(AdventureBundle bundle) =>
        AdventureSourceFileService.HasLocalLoreSourceFiles(bundle);

    public static string? GetDesignConversationId(AdventureBundle bundle) =>
        GenerationUtilitySessionService.GetSession(bundle.Metadata, GenerationJobId.DesignAdventure)?.ConversationId;

    public static string FormatDesignModeOpenStatus(AdventureBundle bundle)
    {
        if (GetDesignConversationId(bundle) is not null
            && AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            return "Local sources ready. Open Project to verify the design thread for AI drafting.";
        }

        if (AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            return "Local sources ready. Open Project → New chat → Use this tab as design thread for AI drafting.";
        }

        return AdventureSourceFileService.HasLocalLoreSourceFiles(bundle)
            ? "Local sources ready. Link a Project for AI-assisted drafting."
            : "Design workspace ready. Link a Project for AI-assisted drafting.";
    }

    public static void ApplyLocalSourcesResumeStep(AdventureBundle bundle)
    {
        if (!AdventureSourceFileService.HasLocalLoreSourceFiles(bundle))
            return;

        AdventureDesignService.EnsureWorkspace(bundle);
        var step = bundle.DesignWorkspace.CurrentStep;
        if (step is AdventureDesignStep.Sources or AdventureDesignStep.Review)
            return;

        AdventureDesignService.GoToStep(bundle, AdventureDesignStep.Sources);
    }

    /// <summary>Post-finalize re-entry: always land on Sources for local file work.</summary>
    public static void ApplyLocalSourcesEditEntry(AdventureBundle bundle)
    {
        if (!CanOpenLocalSourcesEdit(bundle))
            return;

        AdventureDesignService.EnsureWorkspace(bundle);
        AdventureDesignService.GoToStep(bundle, AdventureDesignStep.Sources);
    }

    public static string FormatLocalSourcesEditStatus(AdventureBundle bundle) =>
        CanOpenLocalSourcesEdit(bundle)
            ? "Editing local sources. Regenerate JSON or edit files on disk — Play mode stays available."
            : FormatDesignModeOpenStatus(bundle);

    public static async Task<DesignContextResult> EnsureDesignThreadAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        GenerationJobService jobService,
        AdventureTurnService? turnService = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && !AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            return new DesignContextResult
            {
                Status = DesignContextStatus.MissingProject,
                Error = "Link a ChatGPT Project first.",
            };
        }

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);

        if (ProjectChatDraftService.ShouldStayOnProjectPage(bundle, core.Source))
        {
            return new DesignContextResult
            {
                Status = DesignContextStatus.Ready,
                ConversationId = GetDesignConversationId(bundle),
            };
        }

        var session = await jobService.EnsureUtilityConversationAsync(
            core,
            bundle,
            GenerationJobId.DesignAdventure,
            turnService: turnService,
            seedIfNeeded: false,
            cancellationToken: cancellationToken);

        if (session is null || string.IsNullOrWhiteSpace(session.ConversationId))
        {
            return new DesignContextResult
            {
                Status = DesignContextStatus.NoConversation,
                Error = bundle.Metadata.UtilityConversationLastError
                         ?? DesignTabPinService.DesignPinRequiredError,
            };
        }

        var gizmoId = ChatGptUrls.NormalizeGizmoId(bundle.Metadata.LinkedProjectId);
        var targetUrl = ChatGptUrls.BuildProjectConversationUrl(session.ConversationId, gizmoId);
        bundle.Metadata.PinnedDesignTabUrl = targetUrl;
        AdventureStore.Save(bundle);

        if (!AdventurePlayContextService.IsOnProjectConversationPage(core.Source, session.ConversationId, gizmoId))
        {
            core.Navigate(targetUrl);
            if (!await WaitForDesignNavigationAsync(core, session.ConversationId, gizmoId, cancellationToken))
            {
                return new DesignContextResult
                {
                    Status = DesignContextStatus.NavigationFailed,
                    ConversationId = session.ConversationId,
                    Error = "Timed out waiting for the design thread to load.",
                };
            }
        }

        return new DesignContextResult
        {
            Status = DesignContextStatus.Ready,
            ConversationId = session.ConversationId,
        };
    }

    public static async Task<DesignContextResult> PrepareDesignBrowserAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && !AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            return new DesignContextResult
            {
                Status = DesignContextStatus.MissingProject,
                Error = "Link a ChatGPT Project first.",
            };
        }

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);

        var targetUrl = AdventureNavigationService.ResolveDesignBrowseUrl(bundle, preferThread: false);
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return new DesignContextResult
            {
                Status = DesignContextStatus.MissingProject,
                Error = "Link a ChatGPT Project first.",
            };
        }

        if (!DesignTabPinService.IsOnDesignTarget(core.Source, bundle)
            && AdventureNavigationService.ShouldNavigateToDesignTarget(core.Source, bundle, targetUrl))
        {
            core.Navigate(targetUrl);
            await WaitForChatGptNavigationAsync(core, cancellationToken);
        }

        return new DesignContextResult
        {
            Status = DesignContextStatus.Ready,
            ConversationId = GetDesignConversationId(bundle),
        };
    }

    private static async Task WaitForChatGptNavigationAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(core.Source))
            return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && !string.IsNullOrWhiteSpace(core.Source))
                tcs.TrySetResult(true);
        }

        core.NavigationCompleted += Handler;
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            /* navigation may still have completed */
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static async Task<bool> WaitForDesignNavigationAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        if (AdventurePlayContextService.IsOnProjectConversationPage(core.Source, conversationId, gizmoId))
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess
                && AdventurePlayContextService.IsOnProjectConversationPage(core.Source, conversationId, gizmoId))
            {
                tcs.TrySetResult(true);
            }
        }

        core.NavigationCompleted += Handler;
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            return AdventurePlayContextService.IsOnProjectConversationPage(core.Source, conversationId, gizmoId);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }
}
