using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EntityCanonStateLifecycleTests
{
    [Fact]
    public void GetOrCreate_seeds_mapped_fields_from_canon()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry
        {
            Id = Guid.NewGuid(),
            Name = "Mara",
            Role = "Apothecary",
            Personality = "Dry wit",
            Description = "Runs the shop.",
        };
        bundle.Entities.Characters.Add(npc);

        try
        {
            var record = EntityInternalStateService.GetOrCreate(bundle, EntityInternalStateKind.Npc, npc.Id);
            Assert.Equal(1, record.Revision);

            var state = EntityInternalStateService.GetStateObject(record, EntityInternalStateKind.Npc);
            Assert.NotNull(state);
            Assert.True(EntityInternalStatePathAccessor.TryGetDisplayValue(
                state!, "social.disposition", EntityInternalStateFieldKind.String, out var disposition));
            Assert.Equal("Apothecary", disposition);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Guard_rejects_canon_keys_in_state_patch()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"description":"rewrite biography"}""");
        Assert.False(EntityCanonStateGuardService.TryValidateStatePatch(doc.RootElement, out var reason));
        Assert.Contains("description", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guard_rejects_state_blocks_in_entity_extract_proposal()
    {
        var json = """{"entityType":"person","name":"Mara","emotional":{"mood":"angry"}}""";
        Assert.False(EntityCanonStateGuardService.TryValidateEntityExtractProposal(json, out var reason));
        Assert.Contains("emotional", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectDivergences_finds_role_disposition_mismatch()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry
        {
            Id = Guid.NewGuid(),
            Name = "Mara",
            Role = "Ally",
        };
        bundle.Entities.Characters.Add(npc);

        try
        {
            var record = EntityInternalStateService.GetOrCreate(bundle, EntityInternalStateKind.Npc, npc.Id);
            var state = EntityInternalStateService.GetStateObject(record, EntityInternalStateKind.Npc)!;
            EntityInternalStatePathAccessor.TrySetDisplayValue(
                state, "social.disposition", EntityInternalStateFieldKind.String, "Hostile");

            var divergences = EntityCanonStateOverlapService.DetectDivergences(
                bundle, EntityInternalStateKind.Npc, npc.Id);
            Assert.Contains(divergences, d => d.StatePath == "social.disposition");
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void ApplyAcceptedProposal_merges_state_review_item()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry { Id = Guid.NewGuid(), Name = "Mara" };
        bundle.Entities.Characters.Add(npc);

        try
        {
            var proposal = new EntityStateProposalEntry
            {
                EntityId = npc.Id,
                KindId = EntityInternalStateKind.Npc,
                Proposed = new EntityStateRecord
                {
                    KindId = EntityInternalStateKind.Npc,
                    Character = new CharacterInternalState
                    {
                        Emotional = new EmotionalStateBlock { Mood = "Anxious" },
                    },
                },
            };

            EntityInternalStateProposalService.ApplyAcceptedProposal(bundle, proposal);
            var record = EntityInternalStateService.TryGet(bundle, EntityInternalStateKind.Npc, npc.Id);
            Assert.NotNull(record);
            Assert.Equal("Anxious", record!.Character?.Emotional.Mood);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void ProposalReview_accepts_entity_state_and_canon_evolution()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry { Id = Guid.NewGuid(), Name = "Mara", Role = "Ally" };
        bundle.Entities.Characters.Add(npc);

        try
        {
            bundle.EntityInternalState.ReviewQueue.Add(new EntityStateProposalEntry
            {
                Id = Guid.NewGuid(),
                EntityId = npc.Id,
                KindId = EntityInternalStateKind.Npc,
                Proposed = new EntityStateRecord
                {
                    KindId = EntityInternalStateKind.Npc,
                    Character = new CharacterInternalState
                    {
                        Social = new SocialStateBlock { Disposition = "Wary" },
                    },
                },
            });

            var stateItem = bundle.EntityInternalState.ReviewQueue[0];
            var stateResult = ProposalReviewService.Accept(
                bundle,
                new ProposalReviewItemKey { Category = ProposalReviewCategory.EntityState, Id = stateItem.Id });
            Assert.Equal(ProposalReviewActionStatus.Succeeded, stateResult.Status);

            bundle.Entities.CanonEvolutionReviewQueue.Add(new CanonEvolutionProposalEntry
            {
                Id = Guid.NewGuid(),
                EntityId = npc.Id,
                KindId = EntityInternalStateKind.Npc,
                CanonFieldKey = "role",
                ProposedCanonValue = "Wary ally",
            });

            var canonItem = bundle.Entities.CanonEvolutionReviewQueue[0];
            var canonResult = ProposalReviewService.Accept(
                bundle,
                new ProposalReviewItemKey { Category = ProposalReviewCategory.CanonEvolution, Id = canonItem.Id });
            Assert.Equal(ProposalReviewActionStatus.Succeeded, canonResult.Status);
            Assert.Equal("Wary ally", npc.Role);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void BuildEntityStateSkimBlock_includes_pinned_entity_summary()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry
        {
            Id = Guid.NewGuid(),
            Name = "Mara",
            Pinned = true,
        };
        bundle.Entities.Characters.Add(npc);

        try
        {
            var record = EntityInternalStateService.GetOrCreate(bundle, EntityInternalStateKind.Npc, npc.Id);
            record.Character!.Emotional.Mood = "Tense";
            EntityInternalStateService.Upsert(bundle, record);

            var method = typeof(PromptPacketBuilder).GetMethod(
                "BuildEntityStateSkimBlock",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            var block = method.Invoke(null, [bundle]) as string;
            Assert.NotNull(block);
            Assert.Contains("ENTITY PLAY STATE", block, StringComparison.Ordinal);
            Assert.Contains("Mara", block, StringComparison.Ordinal);
            Assert.Contains("Tense", block, StringComparison.Ordinal);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void EnsureAllCanonEntitiesTracked_seeds_every_character()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry
        {
            Id = Guid.NewGuid(),
            Name = "Mara",
            Role = "Apothecary",
        };
        bundle.Entities.Characters.Add(npc);

        try
        {
            var created = EntityInternalStateService.EnsureAllCanonEntitiesTracked(bundle);
            Assert.True(created >= 1);

            var record = EntityInternalStateService.TryGet(bundle, EntityInternalStateKind.Npc, npc.Id);
            Assert.NotNull(record);
            var state = EntityInternalStateService.GetStateObject(record!, EntityInternalStateKind.Npc);
            Assert.True(EntityInternalStatePathAccessor.TryGetDisplayValue(
                state!, "Social.Disposition", EntityInternalStateFieldKind.String, out var disposition));
            Assert.Equal("Apothecary", disposition);

            Assert.Null(EntityCanonStateOverlapService.DescribeLiveDivergence(
                bundle, EntityInternalStateKind.Npc, npc.Id, "Social.Disposition", disposition));
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void EnsureAllCanonEntitiesTracked_seeds_personality_and_relationship_fields()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry
        {
            Id = Guid.NewGuid(),
            Name = "Mara",
            Personality = "Dry wit",
            RelationshipToPlayer = "Cautious ally",
        };
        bundle.Entities.Characters.Add(npc);

        try
        {
            EntityInternalStateService.EnsureAllCanonEntitiesTracked(bundle);
            var record = EntityInternalStateService.TryGet(bundle, EntityInternalStateKind.Npc, npc.Id);
            Assert.NotNull(record);
            var state = EntityInternalStateService.GetStateObject(record!, EntityInternalStateKind.Npc)!;

            Assert.True(EntityInternalStatePathAccessor.TryGetDisplayValue(
                state, "Emotional.Mood", EntityInternalStateFieldKind.String, out var mood));
            Assert.Contains("Dry wit", mood, StringComparison.Ordinal);

            Assert.True(EntityInternalStatePathAccessor.TryGetDisplayValue(
                state, "Social.TrustTowardPlayer", EntityInternalStateFieldKind.String, out var trust));
            Assert.Equal("Cautious ally", trust);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void DescribeLiveDivergence_detects_edited_disposition_before_save()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        var npc = new CharacterEntry
        {
            Id = Guid.NewGuid(),
            Name = "Mara",
            Role = "Ally",
        };
        bundle.Entities.Characters.Add(npc);

        try
        {
            var message = EntityCanonStateOverlapService.DescribeLiveDivergence(
                bundle,
                EntityInternalStateKind.Npc,
                npc.Id,
                "Social.Disposition",
                "Hostile");
            Assert.NotNull(message);
            Assert.Contains("Hostile", message, StringComparison.Ordinal);
            Assert.Contains("Ally", message, StringComparison.Ordinal);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Scheduler_includes_auto_propose_entity_state_when_enabled()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 1);
        bundle.Metadata.Settings.AutoProposeEntityState = true;
        var turn = new TurnRecord { Index = 1, PlayerText = "Hello", NarratorText = "Hi", Status = TurnStatus.Accepted };

        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);
        Assert.Contains(GenerationJobId.ProposeEntityState, jobs);
    }
}
