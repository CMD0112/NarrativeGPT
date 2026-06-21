using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class NarratorAdvancedDialog : Window
{
    private readonly AdventureBundle _bundle;
    private readonly NarratorOverrideScope _emphasisScope;

    public NarratorAdvancedDialog(AdventureBundle bundle, NarratorOverrideScope emphasisScope)
    {
        _bundle = bundle;
        _emphasisScope = emphasisScope;
        InitializeComponent();

        var settings = bundle.Metadata.Settings;
        TurnDirectiveBox.Text = settings.PlayTurnOverrides.TurnDirective ?? "";

        var session = NarratorOverrideResolver.GetSessionOverrides(bundle)
                      ?? new PlaySessionNarratorOverrides();
        SessionAddendumBox.Text = session.TemporaryAddendum ?? "";

        EmphasizeBoundariesCheck.IsChecked = emphasisScope switch
        {
            NarratorOverrideScope.Turn => settings.PlayTurnOverrides.EmphasizeBoundaries,
            NarratorOverrideScope.Session => session.EmphasizeBoundaries,
            _ => settings.PlayTurnOverrides.EmphasizeBoundaries || session.EmphasizeBoundaries,
        };
        EmphasizePortrayalRulesCheck.IsChecked = emphasisScope switch
        {
            NarratorOverrideScope.Turn => settings.PlayTurnOverrides.EmphasizePortrayalRules,
            NarratorOverrideScope.Session => session.EmphasizePortrayalRules,
            _ => settings.PlayTurnOverrides.EmphasizePortrayalRules || session.EmphasizePortrayalRules,
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = _bundle.Metadata.Settings;
        settings.PlayTurnOverrides.TurnDirective = string.IsNullOrWhiteSpace(TurnDirectiveBox.Text)
            ? null
            : TurnDirectiveBox.Text.Trim();

        var session = NarratorOverrideResolver.GetOrCreateSessionOverrides(_bundle);
        session.TemporaryAddendum = string.IsNullOrWhiteSpace(SessionAddendumBox.Text)
            ? null
            : SessionAddendumBox.Text.Trim();

        switch (_emphasisScope)
        {
            case NarratorOverrideScope.Turn:
                settings.PlayTurnOverrides.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                settings.PlayTurnOverrides.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                break;
            case NarratorOverrideScope.Session:
                session.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                session.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                break;
            case NarratorOverrideScope.Adventure:
                settings.PlayTurnOverrides.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                settings.PlayTurnOverrides.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                session.EmphasizeBoundaries = EmphasizeBoundariesCheck.IsChecked == true;
                session.EmphasizePortrayalRules = EmphasizePortrayalRulesCheck.IsChecked == true;
                break;
        }

        DialogResult = true;
        Close();
    }
}
