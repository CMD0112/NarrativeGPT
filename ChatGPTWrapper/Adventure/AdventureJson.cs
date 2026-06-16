using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatGPTWrapper.Adventure;

internal static class AdventureJson
{
    public const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
