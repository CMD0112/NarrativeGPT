using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Input;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class CanonInboxDialog : ShellDialogWindow
{
    public event EventHandler<CanonInboxItem>? NavigateRequested;

    private readonly AdventureBundle _bundle;

    public CanonInboxDialog(AdventureBundle bundle, bool suppressEntityProposals = false)
    {
        _bundle = bundle;
        InitializeComponent();
        InboxList.ItemsSource = CanonInboxService.ListItems(bundle, suppressEntityProposals);
    }

    private void InboxList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InboxList.SelectedItem is CanonInboxItem item)
        {
            NavigateRequested?.Invoke(this, item);
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
