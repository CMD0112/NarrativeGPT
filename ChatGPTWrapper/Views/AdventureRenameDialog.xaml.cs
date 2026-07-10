using System.Windows;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.Views;

public partial class AdventureRenameDialog : ShellDialogWindow
{
    protected override bool ApplyDesignSizeOnOpen => false;

    protected override bool RestorePersistedSizeOnOpen => false;

    public string NewTitle => TitleBox.Text.Trim();

    public AdventureRenameDialog(string currentTitle)
    {
        InitializeComponent();
        TitleBox.Text = currentTitle;
        TitleBox.SelectAll();
        TitleBox.Focus();
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
