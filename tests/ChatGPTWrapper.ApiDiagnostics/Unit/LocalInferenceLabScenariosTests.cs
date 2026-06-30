using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class LocalInferenceLabScenariosTests
{
    [Fact]
    public void All_includes_pronoun_tracking_entity_extraction_and_diagnostics()
    {
        var ids = LocalInferenceLabScenarios.All.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(LocalInferenceLabScenarios.PronounTrackingId, ids);
        Assert.Contains(LocalInferenceLabScenarios.EntityExtractionId, ids);
        Assert.Contains(LocalInferenceLabScenarios.CustomId, ids);
        Assert.Contains(LocalInferenceLabDiagnosticScenarios.DiagEntityProposalsId, ids);
        Assert.Contains(LocalInferenceLabDiagnosticScenarios.DiagProcessTurnBundleId, ids);
        Assert.True(LocalInferenceLabScenarios.All.Count >= 13);
    }

    [Fact]
    public void Diagnostic_scenarios_request_json_audit_report()
    {
        Assert.Contains("verdict", LocalInferenceLabDiagnosticScenarios.DiagReportOutputContract, StringComparison.Ordinal);
        Assert.Contains("compliance", LocalInferenceLabDiagnosticScenarios.DiagReportOutputContract, StringComparison.Ordinal);

        Assert.True(LocalInferenceLabScenarios.TryGet(
            LocalInferenceLabDiagnosticScenarios.DiagMemoryProposalsId,
            out var memoryDiag));
        Assert.True(memoryDiag.JsonObjectResponse);
        Assert.Contains("propose_memories", memoryDiag.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("CHATGPT WORKER RESPONSE", memoryDiag.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void IsDiagnosticScenario_recognizes_diag_prefix()
    {
        Assert.True(LocalInferenceLabScenarios.IsDiagnosticScenario("diag-audit-entities"));
        Assert.False(LocalInferenceLabScenarios.IsDiagnosticScenario("entity-extraction"));
        Assert.False(LocalInferenceLabScenarios.IsDiagnosticScenario(null));
    }

    [Fact]
    public void Pronoun_tracking_prompt_asks_for_referent_resolution()
    {
        Assert.Contains("coreference", LocalInferenceLabScenarios.PronounTrackingSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ambiguous", LocalInferenceLabScenarios.PronounTrackingSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Marta", LocalInferenceLabScenarios.PronounTrackingUserPrompt, StringComparison.Ordinal);
        Assert.Contains("Sister Caldra", LocalInferenceLabScenarios.PronounTrackingUserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGet_returns_scenario_by_id()
    {
        Assert.True(LocalInferenceLabScenarios.TryGet(LocalInferenceLabScenarios.ContinuityCheckId, out var scenario));
        Assert.Equal("Continuity check", scenario.Label);
        Assert.False(string.IsNullOrWhiteSpace(scenario.SystemPrompt));
    }

    [Fact]
    public void IsKnownUserPrompt_recognizes_builtin_samples()
    {
        Assert.True(LocalInferenceLabScenarios.IsKnownUserPrompt(LocalInferenceLabScenarios.EntityExtractionUserPrompt));
        Assert.False(LocalInferenceLabScenarios.IsKnownUserPrompt("totally custom line"));
    }
}
