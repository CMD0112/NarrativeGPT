using System.Windows;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class ContextViewerDialog : ShellDialogWindow
{
    public ContextViewerDialog(string packetText, string meta, bool useStructuredPreview = true)
    {
        InitializeComponent();
        PacketBox.Text = useStructuredPreview && ContextTagFormat.ContainsTags(packetText)
            ? ContextTagFormat.FormatStructuredPreview(packetText)
            : packetText;
        MetaLine.Text = meta;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PacketBox.Text);
        }
        catch
        {
            /* ignore */
        }
    }
}
