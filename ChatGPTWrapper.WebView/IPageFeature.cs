using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WebView;

public interface IPageFeature
{
    string FeatureId { get; }

    Task ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken = default);

    void RegisterMessageHandlers(PageMessageRouter router);
}
