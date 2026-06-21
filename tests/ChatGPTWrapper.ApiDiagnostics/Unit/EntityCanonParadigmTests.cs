using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntityCanonParadigmTests : IDisposable
{
    private readonly string _root;

    public EntityCanonParadigmTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-canon-paradigm-" + Guid.NewGuid().ToString("N"));
        AppDirectories.TestRootOverride = _root;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    [Fact]
    public void CanonMentionIndexService_finds_name_in_exported_cast()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Mentions");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Nessa", Role = "Guide" });
        AdventureStore.Save(bundle);
        ProjectSourceExportService.ExportForce(bundle);

        var hits = CanonMentionIndexService.FindMentions(bundle, ["Nessa"]);
        Assert.Contains(hits, h => h.File == "cast.md");
    }

    [Fact]
    public void EntityChangePlanBuilder_infers_rename_intent()
    {
        var context = new CanonEditContext
        {
            Category = "Characters",
            EntityId = Guid.NewGuid(),
            PriorName = "Nessa",
            NewName = "Anwen",
        };

        var bundle = AdventureStore.CreateNew("Plan");
        var plan = EntityChangePlanBuilder.BuildFromEditContext(bundle, context);
        Assert.Equal(EntityChangeIntent.Rename, plan.Intent);
    }

    [Fact]
    public void EntityChangePlanQueueService_enqueues_and_dequeues()
    {
        var bundle = AdventureStore.CreateNew("Queue");
        var plan = new EntityChangePlan
        {
            Intent = EntityChangeIntent.Update,
            EntityId = Guid.NewGuid(),
            Category = "Characters",
            NewName = "Test",
        };

        EntityChangePlanQueueService.Enqueue(bundle, plan);
        Assert.True(EntityChangePlanQueueService.HasPending(bundle));

        var dequeued = EntityChangePlanQueueService.Dequeue(bundle, plan.PlanId);
        Assert.NotNull(dequeued);
        Assert.False(EntityChangePlanQueueService.HasPending(bundle));
    }

    [Fact]
    public void CanonInboxService_lists_staged_plans()
    {
        var bundle = AdventureStore.CreateNew("Inbox");
        EntityChangePlanQueueService.Enqueue(bundle, new EntityChangePlan
        {
            Intent = EntityChangeIntent.Update,
            EntityId = Guid.NewGuid(),
            Category = "Characters",
            NewName = "Mara",
        });

        var items = CanonInboxService.ListItems(bundle);
        Assert.Contains(items, i => i.Type == CanonInboxItemType.StagedPlan);
    }

    [Fact]
    public void EntitySyncStatusService_reports_in_sync_for_fresh_entity()
    {
        var id = Guid.NewGuid();
        var bundle = AdventureStore.CreateNew("Sync badge");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "Mara" });
        AdventureStore.Save(bundle);
        ProjectSourceExportService.ExportForce(bundle);

        var status = EntitySyncStatusService.GetStatus(bundle, id, "Characters");
        Assert.Equal(EntitySyncStatus.InSync, status);
    }
}
