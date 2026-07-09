using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class WebViewCookieSync
{
    public static async Task CopyChatGptCookiesAsync(
        CoreWebView2 source,
        CoreWebView2 target,
        CancellationToken cancellationToken = default)
    {
        var cookies = await GetChatGptCookiesAsync(source, cancellationToken);
        await ApplyChatGptCookiesAsync(target, cookies, cancellationToken);
    }

    public static async Task<IReadOnlyList<CoreWebView2Cookie>> GetChatGptCookiesAsync(
        CoreWebView2 source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await source.CookieManager.GetCookiesAsync("https://chatgpt.com");
    }

    public static Task ApplyChatGptCookiesAsync(
        CoreWebView2 target,
        IReadOnlyList<CoreWebView2Cookie> cookies,
        CancellationToken cancellationToken = default)
    {
        foreach (var cookie in cookies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.CookieManager.AddOrUpdateCookie(cookie);
        }

        return Task.CompletedTask;
    }
}
