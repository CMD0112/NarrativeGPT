namespace ChatGPTWrapper.PageIntegration;

/// <summary>Trusted ChatGPT top-level pages that may receive wrapper injections.</summary>
internal static class ChatGptPageGate
{
    internal static bool TestAllowAnyInjectablePage { get; set; }

    public static bool IsInjectable(string? source)
    {
        if (TestAllowAnyInjectablePage && !string.IsNullOrEmpty(source))
            return true;

        if (string.IsNullOrEmpty(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;

        return ChatGptUrls.IsTrustedChatGptTopLevelUri(uri);
    }
}
