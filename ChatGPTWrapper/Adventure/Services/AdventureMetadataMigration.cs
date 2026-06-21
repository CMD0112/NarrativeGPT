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

    public static void EnsureSettingsDefaults(AdventureMetadata metadata) =>
        UtilityStoryContextSettingsService.EnsureDefaults(metadata);

    /// <summary>CMD-263: legacy wrapper composer UI removed; force false on load.</summary>
    public static bool MigrateDeprecatedPlaySettings(AdventureMetadata metadata)
    {
        if (!metadata.Settings.UseWrapperComposer)
            return false;

        metadata.Settings.UseWrapperComposer = false;
        return true;
    }

    /// <summary>CMD-248: dedicated utility threads retired — migrate to play-inline delivery.</summary>
    public static bool MigrateUtilityDeliveryMode(AdventureMetadata metadata)
    {
        const int utilityDeliveryPivotSchema = 5;

        if (metadata.SchemaVersion >= utilityDeliveryPivotSchema)
            return false;

        if (metadata.Settings.UtilityDeliveryMode == UtilityDeliveryMode.SeparateThread)
            metadata.Settings.UtilityDeliveryMode = UtilityDeliveryMode.InlinePlayThread;

        metadata.PinnedUtilityTabKey = null;
        metadata.PinnedUtilityTabTitle = null;

        if (metadata.UtilitySessions is not null)
        {
            var designOnly = new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);
            if (metadata.UtilitySessions.TryGetValue(GenerationJobId.DesignAdventure, out var designSession))
                designOnly[GenerationJobId.DesignAdventure] = designSession;
            metadata.UtilitySessions = designOnly;
        }

        metadata.SchemaVersion = utilityDeliveryPivotSchema;
        return true;
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

    /// <summary>Migrate singleton pin fields into <see cref="AdventureMetadata.ThreadRegistry"/> (CMD-221).</summary>
    public static bool MigrateThreadRegistry(AdventureMetadata metadata)
    {
        if (metadata.ThreadRegistryMigratedAt is not null)
            return false;

        var bundle = new AdventureBundle { Metadata = metadata };
        return AdventureThreadRegistryService.EnsureMigrated(bundle);
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
