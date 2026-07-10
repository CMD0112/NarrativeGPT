using System.IO;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonFormatReferenceService
{
    public static string? TryReadContent(AdventureBundle bundle)
    {
        AdventureSourceFileService.EnsureLayout(bundle);
        var path = AdventureSourceFileService.ResolveAbsolutePath(bundle, SectionSchema.CanonFormatFile);
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        var generated = CanonFormatGenerator.Generate().Trim();
        return string.IsNullOrWhiteSpace(generated) ? null : generated;
    }

    /// <summary>
    /// Full canon-format reference block for utility/design job packets.
    /// </summary>
    public static string BuildPromptBlock(AdventureBundle bundle)
    {
        var content = TryReadContent(bundle);
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var prefixed = AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
            bundle.Metadata.Title,
            SectionSchema.CanonFormatFile);

        return $"""

            === CANON FORMAT REFERENCE ({SectionSchema.CanonFormatFile}) ===
            Follow this reference for section templates, labeled fields, and player/party/npc bucket rules.
            Project path: `{prefixed}`

            {content}
            """;
    }

    /// <summary>
    /// Inline citation embedded in design source file specifications (cast/world/plot).
    /// </summary>
    public static string BuildSpecificationCitation(AdventureBundle bundle)
    {
        var content = TryReadContent(bundle);
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var prefixed = AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
            bundle.Metadata.Title,
            SectionSchema.CanonFormatFile);

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"**Format reference ({SectionSchema.CanonFormatFile})** — follow `{prefixed}` exactly:");
        sb.AppendLine("```");
        sb.AppendLine(content);
        sb.AppendLine("```");
        return sb.ToString().TrimEnd();
    }
}
