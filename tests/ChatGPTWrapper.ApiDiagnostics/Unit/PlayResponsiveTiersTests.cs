using ChatGPTWrapper.Adventure.Models;

using ChatGPTWrapper.Adventure.Services;



namespace ChatGPTWrapper.ApiDiagnostics.Unit;



public sealed class PlayResponsiveTiersTests

{

    [Theory]

    [InlineData(200, false)]

    [InlineData(260, true)]

    public void ShowEntityRole_respects_content_width(double contentWidth, bool expected) =>

        Assert.Equal(expected, PlayResponsiveTiers.ShowEntityRole(contentWidth));



    [Theory]

    [InlineData("writer", 384, 424)]

    [InlineData("gm", 440, 328)]

    [InlineData("minimal", 344, 320)]

    public void OptimalWidthCalculator_uses_requirement_aware_targets(string presetId, double left, double right)

    {

        var settings = new AdventureSettings();

        PlayPanelLayoutService.ApplyPreset(settings, presetId);



        var optimal = PlayPanelOptimalWidthCalculator.Resolve(settings, 640, 480);



        Assert.Equal(left, optimal.LeftWidth);

        Assert.Equal(right, optimal.RightWidth);

    }



    [Fact]

    public void OptimalWidthCalculator_custom_layout_counts_visible_tabs()

    {

        var settings = new AdventureSettings

        {

            PlayTabPlacement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

            {

                ["Reference"] = PlayPanelSide.Left,

                ["Warnings"] = PlayPanelSide.Left,

                ["State"] = PlayPanelSide.Right,

                ["Notes"] = PlayPanelSide.Right,

            },

        };



        var optimal = PlayPanelOptimalWidthCalculator.Resolve(settings, 640, 480);



        Assert.Equal(384, optimal.LeftWidth);

        Assert.Equal(424, optimal.RightWidth);

    }



    [Fact]

    public void OptimalWidthCalculator_clamps_to_available_window_width()

    {

        var settings = new AdventureSettings();

        PlayPanelLayoutService.ApplyPreset(settings, "writer");



        var optimal = PlayPanelOptimalWidthCalculator.Resolve(settings, 200, 480);



        Assert.Equal(200, optimal.LeftWidth);

    }



    [Fact]

    public void ComfortablePanelWidth_targets_320px_content()

    {

        Assert.Equal(344, PlayResponsiveTiers.ComfortablePanelWidth());

        Assert.Equal(320, PlayResponsiveTiers.ContentWidth(344));

    }



    [Fact]

    public void PanelWidthForMinContent_accounts_for_margin_tier()

    {

        Assert.Equal(344, PlayResponsiveTiers.PanelWidthForMinContent(320));

        Assert.Equal(384, PlayResponsiveTiers.PanelWidthForMinContent(360));

        Assert.Equal(424, PlayResponsiveTiers.PanelWidthForMinContent(400));

    }



    [Theory]

    [InlineData("writer")]

    [InlineData("gm")]

    [InlineData("minimal")]

    public void Resolved_optimal_left_width_meets_enhanced_requirements(string presetId)

    {

        var settings = new AdventureSettings();

        PlayPanelLayoutService.ApplyPreset(settings, presetId);



        var optimal = PlayPanelOptimalWidthCalculator.Resolve(settings, 640, 480);

        var fit = PlayPanelOptimalWidthCalculator.ValidateLeft(settings, optimal.LeftWidth);



        Assert.True(fit.MeetsEnhanced, DescribeUnmet(fit));

    }



    [Theory]

    [InlineData("writer")]

    [InlineData("gm")]

    [InlineData("minimal")]

    public void Resolved_optimal_right_width_meets_enhanced_requirements(string presetId)

    {

        var settings = new AdventureSettings();

        PlayPanelLayoutService.ApplyPreset(settings, presetId);



        var optimal = PlayPanelOptimalWidthCalculator.Resolve(settings, 640, 480);

        var fit = PlayPanelOptimalWidthCalculator.ValidateRight(settings, optimal.RightWidth);



        Assert.True(fit.MeetsEnhanced, DescribeUnmet(fit));

    }



    [Fact]

    public void Writer_preset_left_360px_fails_wide_reference_requirement()

    {

        var settings = new AdventureSettings();

        PlayPanelLayoutService.ApplyPreset(settings, "writer");



        var fit = PlayPanelOptimalWidthCalculator.ValidateLeft(settings, 360);



        Assert.False(fit.MeetsEnhanced);

        Assert.Contains(fit.UnmetEnhanced, r => r.Id == "reference.wide");

    }



    [Fact]

    public void Writer_preset_left_384px_passes_wide_reference_requirement()

    {

        var settings = new AdventureSettings();

        PlayPanelLayoutService.ApplyPreset(settings, "writer");



        var fit = PlayPanelOptimalWidthCalculator.ValidateLeft(settings, 384);



        Assert.True(fit.MeetsEnhanced);

    }



    [Fact]

    public void RequiredContentWidth_writer_left_targets_reference_wide_template()

    {

        var settings = new AdventureSettings();

        PlayPanelLayoutService.ApplyPreset(settings, "writer");



        Assert.Equal(360, PlayPanelWidthRequirements.RequiredContentWidth(settings, PlayPanelSide.Left, PlayPanelWidthTier.Enhanced));

    }



    private static string DescribeUnmet(PlayPanelWidthFit fit)

    {

        if (fit.MeetsEnhanced)

            return string.Empty;



        return string.Join(

            ", ",

            fit.UnmetEnhanced.Select(r => $"{r.Id} needs {r.MinContentWidth}px (have {fit.ContentWidth:0.#})"));

    }

}


