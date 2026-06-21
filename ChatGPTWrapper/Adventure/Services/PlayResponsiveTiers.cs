using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Content-width breakpoints for play companion panels (after outer margin).
/// Boolean helpers delegate to <see cref="PlayLayoutCapabilities"/> — the single source of truth.
/// </summary>
public static class PlayResponsiveTiers
{
    public const double CompactMargin = 8;
    public const double NormalMargin = 12;
    public const double CompactMarginThreshold = 264;

    public const double ShellSourcesVisible = 200;
    public const double ShellBackFull = 220;
    public const double EntityDescriptionVisible = 220;
    public const double StateAllFieldsVisible = 240;
    public const double EntityRoleVisible = 260;
    public const double ShellPlaySettingsFull = 260;
    public const double NotesFullChrome = 280;
    public const double WarningsSourceVisible = 280;
    public const double ShellHeaderFullChrome = 280;
    public const double EntityPinVisible = 290;
    public const double ShellFooterFullChrome = 300;
    public const double MinComfortableContentWidth = 320;
    public const double ReferenceWideTemplate = 360;
    public const double StateWidePreview = 400;

    public static double ContentWidth(double panelWidth, double margin = NormalMargin) =>
        Math.Max(0, panelWidth - margin * 2);

    public static double MarginForPanelWidth(double panelWidth) =>
        panelWidth < CompactMarginThreshold ? CompactMargin : NormalMargin;

    public static double PanelWidthForMinContent(double minContentWidth)
    {
        if (minContentWidth <= 0)
            return 0;

        var normalPanel = minContentWidth + NormalMargin * 2;
        if (ContentWidth(normalPanel, NormalMargin) >= minContentWidth)
            return normalPanel;

        return minContentWidth + CompactMargin * 2;
    }

    public static bool MeetsContentTarget(double panelWidth, double minContentWidth)
    {
        if (minContentWidth <= 0)
            return true;

        var margin = MarginForPanelWidth(panelWidth);
        return ContentWidth(panelWidth, margin) >= minContentWidth;
    }

    public static bool ShowEntityRole(double contentWidth) =>
        Cap(contentWidth).ShowEntityRole;

    public static bool ShowEntityDescription(double contentWidth) =>
        Cap(contentWidth).ShowEntityDescription;

    public static bool ShowEntityPin(double contentWidth) =>
        Cap(contentWidth).ShowEntityPin;

    public static double EntityDescriptionMaxHeight(double contentWidth) =>
        Cap(contentWidth).EntityDescriptionMaxHeight;

    public static bool StackEntityActions(double contentWidth) =>
        Cap(contentWidth).StackEntityActions;

    public static bool ShowWarningSource(double contentWidth) =>
        Cap(contentWidth).ShowWarningSource;

    public static bool ShowStateAllFields(double contentWidth) =>
        Cap(contentWidth).ShowStateAllFields;

    public static double StateFieldColumnWidth(double contentWidth) =>
        Cap(contentWidth).StateFieldColumnWidth;

    public static bool UseWideStatePreview(double contentWidth) =>
        Cap(contentWidth).UseWideStatePreview;

    public static double ComfortablePanelWidth(double margin = NormalMargin) =>
        MinComfortableContentWidth + margin * 2;

    public static bool UseCompactEntityMoreLabel(double contentWidth) =>
        Cap(contentWidth).UseCompactEntityMore;

    public static bool UseCompactFooterMoreLabel(double contentWidth) =>
        Cap(contentWidth).UseCompactFooterMore;

    public static bool UseCompactNotesChrome(double contentWidth) =>
        Cap(contentWidth).UseCompactNotesChrome;

    private static PlayLayoutCapabilities Cap(double contentWidth) =>
        PlayLayoutCapabilities.FromContentWidth(contentWidth);
}
