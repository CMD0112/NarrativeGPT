using System.IO;
using System.Windows;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class SourceCompareDialog : Window
{
    public SourceCompareDialog(
        string leftPath,
        string rightPath,
        string leftLabel,
        string rightLabel,
        string? canonicalSha = null,
        string? compareSha = null,
        string? publishedSha = null)
    {
        InitializeComponent();

        var leftText = File.Exists(leftPath) ? File.ReadAllText(leftPath) : "";
        var rightText = File.Exists(rightPath) ? File.ReadAllText(rightPath) : "";
        var diff = TextDiffService.ComputeLineDiff(leftText, rightText);
        DiffBox.Text = TextDiffService.FormatUnifiedDiff(diff, leftLabel, rightLabel);

        var parts = new List<string> { $"{leftLabel} vs {rightLabel}" };
        if (!string.IsNullOrWhiteSpace(canonicalSha))
            parts.Add($"Canonical SHA: {SourceManifestHelper.ShortHash(canonicalSha)}");
        if (!string.IsNullOrWhiteSpace(compareSha))
            parts.Add($"Compare SHA: {SourceManifestHelper.ShortHash(compareSha)}");
        if (!string.IsNullOrWhiteSpace(publishedSha))
            parts.Add($"Published SHA: {SourceManifestHelper.ShortHash(publishedSha)}");
        MetaLine.Text = string.Join(" · ", parts);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiffBox.Text);
        }
        catch
        {
            /* ignore */
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
