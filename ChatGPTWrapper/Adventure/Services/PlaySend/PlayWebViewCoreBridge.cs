namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>Host-neutral access to WebView2 core instances from WPF or WinUI.</summary>
internal static class PlayWebViewCoreBridge
{
    public static string? GetSource(object? core)
    {
        if (core is null)
            return null;

        try
        {
            return ((dynamic)core).Source as string;
        }
        catch
        {
            return null;
        }
    }

    public static dynamic Require(object? core) =>
        core ?? throw new InvalidOperationException("WebView core is not ready.");
}
