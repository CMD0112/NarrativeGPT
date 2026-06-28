using System.Text;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonFormatGenerator
{
    public static string Generate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Canon format reference");
        sb.AppendLine();
        sb.AppendLine("> **Purpose:** Model-facing rules for sectioned adventure sources. Upload this file to **ChatGPT Project → Files** so the model uses the same section headers, entry shape, and field labels as the wrapper.");
        sb.AppendLine(">");
        sb.AppendLine("> **Maintenance:** Auto-generated from the wrapper canon schema. Do not edit by hand — use **Refresh export** in Designer or Source Manager to regenerate.");
        sb.AppendLine();
        AppendQuickRules(sb);
        sb.AppendLine();
        AppendUploadGuidance(sb);
        sb.AppendLine();
        AppendFilesTable(sb);
        sb.AppendLine();
        AppendCriticalPatterns(sb);
        sb.AppendLine();
        AppendPlayerSection(sb);
        sb.AppendLine();
        AppendEntryKinds(sb);
        sb.AppendLine();
        AppendJsonMapping(sb);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendQuickRules(StringBuilder sb)
    {
        sb.AppendLine("## Quick rules");
        sb.AppendLine();
        sb.AppendLine("1. **One file per domain** — scenario, cast, world, plot, lexicon (see table below).");
        sb.AppendLine("2. **Section headers** use `## section-id` (e.g. `## party`, `## npcs`, `## locations`).");
        sb.AppendLine("3. **Entries** use `### Display Name` followed by labeled fields — not prose paragraphs before labels.");
        sb.AppendLine("4. **Exact labels** — copy spellings from this reference (`Condition:`, `Role:`, `**Setting:**`, etc.).");
        sb.AppendLine("5. **Party companions** — never repeat the `###` name as the first body line; use labeled fields only.");
        sb.AppendLine("6. **Player block** — a single freeform `## player` section with labeled lines, not `###` entries.");
        sb.AppendLine("7. **Stable IDs** — lowercase slugs on `Id:`; optional `Aliases:`, `Pinned:`, `ImagePath:` for Play edits.");
    }

    private static void AppendUploadGuidance(StringBuilder sb)
    {
        sb.AppendLine("## Upload to Project");
        sb.AppendLine();
        sb.AppendLine("- Upload **`canon-format.md`** together with lore files (`cast.md`, `scenario.md`, etc.).");
        sb.AppendLine("- In the wrapper: **Designer → Sources** or **Source Manager → Refresh export**, then drag from the canonical folder.");
        sb.AppendLine("- Mark **Published** in Source Manager after upload so readiness tracks the reference file.");
        sb.AppendLine("- When schema fields change, refresh export and re-upload this file.");
    }

    private static void AppendFilesTable(StringBuilder sb)
    {
        sb.AppendLine("## Files and sections");
        sb.AppendLine();
        sb.AppendLine("| File | Sections | JSON target |");
        sb.AppendLine("|------|----------|-------------|");
        sb.AppendLine("| scenario.md | opening | scenario.json opening fields |");
        sb.AppendLine("| cast.md | player, party, npcs | entities.player, party[], characters[] |");
        sb.AppendLine("| world.md | rules, locations, factions, concepts | scenario.worldRules, entity collections |");
        sb.AppendLine("| plot.md | essentials, quests, mysteries, conflicts, consequences, events | plot fields + entity collections |");
        sb.AppendLine("| lexicon.md | rules, pools, avoid | lexicon fields on scenario.json |");
        sb.AppendLine("| canon-format.md | (this file) | format reference only — not imported to JSON |");
        sb.AppendLine("| narrator-scales.md | dimension presets, scene profiles | narrator preset catalog — not imported to JSON |");
    }

    private static void AppendCriticalPatterns(StringBuilder sb)
    {
        sb.AppendLine("## Critical patterns");
        sb.AppendLine();
        sb.AppendLine("### Party entry (correct)");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("## party");
        sb.AppendLine("### Nessa Vale");
        sb.AppendLine("Id: nessa-vale");
        sb.AppendLine("Condition: Wounded shoulder");
        sb.AppendLine("Relationship: Old friend");
        sb.AppendLine("Attitude: Wary but loyal");
        sb.AppendLine("Goals: Find her brother");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Party entry (wrong — name repeated as first body line)");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("## party");
        sb.AppendLine("### Nessa Vale");
        sb.AppendLine("Nessa Vale");
        sb.AppendLine("Condition: Wounded shoulder");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**Role:** and **Status:** are accepted aliases for **Condition:** and **Attitude:** on party entries.");
    }

    private static void AppendPlayerSection(StringBuilder sb)
    {
        sb.AppendLine("## Player section (## player)");
        sb.AppendLine();
        sb.AppendLine("Freeform labeled block (no ### entries):");
        sb.AppendLine();
        sb.AppendLine("## player");
        foreach (var field in CanonSchemaRegistry.Player.BodyFields)
            sb.AppendLine(FormatExampleLine(field));
        sb.AppendLine();
        sb.AppendLine("When ## player is absent from cast.md, import preserves existing entities.player JSON.");
    }

    private static void AppendEntryKinds(StringBuilder sb)
    {
        sb.AppendLine("## Entry templates by kind");
        sb.AppendLine();
        foreach (var kind in CanonSchemaRegistry.AllKinds.Where(k => !k.IsSingleton))
        {
            sb.AppendLine($"### {SectionSchema.DisplaySectionTitle(kind.SectionId)} ({kind.SourceFile} ## {kind.SectionId})");
            sb.AppendLine();
            sb.AppendLine($"Ui category: {kind.UiCategory}. JSON collection: {kind.CollectionKey}.");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine($"### Example {kind.TypeLabel}");
            foreach (var field in kind.Fields.Where(f => f.Role == CanonFieldRole.Shell))
                sb.AppendLine(FormatExampleLine(field));
            sb.AppendLine();
            foreach (var field in kind.BodyFields.Where(f => f.Format == CanonFieldFormat.FreeformBody))
                sb.AppendLine("(freeform description prose)");
            foreach (var field in kind.BodyFields.Where(f => f.Format != CanonFieldFormat.FreeformBody))
                sb.AppendLine(FormatExampleLine(field));
            sb.AppendLine("```");
            sb.AppendLine();

            if (string.Equals(kind.KindId, CanonSchemaRegistry.PartyKind, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("Use labeled fields — do not put the companion name as the first body line.");
                sb.AppendLine("Role: and Status: are accepted aliases for Condition: and Attitude:.");
                sb.AppendLine("Optional cast shell fields: Tags:, Aliases: (heading or body), ImagePath:.");
                sb.AppendLine();
            }
        }
    }

    private static void AppendJsonMapping(StringBuilder sb)
    {
        sb.AppendLine("## JSON mapping");
        sb.AppendLine();
        sb.AppendLine("- Stable identity: Id slug, display name, Aliases, Pinned, ImagePath (Play edits).");
        sb.AppendLine("- Long-tail attributes: extendedFields map on each entry (entities.json schema v2).");
        sb.AppendLine("- Import writes typed properties first; unknown labels may land in extendedFields.");
        sb.AppendLine();
        sb.AppendLine("### Known field labels");
        sb.AppendLine();
        foreach (var label in CanonSchemaRegistry.EntryFieldPrefixes.OrderBy(l => l, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- {label}");
    }

    private static string FormatExampleLine(CanonFieldSpec field) =>
        field.Format switch
        {
            CanonFieldFormat.BoldLine => $"**{field.Label}:** …",
            CanonFieldFormat.BlockquoteFlavor => "> Flavor: …",
            CanonFieldFormat.PlainLine => $"{field.Label}: …",
            _ => $"{field.Label}: …",
        };
}
