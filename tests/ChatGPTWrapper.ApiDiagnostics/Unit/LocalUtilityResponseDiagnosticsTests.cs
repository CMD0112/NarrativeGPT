using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class LocalUtilityResponseDiagnosticsTests
{
    [Fact]
    public void Assess_detects_canon_field_sheet_mismatch_for_bootstrap_sections()
    {
        var response = """
            {
              "Relationship": { "Garran Holt": ["Mara"] },
              "Secrets": ["Hidden crown"],
              "Setting": { "Location": "Greyford" },
              "Tone": "Dark"
            }
            """;

        var assessment = LocalUtilityResponseDiagnostics.Assess(
            GenerationJobId.BootstrapSections,
            response);

        Assert.Equal("schema_mismatch", assessment.ComplianceLabel);
        Assert.False(assessment.Parseable);
        Assert.Contains("canon field sheet", assessment.ComplianceHint!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_marks_wrapped_entity_array_compliant()
    {
        var response = """
            {"entities":[{"name":"Greyford Gate","entityType":"place","description":"Checkpoint","aliases":["gate"]}]}
            """;

        var assessment = LocalUtilityResponseDiagnostics.Assess(
            GenerationJobId.BootstrapSections,
            response,
            proposalCount: 1);

        Assert.Equal("compliant", assessment.ComplianceLabel);
        Assert.True(assessment.Parseable);
    }
}

[Trait("Category", "Unit")]
public sealed class UtilityJobPromptBuilderLocalProfileTests
{
    [Fact]
    public void BuildLocalCoreJobBody_omits_canon_format_reference_for_bootstrap_sections()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = new AdventureSettings() },
            Scenario = new ScenarioDocument
            {
                Genre = "Fantasy",
                Setting = "Greyford",
                PlayerRole = "Spellblade",
                OpeningSituation = "Returns to a guarded gate",
                PlotEssentials = "War aftermath",
                WorldRules = "Crownward binds soldiers",
            },
        };

        var context = new GenerationJobContext();
        var remote = UtilityJobPromptBuilder.BuildCoreJobBody(
            bundle,
            GenerationJobId.BootstrapSections,
            context);
        var local = UtilityJobPromptBuilder.BuildLocalCoreJobBody(
            bundle,
            GenerationJobId.BootstrapSections,
            context);

        Assert.Contains("CANON FORMAT REFERENCE", remote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CANON FORMAT REFERENCE", local, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not return labeled canon field sheets", local, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeLocalResponseFormat_requests_wrapped_entities_for_bootstrap_sections()
    {
        var format = UtilityJobPromptBuilder.DescribeLocalResponseFormat(GenerationJobId.BootstrapSections);

        Assert.Contains("\"entities\"", format, StringComparison.Ordinal);
        Assert.Contains("Greyford Gate", format, StringComparison.Ordinal);
        Assert.DoesNotContain("Relationship", format, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesStructuredJsonResponse_includes_array_jobs()
    {
        Assert.True(UtilityJobPromptBuilder.UsesStructuredJsonResponse(GenerationJobId.BootstrapSections));
        Assert.True(UtilityJobPromptBuilder.UsesStructuredJsonResponse(GenerationJobId.ProcessTurn));
        Assert.False(UtilityJobPromptBuilder.UsesStructuredJsonResponse(GenerationJobId.UpdateSummary));
    }
}
