using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatProfileTests
{
    [Fact]
    public void Normalize_seeds_builtin_profiles_when_missing()
    {
        var settings = new UiChromeSettings();
        FormatProfileService.Normalize(settings);

        Assert.Equal(FormatBuiltInPresetCatalog.All.Count, settings.NativeSettings.FormatProfiles.Count);
        Assert.Equal(FormatProfileIds.Default, settings.NativeSettings.ActiveFormatProfileId);
        Assert.Contains(settings.NativeSettings.FormatProfiles, p => p.Id == FormatProfileIds.Compact && p.IsBuiltIn);
        Assert.Contains(settings.NativeSettings.FormatProfiles, p => p.Id == FormatProfileIds.Relaxed && p.IsBuiltIn);
        Assert.Contains(settings.NativeSettings.FormatProfiles, p => p.Id == FormatProfileIds.LongFormReading && p.IsBuiltIn);
    }

    [Fact]
    public void SettingsMatch_detects_equivalent_snapshots()
    {
        var left = ContinuousViewFormatSettings.CreateDefaults();
        var right = left.Clone();
        right.ContentMaxWidthRem = left.ContentMaxWidthRem;

        Assert.True(FormatProfileService.SettingsMatch(left, right));
        right.ContentMaxWidthRem = left.ContentMaxWidthRem + 1;
        Assert.False(FormatProfileService.SettingsMatch(left, right));
    }

    [Fact]
    public void ResolveActiveProfileId_returns_custom_when_modified()
    {
        var settings = new UiChromeSettings();
        FormatProfileService.Normalize(settings);

        var mode = settings.NativeSettings;
        var working = mode.ContinuousViewFormat.Clone();
        working.ContentMaxWidthRem = 99;

        var resolved = FormatProfileService.ResolveActiveProfileId(
            mode,
            working,
            FormatProfileIds.Default);

        Assert.Equal(FormatProfileIds.Custom, resolved);
    }

    [Fact]
    public void Ui_chrome_round_trips_format_profiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cgw-format-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var prior = AppDirectories.TestRootOverride;
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = temp;

        try
        {
            var custom = FormatProfileLibrary.CreateCustom("Wide prose", ContinuousViewFormatSettings.CreateDefaults());
            custom.Format.ContentMaxWidthRem = 88;

            var original = new UiChromeSettings
            {
                TranscriptViewMode = TranscriptViewMode.Continuous,
                ContinuousSettings =
                {
                    ActiveFormatProfileId = custom.Id,
                    AllowFormatValuesOutsideRecommendedRange = true,
                    FormatProfiles =
                    [
                        ..FormatProfileLibrary.CreateDefaultProfileList(),
                        custom,
                    ],
                    ContinuousViewFormat = custom.Format.Clone(),
                },
            };

            UiChromeStore.Save(original);
            var loaded = UiChromeStore.Load();

            Assert.True(loaded.ContinuousSettings.AllowFormatValuesOutsideRecommendedRange);
            Assert.Equal(custom.Id, loaded.ContinuousSettings.ActiveFormatProfileId);
            Assert.Equal(FormatBuiltInPresetCatalog.All.Count + 1, loaded.ContinuousSettings.FormatProfiles.Count);
            var loadedCustom = FormatProfileLibrary.Find(loaded.ContinuousSettings.FormatProfiles, custom.Id);
            Assert.NotNull(loadedCustom);
            Assert.Equal(88, loadedCustom!.Format.ContentMaxWidthRem);
        }
        finally
        {
            AppDirectories.ResetStoresForTests();
            AppDirectories.TestRootOverride = prior;
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Imported_extreme_values_round_trip_without_clamp()
    {
        var json = """
            {
              "contentMaxWidthRem": 120,
              "userFontSizeRem": 2.4,
              "composerClearanceMaxPx": 960
            }
            """;

        var imported = JsonSerializer.Deserialize<ContinuousViewFormatSettings>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.NotNull(imported);
        Assert.Equal(120, imported!.ContentMaxWidthRem);
        Assert.Equal(2.4, imported.UserFontSizeRem);
        Assert.Equal(960, imported.ComposerClearanceMaxPx);

        var clone = imported.Clone();
        Assert.Equal(imported.ContentMaxWidthRem, clone.ContentMaxWidthRem);
    }

    [Fact]
    public void Format_settings_sanity_warns_on_extreme_values()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ContentMaxWidthRem = 120;
        format.UserLineHeight = 0.4;

        var warnings = FormatSettingsSanity.GetWarnings(format);
        Assert.Contains(warnings, w => w.Contains("Content max width", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("Line height", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveActiveProfileId_preserves_explicit_custom_selection()
    {
        var settings = new UiChromeSettings();
        FormatProfileService.Normalize(settings);
        var mode = settings.NativeSettings;
        var working = ContinuousViewFormatSettings.CreateDefaults();

        var resolved = FormatProfileService.ResolveActiveProfileId(
            mode,
            working,
            FormatProfileIds.Custom);

        Assert.Equal(FormatProfileIds.Custom, resolved);
    }

    [Fact]
    public void ResolveInitialProfileId_preserves_custom_active_id()
    {
        var settings = new UiChromeSettings();
        settings.NativeSettings.ActiveFormatProfileId = FormatProfileIds.Custom;
        settings.NativeSettings.ContinuousViewFormat = ContinuousViewFormatSettings.CreateDefaults();
        FormatProfileService.Normalize(settings);

        Assert.Equal(
            FormatProfileIds.Custom,
            FormatProfileService.ResolveInitialProfileId(settings.NativeSettings));
    }

    [Fact]
    public void ResolveInitialProfileId_preserves_saved_custom_profile_id()
    {
        var custom = FormatProfileLibrary.CreateCustom("Mine", ContinuousViewFormatSettings.CreateDefaults());
        var settings = new UiChromeSettings();
        settings.NativeSettings.ActiveFormatProfileId = custom.Id;
        settings.NativeSettings.FormatProfiles = [..FormatProfileLibrary.CreateDefaultProfileList(), custom];
        settings.NativeSettings.ContinuousViewFormat = custom.Format.Clone();
        FormatProfileService.Normalize(settings);

        Assert.Equal(custom.Id, FormatProfileService.ResolveInitialProfileId(settings.NativeSettings));
    }

    [Theory]
    [MemberData(nameof(AllBuiltInProfileIds))]
    public void Built_in_profiles_round_trip(string profileId)
    {
        var profile = FormatProfileLibrary.BuiltInProfiles.First(p => p.Id == profileId);
        var clone = profile.Format.Clone();

        Assert.True(FormatProfileService.SettingsMatch(profile.Format, clone));
        Assert.False(string.IsNullOrWhiteSpace(profile.Description));
    }

    public static IEnumerable<object[]> AllBuiltInProfileIds() =>
        FormatBuiltInPresetCatalog.All.Select(d => new object[] { d.Id });

    [Fact]
    public void Built_in_profiles_cannot_be_deleted()
    {
        var profiles = FormatProfileLibrary.CreateDefaultProfileList();
        Assert.False(FormatProfileService.TryDeleteProfile(profiles, FormatProfileIds.Default, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void HasUnsavedChanges_false_for_equivalent_dialog_snapshots()
    {
        var settings = new UiChromeSettings { TranscriptViewMode = TranscriptViewMode.Continuous };
        FormatDialogChangeService.NormalizeForDialog(settings);
        var profileId = FormatProfileService.ResolveInitialProfileId(settings.ActiveModeSettings());

        Assert.False(FormatDialogChangeService.HasUnsavedChanges(
            settings,
            settings,
            profileId,
            profileId));
    }

    [Fact]
    public void HasUnsavedChanges_true_when_format_layout_changes()
    {
        var original = new UiChromeSettings { TranscriptViewMode = TranscriptViewMode.Continuous };
        FormatDialogChangeService.NormalizeForDialog(original);
        var profileId = FormatProfileService.ResolveInitialProfileId(original.ActiveModeSettings());

        var working = new UiChromeSettings
        {
            TranscriptViewMode = TranscriptViewMode.Continuous,
            ContinuousSettings = original.ContinuousSettings.Clone(),
            NativeSettings = original.NativeSettings.Clone(),
            WeaveSettings = original.WeaveSettings.Clone(),
            ActiveHighlightColorProfileId = original.ActiveHighlightColorProfileId,
            HighlightColorProfiles = original.HighlightColorProfiles.Select(p => p.Clone()).ToList(),
            HighlightColorCustomOptions = original.HighlightColorCustomOptions.Clone(),
        };
        working.ContinuousSettings.ContinuousViewFormat.ContentMaxWidthRem += 1;

        Assert.True(FormatDialogChangeService.HasUnsavedChanges(
            original,
            working,
            profileId,
            profileId));
    }
}
