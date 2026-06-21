using System.Windows;
using System.Windows.Media;

namespace ChatGPTWrapper;

internal static class UiBrushes
{
    public static Brush Success(FrameworkElement element) =>
        (Brush)element.FindResource("SuccessBrush");

    public static Brush Warning(FrameworkElement element) =>
        (Brush)element.FindResource("WarningBrush");

    public static Brush Error(FrameworkElement element) =>
        (Brush)element.FindResource("ErrorBrush");

    public static Brush Muted(FrameworkElement element) =>
        (Brush)element.FindResource("TextMutedBrush");
}
