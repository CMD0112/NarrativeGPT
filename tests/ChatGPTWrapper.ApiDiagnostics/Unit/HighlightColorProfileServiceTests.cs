using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class HighlightColorProfileServiceTests
{
    [Fact]
    public void CreateUserProfile_adds_non_built_in_profile()
    {
        var settings = new UiChromeSettings();
        HighlightColorProfileService.Normalize(settings);

        var profile = HighlightColorProfileService.CreateUserProfile(
            settings,
            "My palette",
            HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.VividStage));

        Assert.False(profile.IsBuiltIn);
        Assert.Contains(settings.HighlightColorProfiles, p => p.Id == profile.Id);
    }

    [Fact]
    public void DeleteProfile_removes_user_profile_only()
    {
        var settings = new UiChromeSettings();
        HighlightColorProfileService.Normalize(settings);
        var profile = HighlightColorProfileService.CreateUserProfile(
            settings,
            "Temp",
            new HighlightColorAssignmentOptions());

        Assert.True(HighlightColorProfileService.DeleteProfile(settings, profile.Id));
        Assert.DoesNotContain(settings.HighlightColorProfiles, p => p.Id == profile.Id);
        Assert.False(HighlightColorProfileService.DeleteProfile(
            settings,
            HighlightColorProfileIds.ThemeHarmony));
    }

    [Fact]
    public void MigrateLegacyCustomBucket_creates_user_profile_when_custom_differs()
    {
        var settings = new UiChromeSettings
        {
            ActiveHighlightColorProfileId = HighlightColorProfileIds.Custom,
            HighlightColorCustomOptions = new HighlightColorAssignmentOptions
            {
                AssignmentStrategy = HighlightAssignmentStrategy.StableHash,
                GeneratedColorCount = 20,
            },
        };

        HighlightColorProfileService.Normalize(settings);

        var migrated = settings.HighlightColorProfiles.FirstOrDefault(p => !p.IsBuiltIn);
        Assert.NotNull(migrated);
        Assert.Equal("Custom", migrated!.Name);
        Assert.Equal(HighlightAssignmentStrategy.StableHash, migrated.Options.AssignmentStrategy);
    }

    [Fact]
    public void ResolveActiveProfileId_returns_custom_when_options_diverge()
    {
        var settings = new UiChromeSettings();
        HighlightColorProfileService.Normalize(settings);
        var working = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        working.GeneratedColorCount = 99;

        var id = HighlightColorProfileService.ResolveActiveProfileId(
            settings,
            working,
            HighlightColorProfileIds.ThemeHarmony);

        Assert.Equal(HighlightColorProfileIds.Custom, id);
    }
}
