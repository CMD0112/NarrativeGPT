using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class TranscriptTextSanitizerTests
{
    [Fact]
    public void Sanitize_strips_filecite_private_use_markers()
    {
        const string raw = "Only rain. \uE200filecite\uE202turn2file1\uE201";
        var cleaned = TranscriptTextSanitizer.Sanitize(raw);

        Assert.DoesNotContain("filecite", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\uE200", cleaned);
        Assert.Contains("Only rain.", cleaned);
    }
}
