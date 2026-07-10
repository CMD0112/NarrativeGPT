using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

internal enum ThreadWebViewResolveIntent
{
    ActivePin,
    NavigateTarget,
    RestoreAfterRestart,
}

internal sealed class ThreadWebViewBindingState
{
    public required WebView2 WebView { get; init; }

    public bool PinRebound { get; init; }

    public bool CreatedNewTab { get; init; }

    public string? TargetUrl { get; init; }
}

/// <summary>
/// Single entry for resolving the WebView bound to a play or design thread.
/// </summary>
internal static class ThreadWebViewResolver
{
    public static WebView2? TryFindExisting(TabControl tabs, AdventureBundle bundle, AdventureThreadKind kind)
    {
        return kind switch
        {
            AdventureThreadKind.Play => PlayTabPinService.TryFindWebViewForPlaySession(tabs, bundle)
                                        ?? PlayTabPinService.TryFindWebViewOnPlayTarget(tabs, bundle),
            AdventureThreadKind.Design => DesignTabPinService.TryFindWebViewForDesignSession(tabs, bundle)
                                          ?? DesignTabPinService.TryFindWebViewOnDesignTarget(tabs, bundle),
            AdventureThreadKind.UtilityWorker => UtilityWorkerPinService.TryFindWebViewForWorkerSession(tabs, bundle),
            _ => null,
        };
    }

    public static WebView2? SelectForRestore(TabControl tabs, AdventureBundle bundle, AdventureThreadKind kind) =>
        kind switch
        {
            AdventureThreadKind.Play => ThreadTabRestoreService.SelectWebViewForPlayRestore(tabs, bundle),
            AdventureThreadKind.Design => ThreadTabRestoreService.SelectWebViewForDesignRestore(tabs, bundle),
            AdventureThreadKind.UtilityWorker => UtilityWorkerPinService.TryFindWebViewForWorkerSession(tabs, bundle)
                                                   ?? ThreadTabBindingService.SelectFirstWebViewTab(tabs),
            _ => ThreadTabBindingService.SelectFirstWebViewTab(tabs),
        };

    public static string? ResolveTargetUrl(AdventureBundle bundle, AdventureThreadKind kind) =>
        kind switch
        {
            AdventureThreadKind.Play => AdventureNavigationService.ResolvePlayBrowseUrl(bundle),
            AdventureThreadKind.Design => AdventureNavigationService.ResolveDesignBrowseUrl(bundle, preferThread: true),
            AdventureThreadKind.UtilityWorker => UtilityWorkerPinService.GetWorkerTargetUrl(bundle),
            _ => null,
        };

    public static bool ShouldNavigateToTarget(
        AdventureBundle bundle,
        AdventureThreadKind kind,
        string? source,
        string targetUrl) =>
        kind switch
        {
            AdventureThreadKind.Play =>
                AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, targetUrl),
            AdventureThreadKind.Design =>
                AdventureNavigationService.ShouldNavigateToDesignTarget(source, bundle, targetUrl),
            _ => false,
        };

    public static bool HasPersistedSession(AdventureBundle bundle, AdventureThreadKind kind) =>
        kind switch
        {
            AdventureThreadKind.Play => PlayTabPinService.HasPersistedPlaySession(bundle),
            AdventureThreadKind.Design => DesignTabPinService.HasPersistedDesignSession(bundle)
                                            || AdventureNavigationService.HasLinkedProject(bundle),
            AdventureThreadKind.UtilityWorker => UtilityWorkerPinService.HasWorkerPin(bundle),
            _ => false,
        };

    public static bool PlayAndDesignTargetsConflict(AdventureBundle bundle) =>
        ThreadTabRestoreService.PlayAndDesignTargetsConflict(bundle);
}
