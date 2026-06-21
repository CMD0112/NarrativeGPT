using System.Windows;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class SyncFromThreadDialog : Window
{
    public bool SyncConfirmed { get; private set; }

    public SyncFromThreadDialog(ThreadLogDriftAnalysis analysis)
    {
        InitializeComponent();
        SummaryLine.Text =
            $"Play thread: {analysis.ThreadTurnCount} turn pair(s) · Local log: {analysis.LogTurnCount} accepted turn(s)";
        DetailLine.Text = analysis.ThreadTurnCount > analysis.LogTurnCount
            ? "Sync rebuilds accepted turns in log.json from the filtered play thread transcript."
            : analysis.ThreadTurnCount < analysis.LogTurnCount
                ? "Sync removes extra local turns and rebuilds from the play thread."
                : "Turn counts match but player or narrator text differs.";
    }

    private void Sync_Click(object sender, RoutedEventArgs e)
    {
        SyncConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SyncConfirmed = false;
        DialogResult = false;
        Close();
    }
}
