using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class UtilitySourceFileIoServiceTests
{
    [Fact]
    public void TryExtractDelimitedBlock_matches_exact_filename()
    {
        const string response = """
            Here is the revised file.

            --- begin output.md ---
            # Hello
            body
            --- end output.md ---
            """;

        var extracted = UtilitySourceFileIoService.TryExtractDelimitedBlock(response, "output.md");
        Assert.NotNull(extracted);
        Assert.Contains("# Hello", extracted, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractDelimitedBlock_matches_prefixed_path()
    {
        const string response = """
            --- begin sources/job-output.md ---
            line one
            --- end sources/job-output.md ---
            """;

        var extracted = UtilitySourceFileIoService.TryExtractDelimitedBlock(response, "job-output.md");
        Assert.Equal("line one", extracted);
    }

    [Fact]
    public void HasCompleteDelimitedDelivery_false_when_block_missing()
    {
        Assert.False(UtilitySourceFileIoService.HasCompleteDelimitedDelivery("no block here", "out.md"));
    }

    [Fact]
    public void BuildDelimitedOutputDeliveryBlock_includes_filename_and_critical_note()
    {
        var block = UtilitySourceFileIoService.BuildDelimitedOutputDeliveryBlock(
            "entities.json",
            "sources/My Adventure - entities.json",
            "- Preserve schemaVersion");

        Assert.Contains("entities.json", block, StringComparison.Ordinal);
        Assert.Contains("--- begin entities.json ---", block, StringComparison.Ordinal);
        Assert.Contains("CRITICAL", block, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSourceRetrieveLine_uses_normalized_path()
    {
        var line = UtilitySourceFileIoService.BuildSourceRetrieveLine(@"sources\foo.md");
        Assert.Contains("sources/foo.md", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractAllDelimitedBlocks_returns_multiple_blocks()
    {
        const string response = """
            --- begin a.md ---
            A
            --- end a.md ---
            --- begin b.md ---
            B
            --- end b.md ---
            """;

        var blocks = UtilitySourceFileIoService.ExtractAllDelimitedBlocks(response);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("A", blocks[0].Content);
        Assert.Equal("B", blocks[1].Content);
    }

    [Fact]
    public void StripOptionalCodeFence_removes_json_fence()
    {
        const string fenced = """
            ```json
            { "ok": true }
            ```
            """;

        var stripped = UtilitySourceFileIoService.StripOptionalCodeFence(fenced);
        Assert.Contains("\"ok\"", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("```", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildE2eJobPacket_includes_sources_pointer_and_utility_wrapper()
    {
        const string gizmo = "g-p-test";
        const string token = "abc123";
        const string remote = "sources/cgw-utility-io/diag/source-io-e2e/abc123/in/diagnostic.md";
        var packet = UtilitySourceFileIoService.BuildE2eJobPacket(gizmo, remote, token, Guid.Parse("4e8faadf-e4af-403d-9686-ede4870f6acf"));

        Assert.Contains("[[cgw:utility", packet, StringComparison.Ordinal);
        Assert.Contains("job=\"source_io_e2e\"", packet, StringComparison.Ordinal);
        Assert.Contains("[[cgw:sources v=\"2\" mode=\"utility-worker\"]]", packet, StringComparison.Ordinal);
        Assert.Contains($"Retrieve from {remote}", packet, StringComparison.Ordinal);
        Assert.Contains(UtilitySourceFileIoService.BuildE2eOutputFileName(token), packet, StringComparison.Ordinal);
        Assert.Contains($"--- begin {UtilitySourceFileIoService.BuildE2eOutputFileName(token)} ---", packet, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractE2eOutput_parses_utility_response_wrapper()
    {
        const string token = "tok1";
        var outputName = UtilitySourceFileIoService.BuildE2eOutputFileName(token);
        var response = $"""
            [[cgw:utility-response job="source_io_e2e" v="1"]]
            --- begin {outputName} ---
            # diag
            E2E confirmed: {token}
            --- end {outputName} ---
            [[/cgw:utility-response]]
            """;

        var extracted = UtilitySourceFileIoService.TryExtractE2eOutput(response, token);
        Assert.NotNull(extracted);
        Assert.True(UtilitySourceFileIoService.E2eOutputContainsToken(extracted, token));
    }
}
