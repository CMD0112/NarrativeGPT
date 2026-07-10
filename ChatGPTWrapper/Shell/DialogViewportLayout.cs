using System.Windows;

namespace ChatGPTWrapper.Shell;

internal static class DialogViewportLayout
{
    public const double ViewportMargin = 24;
    public const double EdgeInset = 8;
    public const double SizeTolerance = 4;

    public sealed class OpenLayoutRequest
    {
        public required Window Window { get; init; }

        public string? LayoutKey { get; init; }

        public double? DesignWidth { get; init; }

        public double? DesignHeight { get; init; }

        public bool ApplyDesignSize { get; init; } = true;

        public bool RestorePersistedSize { get; init; } = true;

        public bool ClampMaxToWorkArea { get; init; } = true;
    }

    public static void ApplyOpenLayout(OpenLayoutRequest request)
    {
        var window = request.Window;
        var workArea = SystemParameters.WorkArea;

        if (request.ClampMaxToWorkArea)
        {
            window.MaxWidth = Math.Max(window.MinWidth, workArea.Width - ViewportMargin);
            window.MaxHeight = Math.Max(window.MinHeight, workArea.Height - ViewportMargin);
        }

        var appliedPersisted = false;
        if (request.RestorePersistedSize
            && !string.IsNullOrWhiteSpace(request.LayoutKey)
            && DialogLayoutStore.TryGet(request.LayoutKey, out var persisted)
            && persisted is not null
            && IsPersistedSizeValid(persisted.Width, persisted.Height, window.MinWidth, window.MinHeight, workArea))
        {
            window.Width = persisted.Width;
            window.Height = persisted.Height;
            appliedPersisted = true;
        }

        if (!appliedPersisted && request.ApplyDesignSize)
        {
            if (request.DesignWidth is > 0)
                window.Width = request.DesignWidth.Value;
            if (request.DesignHeight is > 0)
                window.Height = request.DesignHeight.Value;
        }

        EnforceMinSize(window);
        ClampSizeToWorkArea(window, workArea);
        ClampPositionToWorkArea(window, workArea);
    }

    public static void Reclamp(Window window)
    {
        if (window.WindowState != WindowState.Normal)
            return;

        var workArea = SystemParameters.WorkArea;
        window.MaxWidth = Math.Max(window.MinWidth, workArea.Width - ViewportMargin);
        window.MaxHeight = Math.Max(window.MinHeight, workArea.Height - ViewportMargin);

        EnforceMinSize(window);
        ClampSizeToWorkArea(window, workArea);
        ClampPositionToWorkArea(window, workArea);
    }

    public static void ClampMaxHeightOnly(Window window)
    {
        var workArea = SystemParameters.WorkArea;
        window.MaxHeight = Math.Max(window.MinHeight, workArea.Height - ViewportMargin);
        window.MaxWidth = Math.Max(window.MinWidth, workArea.Width - ViewportMargin);
        ClampPositionToWorkArea(window, workArea);
    }

    public static bool ShouldPersistSize(
        Window window,
        double? designWidth,
        double? designHeight)
    {
        if (window.WindowState != WindowState.Normal)
            return false;

        return ShouldPersistDimensions(window.ActualWidth, window.ActualHeight, designWidth, designHeight);
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

    private static bool IsPersistedSizeValid(
        double width,
        double height,
        double minWidth,
        double minHeight,
        Rect workArea)
    {
        if (width < minWidth || height < minHeight)
            return false;

        if (width > workArea.Width - EdgeInset * 2)
            return false;

        if (height > workArea.Height - EdgeInset * 2)
            return false;

        return width > 0 && height > 0;
    }

    internal static bool ValidatePersistedSize(
        double width,
        double height,
        double minWidth,
        double minHeight,
        Rect workArea) =>
        IsPersistedSizeValid(width, height, minWidth, minHeight, workArea);

    internal static (double Width, double Height) ClampDimensions(
        double width,
        double height,
        Rect workArea)
    {
        if (width > workArea.Width - EdgeInset * 2)
            width = workArea.Width - EdgeInset * 2;

        if (height > workArea.Height - EdgeInset * 2)
            height = workArea.Height - EdgeInset * 2;

        return (width, height);
    }

    private static void EnforceMinSize(Window window)
    {
        if (window.Width < window.MinWidth)
            window.Width = window.MinWidth;
        if (window.Height < window.MinHeight)
            window.Height = window.MinHeight;
    }

    private static void ClampSizeToWorkArea(Window window, Rect workArea)
    {
        if (window.Height > workArea.Height - EdgeInset * 2)
            window.Height = workArea.Height - EdgeInset * 2;

        if (window.Width > workArea.Width - EdgeInset * 2)
            window.Width = workArea.Width - EdgeInset * 2;
    }

    public static void ClampPositionToWorkArea(Window window, Rect? workAreaOverride = null)
    {
        if (window.WindowState != WindowState.Normal)
            return;

        window.UpdateLayout();

        var area = workAreaOverride ?? SystemParameters.WorkArea;

        if (window.Top + window.ActualHeight > area.Bottom - EdgeInset)
            window.Top = Math.Max(area.Top + EdgeInset, area.Bottom - window.ActualHeight - EdgeInset);

        if (window.Top < area.Top + EdgeInset)
            window.Top = area.Top + EdgeInset;

        if (window.Left + window.ActualWidth > area.Right - EdgeInset)
            window.Left = Math.Max(area.Left + EdgeInset, area.Right - window.ActualWidth - EdgeInset);

        if (window.Left < area.Left + EdgeInset)
            window.Left = area.Left + EdgeInset;
    }
}
