namespace ChatGPTWrapper.Adventure.Models;

using ChatGPTWrapper.ChatGptApi;

public sealed class SourceManifest
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool Synced { get; set; }

    public DateTimeOffset? LastRemoteSyncAt { get; set; }

    public string? ApiProfileVersion { get; set; }

    public int LastKnownDuplicateRemotes { get; set; }

    public List<SourceManifestEntry> Entries { get; set; } = [];

    public void RefreshSyncedFlag()
    {
        Synced = Entries.Count > 0 && Entries.All(e => e.SyncState == SourceSyncState.InSync);
    }
}

public enum SourceSyncState
{
    InSync,
    LocalNewer,
    RemoteNewer,
    Conflict,
    LocalOnly,
    MissingRemote,
    RemoteOnly,
}

public enum SourceSyncAction
{
    Skip,
    Pull,
    PushReplace,
    NeedsResolution,
}

public enum SourceConflictResolution
{
    None,
    KeepLocal,
    KeepRemote,
    Skip,
}

public sealed class SourceManifestEntry
{
    public string RelativePath { get; set; } = "";

    /// <summary>Legacy field; migrated to <see cref="LocalSha256"/> on load.</summary>
    public string Sha256 { get; set; } = "";

    public string LocalSha256 { get; set; } = "";

    public string RemoteSha256 { get; set; } = "";

    public string BaselineSha256 { get; set; } = "";

    public SourceSyncState SyncState { get; set; } = SourceSyncState.LocalOnly;

    public SourceSyncAction PlannedAction { get; set; } = SourceSyncAction.Skip;

    public string? RemoteFileId { get; set; }

    public string? RemoteFileName { get; set; }

    public DateTimeOffset? LastPushedAt { get; set; }

    public DateTimeOffset? LastPulledAt { get; set; }

    /// <summary>When the user confirmed this file was uploaded to the ChatGPT Project (manual mode).</summary>
    public DateTimeOffset? ManuallyPublishedAt { get; set; }

    /// <summary>Local SHA-256 at the time of manual publish confirmation.</summary>
    public string? ManuallyPublishedSha256 { get; set; }

    public DateTimeOffset? LastRemoteProbedAt { get; set; }

    public string? LastRemoteProbeSha256 { get; set; }

    public string? RemoteProbeFileId { get; set; }

    public RemoteProbeMatch RemoteProbeMatch { get; set; } = RemoteProbeMatch.Unknown;

    public List<SectionManifestEntry> Sections { get; set; } = [];

    /// <summary>Section body hashes at last manual publish (for SectionDiffService).</summary>
    public Dictionary<string, string> PublishedSectionHashes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string EffectiveLocalSha256 =>
        !string.IsNullOrEmpty(LocalSha256) ? LocalSha256 : Sha256;

    public bool IsManuallyPublished =>
        ManuallyPublishedAt is not null
        && !string.IsNullOrEmpty(ManuallyPublishedSha256);

    public bool IsManuallyCurrent() =>
        IsManuallyPublished
        && string.Equals(
            ManuallyPublishedSha256,
            EffectiveLocalSha256,
            StringComparison.OrdinalIgnoreCase);

    public bool NeedsManualRepublish =>
        !IsManuallyPublished
        || !IsManuallyCurrent();
}

public sealed class SourceSyncPlan
{
    public List<SourceSyncPlanItem> Items { get; set; } = [];

    public List<GizmoFileRef> DetectedRemoteFiles { get; set; } = [];

    public List<GizmoFileRef> UnmatchedRemoteFiles { get; set; } = [];

    public List<GizmoFileRef> ListedNotDownloadableFiles { get; set; } = [];

    public int StaleBindingsCleared { get; set; }

    public bool SyncBlocked { get; set; }

    public string? SyncBlockReason { get; set; }

    public DateTimeOffset? PreflightPassedAt { get; set; }

    public bool CanaryPassed { get; set; }

    public string? PreflightGizmoId { get; set; }

    public int ConflictCount => Items.Count(i => i.Entry.SyncState == SourceSyncState.Conflict);

    public int AutoApplicableCount => Items.Count(i =>
        i.Entry.PlannedAction is SourceSyncAction.Pull or SourceSyncAction.PushReplace);
}

public sealed class SourceSyncPlanItem
{
    public required SourceManifestEntry Entry { get; init; }

    public SourceConflictResolution Resolution { get; set; } = SourceConflictResolution.None;
}
