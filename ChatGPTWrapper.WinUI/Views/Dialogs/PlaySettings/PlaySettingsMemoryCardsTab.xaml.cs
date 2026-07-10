using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsMemoryCardsTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;

    public PlaySettingsMemoryCardsTab()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsChanged;

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        MemoryList.ItemsSource = context.Bundle.Memory.Entries
            .Select(e => new MemoryRowViewModel(e))
            .ToList();
        MemoryReviewList.ItemsSource = context.Bundle.Memory.ReviewQueue;
        MemoryReviewHeader.Text = context.Bundle.Memory.ReviewQueue.Count > 0
            ? $"{context.Bundle.Memory.ReviewQueue.Count} memory proposal(s) awaiting review"
            : "";
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        // Memory edited via review flows and store saves — no inline flush fields.
    }

    private async void SuggestMemories_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.SuggestMemoriesAsync is { } suggest)
            await suggest();
        if (_ctx is not null)
            Bind(_ctx);
    }

    private void ReviewAll_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host is null || sender is not Button { Tag: string tag })
            return;

        if (tag == "Memory")
            _ctx.Host.OpenProposalReviewHub?.Invoke(ProposalReviewCategory.Memory);
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.RaiseReviewQueueChanged();
        _ctx.NotifySettingsChanged();
    }

    private sealed class MemoryRowViewModel(MemoryEntry entry)
    {
        public string Text { get; } = entry.Text;

        public string Subtitle { get; } = entry.Pinned ? "Pinned" : "Memory";

        public double PinOpacity { get; } = entry.Pinned ? 1.0 : 0.0;
    }
}
