namespace ChatGPTWrapper.Theme;

public static class ThemeUserPresetService
{
    public static ThemeUserPreset? Find(IEnumerable<ThemeUserPreset> presets, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : presets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static Dictionary<string, string>? TryGetPresetTokens(
        IEnumerable<ThemeUserPreset> presets,
        string presetId)
    {
        var preset = Find(presets, presetId);
        if (preset is null)
            return null;

        var copy = new Dictionary<string, string>(preset.Tokens, StringComparer.OrdinalIgnoreCase);
        ThemeDerivation.ApplyDerivedTokens(copy);
        return copy;
    }

    public static IReadOnlyList<string> BuildSwatchColors(ThemeUserPreset preset)
    {
        static string Pick(ThemeUserPreset p, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (p.Tokens.TryGetValue(key, out var hex) && !string.IsNullOrWhiteSpace(hex))
                    return hex;
            }

            return "#808080";
        }

        return
        [
            Pick(preset, "BgBase", "BgSurface"),
            Pick(preset, "AccentPrimary"),
            Pick(preset, "TextPrimary"),
            Pick(preset, "BgElevated", "BgSurface"),
        ];
    }

    public static ThemeUserPreset CreateFromSettings(string name, ThemeSettings settings, string? category = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ThemeTokenCatalog.All.Where(t => !t.IsDerived))
            tokens[token.TokenKey] = resolved.GetHex(token.TokenKey);

        return new ThemeUserPreset
        {
            Id = ThemePresetIds.CreateUserPresetId(),
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled theme" : name.Trim(),
            Description = "Custom saved theme",
            Category = ThemePresetNavigation.NormalizeCategory(category ?? InferCategory(settings)),
            Tokens = tokens,
            FontFamily = resolved.FontFamily,
            FontSizeBody = resolved.FontSizeBody,
            FontSizeTitle = resolved.FontSizeTitle,
            FontSizeHint = resolved.FontSizeHint,
            SpaceXs = resolved.SpaceXs,
            SpaceSm = resolved.SpaceSm,
            SpaceMd = resolved.SpaceMd,
            SpaceLg = resolved.SpaceLg,
            SpaceXl = resolved.SpaceXl,
            RadiusControl = resolved.RadiusControl,
            RadiusCard = resolved.RadiusCard,
        };
    }

    public static ThemeUserPreset CreateCopy(string name, ThemeUserPreset source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var copy = source.Clone();
        copy.Id = ThemePresetIds.CreateUserPresetId();
        copy.Name = string.IsNullOrWhiteSpace(name) ? $"{source.Name} copy" : name.Trim();
        copy.Description = string.IsNullOrWhiteSpace(source.Description)
            ? "Custom saved theme"
            : source.Description;
        return copy;
    }

    public static ThemeUserPreset CreateCopyFromBuiltIn(string name, string builtInPresetId)
    {
        var tokens = ThemePresetLibrary.TryGetPresetTokens(builtInPresetId)
            ?? ThemeTokenCatalog.CreateDefaultDarkTokens();

        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ThemeTokenCatalog.All.Where(t => !t.IsDerived))
        {
            if (tokens.TryGetValue(token.TokenKey, out var hex))
                stored[token.TokenKey] = hex;
        }

        var builtIn = ThemePresetLibrary.TryGetPreset(builtInPresetId);
        return new ThemeUserPreset
        {
            Id = ThemePresetIds.CreateUserPresetId(),
            Name = string.IsNullOrWhiteSpace(name) ? $"{builtIn?.Name ?? "Theme"} copy" : name.Trim(),
            Description = builtIn?.Description ?? "Custom saved theme",
            Category = ThemePresetNavigation.GetCategory(builtInPresetId),
            Tokens = stored,
        };
    }

    public static string InferCategory(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (ThemePresetIds.IsUserPresetId(settings.ActivePresetId))
        {
            var userPreset = Find(settings.UserPresets, settings.ActivePresetId);
            if (userPreset is not null)
                return ThemePresetNavigation.NormalizeCategory(userPreset.Category);
        }

        if (!settings.ActivePresetId.Equals(ThemePresetIds.Custom, StringComparison.OrdinalIgnoreCase))
            return ThemePresetNavigation.GetCategory(settings.ActivePresetId);

        return ThemePresetCategories.MyPresets;
    }

    public static bool TrySetPresetCategory(ThemeUserPreset preset, string? category, out string? error)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (string.IsNullOrWhiteSpace(category))
        {
            error = "Choose a category.";
            return false;
        }

        preset.Category = ThemePresetNavigation.NormalizeCategory(category);
        error = null;
        return true;
    }

    public static void ApplyToSettings(ThemeUserPreset preset, ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(settings);

        settings.ActivePresetId = preset.Id;
        settings.CustomOverrides.Clear();
        settings.FontFamily = preset.FontFamily;
        settings.FontSizeBody = preset.FontSizeBody;
        settings.FontSizeTitle = preset.FontSizeTitle;
        settings.FontSizeHint = preset.FontSizeHint;
        settings.SpaceXs = preset.SpaceXs;
        settings.SpaceSm = preset.SpaceSm;
        settings.SpaceMd = preset.SpaceMd;
        settings.SpaceLg = preset.SpaceLg;
        settings.SpaceXl = preset.SpaceXl;
        settings.RadiusControl = preset.RadiusControl;
        settings.RadiusCard = preset.RadiusCard;
    }

    public static void ClearLayoutOverrides(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.FontFamily = null;
        settings.FontSizeBody = null;
        settings.FontSizeTitle = null;
        settings.FontSizeHint = null;
        settings.SpaceXs = null;
        settings.SpaceSm = null;
        settings.SpaceMd = null;
        settings.SpaceLg = null;
        settings.SpaceXl = null;
        settings.RadiusControl = null;
        settings.RadiusCard = null;
    }

    public static void SaveSettingsToPreset(ThemeUserPreset preset, ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(settings);

        var snapshot = CreateFromSettings(preset.Name, settings);
        preset.Tokens = snapshot.Tokens;
        preset.FontFamily = snapshot.FontFamily;
        preset.FontSizeBody = snapshot.FontSizeBody;
        preset.FontSizeTitle = snapshot.FontSizeTitle;
        preset.FontSizeHint = snapshot.FontSizeHint;
        preset.SpaceXs = snapshot.SpaceXs;
        preset.SpaceSm = snapshot.SpaceSm;
        preset.SpaceMd = snapshot.SpaceMd;
        preset.SpaceLg = snapshot.SpaceLg;
        preset.SpaceXl = snapshot.SpaceXl;
        preset.RadiusControl = snapshot.RadiusControl;
        preset.RadiusCard = snapshot.RadiusCard;
    }

    public static bool MatchesSettings(ThemeUserPreset preset, ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.CustomOverrides.Count > 0)
            return false;

        var snapshot = CreateFromSettings(preset.Name, settings);
        if (!string.Equals(preset.FontFamily, snapshot.FontFamily, StringComparison.Ordinal))
            return false;
        if (preset.FontSizeBody != snapshot.FontSizeBody
            || preset.FontSizeTitle != snapshot.FontSizeTitle
            || preset.FontSizeHint != snapshot.FontSizeHint
            || preset.SpaceXs != snapshot.SpaceXs
            || preset.SpaceSm != snapshot.SpaceSm
            || preset.SpaceMd != snapshot.SpaceMd
            || preset.SpaceLg != snapshot.SpaceLg
            || preset.SpaceXl != snapshot.SpaceXl
            || preset.RadiusControl != snapshot.RadiusControl
            || preset.RadiusCard != snapshot.RadiusCard)
        {
            return false;
        }

        foreach (var token in ThemeTokenCatalog.All.Where(t => !t.IsDerived))
        {
            if (!preset.Tokens.TryGetValue(token.TokenKey, out var expected))
                continue;

            if (!snapshot.Tokens.TryGetValue(token.TokenKey, out var actual)
                || !HexEquals(expected, actual))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryRenamePreset(ThemeUserPreset preset, string newName, out string? error)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            error = "Enter a preset name.";
            return false;
        }

        preset.Name = newName.Trim();
        error = null;
        return true;
    }

    public static bool TryDeletePreset(List<ThemeUserPreset> presets, string presetId, out string? error)
    {
        var preset = Find(presets, presetId);
        if (preset is null)
        {
            error = "Preset not found.";
            return false;
        }

        presets.Remove(preset);
        error = null;
        return true;
    }

    public static void MergeImportedPresets(List<ThemeUserPreset> into, IEnumerable<ThemeUserPreset> imported)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(imported);

        foreach (var preset in imported)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Name))
                continue;

            var existing = Find(into, preset.Id);
            if (existing is not null)
                into.Remove(existing);

            into.Add(preset.Clone());
        }
    }

    public static void ApplyImportedThemeFields(ThemeSettings target, ThemeSettings imported, bool mergeUserPresets)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(imported);

        var normalized = ThemeApplicationService.NormalizeSettings(imported);

        if (mergeUserPresets)
            MergeImportedPresets(target.UserPresets, normalized.UserPresets);

        target.ActivePresetId = normalized.ActivePresetId;
        target.CustomOverrides = new Dictionary<string, string>(normalized.CustomOverrides, StringComparer.OrdinalIgnoreCase);
        target.FontFamily = normalized.FontFamily;
        target.FontSizeBody = normalized.FontSizeBody;
        target.FontSizeTitle = normalized.FontSizeTitle;
        target.FontSizeHint = normalized.FontSizeHint;
        target.SpaceSm = normalized.SpaceSm;
        target.SpaceMd = normalized.SpaceMd;
        target.SpaceLg = normalized.SpaceLg;
        target.SpaceXs = normalized.SpaceXs;
        target.SpaceXl = normalized.SpaceXl;
        target.RadiusControl = normalized.RadiusControl;
        target.RadiusCard = normalized.RadiusCard;

        ThemeUserPresetService.Normalize(target);
    }

    public static ThemeUserPreset SaveImportedThemeAsPreset(
        List<ThemeUserPreset> presets,
        ThemeSettings imported,
        string name)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(imported);

        var snapshot = ThemeApplicationService.NormalizeSettings(imported.Clone());
        var preset = CreateFromSettings(name, snapshot, InferCategory(snapshot));
        presets.Add(preset);
        return preset;
    }

    public static void Normalize(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.UserPresets ??= [];

        for (var i = settings.UserPresets.Count - 1; i >= 0; i--)
        {
            var preset = settings.UserPresets[i];
            if (string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Name))
            {
                settings.UserPresets.RemoveAt(i);
                continue;
            }

            preset.Tokens ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            preset.Category = ThemePresetNavigation.NormalizeCategory(preset.Category);
        }

        if (ThemePresetIds.IsUserPresetId(settings.ActivePresetId)
            && Find(settings.UserPresets, settings.ActivePresetId) is null
            && !settings.ActivePresetId.Equals(ThemePresetIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            settings.ActivePresetId = ThemePresetIds.DefaultDark;
        }
    }

    private static bool HexEquals(string a, string b) =>
        string.Equals(NormalizeHex(a), NormalizeHex(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHex(string hex)
    {
        var trimmed = hex.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }
}
