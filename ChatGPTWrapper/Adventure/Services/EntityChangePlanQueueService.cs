using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

public static class EntityChangePlanQueueService
{
    public static IReadOnlyList<EntityChangePlan> GetPending(AdventureBundle bundle) =>
        bundle.SourceManifest.PendingEntityChangePlans;

    public static bool HasPending(AdventureBundle bundle) =>
        bundle.SourceManifest.PendingEntityChangePlans.Count > 0;

    public static void Enqueue(AdventureBundle bundle, EntityChangePlan plan)
    {
        bundle.SourceManifest.PendingEntityChangePlans.Add(plan);
        AdventureStore.Save(bundle);
    }

    public static EntityChangePlan? Dequeue(AdventureBundle bundle, Guid planId)
    {
        var plan = bundle.SourceManifest.PendingEntityChangePlans
            .FirstOrDefault(p => p.PlanId == planId);
        if (plan is null)
            return null;

        bundle.SourceManifest.PendingEntityChangePlans.Remove(plan);
        AdventureStore.Save(bundle);
        return plan;
    }

    public static void DiscardAll(AdventureBundle bundle)
    {
        bundle.SourceManifest.PendingEntityChangePlans.Clear();
        AdventureStore.Save(bundle);
    }

    public static void Discard(AdventureBundle bundle, Guid planId)
    {
        var plan = bundle.SourceManifest.PendingEntityChangePlans
            .FirstOrDefault(p => p.PlanId == planId);
        if (plan is null)
            return;

        bundle.SourceManifest.PendingEntityChangePlans.Remove(plan);
        AdventureStore.Save(bundle);
    }
}
