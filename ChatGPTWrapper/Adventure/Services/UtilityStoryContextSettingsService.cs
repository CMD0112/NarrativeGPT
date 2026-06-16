using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityStoryContextSettingsService
{
    public static UtilityStoryContextSettings Resolve(AdventureBundle bundle, string jobId)
    {
        EnsureDefaults(bundle.Metadata);

        var key = GenerationJobHandlers.GetUtilityJobId(jobId);
        bundle.Metadata.UtilityJobGuideOverrides ??=
            new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase);

        UtilityStoryContextSettings resolved;
        if (bundle.Metadata.UtilityJobGuideOverrides.TryGetValue(key, out var over)
            && over.Context is not null)
        {
            resolved = over.Context;
        }
        else
        {
            resolved = bundle.Metadata.Settings.UtilityStoryContext;
        }

        return UtilityStoryContextProfiles.ApplyJobProfile(
            UtilityStoryContextSettingsNormalizer.Normalize(resolved),
            jobId);
    }

    public static void EnsureDefaults(AdventureMetadata metadata)
    {
        metadata.Settings ??= new AdventureSettings();
        metadata.Settings.UtilityStoryContext ??= new UtilityStoryContextSettings();
    }

    public static void SetJobOverride(AdventureBundle bundle, string jobId, UtilityStoryContextSettings? settings)
    {
        var key = GenerationJobHandlers.GetUtilityJobId(jobId);
        bundle.Metadata.UtilityJobGuideOverrides ??=
            new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase);

        if (settings is null)
        {
            if (bundle.Metadata.UtilityJobGuideOverrides.TryGetValue(key, out var existing))
                existing.Context = null;
            return;
        }

        if (!bundle.Metadata.UtilityJobGuideOverrides.TryGetValue(key, out var over))
        {
            over = new UtilityJobGuideOverride();
            bundle.Metadata.UtilityJobGuideOverrides[key] = over;
        }

        over.Context = settings.Clone();
    }

    public static bool HasJobOverride(AdventureBundle bundle, string jobId)
    {
        var key = GenerationJobHandlers.GetUtilityJobId(jobId);
        return bundle.Metadata.UtilityJobGuideOverrides?.TryGetValue(key, out var over) == true
               && over.Context is not null;
    }
}
