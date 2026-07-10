using ChatGPTWrapper.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace ChatGPTWrapper.WinUI.Shell;

/// <summary>
/// Resizable, movable WinUI dialog window with persisted layout (WPF <see cref="ShellDialogWindow"/> equivalent).
/// </summary>
public class WinUiShellDialogWindow : Window
{
    private readonly Grid _root = new();
    private readonly Border _titleBar = new();
    private readonly TextBlock _titleText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _titleDragRegion = new();
    private bool _openLayoutApplied;
    private bool _designCaptured;
    private double? _designWidth;
    private double? _designHeight;
    private IntPtr _ownerHwnd;
    private TaskCompletionSource<bool?>? _dialogTcs;
    private AppWindow? _appWindow;

    protected WinUiShellDialogWindow()
    {
        MinDialogWidth = 480;
        MinDialogHeight = 400;

        _titleBar.Padding = new Thickness(12, 8, 12, 8);
        _titleBar.Child = _titleText;
        _titleDragRegion.Children.Add(_titleBar);

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_titleDragRegion, 0);
        _root.Children.Add(_titleDragRegion);

        Content = _root;
        Activated += OnActivated;
        Closed += OnClosed;

        TrySetMicaBackdrop();
        ConfigureTitleBar();
    }

    protected virtual string LayoutKey => GetType().Name;

    protected virtual bool PersistLayout => true;

    protected virtual bool ApplyDesignSizeOnOpen => true;

    protected virtual bool RestorePersistedSizeOnOpen => true;

    public double MinDialogWidth { get; set; }

    public double MinDialogHeight { get; set; }

    public void SetDialogSizeConstraints(double minWidth, double minHeight)
    {
        MinDialogWidth = minWidth;
        MinDialogHeight = minHeight;
    }

    protected FrameworkElement BodyHost { get; private set; } = null!;

    public bool? DialogResult { get; protected set; }

    protected void InitializeBody(FrameworkElement body, string title)
    {
        _titleText.Text = title;
        Title = title;
        BodyHost = body;
        Grid.SetRow(body, 1);
        _root.Children.Add(body);
        CaptureDesignSizeIfNeeded();
    }

    protected void ReapplyViewportLayout() =>
        WinUiDialogViewportLayout.Reclamp(this, MinDialogWidth, MinDialogHeight);

    public Task<bool?> ShowDialogAsync(Window? owner)
    {
        if (_dialogTcs is not null)
            throw new InvalidOperationException("Dialog is already shown.");

        _dialogTcs = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (owner is not null)
        {
            _ownerHwnd = WindowNative.GetWindowHandle(owner);
            var dialogHwnd = WindowNative.GetWindowHandle(this);
            WinUiNativeMethods.SetWindowLongPtr(dialogHwnd, WinUiNativeMethods.GwlHwndParent, _ownerHwnd);
            WinUiNativeMethods.EnableWindow(_ownerHwnd, false);
        }

        Activate();
        return _dialogTcs.Task;
    }

    public void CloseDialog(bool? result)
    {
        DialogResult = result;
        Close();
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_titleDragRegion);
    }

    private void TrySetMicaBackdrop()
    {
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Solid fallback is acceptable on unsupported builds.
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_openLayoutApplied)
            return;

        _openLayoutApplied = true;
        EnsureAppWindowHook();
        ApplyOpenViewportLayout();
    }

    private void EnsureAppWindowHook()
    {
        if (_appWindow is not null)
            return;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Changed += OnAppWindowChanged;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
            WinUiDialogViewportLayout.Reclamp(this, MinDialogWidth, MinDialogHeight);
    }

    private void ApplyOpenViewportLayout()
    {
        CaptureDesignSizeIfNeeded();
        WinUiDialogViewportLayout.ApplyOpenLayout(new WinUiDialogViewportLayout.OpenLayoutRequest
        {
            Window = this,
            LayoutKey = LayoutKey,
            DesignWidth = _designWidth,
            DesignHeight = _designHeight,
            MinWidth = MinDialogWidth,
            MinHeight = MinDialogHeight,
            ApplyDesignSize = ApplyDesignSizeOnOpen,
            RestorePersistedSize = RestorePersistedSizeOnOpen,
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_appWindow is not null)
            _appWindow.Changed -= OnAppWindowChanged;

        if (_ownerHwnd != IntPtr.Zero)
            WinUiNativeMethods.EnableWindow(_ownerHwnd, true);

        if (PersistLayout && !string.IsNullOrWhiteSpace(LayoutKey))
        {
            CaptureDesignSizeIfNeeded();
            if (WinUiDialogViewportLayout.ShouldPersistSize(this, _designWidth, _designHeight))
            {
                var size = (_appWindow ?? AppWindow.GetFromWindowId(
                    Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)))).Size;
                DialogLayoutStore.Save(LayoutKey, size.Width, size.Height);
            }
        }

        _dialogTcs?.TrySetResult(DialogResult);
        _dialogTcs = null;
    }

    private void CaptureDesignSizeIfNeeded()
    {
        if (_designCaptured)
            return;

        _designCaptured = true;

        if (_designWidth is null && _designHeight is null)
        {
            _designWidth = MinDialogWidth;
            _designHeight = MinDialogHeight;
        }
    }

    protected void SetDesignSize(double width, double height)
    {
        _designWidth = width;
        _designHeight = height;
        _designCaptured = true;
    }
}
