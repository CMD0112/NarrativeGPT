using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class AdventureNotesPanel : UserControl
{
    private AdventureBundle? _bundle;

    public AdventureNotesPanel()
    {
        InitializeComponent();
    }

    public void LoadAdventure(Guid id)
    {
        _bundle = AdventureStore.Load(id);
        if (_bundle is null)
            return;

        NotesBox.Text = _bundle.Notes ?? "";
    }

    public void SaveConfiguration() => SaveNotes();

    private void SaveNotes()
    {
        if (_bundle is null)
            return;

        _bundle.Notes = NotesBox.Text;
        AdventureStore.Save(_bundle);
    }

    private void NotesBox_LostFocus(object sender, RoutedEventArgs e) => SaveNotes();
}
