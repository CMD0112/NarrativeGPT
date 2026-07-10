using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsNarratorContractTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;

    public PlaySettingsNarratorContractTab()
    {
        InitializeComponent();
        ApplyCardGridLayout();
    }

    public event EventHandler? SettingsChanged;

    private void OnCardsGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyCardGridLayout();

    private void ApplyCardGridLayout() =>
        PlaySettingsCardGridLayout.Apply(
            CardsGrid,
            [IntroCard, PacketPolicyCard, VoiceCard, InstructionsCard],
            [true, false, true, true],
            ActualWidth);

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        var s = context.Bundle.Metadata.Settings;
        if (s.PreferDomPlaySend)
            s.PreferDomPlaySend = false;
        MaxPacketBox.Text = s.MaxPacketChars.ToString();
        ForceFatPacketsCheck.IsChecked = s.ForceInlineLore;
        PerspectiveBox.Text = s.Perspective;
        BoundariesBox.Text = string.Join(Environment.NewLine, s.ContentBoundaries);
        CharacterPortrayalBox.Text = InstructionContractService.SerializeCharacterPortrayalRules(s.CharacterPortrayalRules);
        InstructionAddendumBox.Text = s.InstructionAddendum;
        s.SourcePublishMode = SourcePublishMode.Manual;
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        var s = context.Bundle.Metadata.Settings;
        if (int.TryParse(MaxPacketBox.Text, out var maxPacket))
            s.MaxPacketChars = Math.Clamp(maxPacket, 4000, 50000);
        s.PreferDomPlaySend = false;
        s.UseWrapperComposer = false;
        s.ForceInlineLore = ForceFatPacketsCheck.IsChecked == true;
        s.Perspective = PerspectiveBox.Text.Trim();
        s.ContentBoundaries = BoundariesBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        s.CharacterPortrayalRules = InstructionContractService.ParseCharacterPortrayalRules(CharacterPortrayalBox.Text) ?? [];
        s.InstructionAddendum = InstructionAddendumBox.Text.Trim();
        InstructionContractService.HydrateDesignInstructionFields(context.Bundle);
        s.SourcePublishMode = SourcePublishMode.Manual;
    }

    private async void SyncInstructions_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.SyncInstructionsAsync is { } sync)
            await sync();
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.NotifySettingsChanged();
    }
}
