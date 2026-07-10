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
        PlayInjectionPolicyService.EnsureDefaults(metadata);
    }

    /// <summary>CMD-263: legacy wrapper composer UI removed; force false on load.</summary>
    public static bool MigrateDeprecatedPlaySettings(AdventureMetadata metadata)
    {
        var changed = false;
        if (metadata.Settings.UseWrapperComposer)
        {
            metadata.Settings.UseWrapperComposer = false;
            changed = true;
        }

        if (!metadata.Settings.UseSectionInjection)
        {
            metadata.Settings.UseSectionInjection = true;
            changed = true;
        }

        return changed;
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

        if (metadata.Settings.SourcePublishMode == SourcePublishMode.ApiSync)
            metadata.Settings.SourcePublishMode = SourcePublishMode.Manual;

        metadata.Settings.UseSectionInjection = true;
    }

    /// <summary>Migrate singleton pin fields into <see cref="AdventureMetadata.ThreadRegistry"/> (CMD-221).</summary>
    public static bool MigrateThreadRegistry(AdventureMetadata metadata)
    {
        if (metadata.ThreadRegistryMigratedAt is not null)
            return false;

        var bundle = new AdventureBundle { Metadata = metadata };
        return AdventureThreadRegistryService.EnsureMigrated(bundle);
    }

    /// <summary>CMD-253: registry-only thread binding; strip legacy singleton fields on load.</summary>
    public static bool MigrateThreadBindingRetirement(AdventureMetadata metadata)
    {
        const int threadBindingRetirementSchema = 6;

        if (metadata.SchemaVersion >= threadBindingRetirementSchema)
            return false;

        var bundle = new AdventureBundle { Metadata = metadata };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        PurgeRetiredUtilityEntries(metadata);
        StripLegacyThreadBindingFields(metadata);
        metadata.SchemaVersion = threadBindingRetirementSchema;
        return true;
    }

    public static void StripLegacyThreadBindingFields(AdventureMetadata metadata)
    {
        metadata.LinkedConversationId = null;
        metadata.PinnedPlayTabKey = null;
        metadata.PinnedPlayTabTitle = null;
        metadata.PinnedPlayTabUrl = null;
        metadata.PinnedDesignTabKey = null;
        metadata.PinnedDesignTabTitle = null;
        metadata.PinnedDesignTabUrl = null;
        metadata.PinnedUtilityTabKey = null;
        metadata.PinnedUtilityTabTitle = null;
        metadata.PlayThreadArchive = [];
        metadata.UtilitySessions?.Clear();
        metadata.ActiveThreadIds?.Remove(AdventureThreadRegistryService.KindKey(AdventureThreadKindLegacy.Utility));

        if (metadata.ProjectLink is not null)
            metadata.ProjectLink.PlayConversationId = null;
    }

    private static void PurgeRetiredUtilityEntries(AdventureMetadata metadata)
    {
        if (metadata.ThreadRegistry is null)
            return;

        metadata.ThreadRegistry.RemoveAll(e => e.Kind == AdventureThreadKindLegacy.Utility);
    }

    /// <summary>CMD-62: play thread binding trust — only verified threads drive auto-navigation.</summary>
    public static bool MigratePlayThreadBindingTrust(AdventureBundle bundle)
    {
        const int bindingTrustSchema = 7;

        if (bundle.Metadata.SchemaVersion >= bindingTrustSchema)
            return false;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var log = bundle.Log;

        foreach (var entry in bundle.Metadata.ThreadRegistry ?? [])
        {
            if (entry.Kind != AdventureThreadKind.Play)
                continue;

            if (string.IsNullOrWhiteSpace(entry.ConversationId))
            {
                entry.BindingTrust = PlayThreadBindingTrust.Unbound;
                continue;
            }

            var hasTurns = log.Turns.Any(t =>
                string.Equals(t.ConversationId, entry.ConversationId, StringComparison.OrdinalIgnoreCase));
            if (hasTurns)
            {
                entry.BindingTrust = PlayThreadBindingTrust.Verified;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.PinnedTabKey))
            {
                entry.BindingTrust = PlayThreadBindingTrust.Verified;
                continue;
            }

            entry.BindingTrust = PlayThreadBindingTrust.PendingPin;
        }

        bundle.Metadata.SchemaVersion = bindingTrustSchema;
        return true;
    }
}
