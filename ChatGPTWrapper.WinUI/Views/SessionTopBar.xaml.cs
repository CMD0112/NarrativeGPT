using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUI.Controls;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class SessionTopBar : UserControl
{
    private ShellNavigationService? _navigation;
    private bool _suppressSegmentEvents;

    public SessionTopBar()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;

    public event EventHandler? ReviewRequested;

    public event EventHandler? LinkRequested;

    public event EventHandler? ThreadsRequested;

    public event EventHandler? PlaySettingsRequested;

    public void Bind(ShellNavigationService navigation)
    {
        _navigation = navigation;
        _navigation.SessionChanged += (_, _) => SyncFromService();
        _navigation.AppModeChanged += (_, _) => SyncFromService();
        _navigation.FocusChatChanged += (_, args) => UpdateFocusButton(args.FocusChat);
        InitializeSessionSegment();
        SyncFromService();
    }

    public void ApplyStatus(ShellStatusSnapshot snapshot)
    {
        ReviewChip.Visibility = snapshot.ReviewCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewChip.Count = snapshot.ReviewCount > 0 ? snapshot.ReviewCount : null;

        LinkChip.Visibility = snapshot.NeedsLink ? Visibility.Visible : Visibility.Collapsed;
        JobChip.Visibility = snapshot.JobActive ? Visibility.Visible : Visibility.Collapsed;

        BridgeChip.Label = snapshot.BridgeHealthy ? "Bridge" : "Bridge!";
        BridgeChip.Kind = snapshot.BridgeHealthy ? StatusChipKind.Neutral : StatusChipKind.Attention;
    }

    public void RefreshVisuals() => SessionModeSegment.RefreshVisuals();

    private void InitializeSessionSegment()
    {
        SessionModeSegment.ItemsSource = new List<object>
        {
            new SegmentedItemModel { Content = "Play", Tag = AppMode.Play },
            new SegmentedItemModel { Content = "Design", Tag = AppMode.Design },
        };
    }

    private void SyncFromService()
    {
        if (_navigation is null)
            return;

        SessionTitle.Text = string.IsNullOrWhiteSpace(_navigation.SessionTitle)
            ? "Adventure session"
            : _navigation.SessionTitle;

        _suppressSegmentEvents = true;
        try
        {
            SessionModeSegment.SelectedIndex = _navigation.Mode == AppMode.Design ? 1 : 0;
        }
        finally
        {
            _suppressSegmentEvents = false;
        }

        UpdateFocusButton(_navigation.FocusChat);
    }

    private void UpdateFocusButton(bool focused) =>
        FocusChatButton.Content = focused ? "Show companion" : "Focus chat";

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void SessionModeSegment_SelectionChanged(object sender, EventArgs e)
    {
        if (_suppressSegmentEvents || _navigation is null)
            return;

        var mode = SessionModeSegment.SelectedTag is AppMode tag ? tag : AppMode.Play;
        _navigation.SetMode(mode, _navigation.ActiveAdventureId, _navigation.SessionTitle);
    }

    private void FocusChatButton_Click(object sender, RoutedEventArgs e) =>
        _navigation?.SetFocusChat(!_navigation.FocusChat);

    private void ReviewChip_Click(object sender, RoutedEventArgs e) =>
        ReviewRequested?.Invoke(this, EventArgs.Empty);

    private void LinkChip_Click(object sender, RoutedEventArgs e) =>
        LinkRequested?.Invoke(this, EventArgs.Empty);

    private async void JobChip_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation?.ActiveAdventureId is { } id)
            await WinUiDialogHostService.ShowThreadManagerAsync(App.CurrentMainWindow, id);
    }

    private void BridgeChip_Click(object sender, RoutedEventArgs e)
    {
        if (_navigation?.ActiveAdventureId is { } id)
            _ = WinUiDialogHostService.ShowThreadManagerAsync(App.CurrentMainWindow, id);
    }

    private void SessionOverflowButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        var threadsItem = new MenuFlyoutItem { Text = "Threads…" };
        threadsItem.Click += (_, _) => ThreadsRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(threadsItem);

        var sourcesItem = new MenuFlyoutItem { Text = "Sources…" };
        sourcesItem.Click += (_, _) => LinkRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(sourcesItem);

        var settingsItem = new MenuFlyoutItem { Text = "Play settings…" };
        settingsItem.Click += (_, _) => PlaySettingsRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(settingsItem);

        flyout.ShowAt(SessionOverflowButton);
    }
}
