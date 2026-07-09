using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace ChatGPTWrapper;

/// <summary>WPF <see cref="TabControl"/> implementation of <see cref="IPlayTabRegistry"/>.</summary>
public sealed class WpfPlayTabRegistry : IPlayTabRegistry
{
    private readonly TabControl _tabs;

    public WpfPlayTabRegistry(TabControl tabs) => _tabs = tabs;

    public object? ActiveTabHost
    {
        get
        {
            if (_tabs.SelectedItem is TabItem { Content: WebView2 wv })
                return wv;
            return ThreadTabBindingService.SelectFirstWebViewTab(_tabs);
        }
    }

    public string? GetTabKey(object tabHost) =>
        tabHost is WebView2 wv ? PlayTabPinService.GetTabKey(wv, _tabs) : null;

    public object? FindTabHostByPinKey(string? pinKey) =>
        PlayTabPinService.FindWebViewByPinKey(_tabs, pinKey);

    public object? TryFindTabHostForPlaySession(AdventureBundle bundle) =>
        PlayTabPinService.TryFindWebViewForPlaySession(_tabs, bundle);

    public object? ResolvePlayTabHost(AdventureBundle bundle, object? staleTabHost = null)
    {
        var session = PlayTabSessionFactory.FromBundle(bundle);
        if (PlayTabSessionResolver.ResolvePinnedTabHost(this, session) is { } pinned)
            return pinned;

        if (TryFindTabHostForPlaySession(bundle) is { } sessionTab)
            return sessionTab;

        if (staleTabHost is WebView2 stale
            && PlayTabPinService.GetTabKey(stale, _tabs) is not null)
        {
            return stale;
        }

        return null;
    }

    public object? GetCoreWebView(object tabHost) =>
        tabHost is WebView2 wv ? wv.CoreWebView2 : null;

    public string? GetTabTitle(object tabHost) =>
        tabHost is WebView2 wv ? ThreadTabBindingService.GetTabTitle(wv, _tabs) : null;

    public IReadOnlyList<(string Header, object TabHost)> ListTabs()
    {
        var list = new List<(string, object)>();
        foreach (var snap in ThreadTabBindingService.ListWebViewTabs(_tabs))
            list.Add((snap.Title, snap.WebView));
        return list;
    }

    public void SelectTabHost(object tabHost)
    {
        if (tabHost is not WebView2 wv)
            return;

        var tab = ThreadTabBindingService.FindTabItem(wv, _tabs);
        if (tab is not null)
            _tabs.SelectedItem = tab;
    }

    public void FocusTabHost(object tabHost)
    {
        if (tabHost is WebView2 wv)
            wv.Focus();
    }
}
