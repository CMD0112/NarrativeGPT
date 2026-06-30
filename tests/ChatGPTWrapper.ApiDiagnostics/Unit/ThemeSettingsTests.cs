using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ChatGPTWrapper;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ThemeSettingsTests
{
    [Fact]
    public void Default_dark_tokens_match_shipped_palette()
    {
        var tokens = ThemeTokenCatalog.CreateDefaultDarkTokens();
        Assert.Equal("#161618", tokens["BgBase"]);
        Assert.Equal("#5B9FD4", tokens["AccentPrimary"]);
        Assert.Equal("#EDEDF0", tokens["TextPrimary"]);
    }

    [Fact]
    public void Catalog_covers_wrapper_overrides_css_variables()
    {
        var css = WrapperAssetTestHelpers.ReadAsset("wrapper-overrides.css");
        var vars = ExtractCssVariables(css);
        var catalogVars = ThemeTokenCatalog.ByCssVariable.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spacingVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--cgw-radius" };

        foreach (var variable in vars)
        {
            if (spacingVars.Contains(variable))
                continue;

            Assert.True(catalogVars.Contains(variable), $"Missing catalog entry for {variable}");
        }
    }

    [Fact]
    public void Ui_chrome_round_trips_without_theme_block()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cgw-theme-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var prior = AppDirectories.TestRootOverride;
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = temp;

        try
        {
            var original = new UiChromeSettings
            {
                ContinuousViewEnabled = true,
                ChromePreferencesRevision = 2,
            };
            UiChromeStore.Save(original);

            var loaded = UiChromeStore.Load();
            Assert.True(loaded.ContinuousViewEnabled);
            Assert.Equal(2, loaded.ChromePreferencesRevision);
            Assert.Equal(ThemePresetIds.DefaultDark, loaded.Theme.ActivePresetId);
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
    public void Ui_chrome_round_trips_theme_block()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cgw-theme-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var prior = AppDirectories.TestRootOverride;
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = temp;

        try
        {
            var original = new UiChromeSettings
            {
                ThemeRevision = 4,
                Theme = new ThemeSettings
                {
                    ActivePresetId = ThemePresetIds.HighContrast,
                    CustomOverrides = new Dictionary<string, string>
                    {
                        ["AccentPrimary"] = "#ABCDEF",
                    },
                    FontSizeBody = 14,
                },
            };
            UiChromeStore.Save(original);

            var loaded = UiChromeStore.Load();
            Assert.Equal(4, loaded.ThemeRevision);
            Assert.Equal(ThemePresetIds.HighContrast, loaded.Theme.ActivePresetId);
            Assert.Equal("#ABCDEF", loaded.Theme.CustomOverrides["AccentPrimary"]);
            Assert.Equal(14, loaded.Theme.FontSizeBody);
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
    public void ResolveEffectiveTheme_merges_preset_and_overrides()
    {
        var settings = new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.DefaultDark,
            CustomOverrides = new Dictionary<string, string>
            {
                ["AccentPrimary"] = "#7AB8E8",
            },
        };

        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
        Assert.Equal("#7AB8E8", resolved.GetHex("AccentPrimary"));
        Assert.True(ThemeContrast.IsReadable(resolved.GetHex("AccentPrimary"), resolved.GetHex("BgBase")));
        Assert.Equal("#161618", resolved.GetHex("BgBase"));
    }

    [Fact]
    public void BuildCssVariableBlock_emits_root_block()
    {
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var css = ThemeApplicationService.BuildCssVariableBlock(resolved);

        Assert.Contains(":root {", css);
        Assert.Contains("--cgw-bg-base: #161618;", css);
        Assert.Contains($"--cgw-accent: {resolved.GetHex("AccentPrimary")};", css);
        Assert.True(ThemeContrast.IsReadable(resolved.GetHex("AccentPrimary"), resolved.GetHex("BgBase")));
        Assert.Contains("--cgw-radius:", css);
    }

    [Fact]
    public void Preset_library_includes_all_shipped_presets()
    {
        var ids = ThemePresetLibrary.Presets.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in ThemePresetIds.AllBuiltIn)
            Assert.Contains(expected, ids);

        Assert.Equal(ThemePresetIds.AllBuiltIn.Count, ids.Count);
        Assert.True(ids.Count >= 25, "Expected a comprehensive preset catalog (25+).");
    }

    [Fact]
    public void Each_preset_has_navigation_category()
    {
        foreach (var preset in ThemePresetLibrary.Presets)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Category));
            Assert.Contains(preset.Category, ThemePresetCategories.All);
            Assert.Equal(
                ThemePresetNavigation.GetCategory(preset.Id),
                preset.Category);
        }
    }

    [Fact]
    public void Preset_navigation_covers_all_built_in_ids()
    {
        foreach (var id in ThemePresetIds.AllBuiltIn)
            Assert.False(string.IsNullOrWhiteSpace(ThemePresetNavigation.GetCategory(id)));
    }

    [Fact]
    public void Each_preset_has_complete_non_derived_token_coverage()
    {
        var requiredKeys = ThemeTokenCatalog.All
            .Where(t => !t.IsDerived)
            .Select(t => t.TokenKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in ThemePresetLibrary.Presets)
        {
            foreach (var key in requiredKeys)
            {
                Assert.True(
                    preset.Tokens.ContainsKey(key),
                    $"Preset '{preset.Id}' is missing token '{key}'.");
            }
        }
    }

    [Fact]
    public void Theme_token_display_provides_human_labels()
    {
        Assert.Equal("Elevated surface", ThemeTokenDisplay.GetLabel("BgElevated"));
        Assert.False(string.IsNullOrWhiteSpace(ThemeTokenDisplay.GetDescription("AccentPrimary")));
        Assert.Contains("Accent", ThemeTokenDisplay.GetSearchText(ThemeTokenCatalog.ByTokenKey["AccentPrimary"]));
    }

    [Fact]
    public void ValidateUserTokens_flags_low_contrast_custom_override()
    {
        var settings = new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.DefaultDark,
            CustomOverrides = new Dictionary<string, string>
            {
                ["TextPrimary"] = "#333333",
            },
        };

        var failures = ThemeApplicationService.ValidateUserTokens(settings);
        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.ForegroundToken == "TextPrimary");
    }

    [Fact]
    public void EnforceUserTokenContrast_fixes_unreadable_override()
    {
        var settings = new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.DefaultDark,
            CustomOverrides = new Dictionary<string, string>
            {
                ["TextPrimary"] = "#333333",
            },
        };

        ThemeApplicationService.EnforceUserTokenContrast(settings);
        var failures = ThemeApplicationService.ValidateUserTokens(settings);
        Assert.Empty(failures);
    }

    [Fact]
    public void User_preset_tokens_resolve_through_theme_service()
    {
        var settings = new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.CreateUserPresetId(),
            UserPresets =
            [
                new ThemeUserPreset
                {
                    Id = "user-test123",
                    Name = "Test",
                    Tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["BgBase"] = "#112233",
                        ["BgSurface"] = "#1E1E22",
                        ["BgElevated"] = "#28282E",
                        ["BgChrome"] = "#28282E",
                        ["BgWorkspace"] = "#1E1E22",
                        ["BgInset"] = "#112233",
                        ["TextPrimary"] = "#EDEDF0",
                        ["TextMuted"] = "#9898A4",
                        ["TextOnAccent"] = "#FFFFFF",
                        ["AccentPrimary"] = "#AABBCC",
                        ["Success"] = "#6BCB8E",
                        ["Warning"] = "#E5B567",
                        ["Error"] = "#E57373",
                        ["BorderSubtle"] = "#32323A",
                        ["BorderStrong"] = "#45454F",
                        ["RowHover"] = "#32323A",
                        ["RowSelected"] = "#3A3A44",
                        ["RowAlternate"] = "#222228",
                        ["Header"] = "#28282E",
                        ["Popup"] = "#1E1E22",
                        ["ButtonGhost"] = "#28282E",
                        ["ButtonGhostHover"] = "#32323A",
                        ["ButtonGhostPressed"] = "#1E1E22",
                        ["ContextMenuBackground"] = "#1E1E22",
                        ["ContextMenuForeground"] = "#EDEDF0",
                        ["MenuPopup"] = "#1E1E22",
                    },
                },
            ],
        };
        settings.ActivePresetId = settings.UserPresets[0].Id;

        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
        Assert.Equal("#112233", resolved.GetHex("BgBase"));
        Assert.Equal("#AABBCC", resolved.GetHex("AccentPrimary"));
    }

    [Fact]
    public void User_preset_create_copy_and_matches_settings()
    {
        var settings = ThemeApplicationService.CreateDefaultSettings();
        settings.CustomOverrides["AccentPrimary"] = "#FF8800";

        var preset = ThemeUserPresetService.CreateFromSettings("Sunset custom", settings);
        Assert.StartsWith("user-", preset.Id, StringComparison.OrdinalIgnoreCase);

        var copy = ThemeUserPresetService.CreateCopy("Sunset custom copy", preset);
        Assert.NotEqual(preset.Id, copy.Id);
        Assert.Equal("Sunset custom copy", copy.Name);

        settings.UserPresets.Add(preset);
        ThemeUserPresetService.ApplyToSettings(preset, settings);
        Assert.True(ThemeUserPresetService.MatchesSettings(preset, settings));

        settings.CustomOverrides["TextPrimary"] = "#FFFFFF";
        Assert.False(ThemeUserPresetService.MatchesSettings(preset, settings));
    }

    [Fact]
    public void MergeImportedPresets_replaces_by_id_and_adds_new()
    {
        var existing = new List<ThemeUserPreset>
        {
            new() { Id = "user-keep", Name = "Keep", Tokens = { ["BgBase"] = "#111111" } },
            new() { Id = "user-replace", Name = "Old", Tokens = { ["BgBase"] = "#222222" } },
        };

        ThemeUserPresetService.MergeImportedPresets(existing,
        [
            new ThemeUserPreset { Id = "user-replace", Name = "New", Tokens = { ["BgBase"] = "#333333" } },
            new ThemeUserPreset { Id = "user-added", Name = "Added", Tokens = { ["BgBase"] = "#444444" } },
        ]);

        Assert.Equal(3, existing.Count);
        Assert.Equal("#333333", ThemeUserPresetService.Find(existing, "user-replace")!.Tokens["BgBase"]);
        Assert.Equal("#444444", ThemeUserPresetService.Find(existing, "user-added")!.Tokens["BgBase"]);
    }

    [Fact]
    public void ApplyImportedThemeFields_merges_presets_without_wiping_existing()
    {
        var target = ThemeApplicationService.CreateDefaultSettings();
        target.UserPresets.Add(new ThemeUserPreset
        {
            Id = "user-local",
            Name = "Local",
            Tokens = { ["BgBase"] = "#101010" },
        });

        var imported = new ThemeSettings
        {
            ActivePresetId = "user-imported",
            CustomOverrides = { ["AccentPrimary"] = "#AABBCC" },
            UserPresets =
            [
                new ThemeUserPreset
                {
                    Id = "user-imported",
                    Name = "Imported",
                    Tokens = { ["BgBase"] = "#202020", ["AccentPrimary"] = "#AABBCC" },
                },
            ],
        };

        ThemeUserPresetService.ApplyImportedThemeFields(target, imported, mergeUserPresets: true);

        Assert.Equal("user-imported", target.ActivePresetId);
        Assert.Equal("#AABBCC", target.CustomOverrides["AccentPrimary"]);
        Assert.Equal(2, target.UserPresets.Count);
        Assert.NotNull(ThemeUserPresetService.Find(target.UserPresets, "user-local"));
        Assert.NotNull(ThemeUserPresetService.Find(target.UserPresets, "user-imported"));
    }

    [Fact]
    public void SaveImportedThemeAsPreset_captures_effective_imported_theme()
    {
        var presets = new List<ThemeUserPreset>();
        var imported = new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.DefaultDark,
            CustomOverrides = { ["AccentPrimary"] = "#FF8800" },
            FontSizeBody = 15,
        };

        var preset = ThemeUserPresetService.SaveImportedThemeAsPreset(presets, imported, "Imported sunset");

        Assert.Single(presets);
        Assert.Equal("Imported sunset", preset.Name);
        Assert.Equal(15, preset.FontSizeBody);
        Assert.Equal("#FF8800", preset.Tokens["AccentPrimary"]);
        Assert.Equal(ThemePresetCategories.Essentials, preset.Category);
    }

    [Fact]
    public void NormalizeCategory_maps_known_and_unknown_values()
    {
        Assert.Equal(ThemePresetCategories.DarkAccents, ThemePresetNavigation.NormalizeCategory("dark accents"));
        Assert.Equal(ThemePresetCategories.MyPresets, ThemePresetNavigation.NormalizeCategory(null));
        Assert.Equal(ThemePresetCategories.MyPresets, ThemePresetNavigation.NormalizeCategory("not-a-real-category"));
    }

    [Fact]
    public void Imported_user_preset_category_is_preserved_on_merge()
    {
        var existing = new List<ThemeUserPreset>();
        ThemeUserPresetService.MergeImportedPresets(existing,
        [
            new ThemeUserPreset
            {
                Id = "user-imported",
                Name = "Imported",
                Category = ThemePresetCategories.ClassicDark,
                Tokens = { ["BgBase"] = "#202020" },
            },
        ]);

        Assert.Equal(ThemePresetCategories.ClassicDark, existing[0].Category);
    }

    [Fact]
    public void InferCategory_uses_active_user_preset_category()
    {
        var settings = new ThemeSettings
        {
            ActivePresetId = "user-abc",
            UserPresets =
            [
                new ThemeUserPreset
                {
                    Id = "user-abc",
                    Name = "Mine",
                    Category = ThemePresetCategories.Reading,
                },
            ],
        };

        Assert.Equal(ThemePresetCategories.Reading, ThemeUserPresetService.InferCategory(settings));
    }

    private static readonly JsonSerializerOptions ThemeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ThemeImportService_parses_preset_array()
    {
        const string json = """
            [
              {
                "id": "user-a",
                "name": "Theme A",
                "category": "Dark accents",
                "tokens": { "BgBase": "#111111", "AccentPrimary": "#AABBCC" }
              },
              {
                "id": "user-b",
                "name": "Theme B",
                "category": "Classic dark",
                "tokens": { "BgBase": "#222222", "AccentPrimary": "#CCBBAA" }
              }
            ]
            """;

        var result = ThemeImportService.Parse(json, ThemeJsonOptions);

        Assert.Null(result.ThemeToApply);
        Assert.Equal(2, result.PresetCount);
        Assert.True(result.IsMultiPresetImport);
        Assert.Equal("Theme A", result.PresetsToMerge[0].Name);
        Assert.Equal(ThemePresetCategories.ClassicDark, result.PresetsToMerge[1].Category);
    }

    [Fact]
    public void ThemeImportService_parses_preset_pack_object()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "presets": [
                {
                  "id": "user-pack",
                  "name": "Pack theme",
                  "category": "Reading",
                  "tokens": { "BgBase": "#333333" }
                }
              ]
            }
            """;

        var result = ThemeImportService.Parse(json, ThemeJsonOptions);

        Assert.Null(result.ThemeToApply);
        Assert.Single(result.PresetsToMerge);
        Assert.Equal("Pack theme", result.PresetsToMerge[0].Name);
        Assert.True(result.IsPresetPackOnly);
    }

    [Fact]
    public void ThemeImportService_parses_full_theme_with_multiple_user_presets()
    {
        var json = JsonSerializer.Serialize(new ThemeSettings
        {
            ActivePresetId = "user-one",
            UserPresets =
            [
                new ThemeUserPreset
                {
                    Id = "user-one",
                    Name = "One",
                    Category = ThemePresetCategories.Essentials,
                    Tokens = { ["BgBase"] = "#101010" },
                },
                new ThemeUserPreset
                {
                    Id = "user-two",
                    Name = "Two",
                    Category = ThemePresetCategories.DarkAccents,
                    Tokens = { ["BgBase"] = "#202020" },
                },
            ],
        }, ThemeJsonOptions);

        var result = ThemeImportService.Parse(json, ThemeJsonOptions);

        Assert.NotNull(result.ThemeToApply);
        Assert.Equal(2, result.PresetCount);
        Assert.True(result.IsMultiPresetImport);
    }

    [Fact]
    public void ThemeImportService_parses_theme_settings_array()
    {
        var json = JsonSerializer.Serialize(new List<ThemeSettings>
        {
            new()
            {
                ActivePresetId = ThemePresetIds.Forest,
                CustomOverrides = { ["AccentPrimary"] = "#00FF00" },
            },
            new()
            {
                ActivePresetId = ThemePresetIds.Ocean,
                CustomOverrides = { ["AccentPrimary"] = "#0000FF" },
            },
        }, ThemeJsonOptions);

        var result = ThemeImportService.Parse(json, ThemeJsonOptions);

        Assert.NotNull(result.ThemeToApply);
        Assert.Equal(2, result.PresetCount);
        Assert.Equal("Imported theme 1", result.PresetsToMerge[0].Name);
        Assert.Equal(ThemePresetCategories.DarkAccents, result.PresetsToMerge[0].Category);
        Assert.Equal(ThemePresetCategories.DarkAccents, result.PresetsToMerge[1].Category);
    }

    [Fact]
    public void ThemeImportService_combines_multiple_single_theme_files()
    {
        var forest = JsonSerializer.Serialize(new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.Forest,
            CustomOverrides = { ["AccentPrimary"] = "#00FF00" },
        }, ThemeJsonOptions);

        var ocean = JsonSerializer.Serialize(new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.Ocean,
            CustomOverrides = { ["AccentPrimary"] = "#0000FF" },
        }, ThemeJsonOptions);

        var combined = ThemeImportService.Combine(
        [
            ("Forest theme", ThemeImportService.Parse(forest, ThemeJsonOptions)),
            ("Ocean theme", ThemeImportService.Parse(ocean, ThemeJsonOptions)),
        ]);

        Assert.Equal(2, combined.SourceFileCount);
        Assert.True(combined.IsMultiFileImport);
        Assert.Equal(2, combined.PresetCount);
        Assert.Equal("Forest theme", combined.PresetsToMerge[0].Name);
        Assert.Equal("Ocean theme", combined.PresetsToMerge[1].Name);
        Assert.True(combined.UseBulkImportFlow);
    }

    [Fact]
    public void ApplyToWpf_skips_when_fingerprint_unchanged()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                    _ = new System.Windows.Application();

                var resolved = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
                ThemeApplicationService.InvalidateApplyCache();
                Assert.True(ThemeApplicationService.ApplyToWpf(resolved));
                Assert.False(ThemeApplicationService.ApplyToWpf(resolved));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));
        if (failure is not null)
            throw failure;
    }

    [Fact]
    public void ApplyToWpf_updates_bg_base_brush()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                    _ = new System.Windows.Application();

                var app = System.Windows.Application.Current
                    ?? throw new InvalidOperationException("WPF Application not initialized.");

                var settings = new ThemeSettings
                {
                    ActivePresetId = ThemePresetIds.DefaultDark,
                    CustomOverrides = new Dictionary<string, string>
                    {
                        ["BgBase"] = "#112233",
                    },
                };

                var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
                ThemeApplicationService.InvalidateApplyCache();
                var applied = ThemeApplicationService.ApplyToWpf(resolved);
                Assert.True(applied);

                var brush = app.Resources["BgBaseBrush"] as SolidColorBrush;
                Assert.NotNull(brush);
                Assert.Equal("#FF112233", brush.Color.ToString());
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));
        if (failure is not null)
            throw failure;
    }

    private static IEnumerable<string> ExtractCssVariables(string css)
    {
        const string prefix = "--cgw-";
        foreach (var line in css.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var end = trimmed.IndexOf(':');
            if (end <= 0)
                continue;

            yield return trimmed[..end].Trim();
        }
    }
}
