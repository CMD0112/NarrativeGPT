using System.Text.Json;
using ChatGPTWrapper.Bridges;

namespace ChatGPTWrapper.PageIntegration;

public sealed class PageMessageRouter
{
    private readonly Dictionary<string, List<Action<string, JsonElement>>> _byFeature = new(StringComparer.Ordinal);
    private readonly List<Action<string, JsonElement>> _legacyHandlers = [];

    public void Register(string featureId, Action<string, JsonElement> handler)
    {
        if (!_byFeature.TryGetValue(featureId, out var list))
        {
            list = [];
            _byFeature[featureId] = list;
        }

        list.Add(handler);
    }

    public void RegisterLegacy(Action<string, JsonElement> handler) => _legacyHandlers.Add(handler);

    public void Route(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString() ?? ""
                : "";

            var feature = root.TryGetProperty("feature", out var featureEl) && featureEl.ValueKind == JsonValueKind.String
                ? featureEl.GetString()
                : null;

            if (string.IsNullOrEmpty(feature)
                && root.TryGetProperty("channel", out var channelEl)
                && channelEl.ValueKind == JsonValueKind.String)
            {
                feature = MapChannelToFeature(channelEl.GetString());
            }

            if (string.IsNullOrEmpty(feature))
                feature = InferFeature(type);

            if (!string.IsNullOrEmpty(feature)
                && _byFeature.TryGetValue(feature, out var handlers))
            {
                foreach (var handler in handlers)
                    handler(type, root);
            }

            foreach (var legacy in _legacyHandlers)
                legacy(type, root);
        }
        catch
        {
            /* ignore malformed messages */
        }
    }

    private static string? InferFeature(string type)
    {
        if (string.IsNullOrEmpty(type))
            return null;
        if (type.StartsWith("cgwCompose", StringComparison.Ordinal))
            return PageFeatureIds.PlayCompose;
        if (string.Equals(type, "cgwPlaySendLog", StringComparison.Ordinal))
            return PageFeatureIds.PlayCompose;
        return type switch
        {
            "bridgeReady" or "turnComplete" or "promptSubmitted" or "captureResult"
                or "assistantTurnCount" or "userTurnCount" or "probeResult" or "regenerateResult" or "pong" => PageFeatureIds.AdventureBridge,
            "apiResult" or "apiError" => PageFeatureIds.ApiBridge,
            _ => null,
        };
    }

    private static string? MapChannelToFeature(string? channel) =>
        channel switch
        {
            BridgeProtocol.ChannelApi => PageFeatureIds.ApiBridge,
            BridgeProtocol.ChannelPlay => PageFeatureIds.AdventureBridge,
            BridgeProtocol.ChannelDisplay => PageFeatureIds.ContextTags,
            _ => null,
        };
}
