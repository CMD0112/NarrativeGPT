using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureMetadataMigration
{
    public static void MigrateUtilitySessions(AdventureMetadata metadata)
    {
        metadata.UtilitySessions ??= new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);
        metadata.UtilitySessionArchive ??= [];

        if (metadata.EntityUtility is { } legacy
            && !metadata.UtilitySessions.ContainsKey(GenerationJobId.ExtractEntities))
        {
            metadata.UtilitySessions[GenerationJobId.ExtractEntities] = new GenerationUtilitySession
            {
                ConversationId = legacy.ConversationId,
                Sequence = legacy.Sequence,
                SeedVersion = legacy.SeedVersion,
                JobCount = legacy.JobCount,
                ConsecutiveParseFailures = legacy.ConsecutiveParseFailures,
                CreatedAt = legacy.CreatedAt,
                LastUsedAt = legacy.LastUsedAt,
            };
            metadata.EntityUtility = null;
        }

        foreach (var archived in metadata.EntityUtilityArchive)
        {
            metadata.UtilitySessionArchive.Add(new GenerationUtilitySessionArchive
            {
                JobId = GenerationJobId.ExtractEntities,
                ConversationId = archived.ConversationId,
                Sequence = archived.Sequence,
                RotatedAt = archived.RotatedAt,
                Reason = archived.Reason,
            });
        }

        metadata.EntityUtilityArchive.Clear();
    }

    public static void EnsureSettingsDefaults(AdventureMetadata metadata)
    {
        UtilityStoryContextSettingsService.EnsureDefaults(metadata);
    }

    public static bool MigrateProjectLinkFields(AdventureMetadata metadata)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(metadata.LinkedProjectId))
        {
            if (!string.IsNullOrWhiteSpace(metadata.ProjectLink?.GizmoId))
            {
                metadata.LinkedProjectId = metadata.ProjectLink.GizmoId;
                changed = true;
            }
            else if (!string.IsNullOrWhiteSpace(metadata.LinkedProjectHint))
            {
                metadata.LinkedProjectId = metadata.LinkedProjectHint;
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(metadata.LinkedConversationId)
            && !string.IsNullOrWhiteSpace(metadata.ProjectLink?.PlayConversationId))
        {
            metadata.LinkedConversationId = metadata.ProjectLink.PlayConversationId;
            changed = true;
        }

        return changed;
    }

    public static void MigrateSourcePublishMode(AdventureMetadata metadata, SourceManifest manifest)
    {
        EnsureSettingsDefaults(metadata);

        const int publishModeSchema = 2;
        const int manualOnlySchema = 3;

        if (metadata.SchemaVersion < publishModeSchema)
        {
            if (!string.IsNullOrWhiteSpace(metadata.LinkedProjectId)
                && (manifest.LastRemoteSyncAt is not null
                    || manifest.Entries.Any(e => !string.IsNullOrEmpty(e.RemoteFileId))))
            {
                metadata.Settings.SourcePublishMode = SourcePublishMode.ApiSync;
            }

            metadata.SchemaVersion = publishModeSchema;
        }

        if (metadata.SchemaVersion < manualOnlySchema)
        {
            if (metadata.Settings.SourcePublishMode == SourcePublishMode.ApiSync)
                metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;

            metadata.SchemaVersion = manualOnlySchema;
        }

        MigrateSectionInjection(metadata);
    }

    private static void MigrateSectionInjection(AdventureMetadata metadata)
    {
        const int sectionInjectionSchema = 4;

        if (metadata.SchemaVersion >= sectionInjectionSchema)
            return;

        if (metadata.SectionInjectionMigratedAt is null && metadata.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-1))
            metadata.Settings.UseSectionInjection = false;

        metadata.SchemaVersion = sectionInjectionSchema;
    }
}
