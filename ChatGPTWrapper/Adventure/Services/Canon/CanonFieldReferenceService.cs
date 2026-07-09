using System.Text;

namespace ChatGPTWrapper.Adventure.Services.Canon;

/// <summary>
/// Builds model-facing canon field definitions for prompts and canon-format export.
/// </summary>
internal static class CanonFieldReferenceService
{
    public static string BuildExtendedFieldsPolicy() =>
        """
        ### Custom and extended fields

        - **Typed fields** listed per kind below map to `entities.json` properties (markdown label → jsonKey).
        - **extendedFields** is a string map on each entry for adventure-specific attributes without a typed field.
        - On import, known labels populate typed fields first; unrecognized labels may land in extendedFields.
        - Prefer typed fields when a label matches this reference. Use extendedFields only for novel attributes.
        - extendedFields persist in JSON and round-trip to markdown sources on export.
        - AI extraction and JSON import may include `extendedFields` for custom keys; use canonical spellings when a typed field exists.
        """;

    public static string BuildKindFieldDefinitions(CanonEntityKindSpec kind)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#### {kind.TypeLabel} (`## {kind.SectionId}` → `{kind.CollectionKey}`)");
        sb.AppendLine();
        sb.AppendLine("| Label | jsonKey | Notes |");
        sb.AppendLine("|-------|---------|-------|");

        foreach (var field in kind.Fields.Where(f =>
                     f.Role != CanonFieldRole.Shell
                     && f.Format != CanonFieldFormat.FreeformBody))
        {
            var notes = new List<string>();
            if (field.AlternateLabels.Count > 0)
                notes.Add($"aliases: {string.Join(", ", field.AlternateLabels)}");
            if (field.Format == CanonFieldFormat.BlockquoteFlavor)
                notes.Add("markdown: > Flavor:");
            if (field.Multiline)
                notes.Add("multiline");
            if (string.Equals(field.JsonKey, "useInPlay", StringComparison.Ordinal))
                notes.Add("out-of-character scene-running notes; not Role or Motives");

            sb.AppendLine($"| {field.Label} | `{field.JsonKey}` | {string.Join("; ", notes)} |");
        }

        return sb.ToString().TrimEnd();
    }

    public static string BuildCastFieldDefinitions()
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildKindFieldDefinitions(CanonSchemaRegistry.Party));
        sb.AppendLine();
        sb.AppendLine(BuildKindFieldDefinitions(CanonSchemaRegistry.Npc));
        return sb.ToString().TrimEnd();
    }

    public static string BuildEntityFieldDefinitionsAppendix()
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Entity field definitions (cast)");
        sb.AppendLine();
        sb.AppendLine(BuildCastFieldDefinitions());
        sb.AppendLine();
        sb.AppendLine(BuildExtendedFieldsPolicy());
        return sb.ToString().TrimEnd();
    }

    public static string BuildPromptFieldSummaryForKind(string kindId, int maxFields = 16)
    {
        var kind = CanonSchemaRegistry.AllKinds.FirstOrDefault(k =>
            string.Equals(k.KindId, kindId, StringComparison.OrdinalIgnoreCase));
        if (kind is null)
            return "";

        return string.Join(", ",
            kind.BodyFields
                .Where(f => f.Format != CanonFieldFormat.FreeformBody)
                .Take(maxFields)
                .Select(f => f.AlternateLabels.Count > 0
                    ? $"{f.Label} ({string.Join("/", f.AlternateLabels)})"
                    : f.Label));
    }

    public static string BuildPromptCastFieldSummary() =>
        $"Party: {BuildPromptFieldSummaryForKind(CanonSchemaRegistry.PartyKind)}. "
        + $"NPC: {BuildPromptFieldSummaryForKind(CanonSchemaRegistry.NpcKind)}.";
}
