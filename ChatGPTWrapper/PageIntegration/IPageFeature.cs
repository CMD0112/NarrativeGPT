using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace ChatGPTWrapper.PageIntegration;

public interface IPageFeature
{
    string FeatureId { get; }

    Task ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken = default);

    void RegisterMessageHandlers(PageMessageRouter router);
}
