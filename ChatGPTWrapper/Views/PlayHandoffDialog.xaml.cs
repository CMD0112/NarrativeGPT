using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class PlayHandoffDialog : ShellDialogWindow
{
    private readonly AdventureBundle _bundle;
    private readonly PlayHandoffSnapshot _snapshot;
    private PlayHandoffCheckpoint _checkpoint;

    public Func<PlayThreadStartRequest, Task>? StartNewPlayThreadAsync { get; init; }

    public Func<Task>? RollbackHandoffAsync { get; init; }

    public PlayHandoffDialog(AdventureBundle bundle, PlayHandoffSnapshot snapshot, PlayHandoffCheckpoint checkpoint)
    {
        _bundle = bundle;
        _snapshot = snapshot;
        _checkpoint = checkpoint;
        InitializeComponent();
        SummaryBox.Text = snapshot.RollingSummary;
        VerifyPacketBox.Text = checkpoint.HandoffPacket;
        UpdateMetaLine();
        RefreshPacketPreview();
    }

    private PlayHandoffOptions CurrentOptions()
    {
        var mode = HandoffModeCombo.SelectedItem is ComboBoxItem item
                   && Enum.TryParse<PlayHandoffMode>(item.Tag?.ToString(), out var parsed)
            ? parsed
            : PlayHandoffMode.SummaryWithTranscript;

        return new PlayHandoffOptions
        {
            Mode = mode,
            CarryForwardSummary = SummaryBox.Text,
        };
    }

    private void UpdateMetaLine()
    {
        MetaLine.Text =
            $"Hash: {_checkpoint.CheckpointHash} · Thread turns: {_snapshot.AcceptedTurnCount} · "
            + $"Adventure turns: {_snapshot.AdventureTurnOrdinal} · Entities: {_snapshot.EntityFingerprint} · "
            + $"Manifest: {_snapshot.ManifestFingerprint}";
    }

    private AdventureBundle FreshBundle() =>
        PlayThreadPacketService.ReloadFresh(_bundle.Metadata.Id) ?? _bundle;

    private void RefreshPacketPreview()
    {
        var bundle = FreshBundle();
        var options = CurrentOptions();
        var packet = PlayThreadPacketService.BuildHandoffPacket(bundle, options);
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        _checkpoint = PlayHandoffService.BuildCheckpoint(bundle, snapshot, options);
        _checkpoint.HandoffPacket = packet;
        PacketPreviewBox.Text = bundle.Metadata.Settings.UseContextTags
            ? ContextTagFormat.FormatStructuredPreview(packet)
            : packet;
        UpdateMetaLine();
    }

    private void HandoffSettings_Changed(object sender, RoutedEventArgs e) =>
        RefreshPacketPreview();

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) =>
        RefreshPacketPreview();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PlayThreadPacketService.BuildHandoffPacket(_bundle, CurrentOptions()));
        }
        catch
        {
            /* ignore */
        }
    }

    private async void CreateNewThread_Click(object sender, RoutedEventArgs e)
    {
        if (StartNewPlayThreadAsync is null)
        {
            NewThreadStatusLine.Text = "Thread rotation is not wired from the host.";
            return;
        }

        CreateNewThreadButton.IsEnabled = false;
        try
        {
            var options = CurrentOptions();
            var packet = PlayThreadPacketService.BuildHandoffPacket(_bundle, options);
            var request = new PlayThreadStartRequest
            {
                Kind = PlayThreadStartKind.Handoff,
                HandoffOptions = options,
                SkipConfirmation = true,
            };

            await StartNewPlayThreadAsync(request);
            NewThreadStatusLine.Text =
                "Play thread rotation complete. Paste the handoff packet in the new chat if you have not already, then verify on step 3.";
            VerifyPacketBox.Text = packet;
            WizardTabs.SelectedIndex = 2;
        }
        catch (Exception ex)
        {
            NewThreadStatusLine.Text = ex.Message;
        }
        finally
        {
            CreateNewThreadButton.IsEnabled = true;
        }
    }

    private void Verify_Click(object sender, RoutedEventArgs e)
    {
        var verifyCheckpoint = PlayHandoffService.TryLoadCheckpointSidecar(_bundle, out var sidecar) && sidecar is not null
            ? sidecar
            : _checkpoint;

        var hash = PlayHandoffService.ComputePacketHash(VerifyPacketBox.Text);
        var pass = PlayHandoffService.VerifyCheckpointHash(verifyCheckpoint, VerifyPacketBox.Text);
        VerifyResultLine.Text = pass
            ? $"PASS — hash matches ({hash}). Expected adventure turn after handoff: {_snapshot.AdventureTurnOrdinal + 1} on new thread."
            : $"FAIL — expected {verifyCheckpoint.CheckpointHash}, got {hash}";
        VerifyResultLine.Foreground = pass
            ? System.Windows.Media.Brushes.ForestGreen
            : System.Windows.Media.Brushes.IndianRed;
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (RollbackHandoffAsync is not null)
        {
            await RollbackHandoffAsync();
            VerifyResultLine.Text = "Rollback requested. Close this dialog and confirm your play thread binding in Play settings.";
            VerifyResultLine.Foreground = System.Windows.Media.Brushes.Goldenrod;
            return;
        }

        if (PlayHandoffService.TryRollbackPendingHandoff(_bundle))
        {
            VerifyResultLine.Text = "Rollback complete — prior play thread binding restored.";
            VerifyResultLine.Foreground = System.Windows.Media.Brushes.ForestGreen;
            return;
        }

        VerifyResultLine.Text = "Rollback unavailable — handoff may already be completed or turns exist on the new thread.";
        VerifyResultLine.Foreground = System.Windows.Media.Brushes.IndianRed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
