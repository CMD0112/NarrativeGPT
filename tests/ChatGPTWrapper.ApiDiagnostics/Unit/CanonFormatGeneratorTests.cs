using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class CanonFormatGeneratorTests
{
    [Fact]
    public void Generated_output_includes_upload_guidance_sections()
    {
        var output = CanonFormatGenerator.Generate();

        Assert.Contains("## Quick rules", output, StringComparison.Ordinal);
        Assert.Contains("## Upload to Project", output, StringComparison.Ordinal);
        Assert.Contains("## Critical patterns", output, StringComparison.Ordinal);
        Assert.Contains("ChatGPT Project → Files", output, StringComparison.Ordinal);
        Assert.Contains("Do not edit by hand", output, StringComparison.Ordinal);
        Assert.Contains("### Entity field definitions (cast)", output, StringComparison.Ordinal);
        Assert.Contains("| Personality | `personality` |", output, StringComparison.Ordinal);
        Assert.Contains("| Author guidance | `useInPlay` |", output, StringComparison.Ordinal);
        Assert.Contains("### Custom and extended fields", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_output_includes_all_registry_kind_labels()
    {
        var output = CanonFormatGenerator.Generate();

        Assert.Contains("Condition:", output, StringComparison.Ordinal);
        Assert.Contains("Relationship:", output, StringComparison.Ordinal);
        Assert.Contains("**Name:**", output, StringComparison.Ordinal);
        Assert.Contains("**Background:**", output, StringComparison.Ordinal);

        foreach (var kind in CanonSchemaRegistry.AllKinds)
        {
            foreach (var field in kind.Fields.Where(f => f.Role != CanonFieldRole.Shell
                                                          && f.Format != CanonFieldFormat.FreeformBody))
            {
                var needle = field.Format == CanonFieldFormat.BoldLine
                    ? $"**{field.Label}:**"
                    : $"{field.Label}:";
                Assert.Contains(needle, output, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void CanonFormatTemplate_delegates_to_generator()
    {
        Assert.Equal(CanonFormatGenerator.Generate(), CanonFormatTemplate.Content);
    }
}

public sealed class CanonValidationServiceTests
{
    [Fact]
    public void Valid_party_entry_passes_validation()
    {
        const string cast = """
            ## party
            ### Nessa Vale
            Id: nessa-vale
            Condition: Wounded shoulder
            Relationship: Old friend
            Attitude: Wary but loyal
            Goals: Find her brother
            """;

        var issues = CanonValidationService.ValidateFile("cast.md", cast);
        Assert.DoesNotContain(issues, i => i.Severity == CanonValidationSeverity.Error);
    }

    [Fact]
    public void Party_name_as_first_body_line_is_error()
    {
        const string cast = """
            ## party
            ### Nessa Vale
            Nessa Vale
            Condition: Wounded shoulder
            """;

        var issues = CanonValidationService.ValidateFile("cast.md", cast);
        Assert.Contains(issues, i =>
            i.Severity == CanonValidationSeverity.Error
            && i.Message.Contains("name-as-first-body-line", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unknown_label_in_npc_body_is_warning()
    {
        const string cast = """
            ## npcs
            ### Test NPC
            Id: test-npc
            UnknownField: value
            Role: Merchant
            """;

        var issues = CanonValidationService.ValidateFile("cast.md", cast);
        Assert.Contains(issues, i =>
            i.Severity == CanonValidationSeverity.Warning
            && i.Message.Contains("Unknown label", StringComparison.OrdinalIgnoreCase));
    }
}
