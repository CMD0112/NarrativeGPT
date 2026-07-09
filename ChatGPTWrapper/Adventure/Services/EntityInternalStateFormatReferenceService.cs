using System.IO;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityInternalStateFormatReferenceService
{
    public static string? TryReadContent(AdventureBundle bundle)
    {
        AdventureSourceFileService.EnsureLayout(bundle);
        var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.EntityStateFormatFile);
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        var generated = EntityInternalStateFormatGenerator.Generate().Trim();
        return string.IsNullOrWhiteSpace(generated) ? null : generated;
    }

    /// <summary>
    /// Full entity-state format reference for utility job packets.
    /// </summary>
    public static string BuildPromptBlock(AdventureBundle bundle)
    {
        var content = TryReadContent(bundle);
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var prefixed = AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
            bundle.Metadata.Title,
            SectionSchema.EntityStateFormatFile);

        return $"""

            === ENTITY STATE FORMAT REFERENCE ({SectionSchema.EntityStateFormatFile}) ===
            Patch mutable play state only — not canon profile fields in entities.json.
            Project path: `{prefixed}`

            {content}
            """;
    }

    /// <summary>Compact appendix when full reference would exceed local inference budget.</summary>
    public static string BuildCompactAppendix()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Entity state paths use nested blocks (emotional, physical, social, presence, …).");
        sb.AppendLine("kindId: player, party, npc, location, faction, quest, inventory, vehicle, custom.");
        sb.AppendLine("See entity-state-format.md for full field paths.");
        return sb.ToString().TrimEnd();
    }
}
