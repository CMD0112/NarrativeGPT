namespace ChatGPTWrapper.Adventure.Models;

/// <summary>How play utility jobs choose between play injection and utility worker lane.</summary>
public enum UtilityExecutionPolicy
{
    /// <summary>Auto → play injection; manual → worker when capabilities green.</summary>
    PlayInjectionPreferred,

    /// <summary>Manual and heavy jobs prefer worker; auto still injection unless spill.</summary>
    WorkerPreferred,

    /// <summary>All data jobs → worker when green; player-utility still play.</summary>
    WorkerOnly,
}
