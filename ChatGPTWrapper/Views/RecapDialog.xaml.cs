using System.Windows;

namespace ChatGPTWrapper.Views;

public partial class RecapDialog : Window
{
    public RecapDialog(string recapText)
    {
        InitializeComponent();
        RecapBox.Text = recapText;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(RecapBox.Text))
            Clipboard.SetText(RecapBox.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
