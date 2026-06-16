using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureDesignExtractionService
{
    public static string BuildExtractPrompt(AdventureBundle bundle, AdventureDesignStep step)
    {
        var draft = AdventureDesignService.BuildStepDraftSummary(bundle, step);
        var chat = AdventureDesignService.BuildRecentChatExcerpt(bundle, step);
        var schema = GetJsonSchemaHint(step);

        return $"""
            === DESIGN EXTRACT JOB ===
            Step: {step}
            Adventure: {bundle.Metadata.Title}

            Extract structured fields from the design conversation and current draft.
            Respond with JSON only matching this schema:
            {schema}

            === CURRENT DRAFT ===
            {draft}

            === RECENT DESIGN CHAT ===
            {(string.IsNullOrWhiteSpace(chat) ? "(none)" : chat)}
            """;
    }

    public static List<DesignStepProposal> ParseExtractResponse(AdventureDesignStep step, string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            return step switch
            {
                AdventureDesignStep.Concept => ParseStringFields(doc.RootElement,
                    "setting", "playerRole", "genre", "tone", "openingSituation"),
                AdventureDesignStep.World => ParseStringFields(doc.RootElement,
                    "worldRules", "startingConstraints"),
                AdventureDesignStep.Plot => ParseStringFields(doc.RootElement,
                    "plotEssentials", "majorConflicts"),
                AdventureDesignStep.Cast => ParseCast(doc.RootElement),
                AdventureDesignStep.Lexicon => ParseStringFields(doc.RootElement,
                    "lexiconRules", "lexiconPools", "lexiconAvoid"),
                AdventureDesignStep.Sources => ParseSourceEdits(doc.RootElement),
                AdventureDesignStep.Instructions => ParseInstructionsFields(doc.RootElement),
                AdventureDesignStep.Setup => ParseStringFields(doc.RootElement,
                    "title", "genreHook", "pitch"),
                _ => [],
            };
        }
        catch
        {
            return [];
        }
    }

    public static List<DesignStepProposal> ParseCastEntities(JsonElement root)
    {
        if (!root.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
            return [];

        var lines = new List<string>();
        foreach (var item in entities.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var description = ReadString(item, "description");
            var role = ReadString(item, "role");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            lines.Add($"- **{name}** ({role}): {description}");
        }

        if (lines.Count == 0)
            return [];

        return
        [
            new DesignStepProposal
            {
                FieldKey = "castNotes",
                ProposedValue = string.Join(Environment.NewLine, lines),
                Rationale = "Extracted character list",
            },
        ];
    }

    private static List<DesignStepProposal> ParseCast(JsonElement root)
    {
        var fromEntities = ParseCastEntities(root);
        if (fromEntities.Count > 0)
            return fromEntities;

        return ParseStringFields(root, "castNotes");
    }

    private static List<DesignStepProposal> ParseSourceEdits(JsonElement root)
    {
        var proposals = new List<DesignStepProposal>();

        if (root.TryGetProperty("sourceOutline", out var outline))
        {
            var text = outline.ValueKind == JsonValueKind.String ? outline.GetString() : outline.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                proposals.Add(new DesignStepProposal
                {
                    FieldKey = "sourceOutline",
                    ProposedValue = text!.Trim(),
                    Rationale = "Source outline",
                });
            }
        }

        if (!root.TryGetProperty("sourceEdits", out var edits) || edits.ValueKind != JsonValueKind.Array)
            return proposals;

        var combined = new List<string>();
        foreach (var edit in edits.EnumerateArray())
        {
            var target = ReadString(edit, "targetFile");
            var content = ReadString(edit, "content");
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(content))
                continue;

            combined.Add($"### {target}\n{content}");
        }

        if (combined.Count > 0)
        {
            proposals.Add(new DesignStepProposal
            {
                FieldKey = "sourceOutline",
                ProposedValue = string.Join(Environment.NewLine + Environment.NewLine, combined),
                Rationale = "Merged source file drafts",
            });
        }

        return proposals;
    }

    private static List<DesignStepProposal> ParseInstructionsFields(JsonElement root)
    {
        var proposals = ParseStringFields(
            root,
            "authorsNote",
            InstructionContractService.GlobalBoundariesFieldKey,
            InstructionContractService.CharacterPortrayalFieldKey,
            InstructionContractService.InstructionAddendumFieldKey);

        if (proposals.Count > 0)
            return proposals;

        return ParseStringFields(root, "authorsNote", InstructionContractService.LegacyNarratorBoundariesFieldKey)
            .Select(p => new DesignStepProposal
            {
                FieldKey = InstructionContractService.GlobalBoundariesFieldKey,
                ProposedValue = p.ProposedValue,
                Rationale = p.Rationale,
            })
            .ToList();
    }

    private static List<DesignStepProposal> ParseStringFields(JsonElement root, params string[] keys)
    {
        var proposals = new List<DesignStepProposal>();
        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var el))
                continue;

            var value = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            proposals.Add(new DesignStepProposal
            {
                FieldKey = key,
                ProposedValue = value!.Trim(),
                Rationale = "Extracted from design chat",
            });
        }

        return proposals;
    }

    private static string GetJsonSchemaHint(AdventureDesignStep step) => step switch
    {
        AdventureDesignStep.Setup =>
            """{ "title": string, "genreHook": string, "pitch": string }""",
        AdventureDesignStep.Concept =>
            """{ "setting": string, "playerRole": string, "genre": string, "tone": string, "openingSituation": string }""",
        AdventureDesignStep.World =>
            """{ "worldRules": string, "startingConstraints": string }""",
        AdventureDesignStep.Plot =>
            """{ "plotEssentials": string, "majorConflicts": string }""",
        AdventureDesignStep.Cast =>
            """{ "castNotes": string, "entities": [ { "name": string, "description": string, "role": string } ] }""",
        AdventureDesignStep.Lexicon =>
            """{ "lexiconRules": string, "lexiconPools": string, "lexiconAvoid": string }""",
        AdventureDesignStep.Sources =>
            """{ "sourceOutline": string, "sourceEdits": [ { "targetFile": string, "operation": "replace"|"append", "content": string } ] }""",
        AdventureDesignStep.Instructions =>
            """{ "authorsNote": string, "globalBoundaries": string, "characterPortrayalRules": string, "instructionAddendum": string }""",
        _ => """{ }""",
    };

    private static string ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? ""
            : "";
}
