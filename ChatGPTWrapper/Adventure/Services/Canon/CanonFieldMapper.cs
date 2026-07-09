using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonFieldMapper
{
    public static void ApplyFreeformBody(object entity, CanonEntityKindSpec spec, string body)
    {
        ApplyStructuredFieldsFromBody(entity, spec, body, overwriteExisting: true);
    }

    /// <summary>
    /// Extracts labeled lines from a blob into typed fields without overwriting populated values.
    /// Used for legacy entities and AI imports that dump structured text into description.
    /// </summary>
    public static bool TryPromoteStructuredFieldsFromBody(object entity, CanonEntityKindSpec spec)
    {
        var (body, sourceField) = ResolvePromotionSource(entity, spec);
        if (string.IsNullOrWhiteSpace(body) || !BodyContainsStructuredFields(body, spec))
            return false;

        var changed = ApplyStructuredFieldsFromBody(entity, spec, body, overwriteExisting: false);

        if (sourceField?.Format == CanonFieldFormat.FreeformBody)
        {
            var stripped = StripStructuredFieldLines(body, spec, entity);
            var current = GetField(entity, spec, sourceField.JsonKey) ?? "";
            if (!string.Equals(current, stripped, StringComparison.Ordinal))
            {
                SetField(entity, spec, sourceField.JsonKey, stripped);
                changed = true;
            }
        }
        else if (sourceField is not null)
        {
            var stripped = StripStructuredFieldLines(body, spec, entity);
            if (!string.Equals(stripped, body, StringComparison.Ordinal))
            {
                SetField(entity, spec, sourceField.JsonKey, stripped);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Moves legacy extendedFields keys into typed canon properties when labels match the schema.
    /// </summary>
    public static bool TryPromoteKnownExtendedFields(object entity, CanonEntityKindSpec spec)
    {
        var dict = GetExtendedDictionary(entity);
        if (dict is null || dict.Count == 0)
            return false;

        var changed = false;
        foreach (var field in spec.BodyFields)
        {
            if (field.Format is CanonFieldFormat.FreeformBody)
                continue;

            var keys = new List<string> { field.Label };
            keys.AddRange(field.AlternateLabels);
            if (field.Format == CanonFieldFormat.BlockquoteFlavor)
                keys.Add("> Flavor");

            foreach (var key in keys)
            {
                if (!dict.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    continue;

                if (!string.IsNullOrWhiteSpace(GetField(entity, spec, field.JsonKey)))
                {
                    dict.Remove(key);
                    changed = true;
                    break;
                }

                SetField(entity, spec, field.JsonKey, value.Trim().Trim('"'));
                dict.Remove(key);
                changed = true;
                break;
            }
        }

        return changed;
    }

    public static string StripStructuredFieldLines(string body, CanonEntityKindSpec spec, object? entity = null)
    {
        var labels = spec.Fields
            .SelectMany(f => new[] { f.Label }.Concat(f.AlternateLabels))
            .Concat(["Id", "Aliases", "Tags", "ImagePath", "Pinned"])
            .ToList();

        var dict = entity is null ? null : GetExtendedDictionary(entity);
        if (dict is not null)
            labels.AddRange(dict.Keys);

        return SectionMarkdownParser.StripLabeledLines(body, labels);
    }

    public static void ApplyEntry(object entity, CanonEntityKindSpec spec, ParsedMarkdownEntry entry)
    {
        SetTitle(entity, spec, entry.Title);

        if (spec.CategorySpec is not null)
            ApplyCategoryShellLines(entity, entry.Body, includeAliases: false);

        ApplyStructuredFieldsFromBody(entity, spec, entry.Body, overwriteExisting: true);
        ApplyExtendedFieldLines(entity, spec, entry.Body);

        if (entry.Aliases.Count > 0 && CanonEntityPropertyGraph.HasProperty(entity, "aliases"))
            SetAliases(entity, entry.Aliases, entry.Title);
    }

    private static bool ApplyStructuredFieldsFromBody(
        object entity,
        CanonEntityKindSpec spec,
        string body,
        bool overwriteExisting)
    {
        var changed = false;

        foreach (var field in spec.BodyFields)
        {
            if (field.Format is CanonFieldFormat.FreeformBody or CanonFieldFormat.BlockquoteFlavor)
                continue;

            var value = ExtractField(body, field);
            if (value is null)
                continue;

            if (!overwriteExisting)
            {
                var current = GetField(entity, spec, field.JsonKey);
                if (!string.IsNullOrWhiteSpace(current))
                    continue;
            }

            SetField(entity, spec, field.JsonKey, value);
            changed = true;
        }

        var flavorField = spec.BodyFields.FirstOrDefault(f => f.Format == CanonFieldFormat.BlockquoteFlavor);
        if (flavorField is not null)
        {
            var flavor = SectionMarkdownParser.ExtractFlavor(body);
            if (!string.IsNullOrWhiteSpace(flavor))
            {
                if (overwriteExisting || string.IsNullOrWhiteSpace(GetField(entity, spec, flavorField.JsonKey)))
                {
                    SetField(entity, spec, flavorField.JsonKey, flavor);
                    changed = true;
                }
            }
        }

        var extendedBefore = GetExtendedDictionary(entity)?.Count ?? 0;
        ApplyExtendedFieldLines(entity, spec, body, overwriteExisting);
        if ((GetExtendedDictionary(entity)?.Count ?? 0) > extendedBefore)
            changed = true;

        var freeformField = spec.BodyFields.FirstOrDefault(f => f.Format == CanonFieldFormat.FreeformBody);
        if (freeformField is not null)
        {
            var stripped = StripStructuredFieldLines(body, spec, entity);
            if (overwriteExisting || string.IsNullOrWhiteSpace(GetField(entity, spec, freeformField.JsonKey)))
            {
                var current = GetField(entity, spec, freeformField.JsonKey) ?? "";
                if (!string.Equals(current, stripped, StringComparison.Ordinal))
                {
                    SetField(entity, spec, freeformField.JsonKey, stripped);
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static (string? Body, CanonFieldSpec? SourceField) ResolvePromotionSource(
        object entity,
        CanonEntityKindSpec spec)
    {
        var freeformField = spec.BodyFields.FirstOrDefault(f => f.Format == CanonFieldFormat.FreeformBody);
        if (freeformField is not null)
        {
            var body = GetField(entity, spec, freeformField.JsonKey);
            if (!string.IsNullOrWhiteSpace(body))
                return (body, freeformField);
        }

        foreach (var field in spec.BodyFields)
        {
            var body = GetField(entity, spec, field.JsonKey);
            if (string.IsNullOrWhiteSpace(body) || !BodyContainsStructuredFields(body, spec))
                continue;

            return (body, field);
        }

        return (null, null);
    }

    private static bool BodyContainsStructuredFields(string body, CanonEntityKindSpec spec)
    {
        foreach (var field in spec.BodyFields)
        {
            if (field.Format == CanonFieldFormat.FreeformBody)
                continue;

            if (field.Format == CanonFieldFormat.BlockquoteFlavor)
            {
                if (!string.IsNullOrWhiteSpace(SectionMarkdownParser.ExtractFlavor(body)))
                    return true;

                continue;
            }

            if (ExtractField(body, field) is not null)
                return true;
        }

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim();
            if (spec.Fields.Any(f => string.Equals(f.Label, key, StringComparison.OrdinalIgnoreCase)
                                     || f.AlternateLabels.Any(a =>
                                         string.Equals(a, key, StringComparison.OrdinalIgnoreCase))))
                return true;
        }

        return LineLooksLikeLabeledField(body);
    }

    private static bool LineLooksLikeLabeledField(string body) =>
        body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line =>
            {
                var colon = line.IndexOf(':');
                return colon > 0 && colon < 48;
            });

    public static string BuildFreeformBody(object entity, CanonEntityKindSpec spec)
    {
        var parts = new List<string>();
        foreach (var field in spec.BodyFields)
        {
            if (field.Format == CanonFieldFormat.FreeformBody)
                continue;

            var value = GetField(entity, spec, field.JsonKey);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            parts.Add(FormatField(field, value));
        }

        return string.Join("\n", parts);
    }

    public static string BuildPlayerCastBody(PlayerCharacterSheet player)
    {
        var spec = CanonSchemaRegistry.Player;
        var parts = new List<string>();
        parts.AddRange(BuildCategoryShellLines(player, includeAliases: true));

        var body = BuildFreeformBody(player, spec);
        if (!string.IsNullOrWhiteSpace(body))
            parts.Add(body);

        AppendExtendedFieldLines(parts, player, spec);
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static void ApplyPlayerCastBody(PlayerCharacterSheet player, string body)
    {
        var spec = CanonSchemaRegistry.Player;
        ApplyCategoryShellLines(player, body, includeAliases: true);
        ApplyFreeformBody(player, spec, body);
        ApplyExtendedFieldLines(player, spec, body);
    }

    public static string BuildEntryBody(object entity, CanonEntityKindSpec spec)
    {
        var parts = new List<string>();

        if (spec.CategorySpec is not null)
            parts.AddRange(BuildCategoryShellLines(entity, includeAliases: false));

        foreach (var field in spec.BodyFields)
        {
            var value = GetField(entity, spec, field.JsonKey);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (field.Format == CanonFieldFormat.BlockquoteFlavor)
            {
                parts.Add($"> Flavor: {value.Trim()}");
                continue;
            }

            if (field.Format == CanonFieldFormat.FreeformBody)
            {
                parts.Add(value.Trim());
                continue;
            }

            parts.Add(FormatField(field, value));
        }

        AppendExtendedFieldLines(parts, entity, spec);
        return string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }

    private static void AppendExtendedFieldLines(List<string> parts, object entity, CanonEntityKindSpec spec)
    {
        var dict = GetExtendedDictionary(entity);
        if (dict is null)
            return;

        foreach (var (key, value) in dict.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (spec.Fields.Any(f => string.Equals(f.JsonKey, key, StringComparison.OrdinalIgnoreCase)))
                continue;

            parts.Add($"{key}: {value.Trim()}");
        }
    }

    private static void ApplyExtendedFieldLines(object entity, CanonEntityKindSpec spec, string body, bool overwriteExisting = true)
    {
        var dict = GetExtendedDictionary(entity);
        if (dict is null)
            return;

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim();
            if (IsKnownFieldLabel(spec, key))
                continue;

            if (!overwriteExisting && dict.ContainsKey(key))
                continue;

            var value = line[(colon + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
                dict[key] = value;
        }
    }

    private static bool IsKnownFieldLabel(CanonEntityKindSpec spec, string key) =>
        spec.Fields.Any(f => string.Equals(f.Label, key, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(f.JsonKey, key, StringComparison.OrdinalIgnoreCase)
                             || f.AlternateLabels.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase)));

    private static List<string> ParseList(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string? GetField(object entity, CanonEntityKindSpec spec, string jsonKey)
    {
        if (!TryGetTypedValue(entity, jsonKey, out var typed))
        {
            if (TryGetExtended(entity, jsonKey, out var extended))
                return extended;
            return null;
        }

        return typed switch
        {
            QuestStatus status => status.ToString(),
            bool b => b ? "true" : "false",
            List<string> list => string.Join(", ", list),
            _ => typed?.ToString(),
        };
    }

    public static void SetField(object entity, CanonEntityKindSpec spec, string jsonKey, string value)
    {
        if (TrySetTyped(entity, jsonKey, value))
            return;

        SetExtended(entity, jsonKey, value);
    }

    public static string GetTitle(object entity, CanonEntityKindSpec spec) =>
        GetField(entity, spec, spec.TitleProperty) ?? "";

    public static string GetSecondary(object entity, CanonEntityKindSpec spec) =>
        string.IsNullOrWhiteSpace(spec.SecondaryProperty)
            ? ""
            : GetField(entity, spec, spec.SecondaryProperty) ?? "";

    public static string GetSnippet(object entity, CanonEntityKindSpec spec) =>
        GetField(entity, spec, spec.SnippetProperty) ?? "";

    public static bool GetPinned(object entity) =>
        entity switch
        {
            CharacterEntry c => c.Pinned,
            LocationEntry l => l.Pinned,
            ConceptEntry c => c.Pinned,
            CustomEntry c => c.Pinned,
            _ => false,
        };

    private static string? ExtractField(string body, CanonFieldSpec field)
    {
        var labels = new[] { field.Label }.Concat(field.AlternateLabels);
        foreach (var label in labels)
        {
            var value = SectionMarkdownParser.ExtractField(body, label);
            if (value is not null)
                return value;
        }

        return null;
    }

    private static string FormatField(CanonFieldSpec field, string value) =>
        field.Format switch
        {
            CanonFieldFormat.BoldLine => $"**{field.Label}:** {value.Trim()}",
            CanonFieldFormat.PlainLine => $"{field.Label}: {value.Trim()}",
            _ => value.Trim(),
        };

    private static void SetTitle(object entity, CanonEntityKindSpec spec, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        TrySetTyped(entity, spec.TitleProperty, title);
    }

    private static void SetAliases(object entity, List<string> aliases, string title)
    {
        if (!CanonEntityPropertyGraph.HasProperty(entity, "aliases"))
            return;

        var filtered = aliases
            .Where(a => !string.Equals(a, title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        CanonEntityPropertyGraph.TrySetValue(entity, "aliases", string.Join(", ", filtered));
    }

    private static IEnumerable<string> BuildCategoryShellLines(object entity, bool includeAliases)
    {
        var parts = new List<string>();
        if (CanonEntityPropertyGraph.TryGetValue(entity, "imagePath", out var image)
            && image is string imagePath
            && !string.IsNullOrWhiteSpace(imagePath))
        {
            parts.Add($"ImagePath: {imagePath.Trim()}");
        }

        if (CanonEntityPropertyGraph.TryGetValue(entity, "tags", out var tags)
            && tags is IEnumerable<string> tagList)
        {
            var tagText = string.Join(", ", tagList.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (!string.IsNullOrWhiteSpace(tagText))
                parts.Add($"Tags: {tagText}");
        }

        if (includeAliases
            && CanonEntityPropertyGraph.TryGetValue(entity, "aliases", out var aliases)
            && aliases is IEnumerable<string> aliasList)
        {
            var aliasText = string.Join(", ", aliasList.Where(a => !string.IsNullOrWhiteSpace(a)));
            if (!string.IsNullOrWhiteSpace(aliasText))
                parts.Add($"Aliases: {aliasText}");
        }

        return parts;
    }

    private static void ApplyCategoryShellLines(object entity, string body, bool includeAliases)
    {
        var imagePath = SectionMarkdownParser.ExtractField(body, "ImagePath");
        if (imagePath is not null && CanonEntityPropertyGraph.HasProperty(entity, "imagePath"))
            CanonEntityPropertyGraph.TrySetValue(entity, "imagePath", imagePath.Trim());

        var tags = SectionMarkdownParser.ExtractField(body, "Tags");
        if (tags is not null && CanonEntityPropertyGraph.HasProperty(entity, "tags"))
            CanonEntityPropertyGraph.TrySetValue(entity, "tags", tags);

        if (includeAliases)
        {
            var aliases = SectionMarkdownParser.ExtractField(body, "Aliases");
            if (aliases is not null && CanonEntityPropertyGraph.HasProperty(entity, "aliases"))
                CanonEntityPropertyGraph.TrySetValue(entity, "aliases", aliases);
        }
    }

    private static bool TryGetTypedValue(object entity, string jsonKey, out object? value)
    {
        if (CanonEntityPropertyGraph.TryGetValue(entity, jsonKey, out value))
            return true;

        value = null;
        return false;
    }

    private static bool HasTypedKey(object entity, string jsonKey) =>
        CanonEntityPropertyGraph.HasProperty(entity, jsonKey);

    private static bool TrySetTyped(object entity, string jsonKey, string value) =>
        CanonEntityPropertyGraph.TrySetValue(entity, jsonKey, value);

    private static bool TryGetExtended(object entity, string jsonKey, out string? value)
    {
        value = GetExtendedDictionary(entity)?.TryGetValue(jsonKey, out var v) == true ? v : null;
        return value is not null;
    }

    private static void SetExtended(object entity, string jsonKey, string value)
    {
        var dict = GetExtendedDictionary(entity);
        if (dict is null)
            return;

        dict[jsonKey] = value;
    }

    private static Dictionary<string, string>? GetExtendedDictionary(object entity) =>
        entity switch
        {
            PlayerCharacterSheet p => p.ExtendedFields,
            CompanionEntry c => c.ExtendedFields,
            CharacterEntry c => c.ExtendedFields,
            LocationEntry l => l.ExtendedFields,
            FactionEntry f => f.ExtendedFields,
            ConceptEntry c => c.ExtendedFields,
            QuestEntry q => q.ExtendedFields,
            MysteryEntry m => m.ExtendedFields,
            ConflictEntry c => c.ExtendedFields,
            ConsequenceEntry c => c.ExtendedFields,
            _ => null,
        };
}
