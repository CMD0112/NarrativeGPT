using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>WinUI <see cref="ChatTabHost"/> implementation of <see cref="IPlayTabRegistry"/>.</summary>
internal sealed class WinUiPlayTabRegistry : IPlayTabRegistry
{
    private readonly ChatTabHost _host;

    public WinUiPlayTabRegistry(ChatTabHost host) => _host = host;

    public object? ActiveTabHost => _host.GetActiveWebView() ?? _host.GetFirstWebView();

    public string? GetTabKey(object tabHost)
    {
        if (tabHost is not WebView2 wv)
            return null;

        var tab = _host.FindTabForWebView(wv);
        return tab?.Tag as string;
    }

    public object? FindTabHostByPinKey(string? pinKey) => _host.FindWebViewByPinKey(pinKey);

    public object? TryFindTabHostForPlaySession(AdventureBundle bundle)
    {
        var pinKey = PlayTabPinService.GetPlayPinKey(bundle);
        if (!string.IsNullOrWhiteSpace(pinKey)
            && _host.FindWebViewByPinKey(pinKey) is { } pinned)
        {
            return pinned;
        }

        return _host.GetActiveWebView() ?? _host.GetFirstWebView();
    }

    public object? ResolvePlayTabHost(AdventureBundle bundle, object? staleTabHost = null)
    {
        var session = PlayTabSessionFactory.FromBundle(bundle);
        if (PlayTabSessionResolver.ResolvePinnedTabHost(this, session) is { } pinned)
            return pinned;

        if (TryFindTabHostForPlaySession(bundle) is { } sessionTab)
            return sessionTab;

        if (staleTabHost is WebView2 stale && GetTabKey(stale) is not null)
            return stale;

        return null;
    }

    public object? GetCoreWebView(object tabHost) =>
        tabHost is WebView2 wv ? wv.CoreWebView2 : null;

    public string? GetTabTitle(object tabHost)
    {
        if (tabHost is not WebView2 wv)
            return null;

        return _host.FindTabForWebView(wv)?.Header?.ToString();
    }

    public IReadOnlyList<(string Header, object TabHost)> ListTabs() =>
        _host.ListTabs().Select(t => (t.Header, (object)t.WebView)).ToList();

    public void SelectTabHost(object tabHost)
    {
        if (tabHost is WebView2 wv)
            _host.SelectWebView(wv);
    }

    public void FocusTabHost(object tabHost)
    {
        if (tabHost is WebView2 wv)
            wv.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }
}
