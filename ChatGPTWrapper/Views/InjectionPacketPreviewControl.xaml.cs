using System.Windows;

using System.Windows.Controls;

using ChatGPTWrapper.Adventure.Services;



namespace ChatGPTWrapper.Views;



public partial class InjectionPacketPreviewControl : UserControl

{

    private InjectionPreviewSnapshot? _snapshot;

    private bool _compactMode;

    private bool _suppressSectionSelection;



    public InjectionPacketPreviewControl()

    {

        InitializeComponent();

    }



    public bool CompactMode

    {

        get => _compactMode;

        set

        {

            _compactMode = value;

            PacketBodyExpander.IsExpanded = !value;

            PacketBodyExpander.Visibility = value ? Visibility.Collapsed : Visibility.Visible;

        }

    }



    public void ApplySnapshot(InjectionPreviewSnapshot? snapshot)

    {

        _snapshot = snapshot;



        if (snapshot is null || !snapshot.HasPlayerLine)

        {

            EmptyStatePanel.Visibility = Visibility.Visible;

            SummaryPanel.Visibility = Visibility.Collapsed;

            SectionList.Visibility = Visibility.Collapsed;

            DeltaExpander.Visibility = Visibility.Collapsed;

            PacketBodyExpander.Visibility = _compactMode ? Visibility.Collapsed : Visibility.Visible;



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

        SectionList.Visibility = Visibility.Visible;

        PacketBodyExpander.Visibility = _compactMode ? Visibility.Collapsed : Visibility.Visible;



        DelegationBadgeText.Text = snapshot.DelegationLabel;

        ModeBadgeText.Text = $"Mode: {snapshot.ModeLabel}";

        HashBadgeText.Text = string.IsNullOrWhiteSpace(snapshot.PacketHash)

            ? ""

            : $"Hash: {snapshot.PacketHash[..Math.Min(8, snapshot.PacketHash.Length)]}…";



        CharBudgetBar.Value = InjectionSettingsUiHelper.CharBudgetRatio(snapshot.CharCount, snapshot.MaxPacketChars);

        CharBudgetLabel.Text = InjectionSettingsUiHelper.FormatCharBudget(snapshot.CharCount, snapshot.MaxPacketChars);

        if (snapshot.WasTrimmed)

            CharBudgetLabel.Text += " (trimmed)";



        MetaLineText.Text = snapshot.MetaLine;



        _suppressSectionSelection = true;

        SectionList.ItemsSource = snapshot.SectionRows;

        SectionList.SelectedIndex = -1;

        _suppressSectionSelection = false;



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



    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (_suppressSectionSelection || SectionList.SelectedItem is not InjectionSectionViewModel row)

            return;



        ScrollToSection(row.Id);

    }



    private void ScrollToSection(string sectionId)

    {

        if (string.IsNullOrWhiteSpace(_snapshot?.FormattedBody))

            return;



        if (_compactMode)

        {

            PacketBodyExpander.Visibility = Visibility.Visible;

            PacketBodyExpander.IsExpanded = true;

        }

        else

        {

            PacketBodyExpander.IsExpanded = true;

        }



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



            PacketBodyBox.Focus();

            PacketBodyBox.Select(idx, Math.Min(60, PacketBodyBox.Text.Length - idx));

            PacketBodyBox.ScrollToLine(PacketBodyBox.GetLineIndexFromCharacterIndex(idx));

            break;

        }

    }

}


