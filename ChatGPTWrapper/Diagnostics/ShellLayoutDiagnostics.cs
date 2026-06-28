using System.Windows;

namespace ChatGPTWrapper.Diagnostics;

/// <summary>
/// Compact body-grid snapshots for extended UI diagnostics.
/// </summary>
internal static class ShellLayoutDiagnostics
{
    public static object Capture(
        string? adventureHostContentType,
        Visibility adventureHostVisibility,
        GridLength adventureColumn,
        GridLength chatColumn,
        GridLength notesColumn) =>
        new
        {
            adventureHostContent = adventureHostContentType ?? "(null)",
            adventureHostVisible = adventureHostVisibility == Visibility.Visible,
            adventureColumn = Describe(adventureColumn),
            chatColumn = Describe(chatColumn),
            notesColumn = Describe(notesColumn),
        };

    public static string Describe(GridLength length) =>
        length.IsStar ? $"star:{length.Value}" :
        length.IsAbsolute ? $"px:{length.Value:0.##}" :
        "collapsed";
}
