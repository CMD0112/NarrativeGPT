using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

internal static class SourceEditService
{
    public static string BuildSourceEditPrompt(AdventureBundle bundle, string userPrompt)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var excerpts = new List<string>();
        var excerptPaths = new List<string>();

        foreach (var fileName in SectionSchema.CoreLoreFiles)
        {
            var path = Path.Combine(sourcesDir, fileName);
            if (!File.Exists(path))
                continue;

            var text = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Length > 4000)
                text = text[..4000] + "\n…(truncated)";

            excerptPaths.Add(fileName);
            excerpts.Add($"=== {fileName} ===\n{text}");
        }

        var instructions = InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle);
        var formatReference = CanonFormatReferenceService.BuildPromptBlock(bundle);
        var formatHints = ProjectSourceFileTemplates.BuildInlineFormatsSection(excerptPaths);
        var formatsBlock = string.IsNullOrWhiteSpace(formatHints)
            ? ""
            : $"""

            === SOURCE FILE FORMATS (summary) ===
            {formatHints}
            """;

        return $"""
            === SOURCE EDIT JOB ===
            {userPrompt.Trim()}

            === CURRENT INSTRUCTIONS (instruction-domain) ===
            {instructions}
            {formatReference}
            {formatsBlock}

            === CURRENT SOURCE EXCERPTS ===
            {string.Join(Environment.NewLine + Environment.NewLine, excerpts)}
            """;
    }

    public static bool ApplyAcceptedEdit(AdventureBundle bundle, SourceEditReviewItem item)
    {
        var target = item.TargetFile.Trim();
        var content = item.Content;
        var op = item.Operation.Trim().ToLowerInvariant();

        if (op == "remove")
            return ApplyImportRemoval(bundle, target, content);

        switch (target.ToLowerInvariant())
        {
            case "world.md":
                bundle.Scenario.WorldRules = ApplyText(bundle.Scenario.WorldRules, content, op);
                break;
            case "plot.md":
                bundle.Scenario.PlotEssentials = ApplyText(bundle.Scenario.PlotEssentials, content, op);
                break;
            case "scenario.md":
                ApplyScenarioMdEdit(bundle, content, op);
                break;
            case "instructions":
            case "instructions-snippet.md":
                ApplyInstructionsEdit(bundle, content, op);
                break;
            case "cast.md":
            case "characters.md":
                return false;
            default:
                return false;
        }

        ClearManualPublishForTarget(bundle, target);
        ProjectSourceExportService.ExportForce(bundle);
        return true;
    }

    internal static bool ApplyImportRemoval(AdventureBundle bundle, string targetFile, string content)
    {
        if (!TryParseImportRemovalContent(content, out var sectionId, out var entityId))
            return false;

        var removed = TryRemoveEntity(bundle.Entities, sectionId, entityId);
        if (!removed && EntityExistsInAnyCollection(bundle.Entities, entityId))
            return false;

        RemoveManifestSection(bundle, targetFile, sectionId, entityId);
        ClearManualPublishForTarget(bundle, targetFile);
        ProjectSourceExportService.ExportForce(bundle);
        return true;
    }

    internal static bool MatchesDuplicateProposal(SourceEditReviewItem left, SourceEditReviewItem right)
    {
        if (!string.Equals(left.TargetFile, right.TargetFile, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(left.Operation, right.Operation, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(left.Content, right.Content, StringComparison.Ordinal))
            return true;

        if (!string.Equals(left.Operation, "remove", StringComparison.OrdinalIgnoreCase))
            return false;

        return TryParseImportRemovalContent(left.Content, out _, out var leftId)
               && TryParseImportRemovalContent(right.Content, out _, out var rightId)
               && leftId == rightId;
    }

    internal static void RemoveMatchingReviewProposals(AdventureBundle bundle, SourceEditReviewItem item)
    {
        bundle.Scenario.SourceEditReviewQueue.RemoveAll(q => MatchesDuplicateProposal(item, q));
    }

    internal static bool EntityExistsInAnyCollection(EntitiesDocument entities, Guid entityId) =>
        entities.Characters.Any(c => c.Id == entityId)
        || entities.Party.Any(c => c.Id == entityId)
        || entities.Locations.Any(l => l.Id == entityId)
        || entities.Factions.Any(f => f.Id == entityId)
        || entities.Concepts.Any(c => c.Id == entityId)
        || entities.Quests.Any(q => q.Id == entityId)
        || entities.Mysteries.Any(m => m.Id == entityId)
        || entities.Conflicts.Any(c => c.Id == entityId)
        || entities.Consequences.Any(c => c.Id == entityId)
        || entities.CustomEntries.Any(c => c.Id == entityId);

    internal static bool TryParseImportRemovalContent(string content, out string sectionId, out Guid entityId)
    {
        sectionId = "";
        entityId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var paren = content.LastIndexOf(" (", StringComparison.Ordinal);
        var colonParen = content.IndexOf("): ", StringComparison.Ordinal);
        if (paren < 0 || colonParen <= paren)
            return false;

        sectionId = content[..paren].Trim();
        var idText = content[(paren + 2)..colonParen].Trim();
        if (!Guid.TryParse(idText, out entityId) || string.IsNullOrWhiteSpace(sectionId))
            return false;

        return true;
    }

    private static bool TryRemoveEntity(EntitiesDocument entities, string sectionId, Guid entityId)
    {
        var slash = sectionId.IndexOf('/');
        var prefix = slash >= 0 ? sectionId[..slash] : sectionId;

        return prefix switch
        {
            "npcs" => RemoveById(entities.Characters, entityId),
            "party" => RemoveById(entities.Party, entityId),
            "locations" => RemoveById(entities.Locations, entityId),
            "factions" => RemoveById(entities.Factions, entityId),
            "concepts" => RemoveById(entities.Concepts, entityId),
            "quests" => RemoveById(entities.Quests, entityId),
            "mysteries" => RemoveById(entities.Mysteries, entityId),
            "conflicts" => RemoveById(entities.Conflicts, entityId),
            "consequences" => RemoveById(entities.Consequences, entityId),
            "creatures" or "misc" or "events" => RemoveById(entities.CustomEntries, entityId),
            _ => false,
        };
    }

    private static bool RemoveById<T>(List<T> items, Guid entityId, Func<T, Guid> getId)
    {
        var index = items.FindIndex(i => getId(i) == entityId);
        if (index < 0)
            return false;

        items.RemoveAt(index);
        return true;
    }

    private static bool RemoveById(List<CharacterEntry> items, Guid entityId) =>
        RemoveById(items, entityId, c => c.Id);

    private static bool RemoveById(List<CompanionEntry> items, Guid entityId) =>
        RemoveById(items, entityId, c => c.Id);

    private static bool RemoveById(List<LocationEntry> items, Guid entityId) =>
        RemoveById(items, entityId, l => l.Id);

    private static bool RemoveById(List<FactionEntry> items, Guid entityId) =>
        RemoveById(items, entityId, f => f.Id);

    private static bool RemoveById(List<ConceptEntry> items, Guid entityId) =>
        RemoveById(items, entityId, c => c.Id);

    private static bool RemoveById(List<QuestEntry> items, Guid entityId) =>
        RemoveById(items, entityId, q => q.Id);

    private static bool RemoveById(List<MysteryEntry> items, Guid entityId) =>
        RemoveById(items, entityId, m => m.Id);

    private static bool RemoveById(List<ConflictEntry> items, Guid entityId) =>
        RemoveById(items, entityId, c => c.Id);

    private static bool RemoveById(List<ConsequenceEntry> items, Guid entityId) =>
        RemoveById(items, entityId, c => c.Id);

    private static bool RemoveById(List<CustomEntry> items, Guid entityId) =>
        RemoveById(items, entityId, c => c.Id);

    private static void RemoveManifestSection(
        AdventureBundle bundle,
        string targetFile,
        string sectionId,
        Guid entityId)
    {
        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, targetFile, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;

        entry.Sections.RemoveAll(s =>
            string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase)
            || (Guid.TryParse(s.SourceEntityId, out var sid) && sid == entityId));
    }

    private static void ApplyScenarioMdEdit(AdventureBundle bundle, string content, string op)
    {
        if (op == "append")
        {
            bundle.Scenario.OpeningSituation = ApplyText(bundle.Scenario.OpeningSituation, content, "append");
            return;
        }

        bundle.Scenario.Setting = ExtractMdField(content, "Setting") ?? bundle.Scenario.Setting;
        bundle.Scenario.PlayerRole = ExtractMdField(content, "Player role") ?? bundle.Scenario.PlayerRole;
        bundle.Scenario.Genre = ExtractMdField(content, "Genre") ?? bundle.Scenario.Genre;
        bundle.Scenario.OpeningSituation = ExtractMdField(content, "Opening") ?? content.Trim();
    }

    private static void ApplyInstructionsEdit(AdventureBundle bundle, string content, string op)
    {
        if (op == "append")
        {
            bundle.Scenario.AuthorsNote = ApplyText(bundle.Scenario.AuthorsNote, content, "append");
            return;
        }

        InstructionContractService.TryApplyFromInstructionsBody(bundle, content);
    }

    private static string ApplyText(string existing, string content, string op) =>
        op == "append" && !string.IsNullOrWhiteSpace(existing)
            ? existing.TrimEnd() + Environment.NewLine + Environment.NewLine + content.Trim()
            : content.Trim();

    private static string? ExtractMdField(string markdown, string label)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (line.Contains($"**{label}:**", StringComparison.OrdinalIgnoreCase))
                return line[(line.IndexOf(':') + 1)..].Trim().Trim('*');
        }

        return null;
    }

    private static void ClearManualPublishForTarget(AdventureBundle bundle, string target)
    {
        var normalized = target.Equals("instructions", StringComparison.OrdinalIgnoreCase)
            ? "instructions-snippet.md"
            : target;

        var entry = bundle.SourceManifest.Entries
            .FirstOrDefault(e => string.Equals(e.RelativePath, normalized, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
            SourceManifestHelper.ClearManualPublish(entry);
    }
}
