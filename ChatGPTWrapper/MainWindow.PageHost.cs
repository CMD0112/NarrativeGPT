using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private readonly Dictionary<WebView2, ChatGptPageHost> _pageHosts = new();

    internal ChatGptPageHost GetOrCreatePageHost(WebView2 webView)
    {
        if (!_pageHosts.TryGetValue(webView, out var host))
        {
            host = new ChatGptPageHost(webView);
            _pageHosts[webView] = host;
        }

        return host;
    }
}
