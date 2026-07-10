namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Lifecycle state for a utility job run (worker lane + correlated play injection).</summary>
public enum UtilityJobRunState
{
    Queued,
    Pushed,
    Pulling,
    Complete,
    Failed,
}
