using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ChatGPTWrapper.WinUI.Shell;

/// <summary>Standard scroll-safe body host for Tier 2–4 workbench windows.</summary>
public sealed class WinUiShellDialogHostWindow : WinUiShellDialogWindow
{
    private readonly ScrollViewer _scrollViewer = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Disabled,
        VerticalScrollMode = ScrollMode.Enabled,
    };

    private readonly StackPanel _footer = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(16, 0, 16, 16),
    };

    private readonly Grid _layout = new();

    public WinUiShellDialogHostWindow(
        string title,
        UIElement body,
        string? layoutKey = null,
        double designWidth = 720,
        double designHeight = 560)
    {
        if (!string.IsNullOrWhiteSpace(layoutKey))
            LayoutKeyOverride = layoutKey;

        SetDesignSize(designWidth, designHeight);
        MinDialogWidth = Math.Min(480, designWidth);
        MinDialogHeight = Math.Min(400, designHeight);

        _scrollViewer.Content = body;

        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_scrollViewer, 0);
        Grid.SetRow(_footer, 1);
        _layout.Children.Add(_scrollViewer);
        _layout.Children.Add(_footer);

        InitializeBody(_layout, title);
        Activated += OnHostActivated;
    }

    private bool _closeGuardHooked;
    private bool _closeDialogInProgress;

    private void OnHostActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_closeGuardHooked)
            return;

        _closeGuardHooked = true;
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(windowId).Closing += OnAppWindowClosing;
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose || _confirmCloseAsync is null)
            return;

        args.Cancel = true;
        if (_closeDialogInProgress)
            return;

        _closeDialogInProgress = true;
        try
        {
            if (await _confirmCloseAsync())
            {
                _forceClose = true;
                Close();
            }
        }
        finally
        {
            _closeDialogInProgress = false;
        }
    }

    private string? LayoutKeyOverride { get; }

    protected override string LayoutKey => LayoutKeyOverride ?? base.LayoutKey;

    public void AddFooterButton(Button button) =>
        _footer.Children.Add(button);

    public void SetFooterVisible(bool visible) =>
        _footer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private Func<Task<bool>>? _confirmCloseAsync;
    private bool _forceClose;

    /// <summary>When set, window close (X or Cancel) prompts before discarding. Return true to allow close.</summary>
    public void SetCloseConfirmation(Func<Task<bool>> confirmAsync) =>
        _confirmCloseAsync = confirmAsync;

    public void ForceClose() => _forceClose = true;
}
