using System.Windows;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Views;

public partial class AdventureSettingsDialog : Window
{
    public AdventureSettingsDialog(AdventureBundle bundle)
    {
        InitializeComponent();
        Visibility = Visibility.Hidden;
        Loaded += (_, _) =>
        {
            var dlg = new PlayPromptInjectionDialog(bundle, previewPlayerLine: null, PlaySettingsTab.Settings)
            {
                Owner = Owner ?? Window.GetWindow(this),
            };
            dlg.ShowDialog();
            DialogResult = dlg.DialogResult;
            Close();
        };
    }
}
