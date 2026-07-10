using System.Text;

namespace ChatGPTWrapper.Core.LocalInference;

/// <summary>Builds diagnostic user/system prompts from verbatim adventure file attachments.</summary>
public static class LocalInferenceLabDiagnosticPromptComposer
{
    public const string CanonComparisonInstructions = """
        Attached files are verbatim wrapper-hosted JSON from the adventure folder.
        When auditing worker output:
        - Compare proposals against accepted canon (Characters, Locations, Entries, RollingSummary, etc.)
        - ReviewQueue arrays in entities.json, memory.json, cards.json, and summary.json hold pending proposals not yet accepted
        - Flag duplicate creates for referents already in accepted lists
        - Flag proposals that repeat ReviewQueue items
        - For utility-results/*.json, read rawResponse and parsedPayload; note lane, linkedTurnIndex, and errors
        """;

    public static string BuildUserPrompt(string diagnosticScenarioId, LocalInferenceLabAdventureAttachments attachments)
    {
        var jobId = attachments.JobId ?? ResolveJobId(diagnosticScenarioId);
        var sb = new StringBuilder();
        sb.AppendLine($"jobId: {jobId}");
        sb.AppendLine($"adventure: {attachments.AdventureTitle} ({attachments.AdventureId:N})");
        sb.AppendLine($"directory: {attachments.DirectoryPath}");
        if (attachments.TurnIndex is int turnIndex)
            sb.AppendLine($"focus turn slice: log.json#turn/{turnIndex}");
        if (attachments.UtilityRunId is Guid runId)
            sb.AppendLine($"worker capture: utility-results/{runId}.json");
        sb.AppendLine();
        sb.AppendLine("The following sections are exact file contents from disk (not summarized):");
        sb.AppendLine();

        foreach (var file in attachments.Files)
        {
            sb.AppendLine($"=== FILE: {file.RelativePath} ({file.ByteLength} bytes) ===");
            sb.AppendLine(file.Content);
            sb.AppendLine();
        }

        sb.Append(
            "Audit the worker response in the attached utility-results file (if present) against the canon files above.");
        return sb.ToString().TrimEnd();
    }

    public static string AppendCanonInstructions(string systemPrompt) =>
        string.IsNullOrWhiteSpace(systemPrompt)
            ? CanonComparisonInstructions
            : systemPrompt.TrimEnd() + "\n\n" + CanonComparisonInstructions;

    private static string ResolveJobId(string diagnosticScenarioId) =>
        diagnosticScenarioId switch
        {
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagEntityProposalsId, StringComparison.OrdinalIgnoreCase)
                => "extract_entities",
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagMemoryProposalsId, StringComparison.OrdinalIgnoreCase)
                => "propose_memories",
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagSummaryProposalId, StringComparison.OrdinalIgnoreCase)
                => "update_summary",
            _ when string.Equals(diagnosticScenarioId, LocalInferenceLabDiagnosticScenarios.DiagProcessTurnBundleId, StringComparison.OrdinalIgnoreCase)
                => "process_turn",
            _ => "unknown",
        };
}
