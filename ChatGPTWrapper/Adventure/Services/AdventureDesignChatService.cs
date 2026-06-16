using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class DesignChatSendResult
{
    public bool Success { get; init; }

    public string? AssistantText { get; init; }

    public string? Error { get; init; }
}

public sealed class DesignSourcePullResult
{
    public bool Success { get; init; }

    public int SavedCount { get; init; }

    public IReadOnlyList<string> SavedPaths { get; init; } = [];

    public string? Error { get; init; }
}

internal static class AdventureDesignChatService
{
    public static bool CanUseChat(AdventureBundle bundle) =>
        AdventureProjectBindingService.HasLinkedProject(bundle);

    public static void RecordUserMessage(AdventureBundle bundle, AdventureDesignStep step, string text) =>
        AdventureDesignService.AddChatMessage(bundle, step, "user", text);

    public static void RecordAssistantMessage(AdventureBundle bundle, AdventureDesignStep step, string text) =>
        AdventureDesignService.AddChatMessage(bundle, step, "assistant", text);

    public static string BuildStepSeedIfNeeded(AdventureBundle bundle, AdventureDesignStep step)
    {
        var state = AdventureDesignService.GetOrCreateStep(bundle, step);
        if (state.StepSeedSent)
            return "";

        state.StepSeedSent = true;
        bundle.DesignWorkspace.UpdatedAt = DateTimeOffset.UtcNow;
        return AdventureDesignService.BuildStepSeedPrompt(bundle, step);
    }

    public static string ResolveOutgoingMessage(AdventureBundle bundle, AdventureDesignStep step, string userText)
    {
        var seed = BuildStepSeedIfNeeded(bundle, step);
        if (string.IsNullOrWhiteSpace(seed))
            return userText.Trim();

        return string.IsNullOrWhiteSpace(userText)
            ? seed
            : seed + Environment.NewLine + Environment.NewLine + userText.Trim();
    }

    /// <summary>
    /// Sends a per-file source draft prompt without prepending the generic step seed.
    /// </summary>
    public static string ResolveSourceFilePromptMessage(AdventureBundle bundle, string promptText)
    {
        var state = AdventureDesignService.GetOrCreateStep(bundle, AdventureDesignStep.Sources);
        state.StepSeedSent = true;
        bundle.DesignWorkspace.UpdatedAt = DateTimeOffset.UtcNow;
        return promptText.Trim();
    }
}
