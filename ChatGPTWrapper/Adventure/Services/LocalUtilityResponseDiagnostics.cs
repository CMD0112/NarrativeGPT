using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed record LocalResponseAssessment(
    bool Parseable,
    bool ProposalsParsed,
    string ComplianceLabel,
    string? ComplianceHint,
    string ExpectedShapeSummary);

/// <summary>Exploratory diagnostics for local LLM utility responses (dual-run compare tuning).</summary>
internal static class LocalUtilityResponseDiagnostics
{
    public static LocalResponseAssessment Assess(string jobId, string? responseText, int proposalCount = 0)
    {
        var expected = DescribeExpectedShape(jobId);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new LocalResponseAssessment(
                Parseable: false,
                ProposalsParsed: false,
                ComplianceLabel: "empty",
                ComplianceHint: "Local model returned no text.",
                ExpectedShapeSummary: expected);
        }

        var parseable = GenerationJobHandlers.IsParseableJobResponse(jobId, responseText);
        if (parseable && proposalCount > 0)
        {
            return new LocalResponseAssessment(
                Parseable: true,
                ProposalsParsed: true,
                ComplianceLabel: "compliant",
                ComplianceHint: null,
                ExpectedShapeSummary: expected);
        }

        if (parseable && proposalCount == 0)
        {
            return new LocalResponseAssessment(
                Parseable: true,
                ProposalsParsed: false,
                ComplianceLabel: "parseable_empty",
                ComplianceHint: "JSON matched the contract but produced zero proposals (empty array or missing required fields).",
                ExpectedShapeSummary: expected);
        }

        var hint = InferComplianceGap(jobId, responseText);
        return new LocalResponseAssessment(
            Parseable: false,
            ProposalsParsed: false,
            ComplianceLabel: "schema_mismatch",
            ComplianceHint: hint,
            ExpectedShapeSummary: expected);
    }

    public static string DescribeExpectedShape(string jobId) => jobId switch
    {
        GenerationJobId.BootstrapSections or GenerationJobId.ExpandSection =>
            "Wrapped entity array, e.g. {\"entities\":[{\"name\":\"…\",\"entityType\":\"place|person|concept\",\"description\":\"…\",\"aliases\":[\"…\"]}]}",
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity =>
            "Wrapped entity array, e.g. {\"entities\":[{\"name\":\"…\",\"entityType\":\"…\",\"description\":\"…\"}]}",
        GenerationJobId.ProposeMemories =>
            "Wrapped memory array, e.g. {\"memories\":[{\"text\":\"…\",\"tags\":[],\"pinned\":false}]}",
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard =>
            "Wrapped card array, e.g. {\"items\":[{\"name\":\"…\",\"type\":\"Place\",\"triggers\":[],\"content\":\"…\"}]}",
        GenerationJobId.ProposeSourceEdits =>
            "Wrapped edit array, e.g. {\"proposals\":[{\"targetFile\":\"cast.md\",\"operation\":\"append\",\"content\":\"…\",\"rationale\":\"…\"}]}",
        GenerationJobId.ProcessTurn =>
            "Single object with optional keys: memories (array), entities (array), summary (string).",
        GenerationJobId.ContinuityCheck =>
            "Single object: {\"warnings\":[{\"message\":\"…\",\"severity\":\"info|warning|high\"}]}",
        GenerationJobId.UpdateSummary =>
            "Plain rolling summary text (no JSON).",
        _ => "Follow the job packet and RESPONSE FORMAT block.",
    };

    private static string? InferComplianceGap(string jobId, string responseText)
    {
        var payload = TryExtractJsonPayload(responseText);
        if (string.IsNullOrWhiteSpace(payload))
            return "Response could not be normalized to JSON or plain text.";

        if (GenerationJobHandlers.ExpectsPlainTextResponse(jobId))
            return "Expected plain text summary, not JSON.";

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                if (GenerationJobHandlers.ExpectsJsonArrayResponse(jobId))
                    return "Expected a JSON object wrapping the array (local json_object mode), not a bare array.";
                return "Expected a JSON object.";
            }

            if (GenerationJobHandlers.ExpectsJsonArrayResponse(jobId))
            {
                if (LooksLikeCanonFieldSheet(root))
                {
                    return "Returned a labeled canon field sheet (Relationship, Secrets, Setting, …) "
                           + "instead of an entity array with name / entityType / description.";
                }

                var wrapper = ResolveArrayWrapperKey(jobId);
                if (!root.TryGetProperty(wrapper, out _))
                {
                    return $"Expected a wrapped array under \"{wrapper}\" — not top-level canon or scenario fields.";
                }
            }

            return "JSON parsed but did not match the job parser contract.";
        }
        catch (JsonException)
        {
            return "Response is not valid JSON.";
        }
    }

    private static string ResolveArrayWrapperKey(string jobId) => jobId switch
    {
        GenerationJobId.ProposeMemories => "memories",
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard => "items",
        GenerationJobId.ProposeSourceEdits => "proposals",
        _ => "entities",
    };

    private static bool LooksLikeCanonFieldSheet(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        var canonKeys = 0;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.String)
            {
                if (property.Name is "Relationship" or "Secrets" or "Setting" or "Role" or "Theories"
                    or "Tone" or "Weaknesses" or "relationship" or "secrets" or "setting")
                    canonKeys++;
            }
        }

        return canonKeys >= 2 && !root.TryGetProperty("name", out _);
    }

    private static string? TryExtractJsonPayload(string responseText)
    {
        var objectJson = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (!string.IsNullOrWhiteSpace(objectJson))
            return objectJson;

        var arrayJson = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        return string.IsNullOrWhiteSpace(arrayJson) ? responseText.Trim() : arrayJson;
    }
}
