using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class DesignThreadRotationService
{
    /// <summary>
    /// Archives the active design thread and prepares a fresh registry slot
    /// while keeping the linked Project.
    /// </summary>
    public static void ReleaseDesignThread(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        AdventureThreadRegistryService.EnsureMigrated(bundle);

        AdventureThreadRegistryService.BeginNewActiveThread(bundle, AdventureThreadKind.Design);
        AdventureThreadRegistryService.SyncActiveDesignUtilitySession(bundle);

        var jobId = GenerationJobId.DesignAdventure;
        bundle.Metadata.UtilityConversationLastError = null;
        bundle.Metadata.UtilityJobLastErrors?.Remove(jobId);
        bundle.Metadata.UtilityJobLastErrors?.Remove(GenerationJobId.DesignExtractStep);

        AdventureDesignService.EnsureWorkspace(bundle);
        foreach (var state in bundle.DesignWorkspace.Steps.Values)
            state.StepSeedSent = false;
    }

    public static string BuildStartPacket(AdventureBundle bundle)
    {
        AdventureDesignService.EnsureWorkspace(bundle);

        var jobId = GenerationJobId.DesignAdventure;
        var sequence = GenerationUtilitySessionService.GetNextSequence(bundle.Metadata, jobId);
        var parts = new List<string>
        {
            GenerationJobHandlers.BuildSeedPrompt(bundle, jobId, sequence).Trim(),
            AdventureDesignService.BuildGeneralSeedPrompt(bundle).Trim(),
        };

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static void PersistRelease(AdventureBundle bundle) =>
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);

    public static string FormatStartThreadReadyMessage(string? source, AdventureBundle bundle)
    {
        var where = AdventureNavigationService.DescribeNavigationState(
            source,
            bundle,
            AdventureNavigationIntent.Design);
        return "New design thread started.\n\n"
               + "1. In the Design browser tab, click New chat in your Project.\n"
               + "2. Click the ChatGPT composer and press Ctrl+V.\n"
               + "3. Press Send.\n"
               + "4. Click Use this tab as design thread.\n\n"
               + $"The start packet is on your clipboard (page: {where}). "
               + "Draft prompts will use the new chat after you pin.";
    }

    public static string FormatThreadStatus(AdventureBundle bundle) =>
        AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.Design);
}
