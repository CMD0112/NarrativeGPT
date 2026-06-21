using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.Views;

public partial class PlayRightCompanionHost : UserControl
{
    public PlayRightCompanionHost()
    {
        InitializeComponent();
    }

    public TabControl RightTabControl => PlayRightTabControl;

    public void SetRightTabsVisible(bool visible)
    {
        PlayRightTabControl.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RightTabsRow.Height = visible ? GridLength.Auto : new GridLength(0);
    }

    public void ApplyLayout(PlayLayoutContext companionContext)
    {
        if (NotesSlot.Content is AdventureNotesPanel notesPanel)
            notesPanel.ApplyLayout(companionContext);
    }

    public void UpdateResponsiveLayout(double panelWidth) =>
        ApplyLayout(PlayLayoutContext.FromPanel(PlayPanelSide.Right, panelWidth));
}
