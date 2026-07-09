using ChatGPTWrapper.Adventure.Services;
using Microsoft.Playwright;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery.Automation;

/// <summary>
/// Reuses a single headless Playwright Chrome session across uploads. Headed mode is not supported.
/// </summary>
internal static class HeadlessBrowserSessionPool
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(5);

    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private static IBrowserContext? _context;
    private static IPage? _page;
    private static DateTime _lastUsedUtc;

    public static async Task<IPage> AcquirePageAsync(
        CoreWebView2 cookieSource,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (ShouldRecycle())
                await DisposeBrowserAsync();

            if (_page is null || _page.IsClosed)
            {
                await LaunchAsync(cookieSource, cancellationToken);
            }
            else
            {
                ProjectLinkDiagnostics.Log("Headless browser session reused (warm start)");
            }

            var cookieCount = await PlaywrightCookieImporter.ImportChatGptSessionAsync(
                cookieSource,
                _context!,
                cancellationToken);
            if (cookieCount == 0)
            {
                await DisposeBrowserAsync();
                Gate.Release();
                throw new InvalidOperationException(
                    "automation_no_session_cookies: sign in via ChatGPT Wrapper, then retry");
            }

            _lastUsedUtc = DateTime.UtcNow;
            return _page!;
        }
        catch
        {
            if (_page is null || _page.IsClosed)
                Gate.Release();
            throw;
        }
    }

    public static void Release(bool invalidate)
    {
        try
        {
            if (invalidate)
                _ = DisposeBrowserAsync();
            else
                _lastUsedUtc = DateTime.UtcNow;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool ShouldRecycle() =>
        _browser is not null && (!_browser.IsConnected || DateTime.UtcNow - _lastUsedUtc > IdleTtl);

    private static async Task LaunchAsync(CoreWebView2 cookieSource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _playwright ??= await Playwright.CreateAsync();

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "chrome",
            Headless = true,
            Args =
            [
                "--disable-blink-features=AutomationControlled",
                "--disable-quic",
            ],
        });

        string? userAgent = null;
        try
        {
            userAgent = await cookieSource.ExecuteScriptAsync("navigator.userAgent");
            userAgent = userAgent?.Trim('"');
        }
        catch
        {
            /* match WebView when possible */
        }

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 960, Height = 720 },
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            IgnoreHTTPSErrors = false,
        });

        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(12_000);
        _page.SetDefaultNavigationTimeout(60_000);
        _lastUsedUtc = DateTime.UtcNow;
        ProjectLinkDiagnostics.Log(
            $"Headless browser session launched (cold start) userAgentSynced={!string.IsNullOrWhiteSpace(userAgent)}");
    }

    private static async Task DisposeBrowserAsync()
    {
        if (_context is not null)
        {
            try { await _context.CloseAsync(); }
            catch { /* shutting down */ }
            _context = null;
        }

        if (_browser is not null)
        {
            try { await _browser.CloseAsync(); }
            catch { /* shutting down */ }
            _browser = null;
        }

        _page = null;
    }
}
