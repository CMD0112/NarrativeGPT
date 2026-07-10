namespace ChatGPTWrapper.Theme;

/// <summary>Runtime theme snapshot for WPF and WebView injection.</summary>
public static class ThemeRuntime
{
    private static ResolvedTheme _current =
        ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());

    public static ResolvedTheme Current => _current;

    public static void Update(ThemeSettings settings)
    {
        _current = ThemeApplicationService.ResolveEffectiveTheme(settings);
    }

    public static void Update(ResolvedTheme resolved) => _current = resolved;
}
