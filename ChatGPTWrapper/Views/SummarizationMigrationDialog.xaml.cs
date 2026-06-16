using System.Windows;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class SummarizationMigrationDialog : Window
{
    private readonly SummarizationMigrationCheckpoint _checkpoint;

    public Func<Task>? CreateNewPlayThreadAsync { get; init; }

    public SummarizationMigrationDialog(SummarizationMigrationCheckpoint checkpoint)
    {
        _checkpoint = checkpoint;
        InitializeComponent();
        MetaLine.Text = $"Hash: {checkpoint.CheckpointHash} · Turns: {checkpoint.TurnCount} · Messages: {checkpoint.ThreadMessageCount}";
        PacketBox.Text = checkpoint.MigrationPacket;
        VerifyPacketBox.Text = checkpoint.MigrationPacket;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PacketBox.Text);
        }
        catch
        {
            /* ignore */
        }
    }

    private async void CreateNewThread_Click(object sender, RoutedEventArgs e)
    {
        if (CreateNewPlayThreadAsync is null)
        {
            NewThreadStatusLine.Text = "New thread creation is not wired from the host.";
            return;
        }

        CreateNewThreadButton.IsEnabled = false;
        try
        {
            await CreateNewPlayThreadAsync();
            NewThreadStatusLine.Text = "Play thread creation requested. Paste the checkpoint packet in the new chat, then verify on step 3.";
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
        var hash = SummarizationMigrationService.ComputePacketHash(VerifyPacketBox.Text);
        var pass = string.Equals(hash, _checkpoint.CheckpointHash, StringComparison.OrdinalIgnoreCase);
        VerifyResultLine.Text = pass
            ? $"PASS — hash matches ({hash})"
            : $"FAIL — expected {_checkpoint.CheckpointHash}, got {hash}";
        VerifyResultLine.Foreground = pass
            ? System.Windows.Media.Brushes.ForestGreen
            : System.Windows.Media.Brushes.IndianRed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
