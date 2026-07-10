using ChatGPTWrapper.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace ChatGPTWrapper.WinUI.Shell;

/// <summary>WinUI port of <see cref="DialogViewportLayout"/> for secondary dialog windows.</summary>
internal static class WinUiDialogViewportLayout
{
    public const double ViewportMargin = 24;
    public const double EdgeInset = 8;
    public const double SizeTolerance = 4;

    public sealed class OpenLayoutRequest
    {
        public required Window Window { get; init; }

        public required double MinWidth { get; init; }

        public required double MinHeight { get; init; }

        public string? LayoutKey { get; init; }

        public double? DesignWidth { get; init; }

        public double? DesignHeight { get; init; }

        public bool ApplyDesignSize { get; init; } = true;

        public bool RestorePersistedSize { get; init; } = true;
    }

    public static void ApplyOpenLayout(OpenLayoutRequest request)
    {
        var workArea = GetWorkArea(request.Window);
        var width = request.DesignWidth ?? request.MinWidth;
        var height = request.DesignHeight ?? request.MinHeight;

        if (request.RestorePersistedSize
            && !string.IsNullOrWhiteSpace(request.LayoutKey)
            && DialogLayoutStore.TryGet(request.LayoutKey, out var persisted)
            && persisted is not null
            && ValidatePersistedSize(persisted.Width, persisted.Height, request.MinWidth, request.MinHeight, workArea))
        {
            width = persisted.Width;
            height = persisted.Height;
        }

        (width, height) = ClampDimensions(width, height, workArea);
        width = Math.Max(width, request.MinWidth);
        height = Math.Max(height, request.MinHeight);

        CenterInWorkArea(request.Window, workArea, width, height);
        ConfigureResizable(request.Window);
    }

    public static void Reclamp(Window window, double minWidth, double minHeight)
    {
        var workArea = GetWorkArea(window);
        var appWindow = GetAppWindow(window);
        var size = appWindow.Size;
        var (width, height) = ClampDimensions(size.Width, size.Height, workArea);
        width = Math.Max(width, minWidth);
        height = Math.Max(height, minHeight);

        if (width != size.Width || height != size.Height)
            appWindow.Resize(new SizeInt32((int)Math.Round(width), (int)Math.Round(height)));

        ClampPositionToWorkArea(window, workArea);
        ConfigureResizable(window);
    }

    public static bool ShouldPersistSize(
        Window window,
        double? designWidth,
        double? designHeight)
    {
        var size = GetAppWindow(window).Size;
        return ShouldPersistDimensions(size.Width, size.Height, designWidth, designHeight);
    }

    internal static bool ShouldPersistDimensions(
        double actualWidth,
        double actualHeight,
        double? designWidth,
        double? designHeight)
    {
        var widthChanged = designWidth is null or <= 0
            || Math.Abs(actualWidth - designWidth.Value) > SizeTolerance;
        var heightChanged = designHeight is null or <= 0
            || Math.Abs(actualHeight - designHeight.Value) > SizeTolerance;

        return widthChanged || heightChanged;
    }

    internal static bool ValidatePersistedSize(
        double width,
        double height,
        double minWidth,
        double minHeight,
        RectInt32 workArea) =>
        IsPersistedSizeValid(width, height, minWidth, minHeight, workArea);

    internal static (double Width, double Height) ClampDimensions(
        double width,
        double height,
        RectInt32 workArea)
    {
        var maxW = workArea.Width - EdgeInset * 2;
        var maxH = workArea.Height - EdgeInset * 2;

        if (width > maxW)
            width = maxW;

        if (height > maxH)
            height = maxH;

        return (width, height);
    }

    private static bool IsPersistedSizeValid(
        double width,
        double height,
        double minWidth,
        double minHeight,
        RectInt32 workArea)
    {
        if (width < minWidth || height < minHeight)
            return false;

        if (width > workArea.Width - EdgeInset * 2)
            return false;

        if (height > workArea.Height - EdgeInset * 2)
            return false;

        return width > 0 && height > 0;
    }

    private static RectInt32 GetWorkArea(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        return displayArea.WorkArea;
    }

    public static WorkAreaBounds GetWorkAreaBounds(Window? owner)
    {
        if (owner is null)
            return new WorkAreaBounds(DisplayArea.Primary.WorkArea.Width, DisplayArea.Primary.WorkArea.Height);

        var area = GetWorkArea(owner);
        return new WorkAreaBounds(area.Width, area.Height);
    }

    private static void CenterInWorkArea(Window window, RectInt32 workArea, double width, double height)
    {
        var appWindow = GetAppWindow(window);
        var w = (int)Math.Round(width);
        var h = (int)Math.Round(height);
        var x = workArea.X + Math.Max(0, (workArea.Width - w) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - h) / 2);
        appWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    private static void ClampPositionToWorkArea(Window window, RectInt32 workArea)
    {
        var appWindow = GetAppWindow(window);
        var pos = appWindow.Position;
        var size = appWindow.Size;
        var edgeInset = (int)Math.Round(EdgeInset);

        var x = pos.X;
        var y = pos.Y;

        if (x + size.Width > workArea.X + workArea.Width - edgeInset)
            x = Math.Max(workArea.X + edgeInset, workArea.X + workArea.Width - size.Width - edgeInset);

        if (y + size.Height > workArea.Y + workArea.Height - edgeInset)
            y = Math.Max(workArea.Y + edgeInset, workArea.Y + workArea.Height - size.Height - edgeInset);

        if (x < workArea.X + edgeInset)
            x = workArea.X + edgeInset;

        if (y < workArea.Y + edgeInset)
            y = workArea.Y + edgeInset;

        if (x != pos.X || y != pos.Y)
            appWindow.Move(new PointInt32(x, y));
    }

    private static void ConfigureResizable(Window window)
    {
        if (GetAppWindow(window).Presenter is OverlappedPresenter presenter)
            presenter.IsResizable = true;
    }

    private static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
