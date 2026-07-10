namespace ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;

public enum ProjectPublicationProfile
{
    /// <summary>Diagnostics lab — DOM-first on Snorlax.</summary>
    Lab,

    /// <summary>Batch sync push — API-first repair ladder.</summary>
    BatchSync,

    /// <summary>Utility source I/O — Pure API upload, immediate attach, minimal list confirm.</summary>
    UtilityFast,
}

public enum ProjectPublicationOutcome
{
    Verified,
    Exhausted,
    Cancelled,
}

public enum ProjectPublicationLaneId
{
    BrowserNative,
    HeadlessBrowser,
    PureApi,
    AttachWorker,
    Library,
    RegisterProjectFiles,
}

public enum ProjectPublicationAttemptOutcome
{
    Candidate,
    Verified,
    Failed,
    Skipped,
}
