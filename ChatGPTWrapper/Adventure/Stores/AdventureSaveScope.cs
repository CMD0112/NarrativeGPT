namespace ChatGPTWrapper.Adventure.Stores;

[Flags]
public enum AdventureSaveScope
{
    None = 0,
    Metadata = 1 << 0,
    Scenario = 1 << 1,
    Entities = 1 << 2,
    Log = 1 << 3,
    Summary = 1 << 4,
    State = 1 << 5,
    Memory = 1 << 6,
    Cards = 1 << 7,
    Continuity = 1 << 8,
    PromptHistory = 1 << 9,
    UtilityExchanges = 1 << 10,
    ThreadMetadata = 1 << 11,
    Notes = 1 << 12,
    SourceManifest = 1 << 13,
    ContextIndex = 1 << 14,
    DesignWorkspace = 1 << 15,

    /// <summary>Play settings dialog — never writes entities.json, log.json, or source-manifest.json.</summary>
    PlaySettingsDialog = Metadata
                           | Scenario
                           | Summary
                           | State
                           | Memory
                           | Cards
                           | Continuity
                           | UtilityExchanges
                           | ThreadMetadata,

    /// <summary>Design mode entry — status/workspace only; never overwrites entities or sources.</summary>
    DesignSessionSwitch = Metadata | DesignWorkspace,

    All = Metadata
          | Scenario
          | Entities
          | Log
          | Summary
          | State
          | Memory
          | Cards
          | Continuity
          | PromptHistory
          | UtilityExchanges
          | ThreadMetadata
          | Notes
          | SourceManifest
          | ContextIndex
          | DesignWorkspace,
}
