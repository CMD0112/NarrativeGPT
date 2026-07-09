using ChatGPTWrapper.ChatGptApi;
using Microsoft.Playwright;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery.Automation;

internal static class PlaywrightCookieImporter
{
    private static readonly string[] CookieProbeOrigins =
    [
        "https://chatgpt.com",
        "https://www.chatgpt.com",
        "https://auth.openai.com",
        "https://openai.com",
    ];

    public static async Task<int> ImportChatGptSessionAsync(
        CoreWebView2 cookieSource,
        IBrowserContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var merged = new Dictionary<string, Cookie>(StringComparer.Ordinal);
        void AddCookies(IEnumerable<CoreWebView2Cookie> source)
        {
            foreach (var cookie in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsRelevantDomain(cookie.Domain))
                    continue;

                var key = $"{cookie.Domain}|{cookie.Path}|{cookie.Name}";
                merged[key] = ToPlaywrightCookie(cookie);
            }
        }

        var profileCookies = await cookieSource.CookieManager.GetCookiesAsync(null);
        AddCookies(profileCookies);

        foreach (var origin in CookieProbeOrigins)
        {
            var originCookies = await cookieSource.CookieManager.GetCookiesAsync(origin);
            AddCookies(originCookies);
        }

        if (merged.Count == 0)
            return 0;

        await context.AddCookiesAsync(merged.Values.ToList());
        ProjectLinkDiagnostics.Log(
            $"Headless browser imported {merged.Count} session cookie(s) from WebView2 profile");
        return merged.Count;
    }

    private static bool IsRelevantDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        var normalized = domain.Trim().TrimStart('.').ToLowerInvariant();
        return normalized.EndsWith("chatgpt.com", StringComparison.Ordinal)
               || normalized.EndsWith("openai.com", StringComparison.Ordinal);
    }

    private static Cookie ToPlaywrightCookie(CoreWebView2Cookie cookie)
    {
        var domain = cookie.Domain?.Trim();
        if (string.IsNullOrWhiteSpace(domain))
            domain = "chatgpt.com";

        return new Cookie
        {
            Name = cookie.Name,
            Value = cookie.Value,
            Domain = domain,
            Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
            Expires = ToPlaywrightExpires(cookie.Expires),
            HttpOnly = cookie.IsHttpOnly,
            Secure = cookie.IsSecure,
            SameSite = MapSameSite(cookie.SameSite),
        };
    }

    private static SameSiteAttribute? MapSameSite(CoreWebView2CookieSameSiteKind sameSite) =>
        sameSite switch
        {
            CoreWebView2CookieSameSiteKind.None => SameSiteAttribute.None,
            CoreWebView2CookieSameSiteKind.Lax => SameSiteAttribute.Lax,
            CoreWebView2CookieSameSiteKind.Strict => SameSiteAttribute.Strict,
            _ => null,
        };

    private static float? ToPlaywrightExpires(DateTime expires)
    {
        if (expires == default || expires.Year < 1971)
            return null;

        return (float)new DateTimeOffset(expires).ToUnixTimeSeconds();
    }
}
