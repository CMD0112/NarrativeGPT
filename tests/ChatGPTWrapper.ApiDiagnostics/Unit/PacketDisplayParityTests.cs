using System.Text.Json;
using System.Text.Json.Serialization;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>
/// Documents parse expectations shared with <c>cgw-packet-display.js</c> (<c>parsePacket</c>).
/// Vectors live in <c>Fixtures/packet-display-parity.json</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PacketDisplayParityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Parity_fixture_vectors_match_csharp_parse()
    {
        var vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        foreach (var vector in vectors)
        {
            var packet = BuildPacket(vector);
            var parsed = PacketDisplayParseModel.Parse(packet);

            Assert.Equal(vector.UserLine, parsed.UserLine);
            Assert.Equal(vector.SectionNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
                parsed.SectionNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            Assert.Equal(vector.SectionNames.Count, parsed.SectionCount);
        }
    }

    [Fact]
    public void Parity_fixture_is_valid_json_for_js_reference()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "packet-display-parity.json");
        Assert.True(File.Exists(path), $"Missing fixture: {path}");

        var json = File.ReadAllText(path);
        var vectors = JsonSerializer.Deserialize<List<PacketDisplayParityVector>>(json, JsonOptions);
        Assert.NotNull(vectors);
        Assert.True(vectors.Count >= 3, "Expected at least three parity vectors.");
    }

    private static List<PacketDisplayParityVector> LoadVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "packet-display-parity.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<PacketDisplayParityVector>>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize parity fixture.");
    }

    private static string BuildPacket(PacketDisplayParityVector vector)
    {
        var parts = vector.Blocks
            .Select(b => ContextTagFormat.WrapBlock(b.Tag, b.Body))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        var packet = string.Join("\n\n", parts);
        if (!string.IsNullOrWhiteSpace(vector.Suffix))
        {
            packet = string.IsNullOrEmpty(packet)
                ? vector.Suffix
                : packet + "\n\n" + vector.Suffix;
        }

        return packet;
    }

    private sealed class PacketDisplayParityVector
    {
        public string Name { get; set; } = "";

        public List<PacketBlockSpec> Blocks { get; set; } = [];

        public string? Suffix { get; set; }

        public string UserLine { get; set; } = "";

        public List<string> SectionNames { get; set; } = [];
    }

    private sealed class PacketBlockSpec
    {
        public string Tag { get; set; } = "";

        public string Body { get; set; } = "";
    }
}

/// <summary>
/// Mirrors <c>parsePacket</c> in <c>cgw-packet-display.js</c> for parity tests.
/// </summary>
internal static class PacketDisplayParseModel
{
    public static (string UserLine, IReadOnlyList<string> SectionNames, int SectionCount) Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || !ContextTagFormat.ContainsTags(text))
            return ("", [], 0);

        var blocks = new Dictionary<string, string>(
            ContextTagFormat.ExtractAllBlocks(text),
            StringComparer.OrdinalIgnoreCase);
        var userLine = ContextTagFormat.ExtractUntaggedSuffix(text) ?? "";

        if (blocks.TryGetValue("player", out var playerTagged) && !string.IsNullOrWhiteSpace(playerTagged))
        {
            userLine = playerTagged.Trim();
            blocks.Remove("player");
        }

        var sectionNames = blocks
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Key.ToLowerInvariant())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        return (userLine.Trim(), sectionNames, sectionNames.Count);
    }
}
