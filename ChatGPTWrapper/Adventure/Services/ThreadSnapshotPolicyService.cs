using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ThreadSnapshotPolicyService
{
    public static ThreadSnapshotSettings Resolve(AdventureBundle bundle) =>
        Resolve(bundle.Metadata.Settings.ThreadSnapshot);

    public static ThreadSnapshotSettings Resolve(ThreadSnapshotSettings? settings) =>
        settings ?? new ThreadSnapshotSettings();

    public static bool ShouldCapture(AdventureBundle bundle, string trigger) =>
        ShouldCapture(Resolve(bundle), trigger);

    public static bool ShouldCapture(ThreadSnapshotSettings settings, string trigger) =>
        trigger switch
        {
            ThreadConversationLogSnapshotTrigger.Send => settings.CaptureOnSend,
            ThreadConversationLogSnapshotTrigger.Invalidation => settings.CaptureOnInvalidation,
            ThreadConversationLogSnapshotTrigger.SessionLoad => settings.CaptureOnSessionLoad,
            ThreadConversationLogSnapshotTrigger.WorkerSend => settings.CaptureOnWorkerSend,
            ThreadConversationLogSnapshotTrigger.Manual => true,
            ThreadConversationLogSnapshotTrigger.Migration => true,
            _ => false,
        };

    public static ThreadSnapshotCaptureRequest? TryCreateRequest(
        AdventureBundle bundle,
        string trigger,
        ThreadSnapshotCorrelation? correlation = null)
    {
        if (!ShouldCapture(bundle, trigger))
            return null;

        return new ThreadSnapshotCaptureRequest
        {
            CaptureTrigger = trigger,
            Correlation = correlation,
        };
    }
}
