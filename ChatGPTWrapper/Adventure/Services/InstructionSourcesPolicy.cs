using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Delegation rules for Project custom instructions vs source files.
/// See docs/instruction-sources-paradigm.md.
/// </summary>
internal static class InstructionSourcesPolicy
{
    public const int ThinTranscriptTurnCount = 6;

    public static string BuildStaticInstructionsBody(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;
        var parts = new List<string>
        {
            "You are the narrator for an interactive fiction adventure in this Project.",
            "Use uploaded project sources as canonical world material.",
            "Do not break character or mention being an AI.",
            $"Perspective: {settings.Perspective}. Tense: {settings.Tense}. Detail: {settings.DetailLevel}.",
        };

        if (!string.IsNullOrWhiteSpace(settings.Difficulty))
            parts.Add("Difficulty: " + settings.Difficulty.Trim());

        if (!string.IsNullOrWhiteSpace(settings.ViolenceLevel))
            parts.Add("Violence level: " + settings.ViolenceLevel.Trim());

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.AuthorsNote))
            parts.Add("Author's note (style only): " + bundle.Scenario.AuthorsNote.Trim());

        if (!string.IsNullOrWhiteSpace(settings.Tone))
            parts.Add("Tone: " + settings.Tone);

        var contractSections = InstructionContractService.BuildContractSections(bundle);
        if (!string.IsNullOrWhiteSpace(contractSections))
            parts.Add(contractSections);

        return string.Join("\n\n", parts);
    }

    public static string BuildInstructionsSnippet(AdventureBundle bundle) =>
        BuildStaticInstructionsBody(bundle);

    public static string ComputeInstructionDomainHash(AdventureBundle bundle)
    {
        var canonical = InstructionContractService.BuildInstructionDomainCanonical(bundle);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static bool InstructionDomainChanged(AdventureBundle bundle)
    {
        var hash = ComputeInstructionDomainHash(bundle);
        return !string.Equals(bundle.Metadata.LastProjectInstructionsSyncedHash, hash, StringComparison.OrdinalIgnoreCase);
    }

    public static void RecordInstructionsSynced(AdventureBundle bundle)
    {
        bundle.Metadata.LastProjectInstructionsSyncedAt = DateTimeOffset.UtcNow;
        bundle.Metadata.LastProjectInstructionsSyncedHash = ComputeInstructionDomainHash(bundle);
    }

    public static void RecordInstructionsManuallyPublished(AdventureBundle bundle)
    {
        bundle.Metadata.InstructionsManuallyPublishedAt = DateTimeOffset.UtcNow;
        bundle.Metadata.InstructionsManuallyPublishedHash = ComputeInstructionDomainHash(bundle);
    }

    public static bool InstructionsManuallyCurrent(AdventureBundle bundle)
    {
        var hash = ComputeInstructionDomainHash(bundle);
        return string.Equals(bundle.Metadata.InstructionsManuallyPublishedHash, hash, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatInstructionsManuallyPublished(AdventureBundle bundle)
    {
        if (bundle.Metadata.InstructionsManuallyPublishedAt is not { } at)
            return "Not marked pasted";

        if (!InstructionsManuallyCurrent(bundle))
            return $"Pasted {at.LocalDateTime:g} (instructions changed since)";

        return $"Pasted {at.LocalDateTime:g}";
    }

    public static string FormatInstructionSyncStatus(AdventureBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return "";

        if (InstructionDomainChanged(bundle))
            return "Instructions: drift";

        if (bundle.Metadata.LastProjectInstructionsSyncedAt is { } syncedAt)
            return $"Instructions: synced {syncedAt.LocalDateTime:g}";

        return "Instructions: not pushed";
    }

    public static string FormatInstructionDriftHint(AdventureBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return "";

        if (bundle.Metadata.Settings.SourcePublishMode == SourcePublishMode.Manual)
        {
            if (!InstructionDomainChanged(bundle))
                return "Instructions: copy to ChatGPT Project settings when you change perspective, tone, or boundaries.";

            return "Instructions changed locally — use Copy instructions and paste into your ChatGPT Project settings.";
        }

        if (!InstructionDomainChanged(bundle))
        {
            if (bundle.Metadata.LastProjectInstructionsSyncedAt is { } at)
                return $"Project instructions up to date (last push {at.LocalDateTime:g}).";
            return "Project instructions not pushed yet — link with Push narrator instructions or enable auto-sync.";
        }

        return "Project instructions may be stale — instruction-domain fields changed since last push. "
               + "Enable auto-sync on OK or re-link with Push narrator instructions.";
    }
}
