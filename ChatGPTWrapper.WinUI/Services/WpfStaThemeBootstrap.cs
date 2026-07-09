using System.Windows;
using ChatGPTWrapper;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Theme;
using WpfApplication = System.Windows.Application;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>
/// Loads WPF shell theme dictionaries for modal dialogs hosted on the WinUI STA thread.
/// </summary>
internal static class WpfStaThemeBootstrap
{
    private const string ShellTabScrollViewerStyleKey = "ShellTabScrollViewerStyle";

    private static readonly Uri WrapperChromeUri = new(
        $"/{typeof(UiChromeStore).Assembly.GetName().Name};component/Themes/WrapperChrome.xaml",
        UriKind.Relative);

    public static void EnsureApplied(WpfApplication app)
    {
        EnsureWrapperResources(app);

        ThemeApplicationService.ResetWpfApplyFingerprint();
        var chrome = UiChromeStore.Load();
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(chrome.Theme);
        ThemeRuntime.Update(resolved);
        ThemeApplicationService.ApplyToWpf(resolved);

        if (!ContainsResource(app.Resources, ShellTabScrollViewerStyleKey)
            || !ContainsResource(app.Resources, "BgBaseBrush"))
        {
            throw new InvalidOperationException(
                "WPF STA theme bootstrap failed — wrapper chrome resources are unavailable.");
        }
    }

    private static void EnsureWrapperResources(WpfApplication app)
    {
        if (ContainsResource(app.Resources, ShellTabScrollViewerStyleKey))
            return;

        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var merged = app.Resources.MergedDictionaries[i];
            if (merged.Source?.OriginalString?.Contains("WrapperChrome.xaml", StringComparison.OrdinalIgnoreCase) == true)
                app.Resources.MergedDictionaries.RemoveAt(i);
        }

        var chrome = new ResourceDictionary { Source = WrapperChromeUri };
        app.Resources.MergedDictionaries.Insert(0, chrome);

        // Force dictionary load before dialog XAML parse.
        _ = chrome[ShellTabScrollViewerStyleKey];
    }

    private static bool ContainsResource(ResourceDictionary resources, string key)
    {
        if (resources.Contains(key))
            return true;

        foreach (var merged in resources.MergedDictionaries)
        {
            if (ContainsResource(merged, key))
                return true;
        }

        return false;
    }
}
