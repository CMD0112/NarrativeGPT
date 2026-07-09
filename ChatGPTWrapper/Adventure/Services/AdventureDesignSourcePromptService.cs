using System.IO;
using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

internal readonly record struct DesignSourcePromptDefinition(
    string RelativePath,
    string ButtonLabel,
    string Summary,
    AdventureDesignStep? PrimaryStep);

internal sealed class SourcePipelineChecklistRow
{
    public int Position { get; init; }

    public int LoreTotal { get; init; }

    public required string RelativePath { get; init; }

    public required string Label { get; init; }

    public bool PromptSent { get; init; }

    public bool PresentOnDisk { get; init; }

    public bool IsNextRecommended { get; init; }

    public bool IsBlocked { get; init; }

    public string? BlockedReason { get; init; }

    public AdventureDesignStep? PrimaryStep { get; init; }

    public bool IsLoreFile { get; init; }

    public bool IsReferenceFile { get; init; }

    public bool IsPublishedToProject { get; init; }
}

internal static class AdventureDesignSourcePromptService
{
    public static IReadOnlyList<DesignSourcePromptDefinition> AllDefinitions { get; } =
    [
        new(
            SectionSchema.ScenarioFile,
            "Draft scenario.md",
            "Opening situation, setting, player role, genre",
            AdventureDesignStep.Concept),
        new(
            SectionSchema.WorldFile,
            "Draft world.md",
            "World rules, locations, factions, concepts",
            AdventureDesignStep.World),
        new(
            SectionSchema.PlotFile,
            "Draft plot.md",
            "Plot essentials, quests, mysteries, conflicts",
            AdventureDesignStep.Plot),
        new(
            SectionSchema.CastFile,
            "Draft cast.md",
            "Player character, party, NPCs",
            AdventureDesignStep.Cast),
        new(
            SectionSchema.LexiconFile,
            "Draft lexicon.md",
            "Naming, tone, pools, and anti-repetition for all entities",
            AdventureDesignStep.Lexicon),
        new(
            "instructions-snippet.md",
            "Refine instructions with AI",
            "Refine the generated narrator contract (deterministic base + optional AI polish)",
            AdventureDesignStep.Instructions),
    ];

    public static IReadOnlyList<DesignSourcePromptDefinition> ReferenceDefinitions { get; } =
    [
        new(
            SectionSchema.CanonFormatFile,
            "Canon format reference",
            "Section templates, field labels, party/NPC rules — auto-generated; upload to Project Files",
            AdventureDesignStep.Sources),
        new(
            SectionSchema.NarratorScalesFile,
            "Narrator scales reference",
            "Preset definitions for length, detail, tone, difficulty, violence — auto-generated; upload to Project Files",
            AdventureDesignStep.Sources),
        new(
            SectionSchema.EntityStateFormatFile,
            "Entity state format reference",
            "Mutable play-state field paths for entity-state.json — auto-generated; upload to Project Files",
            AdventureDesignStep.Sources),
    ];

    public static bool ExportReferenceFiles(AdventureBundle bundle, SourceExportMode mode = SourceExportMode.IfStale) =>
        ProjectSourceExportService.ExportReferenceFiles(bundle, mode);

    public static bool AnyReferenceFilesMissing(AdventureBundle bundle) =>
        ReferenceDefinitions.Any(def =>
            !File.Exists(AdventureSourceFileService.ResolveAbsolutePath(bundle, def.RelativePath)));

    public static IReadOnlyList<string> PromptPipelineOrder { get; } =
    [
        SectionSchema.CastFile,
        SectionSchema.ScenarioFile,
        SectionSchema.WorldFile,
        SectionSchema.PlotFile,
        SectionSchema.LexiconFile,
        "instructions-snippet.md",
    ];

    private static readonly Dictionary<string, string[]> PipelineDependencies =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SectionSchema.CastFile] = [],
            [SectionSchema.ScenarioFile] = [SectionSchema.CastFile],
            [SectionSchema.WorldFile] = [SectionSchema.CastFile, SectionSchema.ScenarioFile],
            [SectionSchema.PlotFile] =
            [
                SectionSchema.CastFile,
                SectionSchema.ScenarioFile,
                SectionSchema.WorldFile,
            ],
            [SectionSchema.LexiconFile] =
            [
                SectionSchema.CastFile,
                SectionSchema.ScenarioFile,
                SectionSchema.WorldFile,
                SectionSchema.PlotFile,
            ],
            ["instructions-snippet.md"] =
            [
                SectionSchema.CastFile,
                SectionSchema.ScenarioFile,
                SectionSchema.WorldFile,
                SectionSchema.PlotFile,
                SectionSchema.LexiconFile,
            ],
        };

    public static IEnumerable<DesignSourcePromptDefinition> ForDesignStep(AdventureDesignStep step)
    {
        if (step is AdventureDesignStep.Sources or AdventureDesignStep.Review)
            return AllDefinitions;

        return AllDefinitions.Where(d => d.PrimaryStep == step);
    }

    public static IEnumerable<DesignSourcePromptDefinition> ForDesignStepInPipelineOrder(AdventureDesignStep step)
    {
        var allowed = ForDesignStep(step)
            .Select(d => d.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in PromptPipelineOrder)
        {
            if (allowed.Contains(path) && TryGetDefinition(path, out var definition))
                yield return definition;
        }
    }

    public static IReadOnlyList<string> GetPipelineDependencies(string relativePath) =>
        PipelineDependencies.TryGetValue(relativePath, out var deps)
            ? deps
            : [];

    public static string? GetNextRecommendedPath(AdventureBundle bundle)
    {
        AdventureDesignService.EnsureWorkspace(bundle);
        foreach (var path in PromptPipelineOrder)
        {
            if (!AdventureDesignService.IsSourceFilePromptSent(bundle, path))
                return path;
        }

        return null;
    }

    public static bool IsOutOfOrder(AdventureBundle bundle, string relativePath)
    {
        AdventureDesignService.EnsureWorkspace(bundle);
        return GetPipelineDependencies(relativePath)
            .Any(dep => !AdventureDesignService.IsSourceFilePromptSent(bundle, dep));
    }

    public static string? GetOutOfOrderTooltip(AdventureBundle bundle, string relativePath)
    {
        AdventureDesignService.EnsureWorkspace(bundle);
        var missing = GetPipelineDependencies(relativePath)
            .Where(dep => !AdventureDesignService.IsSourceFilePromptSent(bundle, dep))
            .ToList();

        return missing.Count == 0
            ? null
            : $"Recommended after: {string.Join(", ", missing)}";
    }

    public static IReadOnlyList<SourcePipelineChecklistRow> BuildPipelineChecklist(AdventureBundle bundle)
    {
        AdventureDesignService.EnsureWorkspace(bundle);
        var onDisk = AdventureSourceFileService.GetPipelineStatuses(bundle)
            .ToDictionary(s => s.RelativePath, s => s.Present, StringComparer.OrdinalIgnoreCase);
        var next = GetNextRecommendedPath(bundle);
        var loreCount = PromptPipelineOrder.Count(p =>
            !string.Equals(p, InstructionContractService.InstructionsSnippetFile, StringComparison.OrdinalIgnoreCase));

        var rows = new List<SourcePipelineChecklistRow>();
        foreach (var refDef in ReferenceDefinitions)
        {
            var refPath = refDef.RelativePath;
            var absolutePath = AdventureSourceFileService.ResolveAbsolutePath(bundle, refPath);
            var present = File.Exists(absolutePath);
            var entry = bundle.SourceManifest.Entries.FirstOrDefault(e =>
                string.Equals(e.RelativePath, refPath, StringComparison.OrdinalIgnoreCase));

            rows.Add(new SourcePipelineChecklistRow
            {
                Position = 0,
                LoreTotal = loreCount,
                RelativePath = refPath,
                Label = refDef.ButtonLabel,
                PromptSent = true,
                PresentOnDisk = present,
                IsNextRecommended = false,
                IsBlocked = false,
                PrimaryStep = refDef.PrimaryStep,
                IsLoreFile = false,
                IsReferenceFile = true,
                IsPublishedToProject = entry?.IsManuallyCurrent() ?? false,
            });
        }

        var lorePosition = 0;
        for (var i = 0; i < PromptPipelineOrder.Count; i++)
        {
            var path = PromptPipelineOrder[i];
            TryGetDefinition(path, out var def);
            onDisk.TryGetValue(path, out var present);
            var blocked = IsOutOfOrder(bundle, path);
            var isLore = !string.Equals(path, InstructionContractService.InstructionsSnippetFile, StringComparison.OrdinalIgnoreCase);
            if (isLore)
                lorePosition++;

            rows.Add(new SourcePipelineChecklistRow
            {
                Position = isLore ? lorePosition : 0,
                LoreTotal = loreCount,
                RelativePath = path,
                Label = def.ButtonLabel ?? path,
                PromptSent = AdventureDesignService.IsSourceFilePromptSent(bundle, path),
                PresentOnDisk = present,
                IsNextRecommended = string.Equals(path, next, StringComparison.OrdinalIgnoreCase),
                IsBlocked = blocked,
                BlockedReason = blocked ? GetOutOfOrderTooltip(bundle, path) : null,
                PrimaryStep = def.PrimaryStep,
                IsLoreFile = isLore,
                IsReferenceFile = false,
            });
        }

        return rows;
    }

    public static string? GetCombinedSelectionWarning(AdventureBundle bundle, IReadOnlyList<string> selectedPaths)
    {
        AdventureDesignService.EnsureWorkspace(bundle);
        var normalized = NormalizeSelectedPaths(selectedPaths);
        if (normalized.Count == 0)
            return null;

        var selected = normalized.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in normalized)
        {
            var missing = GetPipelineDependencies(path)
                .Where(dep =>
                    !AdventureDesignService.IsSourceFilePromptSent(bundle, dep)
                    && !selected.Contains(dep))
                .ToList();
            if (missing.Count > 0)
            {
                return $"{path} needs prior prompts sent or included in selection: {string.Join(", ", missing)}.";
            }
        }

        return null;
    }

    public static bool TryGetDefinition(string relativePath, out DesignSourcePromptDefinition definition)
    {
        if (SectionSchema.IsReferenceSourceFile(relativePath))
        {
            var match = ReferenceDefinitions.FirstOrDefault(d =>
                string.Equals(d.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            if (match.RelativePath is not null)
            {
                definition = match;
                return true;
            }
        }

        var loreMatch = AllDefinitions.FirstOrDefault(d =>
            string.Equals(d.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (loreMatch.RelativePath is null)
        {
            definition = default;
            return false;
        }

        definition = loreMatch;
        return true;
    }

    public static string BuildPrefixedFileName(string adventureTitle, string relativePath)
    {
        var safeTitle = SanitizeFileNameComponent(adventureTitle);
        return $"{safeTitle} - {relativePath}";
    }

    public static string BuildPrefixedSourcesPath(string adventureTitle, string relativePath) =>
        $"sources/{BuildPrefixedFileName(adventureTitle, relativePath)}";

    private static string SanitizeFileNameComponent(string? adventureTitle)
    {
        if (string.IsNullOrWhiteSpace(adventureTitle))
            return "Adventure";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(adventureTitle.Length);
        foreach (var c in adventureTitle.Trim())
        {
            sb.Append(invalid.Contains(c) ? '-' : c);
        }

        var sanitized = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Adventure" : sanitized;
    }

    public static string BuildPrompt(AdventureBundle bundle, string relativePath)
    {
        if (!TryGetDefinition(relativePath, out _))
            throw new ArgumentException($"Unknown design source file: {relativePath}", nameof(relativePath));

        AdventureDesignService.EnsureWorkspace(bundle);
        var title = bundle.Metadata.Title;
        var context = BuildDesignContextBlock(bundle);

        return relativePath.ToLowerInvariant() switch
        {
            SectionSchema.ScenarioFile => BuildSingleFilePrompt(
                title, context, bundle, SectionSchema.ScenarioFile,
                "Opening situation, setting, player role, and genre — the playable starting point.",
                BuildScenarioSpecification(title, bundle)),
            SectionSchema.WorldFile => BuildSingleFilePrompt(
                title, context, bundle, SectionSchema.WorldFile,
                "World bible — rules, locations, factions, and concepts for RAG lookup during play.",
                BuildWorldSpecification(bundle)),
            SectionSchema.PlotFile => BuildSingleFilePrompt(
                title, context, bundle, SectionSchema.PlotFile,
                "Plot spine — essentials, quests, mysteries, conflicts, and events for RAG lookup during play.",
                BuildPlotSpecification(bundle)),
            SectionSchema.CastFile => BuildSingleFilePrompt(
                title, context, bundle, SectionSchema.CastFile,
                "Cast reference — player, party, and NPCs for RAG lookup during play.",
                BuildCastSpecification(bundle)),
            SectionSchema.LexiconFile => BuildSingleFilePrompt(
                title, context, bundle, SectionSchema.LexiconFile,
                "Lexicon — naming rules, tone consistency, anti-repetition guidance, name pools, and registry of names already in use for all entity types.",
                BuildLexiconSpecification(bundle)),
            "instructions-snippet.md" => InstructionRefinementPromptService.BuildRefinementPrompt(bundle),
            _ => throw new ArgumentException($"Unknown design source file: {relativePath}", nameof(relativePath)),
        };
    }

    public static IReadOnlyList<string> NormalizeSelectedPaths(IEnumerable<string> relativePaths) =>
        relativePaths
            .Where(p => TryGetDefinition(p, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => PromptPipelineOrder.ToList().FindIndex(o =>
                string.Equals(o, p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    public static string BuildCombinedPrompt(AdventureBundle bundle, IEnumerable<string> relativePaths)
    {
        var paths = NormalizeSelectedPaths(relativePaths);
        if (paths.Count == 0)
            throw new ArgumentException("Select at least one source file.", nameof(relativePaths));

        if (paths.Count == 1)
            return BuildPrompt(bundle, paths[0]);

        AdventureDesignService.EnsureWorkspace(bundle);
        var title = bundle.Metadata.Title;
        var context = BuildDesignContextBlock(bundle);
        var specBlocks = paths
            .Select(path => $"""
                === FILE: {BuildPrefixedSourcesPath(title, path)} ===
                Canonical source type: `{path}`
                {BuildFileSpecification(bundle, path)}
                """)
            .ToList();

        return $"""
            === DELIVERABLE: multiple Project source files ({paths.Count}) ===
            Adventure: {title}

            {BuildAdventureTitleBlock(title)}
            {BuildGroupedCoherenceBlock(title, paths)}
            {BuildMultiFileDeliveryBlock(title, paths)}

            {string.Join(Environment.NewLine + Environment.NewLine, specBlocks)}

            --- DESIGN CONTEXT (shared — all files must align) ---
            {context}
            --- END CONTEXT ---

            {BuildMultiFileDeliveryClosing(title, paths)}
            """;
    }

    private static string BuildSingleFilePrompt(
        string title,
        string context,
        AdventureBundle bundle,
        string relativePath,
        string filePurpose,
        string specification)
    {
        var prefixedPath = BuildPrefixedSourcesPath(title, relativePath);
        return $"""
            === DELIVERABLE: {prefixedPath} ===
            Adventure: {title}

            {BuildAdventureTitleBlock(title)}
            {BuildPriorSourceFilesBlock(bundle, relativePath)}
            {BuildFileDeliveryBlock(title, relativePath, filePurpose)}

            {specification}

            --- DESIGN CONTEXT ---
            {context}
            --- END CONTEXT ---

            Write the full contents of `{prefixedPath}` now. {BuildFileDeliveryClosing(title, relativePath)}
            """;
    }

    public static string BuildAdventureTitleBlockForPrompt(string title) =>
        BuildAdventureTitleBlock(title);

    private static string BuildAdventureTitleBlock(string title)
    {
        var example = BuildPrefixedSourcesPath(title, SectionSchema.ScenarioFile);
        return $"""
        **Adventure identity (mandatory)**
        - Wrapper adventure title: **{title}**
        - Use this exact title in scenario H1, instructions header, and prefixed download filenames.
        - Filename pattern: `{SanitizeFileNameComponent(title)} - [source file name].md` (e.g. `{example}`)
        - Do **not** use a different adventure name from chat history or the ChatGPT Project UI.
        """;
    }

    private static string BuildPriorSourceFilesBlock(AdventureBundle bundle, string relativePath)
    {
        AdventureDesignService.EnsureWorkspace(bundle);
        var sentPrior = GetPipelineDependencies(relativePath)
            .Where(dep => AdventureDesignService.IsSourceFilePromptSent(bundle, dep))
            .ToList();

        if (sentPrior.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("--- PRIOR SOURCE FILES (this design thread) ---");
        sb.AppendLine("Already drafted — align with your prior replies for these files; do not contradict them:");
        foreach (var path in sentPrior)
        {
            sb.AppendLine($"- `{path}`");
            if (bundle.DesignWorkspace.SourceFilesPrompted.TryGetValue(path, out var state)
                && !string.IsNullOrWhiteSpace(state.AssistantExcerpt))
            {
                sb.AppendLine($"  Excerpt from last reply:");
                sb.AppendLine(state.AssistantExcerpt.Trim());
            }
        }

        sb.AppendLine();
        sb.AppendLine($"You are now drafting: `{relativePath}`");
        sb.AppendLine("Use the same proper names, places, and facts established above.");
        sb.AppendLine("Do not revert to role-only placeholders (\"the wife\", \"the rescued girl\", \"the player\") for entities already named.");
        sb.AppendLine("--- END PRIOR ---");
        return sb.ToString().Trim();
    }

    private static string BuildGroupedCoherenceBlock(string title, IReadOnlyList<string> relativePaths)
    {
        var fileList = string.Join(", ", relativePaths.Select(p => $"`{p}`"));
        return $"""
        **Coherence rules (all {relativePaths.Count} files)**
        - This is one source pack for a single adventure: **{title}**.
        - Files requested: {fileList}
        - Use **specific proper names** for all major people, places, and factions in every file.
        - Do not use role-only placeholders once a name exists in cast or scenario.
        - scenario, world, plot, cast, and lexicon must agree on names, geography, family relationships, war status, and opening situation.
        - `lexicon.md` `## in-use` must list names actually used in the other files — not an empty naming scaffold.
        - `instructions-snippet.md` must reflect only the canonical manual instruction contract — refine wording, do not invent boundaries from lore.
        """;
    }

    private static string BuildFileSpecification(AdventureBundle bundle, string relativePath) =>
        relativePath.ToLowerInvariant() switch
        {
            SectionSchema.ScenarioFile => BuildScenarioSpecification(bundle.Metadata.Title, bundle),
            SectionSchema.WorldFile => BuildWorldSpecification(bundle),
            SectionSchema.PlotFile => BuildPlotSpecification(bundle),
            SectionSchema.CastFile => BuildCastSpecification(bundle),
            SectionSchema.LexiconFile => BuildLexiconSpecification(bundle),
            "instructions-snippet.md" => BuildInstructionsRefinementSpecification(bundle),
            _ => throw new ArgumentException($"Unknown design source file: {relativePath}", nameof(relativePath)),
        };

    private static string BuildMultiFileDeliveryBlock(string adventureTitle, IReadOnlyList<string> relativePaths)
    {
        var prefixedNames = relativePaths
            .Select(p => BuildPrefixedFileName(adventureTitle, p))
            .ToList();
        var fileList = string.Join(
            Environment.NewLine,
            relativePaths.Select(p => $"- `{BuildPrefixedSourcesPath(adventureTitle, p)}` (canonical type: `{p}`)"));
        var inlineBlocks = string.Join(
            Environment.NewLine,
            prefixedNames.Select(name => $"   --- begin {name} --- / --- end {name} ---"));

        return $"""
        **DELIVERABLE — {relativePaths.Count} Project source files (one batch)**
        You are being asked to produce **multiple separate markdown files** in a single response. Each file is independent and will live in the Project `sources/` folder.
        **Filename rule:** every file must be named `{SanitizeFileNameComponent(adventureTitle)} - [source file name].md` (adventure title prepended).

        **Files requested ({relativePaths.Count})**
        {fileList}

        **Two-part response (both required for EACH file)**
        1. **Downloadable files** — create and output **{relativePaths.Count} separate** markdown files, one per prefixed filename above (use file-creation / export; filenames must match exactly, including the adventure title prefix).
        2. **Inline file contents** — in the same reply, include the **complete markdown** for **every** file in its own marked block, in the order listed above:
        {inlineBlocks}
        Each downloadable file must match its inline block exactly.

        **Cross-file rules**
        - Produce all {relativePaths.Count} files in this request — do not skip any.
        - Keep facts consistent across files and with the shared design context below.
        - Do not merge multiple files into one document or one download.
        - A short intro before the first inline block is fine; do not substitute commentary for file bodies.
        """;
    }

    private static string BuildMultiFileDeliveryClosing(string adventureTitle, IReadOnlyList<string> relativePaths)
    {
        var names = string.Join(", ", relativePaths.Select(p => $"`{BuildPrefixedFileName(adventureTitle, p)}`"));
        return $"""
        Deliver all {relativePaths.Count} files now: separate downloads for {names}, plus matching `--- begin … ---` / `--- end … ---` inline blocks for each prefixed filename.
        """;
    }

    private static string BuildFileDeliveryBlock(string adventureTitle, string relativePath, string filePurpose)
    {
        var prefixedName = BuildPrefixedFileName(adventureTitle, relativePath);
        var prefixedPath = BuildPrefixedSourcesPath(adventureTitle, relativePath);
        return $"""
        **DELIVERABLE — one Project source file**
        - File: `{prefixedName}` in the Project `sources/` folder (`{prefixedPath}`)
        - Canonical source type: `{relativePath}` (content schema — the **filename** must still use the adventure prefix)
        - Purpose: {filePurpose}
        - This is canonical lore retrieved by RAG during play. Write the real file — do not summarize it or tell the author what to put in it.

        **Two-part response (both required)**
        1. **Downloadable file** — create and output a `{prefixedName}` markdown file the author can download and upload to the Project (use your file-creation / export capability; filename must be `{prefixedName}` exactly).
        2. **Inline file contents** — in the same reply, include the **complete markdown text** of that file inside a clearly marked block:
           ```
           --- begin {prefixedName} ---
           (full file contents)
           --- end {prefixedName} ---
           ```
        The downloadable file and the inline block must contain **identical** markdown.

        **Response rules**
        - A one- or two-sentence intro before the inline block is fine; do not substitute commentary for the file body.
        - Do not wrap the inline block in an extra outer code fence labeled "markdown".
        - One file only — no alternate versions, no JSON.
        """;
    }

    private static string BuildFileDeliveryClosing(string adventureTitle, string relativePath)
    {
        var prefixedName = BuildPrefixedFileName(adventureTitle, relativePath);
        var prefixedPath = BuildPrefixedSourcesPath(adventureTitle, relativePath);
        return $"""
        Deliver `{prefixedPath}` now: (1) downloadable `{prefixedName}` file, and (2) the same contents in an `--- begin {prefixedName} ---` … `--- end {prefixedName} ---` block.
        """;
    }

    private static string BuildScenarioSpecification(string title, AdventureBundle bundle)
    {
        var template = GetTemplate(SectionSchema.ScenarioFile);
        return $"""
            **Required file shape**
            ```
            # {title}
            ## opening
            **Setting:** …
            **Player role:** …
            **Genre:** …
            **Opening:** …
            ```
            Optional lines when relevant: **Conflicts:**, **Constraints:**, **Tone:**.

            **Content rules**
            - The H1 must be exactly `# {title}` — no other adventure title.
            - Use the exact `## opening` section header (machine-readable).
            - Use proper names from cast.md for PC, family, NPCs, and key locations — no generic placeholders.
            - Keep the opening playable: one clear starting situation the narrator can run immediately.
            - Do not invent facts that contradict the design context below.

            **Format hint:** {template.InlineHint}
            """;
    }

    private static string BuildWorldSpecification(AdventureBundle bundle)
    {
        var template = GetTemplate(SectionSchema.WorldFile);
        var worldDraft = AdventureDesignService.BuildStepDraftSummary(bundle, AdventureDesignStep.World);
        return $"""
            **Required top-level sections** (include only sections with content):
            - `## rules` — how the world works, magic/tech limits, social order, hard constraints
            - `## locations` — places the story may visit
            - `## factions` — groups with goals and relationships
            - `## concepts` — important ideas, artifacts, phenomena (non-NPC)

            **Entry format** (locations, factions, concepts):
            ```
            ### Place or Faction Name
            Id: url-safe-slug
            Aliases: comma, separated
            Body text…
            ```

            **Content rules**
            - Start the file with `# World`.
            - Use stable `Id:` slugs (lowercase, hyphens).
            - Use proper names from cast and scenario — no role-only placeholders for major entities.
            - Prefer concrete, play-ready facts over vague lore dumps.
            - Align with scenario opening and plot essentials in the context below.

            **Format hint:** {template.InlineHint}
            {BuildCanonFormatCitation(bundle)}

            **Current world step draft**
            {worldDraft}
            """;
    }

    private static string BuildPlotSpecification(AdventureBundle bundle)
    {
        var template = GetTemplate(SectionSchema.PlotFile);
        var plotDraft = AdventureDesignService.BuildStepDraftSummary(bundle, AdventureDesignStep.Plot);
        return $"""
            **Required top-level sections** (include only sections with content):
            - `## essentials` — what the story is about; core tension; what must not be forgotten
            - `## quests` — explicit goals the player might pursue
            - `## mysteries` — unknowns to uncover
            - `## conflicts` — opposing forces, deadlines, dilemmas
            - `## events` — optional scheduled or likely story beats

            **Entry format** (quests, mysteries, conflicts, events):
            ```
            ### Short Title
            Id: url-safe-slug
            Body text…
            ```

            **Content rules**
            - Start the file with `# Plot`.
            - Use proper names from cast and scenario in quests, mysteries, and conflicts.
            - Essentials must be readable in one pass before play.
            - Do not write prose narration — write reference material for the narrator.

            **Format hint:** {template.InlineHint}
            {BuildCanonFormatCitation(bundle)}

            **Current plot step draft**
            {plotDraft}
            """;
    }

    private static string BuildCastSpecification(AdventureBundle bundle)
    {
        var template = GetTemplate(SectionSchema.CastFile);
        var castDraft = AdventureDesignService.BuildStepDraftSummary(bundle, AdventureDesignStep.Cast);
        var castNotes = AdventureDesignService.GetFreeform(bundle, AdventureDesignStep.Cast);
        return $"""
            **Required sections**
            - `## player` — name, background, appearance, personality, abilities, weaknesses, goals (plain lines or bullets)
            - `## party` — optional companions (### entries with Id; Condition, Relationship, Attitude, Goals, Personality, Abilities, Weaknesses)
            - `## npcs` — important non-player characters (### entries with Id, Aliases, Role, Relationship, Motives, Personality, Author guidance, optional > Flavor quote)

            **NPC entry example**
            ```
            ### Name
            Id: name-slug
            Aliases: Alias One, Alias Two
            Role: …
            Relationship: …
            Motives: …
            Personality: …
            Author guidance: …

            > Flavor: optional voice line
            ```

            **Party entry example**
            ```
            ### Companion Name
            Id: companion-slug
            Condition: …
            Relationship: …
            Attitude: …
            Goals: …
            Personality: …
            Abilities: …
            Weaknesses: …
            ```

            **Content rules**
            - Start the file with `# Cast`.
            - Assign **concrete proper names** to the player, family, rescued child, old friend, and every major NPC.
            - Major entities must be named here — other source files will use these names.
            - Give every ### entry a unique `Id:` slug.
            - NPCs should be playable: motives, relationship to player, and status when known.

            **Format hint:** {template.InlineHint}
            {BuildCanonFormatCitation(bundle)}

            **Cast step draft**
            {castDraft}
            {(string.IsNullOrWhiteSpace(castNotes) ? "" : "\nAdditional cast notes:\n" + castNotes.Trim())}
            """;
    }

    private static string BuildLexiconSpecification(AdventureBundle bundle)
    {
        var template = GetTemplate(SectionSchema.LexiconFile);
        var lexiconDraft = AdventureDesignService.BuildStepDraftSummary(bundle, AdventureDesignStep.Lexicon);
        var inUsePreview = LexiconExportService.BuildInUsePreview(bundle);
        return $"""
            **Required top-level sections**
            - `## rules` — how to name new people, places, groups, realms, landmarks; tone and diction consistency; avoid repeating the same phrases
            - `## in-use` — registry of names already taken (people, places, groups, plot entities, other). Preserve or extend the preview below; do not remove listed names.
            - `## pools` — setting-appropriate name pools grouped by region, culture, or entity type (people, places, realms, factions)
            - `## avoid` — overused default names and repetitive phrases the narrator must not reuse in this adventure

            **Entity coverage**
            - People: player, NPCs, companions, titles used as names
            - Places: cities, regions, landmarks, dungeons, ships, estates
            - Groups: factions, kingdoms, orders, guilds, armies, religions
            - Plot: quest titles, mystery names, major conflict labels when used as proper nouns
            - Other: artifacts, creatures, concepts when they have proper names

            **Content rules**
            - Start the file with `# Lexicon`.
            - Build a populated `## in-use` registry from names established in cast, scenario, world, and plot — not an empty scaffold.
            - The wrapper auto-refreshes `## in-use` on export — still author a complete registry here for design.
            - Pools should fit the setting in the design context; avoid generic fantasy filler unless appropriate.
            - Rules must tell the narrator to check in-use before inventing any new proper name.

            **Format hint:** {template.InlineHint}

            **Current in-use preview (from local entities — merge into ## in-use)**
            {inUsePreview}

            **Lexicon step draft**
            {lexiconDraft}
            """;
    }

    private static string BuildInstructionsRefinementSpecification(AdventureBundle bundle)
    {
        var canonical = InstructionContractService.BuildInstructionsSnippetFileContent(bundle);
        return $"""
            **Refinement spec (instructions-snippet.md)**
            Refine the canonical manual version below. Do not invent boundaries or portrayal rules from other files.

            **Canonical manual version**
            ```
            {canonical.Trim()}
            ```

            See the combined deliverable block for response format.
            """;
    }

    private static SourceFileTemplate GetTemplate(string relativePath)
    {
        if (ProjectSourceFileTemplates.TryGet(relativePath, out var template))
            return template;

        return new SourceFileTemplate
        {
            RelativePath = relativePath,
            Role = relativePath,
            Summary = "",
            InlineHint = "(see section schema)",
        };
    }

    private static string BuildDesignContextBlock(AdventureBundle bundle)
    {
        var sb = new StringBuilder();
        AppendStepSummary(sb, bundle, AdventureDesignStep.Concept);
        AppendStepSummary(sb, bundle, AdventureDesignStep.World);
        AppendStepSummary(sb, bundle, AdventureDesignStep.Plot);
        AppendStepSummary(sb, bundle, AdventureDesignStep.Cast);
        AppendStepSummary(sb, bundle, AdventureDesignStep.Lexicon);
        AppendStepSummary(sb, bundle, AdventureDesignStep.Instructions);

        var genre = bundle.Metadata.Genre;
        if (!string.IsNullOrWhiteSpace(genre))
            sb.AppendLine($"Genre tag: {genre.Trim()}");

        var text = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? "(No prior design drafts yet — infer carefully from the adventure title.)" : text;
    }

    private static void AppendStepSummary(StringBuilder sb, AdventureBundle bundle, AdventureDesignStep step)
    {
        var summary = AdventureDesignService.BuildStepDraftSummary(bundle, step);
        if (string.IsNullOrWhiteSpace(summary) || summary == "(empty)")
            return;

        sb.AppendLine($"[{AdventureDesignService.GetStepDisplayName(step)}]");
        sb.AppendLine(summary);
        sb.AppendLine();
    }

    private static string BuildCanonFormatCitation(AdventureBundle bundle) =>
        CanonFormatReferenceService.BuildSpecificationCitation(bundle);
}
