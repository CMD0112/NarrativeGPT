namespace ChatGPTWrapper.Adventure.Services.PlayLayout;

public sealed class PlayLayoutContext
{
    private PlayLayoutContext(
        string side,
        double panelWidth,
        double margin,
        double contentWidth,
        PlayLayoutTier tier,
        PlayLayoutCapabilities capabilities)
    {
        Side = side;
        PanelWidth = panelWidth;
        Margin = margin;
        ContentWidth = contentWidth;
        Tier = tier;
        Capabilities = capabilities;
    }

    public string Side { get; }

    public double PanelWidth { get; }

    public double Margin { get; }

    public double ContentWidth { get; }

    public PlayLayoutTier Tier { get; }

    public PlayLayoutCapabilities Capabilities { get; }

    public bool IsUsable => PanelWidth > 0;

    public bool AtLeast(PlayLayoutTier tier) => Tier >= tier;

    public static PlayLayoutContext Empty(string side) =>
        FromPanel(side, 0);

    public static PlayLayoutContext FromPanel(string side, double panelWidth)
    {
        if (panelWidth <= 0)
        {
            return new PlayLayoutContext(
                side,
                0,
                PlayResponsiveTiers.NormalMargin,
                0,
                PlayLayoutTier.Compact,
                PlayLayoutCapabilities.FromContentWidth(0));
        }

        var margin = side == PlayPanelSide.Right
            ? PlayResponsiveTiers.CompactMargin
            : PlayResponsiveTiers.MarginForPanelWidth(panelWidth);
        var contentWidth = PlayResponsiveTiers.ContentWidth(panelWidth, margin);
        var tier = ResolveTier(contentWidth);
        return new PlayLayoutContext(
            side,
            panelWidth,
            margin,
            contentWidth,
            tier,
            PlayLayoutCapabilities.FromContentWidth(contentWidth));
    }

    public static PlayLayoutTier ResolveTier(double contentWidth) =>
        contentWidth switch
        {
            < PlayResponsiveTiers.ShellBackFull => PlayLayoutTier.Compact,
            < PlayResponsiveTiers.StateAllFieldsVisible => PlayLayoutTier.Cozy,
            < PlayResponsiveTiers.ShellHeaderFullChrome => PlayLayoutTier.Standard,
            < PlayResponsiveTiers.MinComfortableContentWidth => PlayLayoutTier.Comfortable,
            < PlayResponsiveTiers.StateWidePreview => PlayLayoutTier.Wide,
            _ => PlayLayoutTier.ExtraWide,
        };
}

public sealed record PlayLayoutSnapshot(
    PlayLayoutContext Shell,
    PlayLayoutContext Companion);
