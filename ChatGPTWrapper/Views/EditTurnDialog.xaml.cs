using System.Windows;

namespace ChatGPTWrapper.Views;

public partial class EditTurnDialog : Window
{
    public string PlayerText => PlayerBox.Text;

    public string NarratorText => NarratorBox.Text;

    public EditTurnDialog(string player, string narrator)
    {
        InitializeComponent();
        PlayerBox.Text = player;
        NarratorBox.Text = narrator;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
