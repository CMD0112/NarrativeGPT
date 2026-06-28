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

        source ??= webView?.CoreWebView2?.Source;
        var candidateTabKey = webView is not null && tabs is not null
            ? PlayTabPinService.GetTabKey(webView, tabs)
            : null;
        var pinKey = PlayTabPinService.GetPlayPinKey(bundle);
        var draftKind = ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id);
        var isDraftTab = webView is not null
                         && tabs is not null
                         && ProjectChatDraftService.IsDraftTab(bundle, webView, tabs);

        return new PlayTabCapabilityContext(
            bundle,
            source,
            candidateTabKey,
            pinKey,
            isDraftTab,
            draftKind,
            IsPlayMode: true);
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
