using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ContextPointerRenderer
{
    public static string FormatProsePointer(AdventureBundle bundle, ContextPointer pointer)
    {
        var fileRef = FormatProjectFileReference(bundle, pointer.FileName);

        if (pointer.Mode == RenderMode.ClusterSummary)
        {
            var names = string.Join(", ", pointer.ClusterNames);
            return $"Retrieve from {fileRef} — section \"{SectionSchema.DisplaySectionTitle(pointer.SectionId)}\" — NPCs in scene: {names}";
        }

        var sectionTitle = DisplaySectionPath(pointer.SectionId);
        if (pointer.SectionId.Contains('/'))
        {
            var parts = pointer.SectionId.Split('/', 2);
            var parent = SectionSchema.DisplaySectionTitle(parts[0]);
            return $"Retrieve from {fileRef} — section \"{parent}\" — entry \"{pointer.Title}\" (id: {parts[1]})";
        }

        return $"Retrieve from {fileRef} — section \"{sectionTitle}\" (id: {pointer.SectionId})";
    }

    public static string BuildSourcesV2Block(
        AdventureBundle bundle,
        ContextResolveResult resolved,
        PacketProfile profile,
        ProjectSourceReadiness? readiness = null,
        bool useContextTags = true)
    {
        var inner = BuildSourcesInnerLines(bundle, resolved, profile, readiness);
        if (!useContextTags)
            return "=== PROJECT SOURCES ===\n" + string.Join('\n', inner);

        var modeAttr = PacketProfileResolver.ProfileMetaMode(profile);
        var lines = new List<string>
        {
            $"[[cgw:sources v=\"2\" mode=\"{modeAttr}\"]]",
        };
        lines.AddRange(inner);
        lines.Add("[[/cgw:sources]]");
        return string.Join('\n', lines);
    }

    public static string BuildMinimalLocalSourcesBlock(bool useContextTags = true)
    {
        const string body = """
            ALWAYS RETRIEVE:
            - No ChatGPT Project linked — link a Project and publish sources for retrieval.
            - Until then, use the scenario opening and session state in this packet.

            THIS TURN:
            - (none)
            """;

        if (!useContextTags)
            return "=== PROJECT SOURCES ===\n" + body;

        return $"[[cgw:sources v=\"2\" mode=\"minimal\"]]\n{body}\n[[/cgw:sources]]";
    }

    public static string BuildUtilityWorkerSourcesBlock(
        AdventureBundle bundle,
        ContextResolveResult resolved,
        ProjectSourceReadiness readiness,
        bool useContextTags = true)
    {
        var inner = BuildUtilityWorkerInnerLines(bundle, resolved, readiness);
        if (inner.Count == 0)
            return "";

        if (!useContextTags)
            return "=== UTILITY PROJECT SOURCES ===\n" + string.Join('\n', inner);

        return $"[[cgw:sources v=\"2\" mode=\"utility-worker\"]]\n{string.Join('\n', inner)}\n[[/cgw:sources]]";
    }

    private static List<string> BuildUtilityWorkerInnerLines(
        AdventureBundle bundle,
        ContextResolveResult resolved,
        ProjectSourceReadiness readiness)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            lines.Add($"Project: {bundle.Metadata.LinkedProjectId}");

        if (lines.Count > 0)
            lines.Add("");

        lines.Add("CANON CORE:");
        if (resolved.Baseline.Count == 0)
            lines.Add("- (none — retrieve task-scoped sections below)");
        else
            foreach (var p in resolved.Baseline)
                lines.Add("- " + FormatProsePointer(bundle, p));

        lines.Add("");
        lines.Add("TASK-SCOPED:");
        var taskPointers = resolved.ThisTurn
            .Where(p => p.Mode != RenderMode.InlineFull && p.Mode != RenderMode.InlineFlavor)
            .ToList();
        if (taskPointers.Count == 0)
            lines.Add("- (none matched this job scope)");
        else
            foreach (var p in taskPointers)
                lines.Add("- " + FormatProsePointer(bundle, p));

        if (!readiness.CanDelegateStaticContent && readiness.HasLinkedProject)
        {
            lines.Add("");
            lines.Add("READINESS:");
            if (!string.IsNullOrWhiteSpace(readiness.BlockingReason))
                lines.Add($"- Sources not ready: {readiness.BlockingReason}");
            if (!string.IsNullOrWhiteSpace(readiness.SuggestedAction))
                lines.Add($"- {readiness.SuggestedAction}");
        }

        var inlines = resolved.All
            .Where(p => p.Mode is RenderMode.InlineFull or RenderMode.InlineFlavor)
            .ToList();
        if (inlines.Count > 0)
        {
            lines.Add("");
            lines.Add("INLINE EXCERPTS:");
            foreach (var p in inlines)
            {
                lines.Add($"--- Inline: {p.FileName} / {DisplaySectionPath(p.SectionId)} / {p.Title} ---");
                lines.Add(ContextRenderPolicy.ExtractInlineBody(p));
            }
        }

        return lines;
    }

    public static string BuildFatSourcesBlock(
        AdventureBundle bundle,
        ContextResolveResult resolved,
        bool useContextTags = true,
        ProjectSourceReadiness? readiness = null) =>
        BuildSourcesV2Block(bundle, resolved, PacketProfile.InlineFallback, readiness, useContextTags);

    private static List<string> BuildSourcesInnerLines(
        AdventureBundle bundle,
        ContextResolveResult resolved,
        PacketProfile profile,
        ProjectSourceReadiness? readiness)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            lines.Add($"Project: {bundle.Metadata.LinkedProjectId}");

        if (lines.Count > 0)
            lines.Add("");

        lines.Add("ALWAYS RETRIEVE:");
        if (resolved.Baseline.Count == 0)
            AppendBaselineFallback(lines, bundle, readiness, profile);
        else
            foreach (var p in resolved.Baseline)
                lines.Add("- " + FormatProsePointer(bundle, p));

        lines.Add("");
        lines.Add("THIS TURN:");
        var turnPointers = resolved.ThisTurn.Where(p => p.Mode != RenderMode.InlineFull && p.Mode != RenderMode.InlineFlavor).ToList();
        if (turnPointers.Count == 0)
            lines.Add("- (none beyond inline excerpts below)");
        else
            foreach (var p in turnPointers)
                lines.Add("- " + FormatProsePointer(bundle, p));

        var inlines = resolved.All
            .Where(p => p.Mode is RenderMode.InlineFull or RenderMode.InlineFlavor)
            .ToList();
        if (inlines.Count > 0)
        {
            lines.Add("");
            lines.Add("INLINE EXCERPTS:");
            foreach (var p in inlines)
            {
                lines.Add($"--- Inline: {p.FileName} / {DisplaySectionPath(p.SectionId)} / {p.Title} ---");
                lines.Add(ContextRenderPolicy.ExtractInlineBody(p));
            }
        }

        return lines;
    }

    private static void AppendBaselineFallback(
        List<string> lines,
        AdventureBundle bundle,
        ProjectSourceReadiness? readiness,
        PacketProfile profile)
    {
        if (profile == PacketProfile.MinimalLocal)
        {
            lines.Add("- No ChatGPT Project linked — link a Project and publish sources for retrieval.");
            return;
        }

        if (readiness?.CanDelegateStaticContent == true && readiness.SyncedFiles.Count > 0)
        {
            lines.Add("- (section index empty — retrieve these Project source files each turn)");
            foreach (var file in readiness.SyncedFiles)
            {
                var fileRef = FormatProjectFileReference(bundle, file.RelativePath);
                lines.Add($"- {fileRef} — {file.Description}");
            }

            return;
        }

        if (readiness?.HasLinkedProject == true
            && !string.IsNullOrWhiteSpace(readiness.BlockingReason))
        {
            lines.Add($"- Sources not ready: {readiness.BlockingReason}");
            if (!string.IsNullOrWhiteSpace(readiness.SuggestedAction))
                lines.Add($"- {readiness.SuggestedAction}");
            return;
        }

        lines.Add("- (none)");
    }

    private static string FormatProjectFileReference(AdventureBundle bundle, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.Title))
            return relativePath;

        var prefixed = AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
            bundle.Metadata.Title,
            relativePath);
        return string.Equals(prefixed, relativePath, StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : $"{prefixed} (canonical: {relativePath})";
    }

    private static string DisplaySectionPath(string sectionId)
    {
        if (!sectionId.Contains('/'))
            return SectionSchema.DisplaySectionTitle(sectionId);

        var parts = sectionId.Split('/', 2);
        return $"{SectionSchema.DisplaySectionTitle(parts[0])} / {parts[1]}";
    }
}
