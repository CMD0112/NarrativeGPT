using System.Text.Json;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityInternalStateModelTests
{
    [Fact]
    public void EntityInternalStateDocument_roundTripsThroughAdventureJson()
    {
        var doc = new EntityInternalStateDocument
        {
            Entries =
            [
                new EntityStateRecord
                {
                    EntityId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    KindId = EntityInternalStateKind.Npc,
                    Revision = 2,
                    Character = new CharacterInternalState
                    {
                        Emotional = new EmotionalStateBlock
                        {
                            Mood = "wary",
                            Fear = "betrayal",
                            Emotions = ["suspicious", "tired"],
                        },
                        Physical = new PhysicalStateBlock
                        {
                            Condition = "wounded",
                            Injuries = ["cut arm"],
                        },
                        Social = new SocialStateBlock
                        {
                            TrustTowardPlayer = "low",
                            Reputation = "notorious",
                            Relationships = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Captain"] = "loyal",
                            },
                        },
                        Narrative = new NarrativeFocusBlock
                        {
                            ArcStage = "crisis",
                            OpenThreads = ["debt to the guild"],
                        },
                    },
                },
            ],
            ReviewQueue =
            [
                new EntityStateProposalEntry
                {
                    EntityId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    KindId = EntityInternalStateKind.Npc,
                    Rationale = "After the ambush",
                    Proposed = new EntityStateRecord
                    {
                        KindId = EntityInternalStateKind.Npc,
                        Character = new CharacterInternalState
                        {
                            Emotional = new EmotionalStateBlock { Mood = "furious" },
                        },
                    },
                },
            ],
        };

        var json = JsonSerializer.Serialize(doc, AdventureJson.Options);
        var roundTrip = JsonSerializer.Deserialize<EntityInternalStateDocument>(json, AdventureJson.Options);

        Assert.NotNull(roundTrip);
        var entry = roundTrip!.Entries[0];
        Assert.NotNull(entry.Character);
        Assert.Equal("wary", entry.Character!.Emotional.Mood);
        Assert.Equal("cut arm", entry.Character.Physical.Injuries[0]);
        Assert.Equal("loyal", entry.Character.Social.Relationships["Captain"]);
        Assert.Equal("crisis", entry.Character.Narrative.ArcStage);
        Assert.Single(entry.Character.Narrative.OpenThreads);
        Assert.Single(roundTrip.ReviewQueue);
        Assert.Equal("furious", roundTrip.ReviewQueue[0].Proposed.Character!.Emotional.Mood);
    }

    private static AdventureBundle NewBundle() =>
        new() { Metadata = new AdventureMetadata { Title = "Test" } };

    [Fact]
    public void EntityInternalStateService_getOrCreate_initializesKindState()
    {
        var bundle = NewBundle();
        var id = Guid.NewGuid();

        var record = EntityInternalStateService.GetOrCreate(bundle, EntityInternalStateKind.Quest, id);

        Assert.Equal(id, record.EntityId);
        Assert.Equal(EntityInternalStateKind.Quest, record.KindId);
        Assert.NotNull(record.Quest);
        Assert.Same(record, EntityInternalStateService.TryGet(bundle, EntityInternalStateKind.Quest, id));
    }

    [Fact]
    public void EntityInternalStateService_upsert_incrementsRevision()
    {
        var bundle = NewBundle();
        var id = Guid.NewGuid();
        var record = EntityInternalStateService.GetOrCreate(bundle, EntityInternalStateKind.Player, id);
        var before = record.UpdatedAt;

        record.Player!.Emotional.Mood = "determined";
        EntityInternalStateService.Upsert(bundle, record);

        var stored = EntityInternalStateService.TryGet(bundle, EntityInternalStateKind.Player, id);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.Revision);
        Assert.Equal("determined", stored.Player!.Emotional.Mood);
        Assert.True(stored.UpdatedAt >= before);
    }

    [Fact]
    public void ResolveKindIdFromExtractionType_mapsExtendedTypes()
    {
        Assert.Equal(EntityInternalStateKind.Vehicle, EntityInternalStateService.ResolveKindIdFromExtractionType("vessel"));
        Assert.Equal(EntityInternalStateKind.Mystery, EntityInternalStateService.ResolveKindIdFromExtractionType("mystery"));
        Assert.Equal(EntityInternalStateKind.Conflict, EntityInternalStateService.ResolveKindIdFromExtractionType("conflict"));
    }

    [Fact]
    public void LocationInternalState_roundTripsSensoryFields()
    {
        var state = new LocationInternalState
        {
            Atmosphere = "oppressive",
            Smells = ["ozone", "rot"],
            RestrictedAreas = ["vault"],
            ActiveHooks = ["missing heir"],
        };

        var json = JsonSerializer.Serialize(state, AdventureJson.Options);
        var roundTrip = JsonSerializer.Deserialize<LocationInternalState>(json, AdventureJson.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal("oppressive", roundTrip!.Atmosphere);
        Assert.Equal(2, roundTrip.Smells.Count);
        Assert.Contains("vault", roundTrip.RestrictedAreas);
    }

    [Fact]
    public void CustomInternalState_roundTripsExtendedFields()
    {
        var state = new CustomInternalState
        {
            CustomKind = "deity",
            ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["favor"] = "waning",
            },
        };

        var json = JsonSerializer.Serialize(state, AdventureJson.Options);
        var roundTrip = JsonSerializer.Deserialize<CustomInternalState>(json, AdventureJson.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal("deity", roundTrip!.CustomKind);
        Assert.Equal("waning", roundTrip.ExtendedFields["favor"]);
    }

    [Fact]
    public void AdventureSaveScope_all_includesEntityInternalState()
    {
        Assert.True(AdventureSaveScope.All.HasFlag(AdventureSaveScope.EntityInternalState));
    }
}
