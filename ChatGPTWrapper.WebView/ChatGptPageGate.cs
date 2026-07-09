namespace ChatGPTWrapper.WebView;

/// <summary>Trusted ChatGPT top-level pages that may receive wrapper injections.</summary>
public static class ChatGptPageGate
{
    private static readonly string[] SupportedHosts =
    [
        "chatgpt.com",
        "www.chatgpt.com",
    ];

    public static bool TestAllowAnyInjectablePage { get; set; }

    public static bool IsInjectable(string? source)
    {
        if (TestAllowAnyInjectablePage && !string.IsNullOrEmpty(source))
            return true;

        if (string.IsNullOrEmpty(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var host = uri.Host;
        foreach (var supported in SupportedHosts)
        {
            if (string.Equals(host, supported, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
