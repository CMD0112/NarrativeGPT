using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class UtilitySchemaValidation
{
    public bool Ok { get; init; }

    public string? Payload { get; init; }

    public string? Error { get; init; }

    public static UtilitySchemaValidation Success(string payload) =>
        new() { Ok = true, Payload = payload };

    public static UtilitySchemaValidation Failure(string error) =>
        new() { Ok = false, Error = error };
}

/// <summary>Centralizes utility response contracts and light validation (CMD-330).</summary>
internal static class UtilityResponseSchemaRegistry
{
    public static string AppendResponseContract(string jobBody, string jobId) =>
        ContextTagFormat.AppendInlineUtilityResponseContract(
            jobBody,
            jobId,
            GenerationJobHandlers.ExpectsJsonArrayResponse(jobId),
            GenerationJobHandlers.ExpectsJsonObjectResponse(jobId));

    public static UtilitySchemaValidation Validate(string jobId, string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return UtilitySchemaValidation.Failure("empty_response");

        var payload = ContextTagFormat.UnwrapUtilityJobResponse(responseText);
        if (string.IsNullOrWhiteSpace(payload))
            return UtilitySchemaValidation.Failure("missing_response_body");

        if (GenerationJobHandlers.ExpectsJsonArrayResponse(jobId)
            || GenerationJobHandlers.ExpectsJsonObjectResponse(jobId))
        {
            try
            {
                using var _ = JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                return UtilitySchemaValidation.Failure("invalid_json");
            }
        }

        return UtilitySchemaValidation.Success(payload);
    }
}
