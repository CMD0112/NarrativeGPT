namespace ChatGPTWrapper.Adventure.Models;

/// <summary>API transport health for the utility worker conversation.</summary>
public sealed class UtilityWorkerCapabilities
{
    public bool ApiFetchOk { get; set; }

    public bool ApiPushOk { get; set; }

    public bool ApiPullOk { get; set; }

    public bool SseReliable { get; set; }

    public bool HostReady { get; set; }

    /// <summary>DOM ping registered the worker when API POST is not yet available.</summary>
    public bool DomRegistrationVerified { get; set; }

    public DateTimeOffset? LastProbedAt { get; set; }

    public string? LastProbeError { get; set; }

    public string? WorkerConversationId { get; set; }

    /// <summary>Phase 0 API attach spike result (e.g. http_403, success).</summary>
    public string? LastApiAttachProbeResult { get; set; }

    public bool IsGreen => IsProductionReady(this);

    /// <summary>Production worker jobs require full API transport — DOM registration alone is insufficient.</summary>
    public static bool IsProductionReady(UtilityWorkerCapabilities? caps) =>
        caps is { HostReady: true, ApiFetchOk: true, ApiPushOk: true, ApiPullOk: true };
}
