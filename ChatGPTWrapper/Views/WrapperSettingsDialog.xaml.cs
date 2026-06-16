using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class WrapperSettingsDialog : Window
{
    public WrapperSettingsDialog()
    {
        InitializeComponent();
        var settings = WrapperSettingsStore.Current;
        AdventuresPathBox.Text = settings.AdventuresDirectoryOverride ?? "";
        DefaultPathLine.Text = $"Default: {AppDirectories.DefaultAdventuresDirectory}";
    }

    public WrapperSettings? ResultSettings { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose adventures folder",
            InitialDirectory = Directory.Exists(AdventuresPathBox.Text.Trim())
                ? AdventuresPathBox.Text.Trim()
                : AppDirectories.AdventuresDirectory,
        };

        if (dlg.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            AdventuresPathBox.Text = dlg.FolderName;
    }

    private void Default_Click(object sender, RoutedEventArgs e) =>
        AdventuresPathBox.Text = "";

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = AdventuresPathBox.Text.Trim();
        string? normalized = null;

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!WrapperSettingsStore.TryValidateAdventuresDirectory(path, out normalized, out var error))
            {
                MessageBox.Show(this, error ?? "Invalid folder.", "Adventures folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var currentRoot = Path.GetFullPath(AppDirectories.AdventuresDirectory);
            if (!string.Equals(normalized, currentRoot, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(currentRoot)
                && Directory.EnumerateDirectories(currentRoot).Any())
            {
                var warn = MessageBox.Show(
                    this,
                    $"Adventures already exist under the current folder:\n{currentRoot}\n\n"
                    + $"New adventures will use:\n{normalized}\n\n"
                    + "Existing adventures at the old location remain there until you move them or use "
                    + "\"Create folder on disk\" / import.\n\nContinue?",
                    "Change adventures folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (warn != MessageBoxResult.Yes)
                    return;
            }
        }

        ResultSettings = new WrapperSettings
        {
            AdventuresDirectoryOverride = normalized,
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
