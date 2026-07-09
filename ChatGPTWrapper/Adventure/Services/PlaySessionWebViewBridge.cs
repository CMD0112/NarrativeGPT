using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Host-neutral bridge for play WebView promote/bind from WPF or WinUI.</summary>
public static class PlaySessionWebViewBridge
{
    public static void TryPromoteThreadFromSource(AdventureBundle bundle, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        PlayThreadBindingService.TryPromoteVerifiedFromSource(bundle, source);
    }

    public static void TryPromoteThreadFromCore(AdventureBundle bundle, object coreWebView2)
    {
        try
        {
            var source = ((dynamic)coreWebView2).Source as string;
            TryPromoteThreadFromSource(bundle, source);
        }
        catch
        {
            /* best effort */
        }
    }
}
