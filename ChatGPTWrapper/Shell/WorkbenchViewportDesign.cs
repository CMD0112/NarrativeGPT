namespace ChatGPTWrapper.Shell;

/// <summary>
/// Resolves intelligent open sizes for Tier 2–4 workbench windows from monitor work area.
/// Persisted user sizes (via <see cref="DialogLayoutStore"/>) take precedence at open.
/// </summary>
internal enum WorkbenchTier
{
    T2Form,
    T3Hub,
    T4Session,
}

/// <summary>Display-size bucket used to tune in-workbench layout constants.</summary>
internal enum WorkbenchViewportClass
{
    Compact,
    Standard,
    Spacious,
}

internal readonly record struct WorkAreaBounds(int Width, int Height);

internal readonly record struct WorkbenchViewportMetrics(
    double DesignWidth,
    double DesignHeight,
    double MinWidth,
    double MinHeight,
    WorkbenchViewportClass ViewportClass,
    WorkAreaBounds WorkArea)
{
    public double WorkAreaWidth => WorkArea.Width;
    public double WorkAreaHeight => WorkArea.Height;
}

internal static class WorkbenchViewportDesign
{
    public const double CompactWorkAreaWidth = 1280;
    public const double CompactWorkAreaHeight = 800;
    public const double SpaciousWorkAreaWidth = 1920;

    public static WorkbenchViewportMetrics Resolve(WorkbenchTier tier, WorkAreaBounds workArea) =>
        tier switch
        {
            WorkbenchTier.T2Form => ResolveT2Form(workArea),
            WorkbenchTier.T3Hub => ResolveT3Hub(workArea),
            WorkbenchTier.T4Session => ResolveT4Session(workArea),
            _ => ResolveT3Hub(workArea),
        };

    public static WorkbenchViewportMetrics ResolveT4Session(WorkAreaBounds workArea)
    {
        var viewportClass = Classify(workArea);
        var (minW, minH, maxW, maxH, widthRatio, heightRatio) = viewportClass switch
        {
            WorkbenchViewportClass.Compact => (880, 640, 1040, 820, 0.92, 0.90),
            WorkbenchViewportClass.Spacious => (1100, 780, 1440, 980, 0.72, 0.82),
            _ => (1000, 720, 1240, 900, 0.82, 0.86),
        };

        var availableW = workArea.Width - DialogViewportLayout.EdgeInset * 2;
        var availableH = workArea.Height - DialogViewportLayout.EdgeInset * 2;

        return new WorkbenchViewportMetrics(
            Clamp(availableW * widthRatio, minW, maxW),
            Clamp(availableH * heightRatio, minH, maxH),
            minW,
            minH,
            viewportClass,
            workArea);
    }

    public static WorkbenchViewportMetrics ResolveT3Hub(WorkAreaBounds workArea)
    {
        var viewportClass = Classify(workArea);
        var (minW, minH, maxW, maxH, widthRatio, heightRatio) = viewportClass switch
        {
            WorkbenchViewportClass.Compact => (720, 520, 960, 720, 0.90, 0.88),
            WorkbenchViewportClass.Spacious => (900, 640, 1200, 900, 0.68, 0.78),
            _ => (800, 560, 1080, 820, 0.78, 0.84),
        };

        var availableW = workArea.Width - DialogViewportLayout.EdgeInset * 2;
        var availableH = workArea.Height - DialogViewportLayout.EdgeInset * 2;

        return new WorkbenchViewportMetrics(
            Clamp(availableW * widthRatio, minW, maxW),
            Clamp(availableH * heightRatio, minH, maxH),
            minW,
            minH,
            viewportClass,
            workArea);
    }

    public static WorkbenchViewportMetrics ResolveT2Form(WorkAreaBounds workArea)
    {
        var viewportClass = Classify(workArea);
        var (minW, minH, maxW, maxH, widthRatio, heightRatio) = viewportClass switch
        {
            WorkbenchViewportClass.Compact => (480, 400, 640, 560, 0.88, 0.86),
            WorkbenchViewportClass.Spacious => (560, 440, 800, 680, 0.55, 0.70),
            _ => (520, 420, 720, 620, 0.72, 0.80),
        };

        var availableW = workArea.Width - DialogViewportLayout.EdgeInset * 2;
        var availableH = workArea.Height - DialogViewportLayout.EdgeInset * 2;

        return new WorkbenchViewportMetrics(
            Clamp(availableW * widthRatio, minW, maxW),
            Clamp(availableH * heightRatio, minH, maxH),
            minW,
            minH,
            viewportClass,
            workArea);
    }

    public static WorkbenchViewportClass Classify(WorkAreaBounds workArea)
    {
        if (workArea.Width < CompactWorkAreaWidth || workArea.Height < CompactWorkAreaHeight)
            return WorkbenchViewportClass.Compact;

        if (workArea.Width >= SpaciousWorkAreaWidth)
            return WorkbenchViewportClass.Spacious;

        return WorkbenchViewportClass.Standard;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, Math.Round(value)));
}
