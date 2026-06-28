using ChatGPTWrapper.Adventure.Models;



namespace ChatGPTWrapper.Adventure.Services.NarratorScales;



internal sealed record EffectiveNarratorScale(

    string DimensionId,

    string Label,

    string Value,

    NarratorScaleCategory Category);



internal static class NarratorScalesResolver

{

    public const string SourceFileName = SectionSchema.NarratorScalesFile;



    public static IReadOnlyList<EffectiveNarratorScale> GetEffectiveScales(AdventureBundle bundle)

    {

        var settings = bundle.Metadata.Settings;

        UtilityStoryContextSettingsService.EnsureDefaults(bundle.Metadata);



        return

        [

            Scale("response-length", NarratorScaleLabels.ResponseLength, NarratorOverrideResolver.ResolveResponseLength(bundle), NarratorScaleCategory.Narration),

            Scale("detail-level", NarratorScaleLabels.DetailLevel, NarratorOverrideResolver.ResolveDetailLevel(bundle), NarratorScaleCategory.Narration),

            Scale("tone", NarratorScaleLabels.Tone, NarratorOverrideResolver.ResolveTone(bundle), NarratorScaleCategory.Narration),

            Scale("combat-difficulty", NarratorScaleLabels.CombatDifficulty, NarratorOverrideResolver.ResolveDifficulty(bundle), NarratorScaleCategory.Combat),

            Scale("violence-level", NarratorScaleLabels.ViolenceLevel, NarratorOverrideResolver.ResolveViolenceLevel(bundle), NarratorScaleCategory.Combat),

            Scale("narrative-pacing", NarratorScaleLabels.NarrativePacing, NarratorOverrideResolver.ResolveNarrativePacing(bundle), NarratorScaleCategory.Narration),

            Scale("consequence-weight", NarratorScaleLabels.ConsequenceWeight, NarratorOverrideResolver.ResolveConsequenceWeight(bundle), NarratorScaleCategory.Combat),

        ];

    }



    public static IReadOnlyList<EffectiveNarratorScale> GetAdventureBaselineScales(AdventureBundle bundle)

    {

        var settings = bundle.Metadata.Settings;

        UtilityStoryContextSettingsService.EnsureDefaults(bundle.Metadata);

        var tone = string.IsNullOrWhiteSpace(settings.Tone)

            ? NarratorOverrideResolver.ResolveBaselineTone(bundle)

            : settings.Tone.Trim();



        if (string.IsNullOrWhiteSpace(tone))

            tone = "neutral";



        return

        [

            Scale("response-length", NarratorScaleLabels.ResponseLength, "normal", NarratorScaleCategory.Narration),

            Scale("detail-level", NarratorScaleLabels.DetailLevel, settings.DetailLevel, NarratorScaleCategory.Narration),

            Scale("tone", NarratorScaleLabels.Tone, tone, NarratorScaleCategory.Narration),

            Scale("combat-difficulty", NarratorScaleLabels.CombatDifficulty, settings.Difficulty, NarratorScaleCategory.Combat),

            Scale("violence-level", NarratorScaleLabels.ViolenceLevel, settings.ViolenceLevel?.Trim() ?? "moderate", NarratorScaleCategory.Combat),

            Scale("narrative-pacing", NarratorScaleLabels.NarrativePacing, settings.NarrativePacing, NarratorScaleCategory.Narration),

            Scale("consequence-weight", NarratorScaleLabels.ConsequenceWeight, settings.ConsequenceWeight, NarratorScaleCategory.Combat),

        ];

    }



    public static IReadOnlyList<EffectiveNarratorScale> ScalesForCategory(

        IReadOnlyList<EffectiveNarratorScale> scales,

        NarratorScaleCategory category) =>

        scales.Where(s => s.Category == category).ToList();



    public static string BuildQuickReferenceBlock(AdventureBundle bundle) =>

        BuildQuickReferenceBlock(bundle, GetEffectiveScales(bundle));



    public static string BuildBaselineQuickReferenceBlock(AdventureBundle bundle) =>

        BuildQuickReferenceBlock(bundle, GetAdventureBaselineScales(bundle));



    private static string BuildQuickReferenceBlock(AdventureBundle bundle, IReadOnlyList<EffectiveNarratorScale> scales)

    {

        var catalog = NarratorScalesLoader.Catalog;

        var lines = new List<string>

        {

            BuildInspectInstructions(),

            "",

            "=== ACTIVE NARRATOR SCALES ===",

        };



        AppendCategoryBlock(lines, catalog, scales, NarratorScaleCategory.Narration, "Narration (delivery)");

        lines.Add("");

        AppendCategoryBlock(lines, catalog, scales, NarratorScaleCategory.Combat, "Combat & stakes");



        lines.Add("");

        lines.Add($"Inspect full definitions in Project Files → {SourceFileName} (sections ## narration-scales and ## combat-scales).");

        return string.Join(Environment.NewLine, lines);

    }



    public static string BuildInspectInstructions() =>

        "Before narrating: open narrator-scales.md from Project Files. The play packet lists active selectors in === ACTIVE NARRATOR SCALES === (with section pointers). For each selector, locate the matching ### dimension / #### preset in this file and apply the full Summary, Narration behavior, and Avoid bullets. Do not infer meaning from preset names alone.";



    public static string ExpandOverrideLine(string label, string value, bool includeInspectPointer = true)

    {

        if (string.IsNullOrWhiteSpace(value))

            return $"{label}: {value}";



        var catalog = NarratorScalesLoader.Catalog;

        var dimension = catalog.TryGetDimensionByPacketLabel(label)

                        ?? catalog.TryGetDimension(NormalizeDimensionId(label));

        var preset = dimension is not null

            ? catalog.TryGetPresetByPacketValue(dimension.Id, value)

            : null;



        if (preset is null)

            return $"{label}: {value}";



        var pointer = includeInspectPointer

            ? $" (inspect {SourceFileName} § {dimension!.Id}/{preset.Id})"

            : "";



        return $"{label}: {value} — {preset.Summary}{pointer}";

    }



    public static string BuildFatInlineBlock(AdventureBundle bundle, IEnumerable<(string Label, string Value)>? overrides = null)

    {

        var catalog = NarratorScalesLoader.Catalog;

        var overrideMap = overrides?

            .Where(o => !string.IsNullOrWhiteSpace(o.Value))

            .ToDictionary(o => o.Label, o => o.Value, StringComparer.OrdinalIgnoreCase)

            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);



        var lines = new List<string>

        {

            BuildInspectInstructions(),

            "",

            "=== NARRATOR SCALE DEFINITIONS (active) ===",

        };



        foreach (var scale in GetEffectiveScales(bundle))

        {

            var isOverride = overrideMap.ContainsKey(scale.Label);

            if (!isOverride && !ShouldInlineBaseline(scale.DimensionId))

                continue;



            var preset = catalog.TryGetPresetByPacketValue(scale.DimensionId, scale.Value);

            if (preset is null)

            {

                lines.Add($"{scale.Label}: {scale.Value}");

                continue;

            }



            lines.Add($"{scale.Label}: {scale.Value}");

            lines.Add($"  {preset.Summary}");

            foreach (var behavior in preset.Behavior.Take(3))

                lines.Add($"  - {behavior}");

            lines.Add("");

        }



        return string.Join(Environment.NewLine, lines).TrimEnd();

    }



    public static string? TryGetPresetSummary(NarratorParameter parameter, string? value)

    {

        if (string.IsNullOrWhiteSpace(value))

            return null;



        var catalog = NarratorScalesLoader.Catalog;

        var dimension = catalog.Dimensions.FirstOrDefault(d => d.NarratorParameter == parameter);

        return dimension is null

            ? null

            : catalog.TryGetPresetByPacketValue(dimension.Id, value)?.Summary;

    }



    public static string? TryGetViolenceSummary(string? value)

    {

        if (string.IsNullOrWhiteSpace(value))

            return null;



        return NarratorScalesLoader.Catalog.TryGetPresetByPacketValue("violence-level", value)?.Summary;

    }



    public static string BuildInstructionsScalePointer() =>

        "Scale definitions live in narrator-scales.md (Project Files). Inspect that file and read the full preset definitions for every selector above before narrating.";



    private static void AppendCategoryBlock(

        List<string> lines,

        NarratorScalesCatalog catalog,

        IReadOnlyList<EffectiveNarratorScale> scales,

        NarratorScaleCategory category,

        string heading)

    {

        lines.Add(heading + ":");

        foreach (var scale in ScalesForCategory(scales, category))

        {

            lines.Add($"  {ExpandOverrideLine(scale.Label, scale.Value)}");

        }

    }



    private static EffectiveNarratorScale Scale(

        string dimensionId,

        string label,

        string value,

        NarratorScaleCategory category) =>

        new(dimensionId, label, value, category);



    private static string NormalizeDimensionId(string label) =>

        label.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant();



    private static bool ShouldInlineBaseline(string dimensionId) =>

        dimensionId is "detail-level" or "tone" or "combat-difficulty" or "violence-level"
            or "narrative-pacing" or "consequence-weight";

}


