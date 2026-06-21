using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class CanonSchemaDriftTests
{
    [Fact]
    public void Golden_party_fixture_passes_lint()
    {
        var path = FixturePath("valid-party.md");
        var content = File.ReadAllText(path);
        var issues = CanonValidationService.ValidateFile("cast.md", content);
        Assert.DoesNotContain(issues, i => i.Severity == CanonValidationSeverity.Error);
    }

    [Fact]
    public void Golden_invalid_party_fixture_fails_lint()
    {
        var path = FixturePath("invalid-party-positional.md");
        var content = File.ReadAllText(path);
        var issues = CanonValidationService.ValidateFile("cast.md", content);
        Assert.Contains(issues, i => i.Severity == CanonValidationSeverity.Error);
    }

    [Fact]
    public void Generated_canon_format_is_stable_for_party_labels()
    {
        var output = CanonFormatGenerator.Generate();
        foreach (var label in new[] { "Condition:", "Relationship:", "Attitude:", "Goals:" })
            Assert.Contains(label, output, StringComparison.Ordinal);
    }

    private static string FixturePath(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "canon", name));
}
