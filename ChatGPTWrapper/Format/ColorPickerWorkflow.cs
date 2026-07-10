using System.Windows;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.Format;

public static class ColorPickerWorkflow
{
    public static bool TryPickHex(Window owner, string initialHex, out string selectedHex) =>
        TryPickHex(owner, initialHex, contextBackgroundHex: null, out selectedHex);

    public static bool TryPickHex(
        Window owner,
        string initialHex,
        string? contextBackgroundHex,
        out string selectedHex) =>
        TryPickHex(owner, initialHex, contextBackgroundHex, context: null, out selectedHex);

    public static bool TryPickHex(
        Window owner,
        string initialHex,
        string? contextBackgroundHex,
        ColorPickerContext? context,
        out string selectedHex)
    {
        selectedHex = initialHex;
        var chrome = UiChromeStore.Load();
        var background = string.IsNullOrWhiteSpace(contextBackgroundHex)
            ? context?.ContextBackgroundHex ?? ThemeRuntime.Current.GetHex("BgBase")
            : contextBackgroundHex;

        var options = new ColorPickerDialogOptions
        {
            ContextBackgroundHex = background,
            RecentColors = chrome.RecentPickerColors,
            Context = context,
        };

        var dialog = new ThemeColorPickerDialog(owner, initialHex, options);
        if (dialog.ShowDialog() != true)
            return false;

        selectedHex = dialog.SelectedHex;
        RecentPickerColors.Record(chrome.RecentPickerColors, selectedHex);
        UiChromeStore.Save(chrome);
        return true;
    }
}
