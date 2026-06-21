using System.Globalization;
using System.Text;

namespace ChatGPTWrapper.Theme;

public sealed class ResolvedTheme
{
    public required Dictionary<string, string> Tokens { get; init; }

    public string FontFamily { get; init; } = "Segoe UI Variable, Segoe UI";

    public double FontSizeBody { get; init; } = 13;

    public double FontSizeTitle { get; init; } = 15;

    public double FontSizeHint { get; init; } = 11;

    public double SpaceXs { get; init; } = 4;

    public double SpaceSm { get; init; } = 8;

    public double SpaceMd { get; init; } = 12;

    public double SpaceLg { get; init; } = 16;

    public double SpaceXl { get; init; } = 24;

    public double RadiusControl { get; init; } = 6;

    public double RadiusCard { get; init; } = 8;

    public string GetHex(string tokenKey) =>
        Tokens.TryGetValue(tokenKey, out var hex) ? hex : "#000000";

    public string? GetCssVariable(string tokenKey) =>
        ThemeTokenCatalog.ByTokenKey.TryGetValue(tokenKey, out var def) ? def.CssVariable : null;

    public string ComputeFingerprint()
    {
        var sb = new StringBuilder();
        sb.Append(FontFamily).Append('|')
            .Append(FontSizeBody).Append('|')
            .Append(FontSizeTitle).Append('|')
            .Append(FontSizeHint).Append('|')
            .Append(SpaceXs).Append('|')
            .Append(SpaceSm).Append('|')
            .Append(SpaceMd).Append('|')
            .Append(SpaceLg).Append('|')
            .Append(SpaceXl).Append('|')
            .Append(RadiusControl).Append('|')
            .Append(RadiusCard).Append('|');

        foreach (var token in ThemeTokenCatalog.All.OrderBy(t => t.TokenKey, StringComparer.OrdinalIgnoreCase))
        {
            if (Tokens.TryGetValue(token.TokenKey, out var hex))
                sb.Append(token.TokenKey).Append('=').Append(hex).Append(';');
        }

        return sb.ToString();
    }
}

public static class ThemeApplicationService
{
    private static string? _lastAppliedFingerprint;
    private static string? _cachedCssVariableBlock;
    private static string? _cachedCssFingerprint;

    public static ResolvedTheme ResolveEffectiveTheme(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var baseTokens = ThemePresetLibrary.TryGetPresetTokens(settings.ActivePresetId)
            ?? ThemeUserPresetService.TryGetPresetTokens(settings.UserPresets, settings.ActivePresetId)
            ?? ThemeTokenCatalog.CreateDefaultDarkTokens();

        var merged = new Dictionary<string, string>(baseTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings.CustomOverrides)
        {
            if (!string.IsNullOrWhiteSpace(value))
                merged[key] = NormalizeHex(value);
        }

        ThemeDerivation.ApplyDerivedTokens(merged);
        ThemeContrast.EnforceReadableTokens(merged);
        ThemeContrast.EnforceAccentButtonPairs(merged);
        ThemeDerivation.RefreshDerivedTokens(merged, onlyMissing: false);
        ThemeContrast.EnforceReadableTokens(merged);
        ThemeContrast.EnforceAccentButtonPairs(merged);
        ThemeContrast.RefreshAccentLink(merged);

        var userPreset = ThemeUserPresetService.Find(settings.UserPresets, settings.ActivePresetId);

        return new ResolvedTheme
        {
            Tokens = merged,
            FontFamily = settings.FontFamily ?? userPreset?.FontFamily ?? "Segoe UI Variable, Segoe UI",
            FontSizeBody = settings.FontSizeBody ?? userPreset?.FontSizeBody ?? 13,
            FontSizeTitle = settings.FontSizeTitle ?? userPreset?.FontSizeTitle ?? 15,
            FontSizeHint = settings.FontSizeHint ?? userPreset?.FontSizeHint ?? 11,
            SpaceXs = settings.SpaceXs ?? userPreset?.SpaceXs ?? 4,
            SpaceSm = settings.SpaceSm ?? userPreset?.SpaceSm ?? 8,
            SpaceMd = settings.SpaceMd ?? userPreset?.SpaceMd ?? 12,
            SpaceLg = settings.SpaceLg ?? userPreset?.SpaceLg ?? 16,
            SpaceXl = settings.SpaceXl ?? userPreset?.SpaceXl ?? 24,
            RadiusControl = settings.RadiusControl ?? userPreset?.RadiusControl ?? 6,
            RadiusCard = settings.RadiusCard ?? userPreset?.RadiusCard ?? 8,
        };
    }

    public static bool ApplyToWpf(ResolvedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var fingerprint = theme.ComputeFingerprint();
        if (string.Equals(_lastAppliedFingerprint, fingerprint, StringComparison.Ordinal))
            return false;

        ThemeResourceUpdater.ApplyToApplication(theme);
        _lastAppliedFingerprint = fingerprint;
        return true;
    }

    public static string BuildCssVariableBlock(ResolvedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var fingerprint = theme.ComputeFingerprint();
        if (string.Equals(_cachedCssFingerprint, fingerprint, StringComparison.Ordinal)
            && _cachedCssVariableBlock is not null)
            return _cachedCssVariableBlock;

        var sb = new StringBuilder();
        sb.AppendLine(":root {");

        foreach (var token in ThemeTokenCatalog.All)
        {
            if (token.CssVariable is null || !theme.Tokens.TryGetValue(token.TokenKey, out var hex))
                continue;

            var cssValue = token.TokenKey == "AccentSubtle"
                ? ThemeDerivation.ToCssAccentSubtle(hex)
                : hex;

            sb.Append("  ").Append(token.CssVariable).Append(": ").Append(cssValue).AppendLine(";");
        }

        sb.Append("  --cgw-radius: ")
            .Append(theme.RadiusControl.ToString(CultureInfo.InvariantCulture))
            .AppendLine("px;");
        sb.AppendLine("}");
        _cachedCssFingerprint = fingerprint;
        _cachedCssVariableBlock = sb.ToString();
        return _cachedCssVariableBlock;
    }

    public static void InvalidateApplyCache()
    {
        _lastAppliedFingerprint = null;
        _cachedCssFingerprint = null;
        _cachedCssVariableBlock = null;
    }

    public static ThemeSettings NormalizeSettings(ThemeSettings? settings)
    {
        if (settings is null)
            return CreateDefaultSettings();

        settings.SchemaVersion = ThemeSettings.CurrentSchemaVersion;
        settings.ActivePresetId = string.IsNullOrWhiteSpace(settings.ActivePresetId)
            ? ThemePresetIds.DefaultDark
            : settings.ActivePresetId;
        settings.CustomOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.UserPresets ??= [];
        ThemeUserPresetService.Normalize(settings);
        return settings;
    }

    public static ThemeSettings CreateDefaultSettings() => new()
    {
        ActivePresetId = ThemePresetIds.DefaultDark,
    };

    /// <summary>
    /// Merges preset + user overrides and derives tokens without contrast auto-fix (for dialog warnings).
    /// </summary>
    public static Dictionary<string, string> MergeUserTokens(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var baseTokens = ThemePresetLibrary.TryGetPresetTokens(settings.ActivePresetId)
            ?? ThemeUserPresetService.TryGetPresetTokens(settings.UserPresets, settings.ActivePresetId)
            ?? ThemeTokenCatalog.CreateDefaultDarkTokens();

        var merged = new Dictionary<string, string>(baseTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings.CustomOverrides)
        {
            if (!string.IsNullOrWhiteSpace(value))
                merged[key] = NormalizeHex(value);
        }

        ThemeDerivation.ApplyDerivedTokens(merged);
        return merged;
    }

    public static IReadOnlyList<ContrastFailure> ValidateUserTokens(ThemeSettings settings) =>
        ThemeContrast.ValidateTokens(MergeUserTokens(settings));

    public static string GetPresetHex(ThemeSettings settings, string tokenKey)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var presetTokens = ThemePresetLibrary.TryGetPresetTokens(settings.ActivePresetId)
            ?? ThemeUserPresetService.TryGetPresetTokens(settings.UserPresets, settings.ActivePresetId)
            ?? ThemeTokenCatalog.CreateDefaultDarkTokens();

        return presetTokens.TryGetValue(tokenKey, out var hex)
            ? hex
            : ThemeTokenCatalog.ByTokenKey[tokenKey].DefaultHex;
    }

    public static bool IsTokenCustomized(ThemeSettings settings, string tokenKey) =>
        settings.CustomOverrides.ContainsKey(tokenKey);

    public static void EnforceUserTokenContrast(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var merged = MergeUserTokens(settings);
        ThemeContrast.EnforceReadableTokens(merged);
        ThemeContrast.EnforceAccentButtonPairs(merged);
        ThemeDerivation.RefreshDerivedTokens(merged, onlyMissing: false);
        ThemeContrast.EnforceReadableTokens(merged);
        ThemeContrast.EnforceAccentButtonPairs(merged);
        ThemeContrast.RefreshAccentLink(merged);

        var presetTokens = ThemePresetLibrary.TryGetPresetTokens(settings.ActivePresetId)
            ?? ThemeUserPresetService.TryGetPresetTokens(settings.UserPresets, settings.ActivePresetId)
            ?? ThemeTokenCatalog.CreateDefaultDarkTokens();

        settings.CustomOverrides.Clear();
        foreach (var token in ThemeTokenCatalog.All.Where(t => !t.IsDerived))
        {
            if (!merged.TryGetValue(token.TokenKey, out var hex))
                continue;

            if (!presetTokens.TryGetValue(token.TokenKey, out var presetHex)
                || !HexEquals(hex, presetHex))
            {
                settings.CustomOverrides[token.TokenKey] = hex;
            }
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
