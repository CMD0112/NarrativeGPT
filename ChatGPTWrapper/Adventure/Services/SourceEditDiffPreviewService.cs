using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SourceEditDiffPreviewService
{
    public static string BuildPreview(AdventureBundle bundle, SourceEditReviewItem item)
    {
        var target = item.TargetFile.Trim();
        var op = item.Operation.Trim().ToLowerInvariant();

        if (op == "remove"
            && SourceEditService.TryParseImportRemovalContent(item.Content, out var sectionId, out var entityId))
        {
            var removalTarget = ResolveImportRemovalTarget(bundle, target, sectionId, entityId, item.Content);
            return $"""
                --- removal target ({target} / {sectionId}) ---
                {TrimForPreview(removalTarget)}

                --- after accept (remove) ---
                Entity removed from entities.json; {target} re-exported without this section.
                Plot essentials and other freeform sections are not affected.

                {item.Rationale}
                """;
        }

        var current = ResolveCurrentText(bundle, target, item.Content) ?? "(no existing content)";

        if (op == "remove")
        {
            return $"""
                --- current ({target}) ---
                {TrimForPreview(current)}

                --- after accept (remove) ---
                (section or entity removed — see rationale)
                {item.Rationale}
                """;
        }

        var proposed = op == "append"
            ? current.TrimEnd() + Environment.NewLine + Environment.NewLine + item.Content.Trim()
            : item.Content;

        return BuildUnifiedPreview(current, proposed, target);
    }

    private static string ResolveImportRemovalTarget(
        AdventureBundle bundle,
        string fileName,
        string sectionId,
        Guid entityId,
        string fallback)
    {
        var manifestEntry = bundle.SourceManifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, fileName, StringComparison.OrdinalIgnoreCase));
        var section = manifestEntry?.Sections.FirstOrDefault(s =>
            string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.SourceEntityId, entityId.ToString(), StringComparison.OrdinalIgnoreCase));

        if (section is not null && !string.IsNullOrWhiteSpace(section.BodyCache))
        {
            var title = string.IsNullOrWhiteSpace(section.Title) ? sectionId : section.Title;
            return $"## {title}\n{section.BodyCache.Trim()}";
        }

        return fallback;
    }

    private static string? ResolveCurrentText(AdventureBundle bundle, string target, string removeHint)
    {
        var normalized = target.ToLowerInvariant();
        if (normalized is "instructions" or "instructions-snippet.md")
            return InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle);

        var file = normalized switch
        {
            "characters.md" => SectionSchema.CastFile,
            _ => target,
        };

        if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return AdventureSourceFileService.TryRead(bundle, file);

        if (normalized == "remove" || string.IsNullOrWhiteSpace(removeHint))
            return removeHint;

        return removeHint;
    }

    private static string BuildUnifiedPreview(string before, string after, string label)
    {
        var beforeLines = SplitLines(before);
        var afterLines = SplitLines(after);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- {label} (unified preview) ---");

        var max = Math.Max(beforeLines.Count, afterLines.Count);
        for (var i = 0; i < max; i++)
        {
            var a = i < beforeLines.Count ? beforeLines[i] : null;
            var b = i < afterLines.Count ? afterLines[i] : null;
            if (string.Equals(a, b, StringComparison.Ordinal))
            {
                sb.Append(' ').AppendLine(a ?? "");
                continue;
            }

            if (a is not null)
                sb.Append('-').AppendLine(a);
            if (b is not null)
                sb.Append('+').AppendLine(b);
        }

        return sb.ToString().TrimEnd();
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();

    private static string TrimForPreview(string text, int max = 2400) =>
        text.Length <= max ? text : text[..max] + Environment.NewLine + "…(truncated)";
}
