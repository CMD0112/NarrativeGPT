using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Optional WPF host hook so Chromium treats project DOM uploads as compositor-visible.
/// Set from <c>MainWindow</c> at startup.
/// </summary>
public static class ProjectDomCompositor
{
    public static Func<CoreWebView2, IDisposable?>? BeginScope { get; set; }

    internal static IDisposable? TryBegin(CoreWebView2 core) =>
        BeginScope?.Invoke(core);
}
