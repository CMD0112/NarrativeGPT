namespace ChatGPTWrapper.Diagnostics;

public static class PageDiagnostics
{
    private const int MaxPayloadChars = 8192;

    public static void LogInbound(string type, string? feature, string json)
    {
        if (!DiagnosticsOptions.Extended)
            return;

        if (string.Equals(type, "cgwDiagnosticsLog", StringComparison.Ordinal)
            || string.Equals(type, "cgwPlaySendLog", StringComparison.Ordinal))
        {
            return;
        }

        var channel = InferChannel(type, feature);
        DiagnosticsLog.Write(
            channel,
            DiagnosticsLevel.Debug,
            "page_message_in",
            string.IsNullOrWhiteSpace(type) ? "message" : type,
            source: feature ?? "page",
            data: new
            {
                type,
                feature,
                payloadChars = json.Length,
                payload = Truncate(json),
            });
    }

    private static DiagnosticsChannel InferChannel(string type, string? feature)
    {
        if (!string.IsNullOrEmpty(feature))
        {
            return feature.ToLowerInvariant() switch
            {
                "play-compose" => DiagnosticsChannel.Compose,
                "adventure-bridge" => DiagnosticsChannel.Bridge,
                "api-bridge" or "cgw-api" => DiagnosticsChannel.Api,
                _ => DiagnosticsChannel.Page,
            };
        }

        if (type.StartsWith("cgwCompose", StringComparison.Ordinal))
            return DiagnosticsChannel.Compose;

        return type switch
        {
            "apiResult" or "apiError" => DiagnosticsChannel.Api,
            "bridgeReady" or "turnComplete" or "promptSubmitted" or "captureResult"
                or "assistantTurnCount" or "userTurnCount" or "probeResult" or "regenerateResult" or "pong"
                => DiagnosticsChannel.Bridge,
            _ => DiagnosticsChannel.Page,
        };
    }

    private static string Truncate(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxPayloadChars)
            return text;

        return text[..MaxPayloadChars] + "…";
    }
}
