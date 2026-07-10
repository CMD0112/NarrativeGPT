using System.Windows;
using System.Windows.Input;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Shell;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Views;

public partial class BrowserTabPickerDialog : ShellDialogWindow
{
    public WebView2? SelectedWebView { get; private set; }

    public BrowserTabPickerDialog(IReadOnlyList<BrowserTabSnapshot> tabs)
    {
        InitializeComponent();
        TabsList.ItemsSource = tabs.Select(BrowserTabRow.FromSnapshot).ToList();
        if (TabsList.Items.Count > 0)
            TabsList.SelectedIndex = 0;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (TabsList.SelectedItem is not BrowserTabRow row)
        {
            MessageBox.Show(this, "Select a browser tab first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedWebView = row.WebView;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TabsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        Confirm_Click(sender, e);

    private sealed class BrowserTabRow
    {
        public required string DisplayText { get; init; }

        public required WebView2 WebView { get; init; }

        public static BrowserTabRow FromSnapshot(BrowserTabSnapshot snapshot)
        {
            var url = FormatUrl(snapshot.SourceUrl);
            return new BrowserTabRow
            {
                DisplayText = string.IsNullOrWhiteSpace(url)
                    ? snapshot.Title
                    : $"{snapshot.Title} — {url}",
                WebView = snapshot.WebView,
            };
        }

        private static string FormatUrl(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "—";

            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
                return source.Length > 48 ? source[..48] + "…" : source;

            var path = uri.PathAndQuery;
            return path.Length > 48 ? path[..48] + "…" : path;
        }
    }
}
