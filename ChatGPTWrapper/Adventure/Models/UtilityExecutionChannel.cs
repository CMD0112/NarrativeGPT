namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Utility job trigger channel for injection-first orchestration.</summary>
public enum UtilityExecutionChannel
{
    AutoBackground,
    ManualBackground,
    WorkerBackground,
}
