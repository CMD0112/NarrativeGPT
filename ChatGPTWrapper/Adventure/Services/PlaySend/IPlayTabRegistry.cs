using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Host-neutral lookup for play chat tabs (WPF TabControl or WinUI TabView).
/// </summary>
public interface IPlayTabRegistry
{
    /// <summary>Opaque tab host control used as turn-service and bridge identity.</summary>
    object? ActiveTabHost { get; }

    string? GetTabKey(object tabHost);

    object? FindTabHostByPinKey(string? pinKey);

    object? TryFindTabHostForPlaySession(AdventureBundle bundle);

    object? ResolvePlayTabHost(AdventureBundle bundle, object? staleTabHost = null);

    /// <summary>Host WebView2 core (WPF or WinUI); use <see cref="PlayWebViewCoreBridge"/> for property access.</summary>
    object? GetCoreWebView(object tabHost);

    string? GetTabTitle(object tabHost);

    IReadOnlyList<(string Header, object TabHost)> ListTabs();

    void SelectTabHost(object tabHost);

    void FocusTabHost(object tabHost);
}
