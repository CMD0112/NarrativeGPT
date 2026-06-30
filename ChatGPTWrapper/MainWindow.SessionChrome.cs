using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Controls;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private void UpdateSessionStatusChips()
    {
        var inSession = _appMode is AppMode.Play or AppMode.Design && _activeAdventureId is not null;
        var sessionMenuVisibility = inSession ? Visibility.Visible : Visibility.Collapsed;
        if (ShellSessionSectionHeader is not null)
            ShellSessionSectionHeader.Visibility = sessionMenuVisibility;
        if (ShellSessionThreadsMenuItem is not null)
            ShellSessionThreadsMenuItem.Visibility = sessionMenuVisibility;
        if (ShellSessionSourcesMenuItem is not null)
            ShellSessionSourcesMenuItem.Visibility = sessionMenuVisibility;
        if (ShellSessionRenameMenuItem is not null)
            ShellSessionRenameMenuItem.Visibility = sessionMenuVisibility;
        if (ShellSessionDesignMenuItem is not null)
            ShellSessionDesignMenuItem.Visibility = sessionMenuVisibility;
        if (ShellSessionSeparator is not null)
            ShellSessionSeparator.Visibility = sessionMenuVisibility;

        if (!inSession || _activeAdventureId is not { } id)
        {
            if (ShellReviewChip is not null)
                ShellReviewChip.Visibility = Visibility.Collapsed;
            if (ShellLinkChip is not null)
                ShellLinkChip.Visibility = Visibility.Collapsed;
            return;
        }

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var pending = PendingReviewService.GetCounts(bundle).Total;
        if (ShellReviewChip is not null)
        {
            ShellReviewChip.Count = pending > 0 ? pending : null;
            ShellReviewChip.Visibility = Visibility.Visible;
            ShellReviewChip.Kind = pending > 0 ? StatusChipKind.Attention : StatusChipKind.Neutral;
        }

        var needsLink = !AdventureProjectBindingService.HasLinkedProject(bundle);
        if (ShellLinkChip is not null)
        {
            ShellLinkChip.Visibility = needsLink ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ShellReviewChip_Click(object sender, RoutedEventArgs e) =>
        _playView?.OpenProposalReviewHub();

    private void ShellLinkChip_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdventureId is { } id)
            _ = OpenSourceManagerDialogAsync(id);
    }

    private void ShellSessionThreads_Click(object sender, RoutedEventArgs e) =>
        OnPlayManageThreadsRequested(this, EventArgs.Empty);

    private void ShellSessionSources_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdventureId is { } id)
            _ = OpenSourceManagerDialogAsync(id);
    }

    private void ShellSessionRename_Click(object sender, RoutedEventArgs e)
    {
        if (_playView is null)
            return;

        _playView.RenameFromShell();
    }
}
