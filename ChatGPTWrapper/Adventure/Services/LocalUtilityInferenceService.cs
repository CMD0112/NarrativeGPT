using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class LocalUtilityInferenceAttempt
{
  public bool Attempted { get; init; }

  public bool Success { get; init; }

  public string? ResponseText { get; init; }

  public string? Prompt { get; init; }

  public string? PromptHash { get; init; }

  public string? Error { get; init; }

  public static LocalUtilityInferenceAttempt NotAttempted() => new();

  public static LocalUtilityInferenceAttempt Failed(string error) =>
    new() { Attempted = true, Success = false, Error = error };

  public static LocalUtilityInferenceAttempt Succeeded(string prompt, string promptHash, string responseText) =>
    new()
    {
      Attempted = true,
      Success = true,
      Prompt = prompt,
      PromptHash = promptHash,
      ResponseText = responseText,
    };
}

/// <summary>
/// Runs eligible utility jobs against a local OpenAI-compatible server using shared prompt builders.
/// </summary>
internal static class LocalUtilityInferenceService
{
  public static async Task<LocalUtilityInferenceAttempt> TryCompleteAsync(
    AdventureBundle bundle,
    string jobId,
    GenerationJobContext context,
    CancellationToken cancellationToken = default)
  {
    if (!LocalUtilityInferencePolicy.ShouldRunLocalLeg(bundle, jobId, context))
      return LocalUtilityInferenceAttempt.NotAttempted();

    if (!UtilityJobPromptBuilder.HasInstructionGuide(bundle, jobId))
    {
      return LocalUtilityInferenceAttempt.Failed(
        $"local_inference_no_guide:{GenerationJobHandlers.GetUtilityJobId(jobId)}");
    }

    var options = LocalUtilityInferencePolicy.ResolveOptions(bundle);
    var (systemPrompt, userPrompt) = UtilityJobPromptBuilder.BuildLocalInferencePrompts(bundle, jobId, context);
    var promptHash = UtilityMessagePushService.ComputeHash(userPrompt);

    var request = new ChatCompletionRequest
    {
      Model = options.Model,
      Messages =
      [
        ChatMessage.System(systemPrompt),
        ChatMessage.User(userPrompt),
      ],
      Temperature = 0.2,
      JsonObjectResponse = UtilityJobPromptBuilder.UsesStructuredJsonResponse(jobId),
    };

    try
    {
      using var client = new OpenAiCompatibleChatClient(options);
      var completion = await client.CompleteAsync(request, cancellationToken);
      if (!completion.Success || string.IsNullOrWhiteSpace(completion.Content))
        return LocalUtilityInferenceAttempt.Failed(completion.Error ?? "local_inference_empty");

      return LocalUtilityInferenceAttempt.Succeeded(userPrompt, promptHash, completion.Content.Trim());
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      return LocalUtilityInferenceAttempt.Failed(ex.Message);
    }
  }

  internal static string AppendLocalResponseContract(string jobBody, string jobId) =>
    UtilityJobPromptBuilder.AppendLocalResponseContract(jobBody, jobId);
}
