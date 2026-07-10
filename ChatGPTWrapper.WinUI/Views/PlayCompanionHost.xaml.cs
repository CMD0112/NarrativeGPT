using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Helpers;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class PlayCompanionHost : UserControl
{
    private WinUiPlaySessionService? _session;
    private bool _suppressTab;
    private PlayLayoutCapabilities _capabilities = PlayLayoutCapabilities.FromContentWidth(320);

    public PlayCompanionHost()
    {
        InitializeComponent();
    }

    public void Bind(WinUiPlaySessionService session)
    {
        _session = session;
        ReferencePanel.Bind(session);
        ResyncFromStore();
        session.StatusChanged += (_, _) => ResyncFromStore();
    }

    public void RestoreLastTab()
    {
        if (_session is null)
            return;

        var tabName = _session.ResolveCompanionTab();
        _suppressTab = true;
        try
        {
            for (var i = 0; i < CompanionTabs.TabItems.Count; i++)
            {
                if (CompanionTabs.TabItems[i] is TabViewItem item
                    && string.Equals(item.Tag as string, tabName, StringComparison.Ordinal))
                {
                    CompanionTabs.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _suppressTab = false;
        }
    }

    public void ApplyLayout(PlayLayoutContext context)
    {
        _capabilities = context.Capabilities;
        ReferencePanel.ApplyLayout(context);

        StateAllFieldsExpander.Visibility = context.Capabilities.ShowStateAllFields
            ? Visibility.Visible
            : Visibility.Collapsed;

        EditWorldButton.Content = context.Capabilities.UseFullFooterLabels
            ? "Edit world in settings"
            : "Edit world";

        if (_session?.CurrentBundle is { } bundle)
        {
            BindWarnings(bundle);
            BindWorldState(bundle);
        }
    }

    public void ResyncFromStore()
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        BindWarnings(bundle);
        BindWorldState(bundle);
        UpdateTabBadges(bundle);
    }

    private void BindWarnings(AdventureBundle bundle)
    {
        var showSource = _capabilities.ShowWarningSource;
        var warnings = ContinuityWarningDismissalService.FilterActive(bundle.Continuity)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WarningRowVm
            {
                Message = w.Message,
                Source = w.Source ?? "",
                SourceVisibility = showSource && !string.IsNullOrWhiteSpace(w.Source)
                    ? Visibility.Visible
                    : Visibility.Collapsed,
            })
            .ToList();

        WarningsList.ItemsSource = warnings;
        var hasWarnings = warnings.Count > 0;
        WarningsList.Visibility = hasWarnings ? Visibility.Visible : Visibility.Collapsed;
        WarningsEmptyState.Visibility = hasWarnings ? Visibility.Collapsed : Visibility.Visible;

        if (bundle.Continuity.LastCheckedAt is { } checkedAt)
        {
            WarningsLastCheckedBlock.Text = $"Last checked {FormatRelativeTime(checkedAt)}";
            WarningsLastCheckedBlock.Visibility = Visibility.Visible;
        }
        else
        {
            WarningsLastCheckedBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void BindWorldState(AdventureBundle bundle)
    {
        var rows = StateTableHelper.BuildRows(bundle);
        var summary = TruncatePreview(FindStateValue(rows, "Rolling summary"));
        var location = TruncatePreview(FindStateValue(rows, "Location", "Scene location"));
        var objectives = TruncatePreview(FindStateValue(rows, "Objectives"));

        StateSummaryPreview.Text = summary;
        StateLocationPreview.Text = location;
        StateObjectivesPreview.Text = objectives;
        StateAllFieldsList.ItemsSource = rows;

        var allUnset = summary == "(not set)" && location == "(not set)" && objectives == "(not set)";
        StateEmptyStateCard.Visibility = allUnset ? Visibility.Visible : Visibility.Collapsed;

        if (bundle.Metadata.LastPlayedAt != default)
        {
            StateLastUpdatedBlock.Text = $"Last updated {FormatRelativeTime(bundle.Metadata.LastPlayedAt)}";
            StateLastUpdatedBlock.Visibility = Visibility.Visible;
        }
        else
        {
            StateLastUpdatedBlock.Visibility = Visibility.Collapsed;
        }
    }

    private static string FindStateValue(IReadOnlyList<StateTableRow> rows, params string[] fields)
    {
        foreach (var field in fields)
        {
            var row = rows.FirstOrDefault(r => r.Field.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (row is not null && !string.IsNullOrWhiteSpace(row.Value))
                return row.Value;
        }

        return "(not set)";
    }

    private static string TruncatePreview(string value) =>
        value.Length <= 220 ? value : value[..217] + "…";

    private void UpdateTabBadges(AdventureBundle bundle)
    {
        var warningCount = ContinuityWarningDismissalService.FilterActive(bundle.Continuity).Count;
        WarningsTab.Header = warningCount > 0 ? $"Warnings ({warningCount})" : "Warnings";

        var stateCount = StateTableHelper.BuildRows(bundle).Count;
        StateTab.Header = stateCount > 0 ? $"State ({stateCount})" : "State";
    }

    private static string FormatRelativeTime(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta.TotalMinutes < 1)
            return "just now";
        if (delta.TotalHours < 1)
            return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalDays < 1)
            return $"{(int)delta.TotalHours} hr ago";
        if (delta.TotalDays < 7)
            return $"{(int)delta.TotalDays} days ago";
        return when.LocalDateTime.ToString("MMM d, yyyy");
    }

    private void CompanionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTab || CompanionTabs.SelectedItem is not TabViewItem item)
            return;

        if (item.Tag is string tag)
            _session?.SaveCompanionTab(tag);
    }

    private void WarningsList_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        WinUiListFlyoutHelper.SelectItemUnderPointer(sender, e);

    private void WarningsContextFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
            return;

        var hasSelection = WarningsList.SelectedItem is WarningRowVm;
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
            item.IsEnabled = hasSelection;
    }

    private void WarningDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle || WarningsList.SelectedItem is not WarningRowVm row)
            return;

        ContinuityWarningDismissalService.Dismiss(bundle.Continuity, row.Message);
        AdventureStore.Save(bundle);
        ResyncFromStore();
    }

    private void WarningOpenInReference_Click(object sender, RoutedEventArgs e)
    {
        _suppressTab = true;
        try
        {
            CompanionTabs.SelectedIndex = 0;
        }
        finally
        {
            _suppressTab = false;
        }

        _session?.SaveCompanionTab("Reference");
    }

    private async void EditWorldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        await WinUiDialogHostService.ShowPlaySettingsAsync(
            App.CurrentMainWindow,
            bundle.Metadata.Id,
            PlaySettingsTab.World);
    }

    private sealed class WarningRowVm
    {
        public required string Message { get; init; }

        public required string Source { get; init; }

        public Visibility SourceVisibility { get; init; }
    }
}
