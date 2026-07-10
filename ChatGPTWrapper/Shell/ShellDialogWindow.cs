using System.Windows;

namespace ChatGPTWrapper.Shell;

public class ShellDialogWindow : Window
{
    private bool _openLayoutApplied;
    private bool _designCaptured;
    private double? _designWidth;
    private double? _designHeight;

    protected ShellDialogWindow()
    {
        Loaded += OnShellLoaded;
        ContentRendered += OnShellContentRendered;
        Closing += OnShellClosing;
    }

    protected virtual string LayoutKey => GetType().Name;

    protected virtual bool PersistLayout => true;

    protected virtual bool ApplyDesignSizeOnOpen => true;

    protected virtual bool RestorePersistedSizeOnOpen => true;

    protected void ReapplyViewportLayout() =>
        DialogViewportLayout.Reclamp(this);

    protected void ApplyOpenViewportLayout()
    {
        CaptureDesignSizeIfNeeded();
        DialogViewportLayout.ApplyOpenLayout(new DialogViewportLayout.OpenLayoutRequest
        {
            Window = this,
            LayoutKey = LayoutKey,
            DesignWidth = _designWidth,
            DesignHeight = _designHeight,
            ApplyDesignSize = ApplyDesignSizeOnOpen,
            RestorePersistedSize = RestorePersistedSizeOnOpen,
        });
    }

    private void OnShellLoaded(object sender, RoutedEventArgs e) =>
        CaptureDesignSizeIfNeeded();

    private void OnShellContentRendered(object? sender, EventArgs e)
    {
        if (_openLayoutApplied)
            return;

        _openLayoutApplied = true;
        ApplyOpenViewportLayout();
    }

    private void OnShellClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!PersistLayout || string.IsNullOrWhiteSpace(LayoutKey))
            return;

        CaptureDesignSizeIfNeeded();

        if (!DialogViewportLayout.ShouldPersistSize(this, _designWidth, _designHeight))
            return;

        DialogLayoutStore.Save(LayoutKey, ActualWidth, ActualHeight);
    }

    private void CaptureDesignSizeIfNeeded()
    {
        if (_designCaptured)
            return;

        _designCaptured = true;

        if (!double.IsNaN(Width) && Width > 0)
            _designWidth = Width;
        if (!double.IsNaN(Height) && Height > 0)
            _designHeight = Height;
    }
}
