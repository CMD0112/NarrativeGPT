using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class InjectionPacketPreviewPanel : UserControl
{
    private InjectionPreviewSnapshot? _snapshot;

    public InjectionPacketPreviewPanel()
    {
        InitializeComponent();
        SectionList.SelectionChanged += (_, _) =>
        {
            if (SectionList.SelectedItem is InjectionSectionViewModel row)
                ScrollToSection(row.Id);
        };
    }

    public void ApplySnapshot(InjectionPreviewSnapshot? snapshot)
    {
        _snapshot = snapshot;

        if (snapshot is null || !snapshot.HasPlayerLine)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            SummaryPanel.Visibility = Visibility.Collapsed;
            DelegationBadgeText.Text = "No preview";
            EmptyStateText.Text = snapshot?.DeltaMessages.FirstOrDefault()
                                  ?? "Enter a sample player line to preview the injected packet.";
            SectionList.ItemsSource = null;
            DeltaList.ItemsSource = null;
            PacketBodyBox.Text = "";
            return;
        }

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Visible;

        DelegationBadgeText.Text = snapshot.DelegationLabel;
        ModeBadgeText.Text = $"Mode: {snapshot.ModeLabel}";
        HashBadgeText.Text = string.IsNullOrWhiteSpace(snapshot.PacketHash)
            ? ""
            : $"Hash: {snapshot.PacketHash[..Math.Min(8, snapshot.PacketHash.Length)]}…";

        CharBudgetBar.Value = InjectionSettingsUiHelper.CharBudgetRatio(snapshot.CharCount, snapshot.MaxPacketChars);
        ApplyCharBudgetBarStyle(snapshot.CharCount, snapshot.MaxPacketChars, snapshot.WasTrimmed);
        CharBudgetLabel.Text = InjectionSettingsUiHelper.FormatCharBudget(snapshot.CharCount, snapshot.MaxPacketChars);
        if (snapshot.WasTrimmed)
            CharBudgetLabel.Text += " (trimmed)";

        TrimmedBadge.Visibility = snapshot.WasTrimmed ? Visibility.Visible : Visibility.Collapsed;

        MetaLineText.Text = snapshot.MetaLine;
        SectionList.ItemsSource = snapshot.SectionRows;

        if (snapshot.DeltaMessages.Count > 0)
        {
            DeltaExpander.Visibility = Visibility.Visible;
            DeltaList.ItemsSource = snapshot.DeltaMessages;
        }
        else
        {
            DeltaExpander.Visibility = Visibility.Collapsed;
            DeltaList.ItemsSource = null;
        }

        PacketBodyBox.Text = snapshot.FormattedBody;
    }

    private void ApplyCharBudgetBarStyle(int used, int max, bool wasTrimmed)
    {
        var ratio = InjectionSettingsUiHelper.CharBudgetRatio(used, max);
        var resources = Application.Current.Resources;
        SolidColorBrush brush;
        if (wasTrimmed || ratio >= 1.0)
            brush = (SolidColorBrush)resources["WarningBrush"];
        else if (ratio >= 0.85)
            brush = (SolidColorBrush)resources["AccentLinkBrush"];
        else
            brush = (SolidColorBrush)resources["AccentPrimaryBrush"];

        CharBudgetBar.Foreground = brush;
    }

    private void ScrollToSection(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(_snapshot?.FormattedBody))
            return;

        PacketBodyExpander.IsExpanded = true;

        var markers = sectionId switch
        {
            "player" => new[] { "PLAYER TURN", "[[cgw:" },
            "overrides" => new[] { "=== TURN OVERRIDES ===", "[[cgw:overrides" },
            "turn-directive" => new[] { "=== TURN DIRECTIVE ===", "[[cgw:turn-directive" },
            _ => new[] { $"[[cgw:{sectionId}", $"=== {sectionId.ToUpperInvariant()}" },
        };

        foreach (var marker in markers)
        {
            var idx = PacketBodyBox.Text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            PacketBodyBox.Focus(FocusState.Programmatic);
            var start = Math.Min(idx, PacketBodyBox.Text.Length);
            var length = Math.Min(60, PacketBodyBox.Text.Length - start);
            PacketBodyBox.Select(start, length);
            break;
        }
    }
}
