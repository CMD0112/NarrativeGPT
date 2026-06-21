using System.Windows;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.Format;

public static class ColorPickerWorkflow
{
    public static bool TryPickHex(Window owner, string initialHex, out string selectedHex)
    {
        selectedHex = initialHex;
        var dialog = new ThemeColorPickerDialog(owner, initialHex);
        if (dialog.ShowDialog() != true)
            return false;

        selectedHex = dialog.SelectedHex;
        return true;
    }
}
