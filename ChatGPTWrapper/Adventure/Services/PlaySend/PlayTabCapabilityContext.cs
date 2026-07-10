using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Inputs for <see cref="PlayTabCapabilityResolver"/> without WPF when constructed via factory helpers.
/// </summary>
internal readonly record struct PlayTabCapabilityContext(
    AdventureBundle? Bundle,
    string? SourceUrl,
    string? CandidateTabKey,
    string? PlayPinTabKey,
    bool IsDraftTab,
    ProjectChatDraftKind? ActiveDraftKind,
    bool IsPlayMode)
{
    public static PlayTabCapabilityContext FromRegistry(
        AdventureBundle? bundle,
        object? tabHost,
        IPlayTabRegistry registry,
        string? source = null)
    {
        if (bundle is null)
        {
            return new PlayTabCapabilityContext(
                null,
                source ?? (tabHost is not null ? PlayWebViewCoreBridge.GetSource(registry.GetCoreWebView(tabHost)) : null),
                null,
                null,
                false,
                null,
                false);
        }

        source ??= tabHost is not null ? PlayWebViewCoreBridge.GetSource(registry.GetCoreWebView(tabHost)) : null;
        var candidateTabKey = tabHost is not null ? registry.GetTabKey(tabHost) : null;
        var pinKey = PlayTabPinService.GetPlayPinKey(bundle);
        var draftKind = ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id);
        var isDraftTab = tabHost is not null
                         && ProjectChatDraftService.IsDraftTabHost(bundle, tabHost, registry);

        return new PlayTabCapabilityContext(
            bundle,
            source,
            candidateTabKey,
            pinKey,
            isDraftTab,
            draftKind,
            IsPlayMode: true);
    }

    public static PlayTabCapabilityContext From(
        AdventureBundle? bundle,
        WebView2? webView,
        TabControl? tabs,
        string? source = null)
    {
        if (bundle is null)
        {
            return new PlayTabCapabilityContext(
                null,
                source ?? webView?.CoreWebView2?.Source,
                null,
                null,
                false,
                null,
                false);
        }

        if (tabs is null)
        {
            source ??= webView?.CoreWebView2?.Source;
            return new PlayTabCapabilityContext(
                bundle,
                source,
                null,
                PlayTabPinService.GetPlayPinKey(bundle),
                false,
                ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id),
                IsPlayMode: true);
        }

        return FromRegistry(bundle, webView, new WpfPlayTabRegistry(tabs), source);
    }

    public static PlayTabCapabilityContext FromUrl(
        AdventureBundle bundle,
        string? sourceUrl,
        string? candidateTabKey = null,
        bool isDraftTab = false,
        ProjectChatDraftKind? draftKind = null)
    {
        return new PlayTabCapabilityContext(
            bundle,
            sourceUrl,
            candidateTabKey,
            PlayTabPinService.GetPlayPinKey(bundle),
            isDraftTab,
            draftKind,
            IsPlayMode: true);
    }
}
