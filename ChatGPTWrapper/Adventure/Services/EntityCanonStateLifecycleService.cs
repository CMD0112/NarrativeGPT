using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityCanonStateLifecycleService
{
    public static void ResetFromCanon(AdventureBundle bundle, string kindId, Guid entityId)
    {
        var record = EntityInternalStateService.TryGet(bundle, kindId, entityId)
                       ?? EntityInternalStateService.GetOrCreate(bundle, kindId, entityId, seedFromCanon: false);
        EntityCanonStateOverlapService.ResetMappedFieldsFromCanon(bundle, record, kindId);
        EntityInternalStateService.Upsert(bundle, record);
    }

    public static int QueuePromoteDrafts(AdventureBundle bundle, string kindId, Guid entityId)
    {
        var count = 0;
        foreach (var divergence in EntityCanonStateOverlapService.DetectDivergences(bundle, kindId, entityId))
        {
            if (!EntityCanonStateOverlapService.TryBuildPromoteDraft(bundle, kindId, entityId, divergence, out var draft)
                || draft is null)
            {
                continue;
            }

            bundle.Entities.CanonEvolutionReviewQueue.Add(draft);
            count++;
        }

        return count;
    }
}
