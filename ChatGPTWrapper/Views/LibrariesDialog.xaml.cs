using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class LibrariesDialog : ShellDialogWindow
{
    public LibrariesDialog()
    {
        InitializeComponent();
        KindBox.ItemsSource = Enum.GetValues<LibraryStore.LibraryKind>();
        KindBox.SelectedIndex = 0;
        Refresh();
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (KindBox.SelectedItem is not LibraryStore.LibraryKind kind)
            return;

        ItemsList.ItemsSource = LibraryStore.List(kind)
            .Select(i => $"{i.Name} ({i.Genre})")
            .ToList();
    }
}
