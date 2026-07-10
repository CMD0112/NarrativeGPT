using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class RecapPage : UserControl
{
    public event EventHandler? CloseRequested;

    public RecapPage(string recapText)
    {
        InitializeComponent();
        RecapBox.Text = recapText;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RecapBox.Text))
            return;

        var data = new DataPackage();
        data.SetText(RecapBox.Text);
        Clipboard.SetContent(data);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
