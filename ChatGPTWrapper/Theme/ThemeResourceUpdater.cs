using System.Windows;
using System.Windows.Media;

namespace ChatGPTWrapper.Theme;

/// <summary>
/// Applies resolved theme values to the canonical token dictionary (and app root overrides).
/// </summary>
internal static class ThemeResourceUpdater
{
    private const string TokenDictionaryMarker = "BgBaseBrush";
    private static ResourceDictionary? _canonicalTokens;

    public static void ResetCanonicalDictionary() => _canonicalTokens = null;

    public static void ApplyToApplication(ResolvedTheme theme)
    {
        if (Application.Current is null)
            return;

        var tokensDictionary = _canonicalTokens ??= FindTokenDictionary(Application.Current.Resources);
        if (tokensDictionary is not null)
            ApplyToDictionary(theme, tokensDictionary);

        // Root keys win over merged lookups; keep in sync for mixed StaticResource leftovers.
        ApplyToDictionary(theme, Application.Current.Resources);
    }

    private static ResourceDictionary? FindTokenDictionary(ResourceDictionary root)
    {
        if (root.Contains(TokenDictionaryMarker))
            return root;

        foreach (var merged in root.MergedDictionaries)
        {
            var found = FindTokenDictionary(merged);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static void ApplyToDictionary(ResolvedTheme theme, ResourceDictionary resources)
    {
        foreach (var token in ThemeTokenCatalog.All)
        {
            if (!theme.Tokens.TryGetValue(token.TokenKey, out var hex))
                continue;

            resources[token.WpfBrushKey] = ThemeBrushCache.GetBrush(hex);
        }

        resources["WrapperFontFamily"] = new FontFamily(theme.FontFamily);
        resources["FontSizeBody"] = theme.FontSizeBody;
        resources["FontSizeTitle"] = theme.FontSizeTitle;
        resources["FontSizeHint"] = theme.FontSizeHint;
        resources["SpaceXs"] = theme.SpaceXs;
        resources["SpaceSm"] = theme.SpaceSm;
        resources["SpaceMd"] = theme.SpaceMd;
        resources["SpaceLg"] = theme.SpaceLg;
        resources["SpaceXl"] = theme.SpaceXl;
        resources["SpaceMdPadding"] = new Thickness(theme.SpaceMd);
        resources["RadiusControl"] = new CornerRadius(theme.RadiusControl);
        resources["RadiusCard"] = new CornerRadius(theme.RadiusCard);

        ApplySystemColor(resources, SystemColors.WindowColorKey, theme.GetHex("BgBase"));
        ApplySystemColor(resources, SystemColors.WindowTextColorKey, theme.GetHex("TextPrimary"));
        ApplySystemColor(resources, SystemColors.ControlColorKey, theme.GetHex("BgSurface"));
        ApplySystemColor(resources, SystemColors.ControlTextColorKey, theme.GetHex("TextPrimary"));
        ApplySystemColor(resources, SystemColors.MenuColorKey, theme.GetHex("Popup"));
        ApplySystemColor(resources, SystemColors.MenuTextColorKey, theme.GetHex("TextPrimary"));
        ApplySystemColor(resources, SystemColors.HighlightColorKey, theme.GetHex("RowSelected"));
        ApplySystemColor(resources, SystemColors.HighlightTextColorKey, theme.GetHex("TextPrimary"));

        SetBrushResource(resources, SystemColors.WindowBrushKey, theme.GetHex("BgBase"));
        SetBrushResource(resources, SystemColors.ControlBrushKey, theme.GetHex("BgSurface"));
        SetBrushResource(resources, SystemColors.MenuBrushKey, theme.GetHex("Popup"));
        SetBrushResource(resources, SystemColors.HighlightBrushKey, theme.GetHex("RowSelected"));
    }

    private static void ApplySystemColor(ResourceDictionary resources, ResourceKey key, string hex)
    {
        resources[key] = ThemeBrushCache.GetColor(hex);
    }

    private static void SetBrushResource(ResourceDictionary resources, ResourceKey key, string hex)
    {
        resources[key] = ThemeBrushCache.GetBrush(hex);
    }
}
