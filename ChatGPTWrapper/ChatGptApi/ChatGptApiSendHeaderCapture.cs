using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Sanitizes and compares outbound API request headers for send/prepare samples (CMD-436).
/// </summary>
internal static class ChatGptApiSendHeaderCapture
{
    internal static readonly string[] NamesToCapture =
    [
        "authorization",
        "x-conduit-token",
        "openai-sentinel",
        "oai-sentinel",
        "oai-sentinel-latency",
        "chatgpt-account-id",
        "oai-device-id",
        "oai-language",
        "accept",
        "content-type",
        "origin",
        "referer",
        "oai-client-version",
        "oai-client-build",
        "oai-client-app",
        "sec-fetch-site",
        "sec-fetch-mode",
    ];

    public static Dictionary<string, string> ExtractFromRequest(CoreWebView2WebResourceRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var http = request.Headers;
        foreach (var name in NamesToCapture)
        {
            if (!http.Contains(name))
                continue;

            headers[name] = SanitizeValue(name, http.GetHeader(name));
        }

        foreach (var header in http)
        {
            var name = header.Key;
            if (!name.Contains("sentinel", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("oai-echo-logs", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("oai-telemetry", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (headers.ContainsKey(name))
                continue;

            headers[name] = SanitizeValue(name, header.Value);
        }

        return headers;
    }

    public static Dictionary<string, string> SanitizeDeclared(IReadOnlyDictionary<string, string>? declared)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (declared is null)
            return headers;

        foreach (var (name, value) in declared)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            headers[name] = SanitizeValue(name, value);
        }

        return headers;
    }

    public static string SummarizeGap(
        IReadOnlyDictionary<string, string>? goldenWire,
        IReadOnlyDictionary<string, string>? liveWire,
        IReadOnlyDictionary<string, string>? liveBridgeDeclared)
    {
        var goldenKeys = goldenWire?.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var liveWireKeys = liveWire?.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var bridgeKeys = liveBridgeDeclared?.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var missingVsGolden = goldenKeys.Except(liveWireKeys, StringComparer.OrdinalIgnoreCase).OrderBy(static x => x).ToList();
        var extraOnWire = liveWireKeys.Except(goldenKeys, StringComparer.OrdinalIgnoreCase).OrderBy(static x => x).ToList();

        return string.Join(
            " ",
            new[]
            {
                $"wire_keys={liveWireKeys.Count}",
                bridgeKeys.Count > 0 ? $"bridge_declared_keys={bridgeKeys.Count}" : null,
                missingVsGolden.Count > 0 ? $"missing_vs_golden=[{string.Join(",", missingVsGolden)}]" : null,
                extraOnWire.Count > 0 ? $"extra_on_wire=[{string.Join(",", extraOnWire)}]" : null,
                HasSentinel(liveWire) ? "wire_sentinel=1" : "wire_sentinel=0",
                HasSentinel(goldenWire) ? "golden_sentinel=1" : "golden_sentinel=0",
                liveWire?.ContainsKey("x-conduit-token") == true ? "wire_conduit=1" : "wire_conduit=0",
                liveBridgeDeclared?.ContainsKey("x-conduit-token") == true ? "bridge_conduit=1" : "bridge_conduit=0",
            }.Where(static s => s is not null));
    }

    public static string SanitizeValue(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (name.Equals("authorization", StringComparison.OrdinalIgnoreCase)
            || name.Equals("x-conduit-token", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length <= 12 ? "[REDACTED]" : $"{value[..8]}…[REDACTED]";
        }

        return value;
    }

    private static bool HasSentinel(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
            return false;

        return headers.Keys.Any(static k =>
            k.Contains("sentinel", StringComparison.OrdinalIgnoreCase));
    }
}
