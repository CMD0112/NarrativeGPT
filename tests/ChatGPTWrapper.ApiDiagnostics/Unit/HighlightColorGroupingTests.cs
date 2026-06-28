using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class HighlightColorGroupingTests
{
    [Fact]
    public void Resolve_cast_distinct_locations_shared_assigns_same_group_color()
    {
        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctLocationsShared);

        var locA = HighlightColorGroupingResolver.Resolve(profile, Guid.NewGuid(), "Locations", "Harbor", "Harbor");
        var locB = HighlightColorGroupingResolver.Resolve(profile, Guid.NewGuid(), "Locations", "Forest", "Forest");

        Assert.False(locA.IsExcluded);
        Assert.True(locA.ShareColorWithinGroup);
        Assert.Equal(locA.GroupKey, locB.GroupKey);
    }

    [Fact]
    public void Resolve_cast_distinct_world_shared_uses_remainder_group()
    {
        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctWorldShared);

        var character = HighlightColorGroupingResolver.Resolve(profile, Guid.NewGuid(), "Characters", "Guide", "Mara");
        var location = HighlightColorGroupingResolver.Resolve(profile, Guid.NewGuid(), "Locations", "Harbor", "Harbor");

        Assert.Equal("Cast", character.GroupName);
        Assert.False(character.ShareColorWithinGroup);
        Assert.Equal("World", location.GroupName);
        Assert.True(location.ShareColorWithinGroup);
    }

    [Fact]
    public void Resolve_cast_only_excludes_locations()
    {
        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastOnly);

        var location = HighlightColorGroupingResolver.Resolve(profile, Guid.NewGuid(), "Locations", "Harbor", "Harbor");
        var character = HighlightColorGroupingResolver.Resolve(profile, Guid.NewGuid(), "Characters", "Guide", "Mara Holt");

        Assert.True(location.IsExcluded);
        Assert.False(character.IsExcluded);
        Assert.Equal("Cast", character.GroupName);
    }

    [Fact]
    public void Entity_subset_matches_only_listed_characters()
    {
        var vipId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var profile = new HighlightColorGroupingProfile
        {
            Groups =
            [
                new HighlightColorGroupRule
                {
                    Name = "VIP",
                    Priority = 0,
                    IncludeEntities =
                    [
                        new HighlightColorEntityRef { EntityId = vipId, EntityCategory = "Characters", DisplayName = "Mara" },
                    ],
                },
                new HighlightColorGroupRule
                {
                    Name = "Other cast",
                    Priority = 1,
                    EntityCategories = ["Characters"],
                    ShareColorWithinGroup = true,
                },
            ],
        };

        var vip = HighlightColorGroupingResolver.Resolve(profile, vipId, "Characters", "Guide", "Mara Holt");
        var other = HighlightColorGroupingResolver.Resolve(profile, otherId, "Characters", "Guide", "Bram Rusk");

        Assert.Equal("VIP", vip.GroupName);
        Assert.False(vip.ShareColorWithinGroup);
        Assert.Equal("Other cast", other.GroupName);
        Assert.True(other.ShareColorWithinGroup);
    }

    [Fact]
    public void Category_with_excluded_entity_falls_through_to_next_group()
    {
        var excludedId = Guid.NewGuid();
        var profile = new HighlightColorGroupingProfile
        {
            Groups =
            [
                new HighlightColorGroupRule
                {
                    Name = "Cast",
                    Priority = 0,
                    EntityCategories = ["Characters"],
                    ExcludeEntities =
                    [
                        new HighlightColorEntityRef { EntityId = excludedId, EntityCategory = "Characters", DisplayName = "Bram" },
                    ],
                    ShareColorWithinGroup = true,
                },
                new HighlightColorGroupRule
                {
                    Name = "Singles",
                    Priority = 1,
                    IncludeEntities =
                    [
                        new HighlightColorEntityRef { EntityId = excludedId, EntityCategory = "Characters", DisplayName = "Bram" },
                    ],
                },
            ],
        };

        var bram = HighlightColorGroupingResolver.Resolve(profile, excludedId, "Characters", "Guide", "Bram Rusk");
        Assert.Equal("Singles", bram.GroupName);
    }

    [Fact]
    public void Grouped_assignment_shares_color_within_location_group()
    {
        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctLocationsShared)
            .Clone();
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas);
        var state = new HighlightColorAssignmentState();
        var characterColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var harbor = HighlightColorGroupedAssignment.AssignColor(
            options,
            profile,
            new PhraseHighlightRule { Phrase = "Harbor", EntityId = Guid.NewGuid(), EntityCategory = "Locations" },
            "Harbor",
            "Harbor",
            palette,
            canvas,
            characterColors,
            state,
            discoveryIndex: 0,
            theme);

        var forest = HighlightColorGroupedAssignment.AssignColor(
            options,
            profile,
            new PhraseHighlightRule { Phrase = "Forest", EntityId = Guid.NewGuid(), EntityCategory = "Locations" },
            "Forest",
            "Forest",
            palette,
            canvas,
            characterColors,
            state,
            discoveryIndex: 1,
            theme);

        Assert.Equal(harbor, forest, ignoreCase: true);
    }

    [Fact]
    public void Grouped_assignment_keeps_cast_distinct_within_group()
    {
        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctWorldShared)
            .Clone();
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas, minimumDistinctColors: 8);
        var state = new HighlightColorAssignmentState();
        var characterColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var mara = HighlightColorGroupedAssignment.AssignColor(
            options,
            profile,
            new PhraseHighlightRule { Phrase = "Mara Holt", EntityId = Guid.NewGuid(), EntityCategory = "Characters" },
            "Guide",
            "Mara Holt",
            palette,
            canvas,
            characterColors,
            state,
            discoveryIndex: 0,
            theme);

        var bram = HighlightColorGroupedAssignment.AssignColor(
            options,
            profile,
            new PhraseHighlightRule { Phrase = "Bram Rusk", EntityId = Guid.NewGuid(), EntityCategory = "Characters" },
            "Guide",
            "Bram Rusk",
            palette,
            canvas,
            characterColors,
            state,
            discoveryIndex: 1,
            theme);

        Assert.NotEqual(mara.ToUpperInvariant(), bram.ToUpperInvariant());
    }

    [Fact]
    public void Exclude_phrase_prevents_group_match()
    {
        var profile = new HighlightColorGroupingProfile
        {
            Groups =
            [
                new HighlightColorGroupRule
                {
                    Name = "Locations except one",
                    EntityCategories = ["Locations"],
                    ExcludePhrases = ["Harbor"],
                    Priority = 0,
                    MatchRemainder = false,
                },
            ],
            UnmatchedBehavior = HighlightColorUnmatchedBehavior.Exclude,
        };

        var harbor = HighlightColorGroupingResolver.Resolve(profile, Guid.NewGuid(), "Locations", "Harbor", "Harbor");
        Assert.True(harbor.IsExcluded);
    }
}
