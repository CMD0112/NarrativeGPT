using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.Adventure.Services;

internal static class LocalUtilityInferencePolicy
{
  public static bool IsEnabled(AdventureBundle bundle)
  {
    SyncTransportFromDisk(bundle);
    return bundle.Metadata.Settings.LocalUtilityInference.Enabled;
  }

  public static bool IsDualRun(AdventureBundle bundle) =>
    IsEnabled(bundle) && bundle.Metadata.Settings.LocalUtilityInference.DualRun;

  public static bool SupportsJob(string jobId) =>
    UtilityJobPromptBuilder.IsComparablePlayAiTool(jobId);

  public static bool HasStagedWorkerAttachments(
      GenerationJobContext? context = null,
      UtilityOutboxEntry? entry = null) =>
    entry?.Attachments is { Count: > 0 }
    || context?.JobAttachments is { HasAttachments: true };

  /// <summary>Run the local inference leg (exclusive or dual).</summary>
  public static bool ShouldRunLocalLeg(
      AdventureBundle bundle,
      string jobId,
      GenerationJobContext? context = null,
      UtilityOutboxEntry? entry = null) =>
    !HasStagedWorkerAttachments(context, entry)
    && IsEnabled(bundle)
    && SupportsJob(jobId);

  /// <summary>Local-only routing — skip ChatGPT utility lane on success.</summary>
  public static bool ShouldUseLocalExclusive(
      AdventureBundle bundle,
      string jobId,
      GenerationJobContext? context = null,
      UtilityOutboxEntry? entry = null) =>
    ShouldRunLocalLeg(bundle, jobId, context, entry) && !IsDualRun(bundle);

  public static LocalInferenceOptions ResolveOptions(AdventureBundle bundle)
  {
    SyncTransportFromDisk(bundle);
    var env = LocalInferenceOptions.FromEnvironment();
    var settings = bundle.Metadata.Settings.LocalUtilityInference;
    return new LocalInferenceOptions
    {
      BaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? env.BaseUrl : settings.BaseUrl.Trim(),
      Model = string.IsNullOrWhiteSpace(settings.Model) ? env.Model : settings.Model.Trim(),
      RequestTimeout = env.RequestTimeout,
    };
  }

  private static void SyncTransportFromDisk(AdventureBundle bundle)
  {
    bundle.Metadata.Settings.LocalUtilityInference ??= new LocalUtilityInferenceSettings();
    TransportSettingsStore.SyncFromDisk(bundle);
  }
}
