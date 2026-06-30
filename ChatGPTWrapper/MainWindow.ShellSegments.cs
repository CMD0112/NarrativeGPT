using ChatGPTWrapper.Controls;
using System.Windows;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private bool _suppressSegmentEvents;

    private void InitializeShellSegments()
    {
        AppModeSegment.ItemsSource = new SegmentedItem[]
        {
            new() { Content = "Browse", Tag = AppMode.Browse },
            new() { Content = "Adventures", Tag = AppMode.Adventures },
        };
        ShellSessionModeSegment.ItemsSource = new SegmentedItem[]
        {
            new() { Content = "Play", Tag = AppMode.Play },
            new() { Content = "Design", Tag = AppMode.Design },
        };
        SyncAppModeSegmentSelection();
        SyncShellSessionModeSegmentSelection();
    }

    private void AppModeSegment_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSegmentEvents || AppModeSegment.SelectedTag is not AppMode mode)
            return;

        switch (mode)
        {
            case AppMode.Browse:
                BrowseModeButton_Click(sender, e);
                break;
            case AppMode.Adventures:
                AdventuresModeButton_Click(sender, e);
                break;
        }
    }

    private void ShellSessionModeSegment_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSegmentEvents || ShellSessionModeSegment.SelectedTag is not AppMode mode)
            return;

        switch (mode)
        {
            case AppMode.Play:
                ShellPlayModeButton_Click(sender, e);
                break;
            case AppMode.Design:
                ShellDesignModeButton_Click(sender, e);
                break;
        }
    }

    private void SyncAppModeSegmentSelection()
    {
        if (AppModeSegment is null)
            return;

        _suppressSegmentEvents = true;
        try
        {
            AppModeSegment.SelectedIndex = _appMode switch
            {
                AppMode.Browse => 0,
                AppMode.Adventures => 1,
                _ => -1,
            };
        }
        finally
        {
            _suppressSegmentEvents = false;
        }
    }

    private void SyncShellSessionModeSegmentSelection()
    {
        if (ShellSessionModeSegment is null)
            return;

        _suppressSegmentEvents = true;
        try
        {
            ShellSessionModeSegment.SelectedIndex = _appMode switch
            {
                AppMode.Play => 0,
                AppMode.Design => 1,
                _ => -1,
            };
        }
        finally
        {
            _suppressSegmentEvents = false;
        }
    }

    private void RefreshShellSessionModeSegmentItems()
    {
        if (ShellSessionModeSegment is null)
            return;

        var bundle = _activeAdventureId is { } id ? Adventure.Stores.AdventureStore.Load(id) : null;
        var canDesign = Adventure.Services.AdventureSessionModePolicy.CanSwitchToDesign(bundle);
        var switchInProgress = _adventureSessionSwitchInProgress;

        ShellSessionModeSegment.ItemsSource = new SegmentedItem[]
        {
            new() { Content = "Play", Tag = AppMode.Play, IsEnabled = !switchInProgress },
            new()
            {
                Content = "Design",
                Tag = AppMode.Design,
                IsEnabled = canDesign && !switchInProgress,
            },
        };
        SyncShellSessionModeSegmentSelection();
    }
}
