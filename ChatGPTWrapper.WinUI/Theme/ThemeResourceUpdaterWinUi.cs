using ChatGPTWrapper.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChatGPTWrapper.WinUI.Theme;

internal static class ThemeResourceUpdaterWinUi
{
    private const string TokenDictionaryMarker = "BgBaseBrush";
    private static ResourceDictionary? _canonicalTokens;

    public static void ApplyToApplication(ResolvedTheme theme)
    {
        var appResources = Application.Current.Resources;
        var tokensDictionary = _canonicalTokens ??= FindTokenDictionary(appResources);
        if (tokensDictionary is not null)
            ApplyToDictionary(theme, tokensDictionary);

        ApplyToDictionary(theme, appResources);
    }

    private static ResourceDictionary? FindTokenDictionary(ResourceDictionary root)
    {
        if (root.ContainsKey(TokenDictionaryMarker))
            return root;

        foreach (var merged in root.MergedDictionaries)
        {
            var found = FindTokenDictionary(merged);
            if (found is not null)
                return found;
        }

        return null;
    }

    public static void ResetCanonicalDictionary() => _canonicalTokens = null;

    private static void ApplyToDictionary(ResolvedTheme theme, ResourceDictionary resources)
    {
        foreach (var token in ThemeTokenCatalog.All)
        {
            if (!theme.Tokens.TryGetValue(token.TokenKey, out var hex))
                continue;

            SetBrushColor(resources, token.WpfBrushKey, hex);
        }

        resources["WrapperFontFamily"] = theme.FontFamily;
        resources["FontSizeBody"] = theme.FontSizeBody;
        resources["FontSizeTitle"] = theme.FontSizeTitle;
        resources["FontSizeHint"] = theme.FontSizeHint;
        resources["SpaceXs"] = theme.SpaceXs;
        resources["SpaceSm"] = theme.SpaceSm;
        resources["SpaceMd"] = theme.SpaceMd;
        resources["SpaceLg"] = theme.SpaceLg;
        resources["SpaceXl"] = theme.SpaceXl;
        resources["ControlMinHeight"] = theme.ControlMinHeight;
        resources["RadiusControl"] = new Microsoft.UI.Xaml.CornerRadius(theme.RadiusControl);
        resources["RadiusCard"] = new Microsoft.UI.Xaml.CornerRadius(theme.RadiusCard);

        var compact = theme.DensityPreset == ThemeDensityPreset.Compact;
        resources["SegmentButtonPadding"] = compact
            ? new Thickness(10, 4, 10, 4)
            : new Thickness(14, 6, 14, 6);
    }

    /// <summary>
    /// Updates the color on an existing <see cref="SolidColorBrush"/> so elements that already
    /// resolved <c>{ThemeResource}</c> at load time pick up the change. Replacing the dictionary
    /// entry with a new brush leaves shell chrome stale in WinUI.
    /// </summary>
    private static void SetBrushColor(ResourceDictionary resources, string key, string hex)
    {
        var color = ParseHex(hex);
        if (resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static Color ParseHex(string hex)
    {
        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length == 6)
            normalized = "FF" + normalized;

        var value = Convert.ToUInt32(normalized, 16);
        return Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }
}

internal static class WinUiThemeApplication
{
    public static void Register()
    {
        ThemeApplicationService.RegisterWinUiApplyHandler(theme =>
            ThemeResourceUpdaterWinUi.ApplyToApplication(theme));
    }

    public static void ApplyThemeSettings(ThemeSettings settings, ThemeApplyOptions options)
    {
        var chrome = UiChromeStore.Load();
        chrome.Theme = settings.Clone();
        if (options.Persist)
            UiChromeStore.Save(chrome);

        ThemeApplicationService.InvalidateApplyCache();
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(chrome.Theme);
        ThemeRuntime.Update(resolved);
        ThemeApplicationService.ApplyToWinUi(resolved);
    }

    public static void ApplyThemeSettings(ThemeSettings settings, bool persist) =>
        ApplyThemeSettings(settings, new ThemeApplyOptions(Persist: persist, RefreshWebView: true));

    public static void ApplyStartupTheme()
    {
        var chrome = UiChromeStore.Load();
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(chrome.Theme);
        ThemeRuntime.Update(resolved);
        ThemeApplicationService.ApplyToWinUi(resolved);
    }
}
