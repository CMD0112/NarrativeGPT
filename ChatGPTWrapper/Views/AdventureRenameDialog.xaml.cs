using System.Windows;

namespace ChatGPTWrapper.Views;

public partial class AdventureRenameDialog : Window
{
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
