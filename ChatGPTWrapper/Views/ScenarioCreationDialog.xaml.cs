using System.Windows;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Views;

public partial class ScenarioCreationDialog : ShellDialogWindow
{
    public ScenarioDocument? ResultScenario { get; private set; }

    public string AdventureTitle => TitleBox.Text.Trim();

    public bool StartWithOpeningNarration => StartWithOpeningCheck.IsChecked == true;

    public bool RequestDesignWithAi { get; private set; }

    public ScenarioCreationDialog()
    {
        InitializeComponent();
    }

    private void DesignWithAi_Click(object sender, RoutedEventArgs e)
    {
        RequestDesignWithAi = true;
        DialogResult = true;
        Close();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ResultScenario = new ScenarioDocument
        {
            Genre = GenreBox.Text.Trim(),
            Setting = SettingBox.Text.Trim(),
            PlayerRole = PlayerRoleBox.Text.Trim(),
            OpeningSituation = OpeningBox.Text.Trim(),
            PlotEssentials = PlotBox.Text.Trim(),
            AuthorsNote = AuthorsNoteBox.Text.Trim(),
            Tone = GenreBox.Text.Trim(),
        };

        DialogResult = true;
        Close();
    }
}
