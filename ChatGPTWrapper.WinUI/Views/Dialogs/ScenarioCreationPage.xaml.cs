using ChatGPTWrapper.Adventure.Models;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class ScenarioCreationPage : UserControl
{
    public ScenarioCreationPage()
    {
        InitializeComponent();
    }

    public string AdventureTitle => TitleBox.Text.Trim();

    public bool StartWithOpeningNarration => StartWithOpeningCheck.IsChecked == true;

    public ScenarioDocument BuildScenario() =>
        new()
        {
            Genre = GenreBox.Text.Trim(),
            Setting = SettingBox.Text.Trim(),
            PlayerRole = PlayerRoleBox.Text.Trim(),
            OpeningSituation = OpeningBox.Text.Trim(),
            PlotEssentials = PlotBox.Text.Trim(),
            AuthorsNote = AuthorsNoteBox.Text.Trim(),
            Tone = GenreBox.Text.Trim(),
        };
}
