using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    public void StripTrailingInjectionBlocks_removes_canon_notify_after_player_line()
    {
        var packet = """
            [[cgw:meta mode="fat" turn="2"]] [[/cgw:meta]]

            [[cgw:instructions]]Narrate.[[/cgw:instructions]]

            My eyes move to the militia woman.

            "Aye. Garran. Garran Holt. The girl is Anwen. She is with me."

            === CANON UPDATE (check sources) ===

            Re-retrieve: cast.md — party/anwen
            """;

        var userLine = ContextTagFormat.ExtractUntaggedSuffix(packet);

        Assert.Equal(
            """
            My eyes move to the militia woman.

            "Aye. Garran. Garran Holt. The girl is Anwen. She is with me."
            """.Trim(),
            userLine);
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

    [Fact]
    public void Structured_preview_extracts_user_line_for_thread_display()
    {
        var packet = ContextTagFormat.WrapMeta(PacketProfile.SourceDelegated, 4)
                     + "\n\n"
                     + ContextTagFormat.WrapBlock("sources", "Project: g-p-test\n\nALWAYS RETRIEVE:\n* cast.md")
                     + "\n\n"
                     + ContextTagFormat.WrapBlock("instructions", "You are the narrator.")
                     + "\n\n"
                     + "My eyes stay locked on Bram, but I say nothing for now.";

        var preview = ContextTagFormat.FormatStructuredPreview(packet);
        var parsed = PacketDisplayStructuredPreviewModel.Parse(preview);

        Assert.True(PacketDisplayStructuredPreviewModel.IsStructuredPreviewPacket(preview));
        Assert.Equal(
            "My eyes stay locked on Bram, but I say nothing for now.",
            parsed.UserLine);
        Assert.Contains("sources", parsed.SectionNames);
        Assert.Contains("instructions", parsed.SectionNames);
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

/// <summary>
/// Mirrors structured preview parsing in <c>cgw-packet-display.js</c>.
/// </summary>
internal static class PacketDisplayStructuredPreviewModel
{
    private static readonly Regex SectionHeaderRegex = new(
        @"^\[([a-z][a-z0-9_-]*)\]\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "meta", "sources", "instructions", "summary", "state", "cards", "memory", "transcript", "player", "user",
    };

    public static bool IsStructuredPreviewPacket(string text)
    {
        if (string.IsNullOrEmpty(text) || ContextTagFormat.ContainsTags(text))
            return false;

        if (!Regex.IsMatch(text, @"\[(user|player)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        return Regex.IsMatch(
            text,
            @"^\[(meta|sources|instructions|summary|state|cards|memory|transcript)\]",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    public static (string UserLine, IReadOnlyList<string> SectionNames) Parse(string text)
    {
        if (!IsStructuredPreviewPacket(text))
            return ("", []);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string userLine = "";
        string? currentName = null;
        var currentHeaderExtra = "";
        var currentLines = new List<string>();

        void FlushSection()
        {
            if (currentName is null)
                return;

            var body = string.Join('\n', currentLines).Trim();
            switch (currentName.ToLowerInvariant())
            {
                case "user" when string.IsNullOrWhiteSpace(userLine):
                    userLine = body;
                    break;
                case "player":
                    userLine = body;
                    break;
                case "meta":
                {
                    var metaBody = string.IsNullOrWhiteSpace(currentHeaderExtra)
                        ? body
                        : string.IsNullOrWhiteSpace(body)
                            ? currentHeaderExtra
                            : currentHeaderExtra + "\n" + body;
                    if (!string.IsNullOrWhiteSpace(metaBody))
                        blocks["meta"] = metaBody.Trim();
                    break;
                }
                default:
                    if (!string.IsNullOrWhiteSpace(body))
                        blocks[currentName] = body;
                    break;
            }

            currentName = null;
            currentHeaderExtra = "";
            currentLines.Clear();
        }

        foreach (var line in lines)
        {
            var match = SectionHeaderRegex.Match(line);
            if (match.Success && SectionNames.Contains(match.Groups[1].Value))
            {
                FlushSection();
                currentName = match.Groups[1].Value.ToLowerInvariant();
                currentHeaderExtra = match.Groups[2].Value.Trim();
                continue;
            }

            if (currentName is not null)
                currentLines.Add(line);
        }

        FlushSection();

        return (
            userLine.Trim(),
            blocks.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
