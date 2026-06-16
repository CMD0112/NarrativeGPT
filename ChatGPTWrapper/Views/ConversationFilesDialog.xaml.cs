using System.IO;
using System.Windows;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class ConversationFilesDialog : Window
{
    private readonly Func<ConversationFileRef, Task<byte[]>>? _downloadFile;
    private readonly IReadOnlyList<ConversationFileRef> _files;

    public ConversationFilesDialog(
        IReadOnlyList<ConversationFileRef> files,
        Func<ConversationFileRef, Task<byte[]>>? downloadFile = null)
    {
        InitializeComponent();
        _files = files;
        _downloadFile = downloadFile;
        StatusLine.Text = files.Count == 0
            ? "No files found in the linked play thread."
            : $"{files.Count} file(s) in the linked play thread.";
        FilesList.ItemsSource = files;
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not ConversationFileRef file || _downloadFile is null)
            return;

        try
        {
            var bytes = await _downloadFile(file);
            var dlg = new SaveFileDialog
            {
                FileName = string.IsNullOrWhiteSpace(file.Name) ? "download" : file.Name,
            };
            if (dlg.ShowDialog() != true)
                return;

            await File.WriteAllBytesAsync(dlg.FileName, bytes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Download failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
