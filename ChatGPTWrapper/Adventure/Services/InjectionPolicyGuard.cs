using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Reference-first policy checks for thin/delegated packet assembly (CMD-294).
/// </summary>
internal static class InjectionPolicyGuard
{
    private static readonly string[] ThinInlineContractMarkers =
    [
        "Content boundaries:",
        "Character portrayal:",
        "Portrayal rules:",
        "=== SCENARIO ===",
    ];

    internal static void AssertThinDelegationPolicy(string packetText)
    {
        if (string.IsNullOrWhiteSpace(packetText))
            return;

        foreach (var marker in ThinInlineContractMarkers)
        {
            if (packetText.Contains(marker, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Thin delegated packet must not inline '{marker}'. See docs/adr/injection-policy-adr.md.");
        }
    }

    internal static void EnforceMandatorySections(AdventureSettings settings, bool thinDelegated)
    {
        var policy = PlayInjectionPolicyService.Resolve(settings);
        if (!thinDelegated)
            return;

        if (!policy.IncludeSourcesPointers)
            policy.IncludeSourcesPointers = true;
        if (!policy.IncludeState)
            policy.IncludeState = true;
    }
}
