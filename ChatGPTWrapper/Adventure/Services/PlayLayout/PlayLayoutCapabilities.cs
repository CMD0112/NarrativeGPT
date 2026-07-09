namespace ChatGPTWrapper.Adventure.Services.PlayLayout;

/// <summary>
/// Derived feature flags for a single layout context. All responsive UI decisions
/// should read from here instead of comparing raw content widths in views.
/// </summary>
public sealed record PlayLayoutCapabilities
{
    public bool ShowSourcesButton { get; init; }
    public bool ShowFullBackLabel { get; init; }
    public bool ShowFullPlaySettingsLabel { get; init; }
    public bool ShowIntermediatePlaySettingsLabel { get; init; }
    /// <summary>Retired — unified session chrome replaced header flyouts (CMD-421).</summary>
    [Obsolete("Unified session chrome retired flyout menus.")]
    public bool UseShellHeaderFlyouts { get; init; }
    public bool UseUnifiedSessionChrome { get; init; } = true;
    public bool UseFullFooterLabels { get; init; }
    public bool UseCompactFooterMore { get; init; }
    public bool UseCompactEntityMore { get; init; }
    public bool UseCompactSessionPadding { get; init; }
    public bool UseEntityCompactTemplate { get; init; }
    public bool UseEntityWideTemplate { get; init; }
    public bool ShowEntityRole { get; init; }
    public bool ShowEntityPin { get; init; }
    public bool ShowEntityDescription { get; init; }
    public bool StackEntityActions { get; init; }
    public double EntityDescriptionMaxHeight { get; init; }
    public bool ShowWarningSource { get; init; }
    public bool ShowStateAllFields { get; init; }
    public bool UseWideStatePreview { get; init; }
    public double StateFieldColumnWidth { get; init; }
    public bool UseCompactNotesChrome { get; init; }

    public static PlayLayoutCapabilities FromContentWidth(double contentWidth)
    {
        var w = contentWidth;
        return new PlayLayoutCapabilities
        {
            ShowSourcesButton = w >= PlayResponsiveTiers.ShellSourcesVisible,
            ShowFullBackLabel = w >= PlayResponsiveTiers.ShellBackFull,
            ShowIntermediatePlaySettingsLabel = w >= PlayResponsiveTiers.ShellBackFull,
            ShowFullPlaySettingsLabel = w >= PlayResponsiveTiers.ShellPlaySettingsFull,
            UseUnifiedSessionChrome = true,
            UseFullFooterLabels = w >= PlayResponsiveTiers.ShellHeaderFullChrome,
            UseCompactFooterMore = w < PlayResponsiveTiers.ShellFooterFullChrome,
            UseCompactEntityMore = w < PlayResponsiveTiers.ShellFooterFullChrome,
            UseCompactSessionPadding = w < PlayResponsiveTiers.StateAllFieldsVisible,
            UseEntityCompactTemplate = w < PlayResponsiveTiers.ShellBackFull,
            UseEntityWideTemplate = w >= PlayResponsiveTiers.ReferenceWideTemplate,
            ShowEntityRole = w >= PlayResponsiveTiers.EntityRoleVisible,
            ShowEntityPin = w >= PlayResponsiveTiers.EntityPinVisible,
            ShowEntityDescription = w >= PlayResponsiveTiers.EntityDescriptionVisible,
            StackEntityActions = w < PlayResponsiveTiers.ShellBackFull,
            EntityDescriptionMaxHeight = w >= PlayResponsiveTiers.ReferenceWideTemplate ? 64 : 40,
            ShowWarningSource = w >= PlayResponsiveTiers.WarningsSourceVisible,
            ShowStateAllFields = w >= PlayResponsiveTiers.StateAllFieldsVisible,
            UseWideStatePreview = w >= PlayResponsiveTiers.StateWidePreview,
            StateFieldColumnWidth = w < PlayResponsiveTiers.EntityRoleVisible ? 96 : 140,
            UseCompactNotesChrome = w < PlayResponsiveTiers.NotesFullChrome,
        };
    }
}
