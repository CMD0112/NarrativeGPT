namespace ChatGPTWrapper.Adventure.Services;

internal static class ContextRenderPolicy
{
    public const int ScoreThreshold = 20;

    public static int InlineThreshold(string kind, bool fatFallback)
    {
        var thin = kind.ToLowerInvariant() switch
        {
            "person" => 45,
            "place" => 40,
            "quest" => 35,
            "faction" or "concept" or "rule" => 40,
            "custom" or "misc" => 50,
            "player" => int.MaxValue,
            _ => 40,
        };

        return fatFallback ? Math.Max(20, thin - 10) : thin;
    }

    public static RenderMode PickRenderMode(ContextPointer pointer, bool fatFallback)
    {
        if (pointer.Source == PointerSource.Baseline)
            return RenderMode.PointerOnly;

        if (pointer.Mode == RenderMode.ClusterSummary)
            return RenderMode.ClusterSummary;

        if (string.Equals(pointer.Kind, "player", StringComparison.OrdinalIgnoreCase) && !fatFallback)
            return RenderMode.PointerOnly;

        var threshold = InlineThreshold(pointer.Kind, fatFallback);
        if (pointer.Score < threshold)
            return RenderMode.PointerOnly;

        var bodyLen = pointer.BodyCache?.Length ?? 0;
        if (string.Equals(pointer.Kind, "person", StringComparison.OrdinalIgnoreCase)
            && bodyLen > 600
            && pointer.Score < threshold + 15
            && ExtractFlavor(pointer.BodyCache) is not null)
            return RenderMode.InlineFlavor;

        if (bodyLen > 0)
            return RenderMode.InlineFull;

        return RenderMode.PointerOnly;
    }

    public static string? ExtractFlavor(string? bodyCache)
    {
        if (string.IsNullOrWhiteSpace(bodyCache))
            return null;

        foreach (var line in bodyCache.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("> Flavor:", StringComparison.OrdinalIgnoreCase))
                return trimmed["> Flavor:".Length..].Trim();
        }

        return null;
    }

    public static string ExtractInlineBody(ContextPointer pointer)
    {
        if (pointer.Mode == RenderMode.InlineFlavor)
            return ExtractFlavor(pointer.BodyCache) ?? FirstParagraph(pointer.BodyCache);

        if (string.Equals(pointer.Kind, "quest", StringComparison.OrdinalIgnoreCase))
            return pointer.BodyCache ?? "";

        return pointer.BodyCache ?? "";
    }

    private static string FirstParagraph(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        var parts = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : body.Trim();
    }

    public static int EstimateRenderCost(ContextPointer pointer)
    {
        return pointer.Mode switch
        {
            RenderMode.InlineFull => (pointer.BodyCache?.Length ?? 0) + 120,
            RenderMode.InlineFlavor => (ExtractFlavor(pointer.BodyCache)?.Length ?? 0) + 100,
            RenderMode.ClusterSummary => 80 + pointer.ClusterNames.Sum(n => n.Length),
            _ => 80,
        };
    }
}
