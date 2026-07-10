namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Normative revision prompt prefix for hide matching (CMD-352).</summary>
internal static class NarratorRevisionPrompt
{
    public const string Prefix = "For play turn ";

    public static bool IsRevisionPromptUserMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var stripped = PromptInjectionService.StripInvalidationMarkers(text).TrimStart();
        if (stripped.StartsWith(Prefix, StringComparison.Ordinal))
            return true;

        return stripped.Contains("disregard your prior assistant reply for this turn", StringComparison.Ordinal);
    }
}
