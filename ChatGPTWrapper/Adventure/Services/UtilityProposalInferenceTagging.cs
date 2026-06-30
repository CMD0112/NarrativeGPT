using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityProposalInferenceTagging
{
    public static void TagEntity(EntityReviewItem item, GenerationJobContext? context)
    {
        if (context is null)
            return;

        item.InferenceSource = context.InferenceSource;
        item.UtilityRunId = context.UtilityRunId;
    }

    public static void TagMemory(MemoryEntry entry, GenerationJobContext? context)
    {
        if (context is null)
            return;

        entry.InferenceSource = context.InferenceSource;
        entry.UtilityRunId = context.UtilityRunId;
    }

    public static void TagCard(CardReviewItem item, GenerationJobContext? context)
    {
        if (context is null)
            return;

        item.InferenceSource = context.InferenceSource;
        item.UtilityRunId = context.UtilityRunId;
    }

    public const string ChatGptUtilityFilter = "chatgpt-utility";

    public static bool IsLocalSource(string? inferenceSource) =>
        string.Equals(inferenceSource, UtilityLane.LocalLlm, StringComparison.OrdinalIgnoreCase);

    /// <summary>Any ChatGPT utility lane, including legacy untagged proposals (null source).</summary>
    public static bool IsChatGptUtilitySource(string? inferenceSource) =>
        !IsLocalSource(inferenceSource);

    public static bool MatchesSourceFilter(string? itemSource, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)
            || string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(filter, UtilityLane.LocalLlm, StringComparison.OrdinalIgnoreCase))
            return IsLocalSource(itemSource);

        if (string.Equals(filter, ChatGptUtilityFilter, StringComparison.OrdinalIgnoreCase))
            return IsChatGptUtilitySource(itemSource);

        if (string.Equals(filter, UtilityLane.PlayLegacyInline, StringComparison.OrdinalIgnoreCase))
        {
            return IsChatGptUtilitySource(itemSource)
                   && (string.IsNullOrWhiteSpace(itemSource)
                       || string.Equals(itemSource, UtilityLane.PlayLegacyInline, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(itemSource, UtilityLane.PlayInjection, StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals(filter, UtilityLane.Worker, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(itemSource, UtilityLane.Worker, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(itemSource, filter, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatSourceLabel(string? inferenceSource) =>
        inferenceSource switch
        {
            UtilityLane.LocalLlm => "Local LLM",
            UtilityLane.PlayLegacyInline => "ChatGPT inline",
            UtilityLane.Worker => "ChatGPT worker",
            UtilityLane.PlayInjection => "ChatGPT play bundle",
            null or "" => "ChatGPT",
            _ => inferenceSource,
        };
}
