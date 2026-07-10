namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Builds play-packet overlap snapshot for bundled utility dedup (CMD-393).</summary>
internal static class PlayPacketContextSnapshotBuilder
{
    public static PlayPacketContextSnapshot Build(string contextText, string playPacketText)
    {
        var includesSummary = ContainsSummary(contextText, playPacketText);
        var includesState = ContainsState(contextText, playPacketText);
        var transcriptTailChars = EstimateTranscriptChars(contextText, playPacketText);

        return new PlayPacketContextSnapshot
        {
            IncludesRollingSummary = includesSummary,
            IncludesState = includesState,
            TranscriptTailChars = transcriptTailChars,
        };
    }

    private static bool ContainsSummary(string contextText, string playPacketText) =>
        contextText.Contains("[[cgw:summary", StringComparison.OrdinalIgnoreCase)
        || playPacketText.Contains("=== STORY SO FAR", StringComparison.Ordinal)
        || playPacketText.Contains("=== ROLLING SUMMARY ===", StringComparison.Ordinal);

    private static bool ContainsState(string contextText, string playPacketText) =>
        contextText.Contains("[[cgw:state", StringComparison.OrdinalIgnoreCase)
        || playPacketText.Contains("=== STATE DELTA", StringComparison.Ordinal)
        || playPacketText.Contains("=== CURRENT STATE", StringComparison.Ordinal)
        || playPacketText.Contains("\n=== STATE ===", StringComparison.Ordinal);

    private static int EstimateTranscriptChars(string contextText, string playPacketText)
    {
        var tagBody = ContextTagFormat.ExtractBlock(contextText, "transcript")
                      ?? ContextTagFormat.ExtractBlock(playPacketText, "transcript");
        if (!string.IsNullOrWhiteSpace(tagBody))
            return tagBody.Length;

        const string legacyHeader = "=== RECENT TRANSCRIPT ===";
        var legacy = ExtractLegacyBlock(playPacketText, legacyHeader);
        if (legacy.Length > 0)
            return legacy.Length;

        legacy = ExtractLegacyBlock(contextText, legacyHeader);
        return legacy.Length;
    }

    private static string ExtractLegacyBlock(string text, string header)
    {
        var idx = text.IndexOf(header, StringComparison.Ordinal);
        if (idx < 0)
            return "";

        var start = idx + header.Length;
        var next = text.IndexOf("\n=== ", start, StringComparison.Ordinal);
        return next < 0 ? text[start..].Trim() : text[start..next].Trim();
    }
}
