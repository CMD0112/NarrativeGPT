using System.Text.Json;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Apply-path guards separating canon profile from play state (CMD-467).</summary>
internal static class EntityCanonStateGuardService
{
    public static bool TryValidateStatePatch(JsonElement stateEl, out string? rejectionReason)
    {
        if (ContainsForbiddenCanonKeys(stateEl, out var key))
        {
            rejectionReason = $"State patch must not include canon profile field '{key}'.";
            return false;
        }

        rejectionReason = null;
        return true;
    }

    public static bool TryValidateEntityExtractProposal(string proposedChangeJson, out string? rejectionReason)
    {
        if (string.IsNullOrWhiteSpace(proposedChangeJson))
        {
            rejectionReason = null;
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(proposedChangeJson);
            if (ContainsStateShapedKeys(doc.RootElement, out var key))
            {
                rejectionReason = $"Entity extraction must not include internal state block '{key}'. Use propose_entity_state.";
                return false;
            }
        }
        catch (JsonException)
        {
            rejectionReason = null;
            return true;
        }

        rejectionReason = null;
        return true;
    }

    private static bool ContainsForbiddenCanonKeys(JsonElement element, out string? foundKey)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (EntityCanonStateOverlapService.CanonOnlyJsonKeys.Contains(prop.Name))
                {
                    foundKey = prop.Name;
                    return true;
                }

                if (EntityCanonStateOverlapService.LooksLikeStateBlockKey(prop.Name))
                {
                    if (ContainsForbiddenCanonKeys(prop.Value, out foundKey))
                        return true;

                    continue;
                }

                if (ContainsForbiddenCanonKeys(prop.Value, out foundKey))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsForbiddenCanonKeys(item, out foundKey))
                    return true;
            }
        }

        foundKey = null;
        return false;
    }

    private static bool ContainsStateShapedKeys(JsonElement element, out string? foundKey)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            foundKey = null;
            return false;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (EntityCanonStateOverlapService.LooksLikeStateBlockKey(prop.Name))
            {
                foundKey = prop.Name;
                return true;
            }
        }

        foundKey = null;
        return false;
    }
}
