namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityTypeNormalizer
{
    public static string Normalize(string? entityType) => (entityType ?? "").Trim().ToLowerInvariant() switch
    {
        "character" or "person" => "person",
        "location" or "place" => "place",
        "item" or "thing" => "thing",
        "faction" => "faction",
        "quest" => "quest",
        "concept" or "idea" => "concept",
        _ => (entityType ?? "").Trim().ToLowerInvariant(),
    };

    public static string DisplayLabel(string? entityType) => Normalize(entityType) switch
    {
        "person" => "Person",
        "place" => "Place",
        "thing" => "Thing",
        "faction" => "Faction",
        "quest" => "Quest",
        "concept" => "Concept",
        _ => string.IsNullOrWhiteSpace(entityType) ? "Entity" : entityType,
    };
}
