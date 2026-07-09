namespace ChatGPTWrapper.Adventure.Models;

/// <summary>
/// Per-adventure controls for automatic explicit thread branch snapshots
/// (<c>thread-logs/{id}/snapshots/</c>). Rolling log sync is unaffected.
/// </summary>
public sealed class ThreadSnapshotSettings
{
    /// <summary>Capture after verified play send completes and rolling sync runs.</summary>
    public bool CaptureOnSend { get; set; } = true;

    /// <summary>Capture after overlay edit, regenerate, or tail invalidation sync.</summary>
    public bool CaptureOnInvalidation { get; set; } = true;

    /// <summary>Capture when a play or design session loads and reconciles the thread log.</summary>
    public bool CaptureOnSessionLoad { get; set; } = true;

    /// <summary>Capture after utility worker thread sync completes.</summary>
    public bool CaptureOnWorkerSend { get; set; } = true;
}
