using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class WebViewCookieSync
{
    public static async Task CopyChatGptCookiesAsync(
        CoreWebView2 source,
        CoreWebView2 target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cookies = await source.CookieManager.GetCookiesAsync("https://chatgpt.com");
        foreach (var cookie in cookies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.CookieManager.AddOrUpdateCookie(cookie);
        }
    }
}
