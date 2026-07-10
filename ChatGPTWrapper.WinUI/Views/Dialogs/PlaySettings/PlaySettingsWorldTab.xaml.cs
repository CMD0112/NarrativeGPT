using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsWorldTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;

    public PlaySettingsWorldTab()
    {
        InitializeComponent();
        ApplyCardGridLayout();
    }

    public event EventHandler? SettingsChanged;

    private void OnCardsGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyCardGridLayout();

    private void ApplyCardGridLayout()
    {
        var cards = new List<FrameworkElement> { IntroCard };
        var span = new List<bool> { true };
        if (SummaryReviewPanel.Visibility == Visibility.Visible)
        {
            cards.Add(SummaryReviewPanel);
            span.Add(true);
        }

        cards.Add(SummaryCard);
        span.Add(true);
        cards.Add(SceneCard);
        span.Add(false);
        cards.Add(AuthorsNoteCard);
        span.Add(false);

        PlaySettingsCardGridLayout.Apply(CardsGrid, cards, span, ActualWidth);
    }

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        SummaryBox.Text = context.Bundle.Summary.RollingSummary;
        LocationBox.Text = context.Bundle.State.CurrentLocation;
        ObjectivesBox.Text = context.Bundle.State.OpenObjectives;
        AuthorsNoteBox.Text = context.Bundle.Scenario.AuthorsNote;

        var pending = SummaryReviewService.IsPending(context.Bundle.Summary);
        SummaryReviewPanel.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        ProposedSummaryBox.Text = pending ? context.Bundle.Summary.ProposedSummary ?? "" : "";
        ApplyCardGridLayout();
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        context.Bundle.Summary.RollingSummary = SummaryBox.Text;
        context.Bundle.State.CurrentLocation = LocationBox.Text;
        context.Bundle.State.OpenObjectives = ObjectivesBox.Text;
        context.Bundle.Scenario.AuthorsNote = AuthorsNoteBox.Text;
    }

    private async void RefreshSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.RefreshSummaryAsync is { } refresh)
            await refresh();
        if (_ctx is not null)
            Bind(_ctx);
    }

    private void AcceptSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SummaryReviewService.AcceptProposal(_ctx.Bundle, ProposedSummaryBox.Text);
        AdventureStore.Save(_ctx.Bundle, AdventureSaveScope.Summary);
        Bind(_ctx);
        OnChanged(sender, e);
    }

    private void DismissSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SummaryReviewService.DismissProposal(_ctx.Bundle);
        AdventureStore.Save(_ctx.Bundle, AdventureSaveScope.Summary);
        Bind(_ctx);
        OnChanged(sender, e);
    }

    private void ReviewAll_Click(object sender, RoutedEventArgs e) =>
        _ctx?.Host?.OpenProposalReviewHub?.Invoke(ProposalReviewCategory.Summary);

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.NotifySettingsChanged();
    }
}
