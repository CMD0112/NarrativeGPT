using System.Windows;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.Views;

public partial class FlightPacketCompareDialog : ShellDialogWindow
{
    public FlightPacketCompareDialog(string diffText, string metaLine)
    {
        InitializeComponent();
        DiffBox.Text = diffText;
        MetaLine.Text = metaLine;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiffBox.Text);
        }
        catch
        {
            /* ignore */
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
