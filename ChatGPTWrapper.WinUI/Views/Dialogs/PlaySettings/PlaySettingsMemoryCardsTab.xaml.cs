using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
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

        CardsList.ItemsSource = context.Bundle.Cards.Cards
            .Select(c => new StoryCardRowViewModel(c))
            .ToList();
        CardReviewList.ItemsSource = context.Bundle.Cards.ReviewQueue
            .Select(c => new CardReviewListItem(c))
            .ToList();
        CardReviewHeader.Text = context.Bundle.Cards.ReviewQueue.Count > 0
            ? $"{context.Bundle.Cards.ReviewQueue.Count} card proposal(s) awaiting review"
            : "";
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        // Memory/cards edited via review flows and store saves — no inline flush fields.
    }

    private async void SuggestMemories_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.SuggestMemoriesAsync is { } suggest)
            await suggest();
        if (_ctx is not null)
            Bind(_ctx);
    }

    private async void GenerateCards_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.GenerateCardsAsync is { } generate)
            await generate();
        if (_ctx is not null)
            Bind(_ctx);
    }

    private void AddCard_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        _ctx.Bundle.Cards.Cards.Add(new StoryCard
        {
            Id = Guid.NewGuid(),
            Name = "New card",
            Triggers = ["keyword"],
            Content = "Lore text",
        });
        AdventureStore.Save(_ctx.Bundle, AdventureSaveScope.Cards);
        Bind(_ctx);
        OnChanged(sender, e);
    }

    private void ReviewAll_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host is null || sender is not Button { Tag: string tag })
            return;

        var category = tag switch
        {
            "Memory" => ProposalReviewCategory.Memory,
            "Cards" => ProposalReviewCategory.Card,
            _ => (ProposalReviewCategory?)null,
        };
        _ctx.Host.OpenProposalReviewHub?.Invoke(category);
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.RaiseReviewQueueChanged();
        _ctx.NotifySettingsChanged();
    }

    private sealed class CardReviewListItem(CardReviewItem item)
    {
        public string DisplayLabel
        {
            get
            {
                var text = item.ProposedChange ?? "";
                return text.Length <= 80 ? text : text[..80] + "…";
            }
        }
    }

    private sealed class MemoryRowViewModel(MemoryEntry entry)
    {
        public string Text { get; } = entry.Text;

        public string Subtitle { get; } = entry.Pinned ? "Pinned" : "Memory";

        public double PinOpacity { get; } = entry.Pinned ? 1.0 : 0.0;
    }

    private sealed class StoryCardRowViewModel(StoryCard card)
    {
        public string Name { get; } = card.Name;

        public string TriggerSummary { get; } = card.Triggers.Count > 0
            ? string.Join(", ", card.Triggers.Take(3)) + (card.Triggers.Count > 3 ? "…" : "")
            : card.Type.ToString();
    }
}
