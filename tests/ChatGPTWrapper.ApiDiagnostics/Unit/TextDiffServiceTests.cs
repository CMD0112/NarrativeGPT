using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class TextDiffServiceTests
{
    [Fact]
    public void ComputeLineDiff_marks_added_removed_and_unchanged()
    {
        var left = "alpha\nbeta\ngamma";
        var right = "alpha\nBETA\ndelta";

        var diff = TextDiffService.ComputeLineDiff(left, right);

        Assert.Contains(diff, d => d.Kind == DiffLineKind.Unchanged && d.Text == "alpha");
        Assert.Contains(diff, d => d.Kind == DiffLineKind.Removed && d.Text == "beta");
        Assert.Contains(diff, d => d.Kind == DiffLineKind.Added && d.Text == "BETA");
        Assert.Contains(diff, d => d.Kind == DiffLineKind.Removed && d.Text == "gamma");
        Assert.Contains(diff, d => d.Kind == DiffLineKind.Added && d.Text == "delta");
    }

    [Fact]
    public void FormatUnifiedDiff_includes_labels_and_prefixes()
    {
        var diff = TextDiffService.ComputeLineDiff("old\n", "new\n");
        var formatted = TextDiffService.FormatUnifiedDiff(diff, "canonical", "mirror");

        Assert.Contains("--- canonical", formatted);
        Assert.Contains("+++ mirror", formatted);
        Assert.Contains("  old", formatted);
        Assert.Contains("  new", formatted);
    }
}
