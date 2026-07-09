using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChatGPTWrapper.WinUI.Theming;

/// <summary>
/// Maps paradigm scope labels (P2) to semantic badge brushes in <c>WrapperTokens.xaml</c>.
/// </summary>
public static class ScopeBadgePalette
{
    public static void Apply(Border badge, TextBlock label, string? scopeLabel)
    {
        if (badge is null || label is null)
            return;

        if (string.IsNullOrWhiteSpace(scopeLabel))
        {
            badge.Visibility = Visibility.Collapsed;
            return;
        }

        label.Text = scopeLabel;
        badge.Visibility = Visibility.Visible;

        var (backgroundKey, foregroundKey) = ResolveBrushKeys(scopeLabel);
        badge.Background = GetBrush(backgroundKey);
        badge.BorderBrush = GetBrush(foregroundKey);
        badge.BorderThickness = new Thickness(1);
        label.Foreground = GetBrush(foregroundKey);
    }

    private static (string BackgroundKey, string ForegroundKey) ResolveBrushKeys(string scopeLabel)
    {
        return Normalize(scopeLabel) switch
        {
            "thissend" or "nextsend" => ("ScopeBadgeThisSendBackgroundBrush", "ScopeBadgeThisSendForegroundBrush"),
            "preview" => ("ScopeBadgePreviewBackgroundBrush", "ScopeBadgePreviewForegroundBrush"),
            "persistent" => ("ScopeBadgePersistentBackgroundBrush", "ScopeBadgePersistentForegroundBrush"),
            "adventure" => ("ScopeBadgeAdventureBackgroundBrush", "ScopeBadgeAdventureForegroundBrush"),
            "project" => ("ScopeBadgeProjectBackgroundBrush", "ScopeBadgeProjectForegroundBrush"),
            "session" => ("ScopeBadgeSessionBackgroundBrush", "ScopeBadgeSessionForegroundBrush"),
            "jobs" => ("ScopeBadgeJobsBackgroundBrush", "ScopeBadgeJobsForegroundBrush"),
            "chrome" => ("ScopeBadgeChromeBackgroundBrush", "ScopeBadgeChromeForegroundBrush"),
            "readonly" => ("ScopeBadgeReadOnlyBackgroundBrush", "ScopeBadgeReadOnlyForegroundBrush"),
            "developer" => ("ScopeBadgeDeveloperBackgroundBrush", "ScopeBadgeDeveloperForegroundBrush"),
            _ => ("ScopeBadgeDefaultBackgroundBrush", "ScopeBadgeDefaultForegroundBrush"),
        };
    }

    private static string Normalize(string scopeLabel) =>
        new string(scopeLabel.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    private static Brush GetBrush(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var resource) == true && resource is Brush brush)
            return brush;

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
