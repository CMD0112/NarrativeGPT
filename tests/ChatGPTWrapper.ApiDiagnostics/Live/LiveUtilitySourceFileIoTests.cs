using ChatGPTWrapper.ApiDiagnostics.Reporting;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

[Collection("LiveWebView")]
[Trait("Category", "Live")]
[Trait("Diagnostics", "Logged")]
public sealed class LiveUtilitySourceFileIoTests
{
    private readonly LiveWebViewFixture _fixture;

    public LiveUtilitySourceFileIoTests(LiveWebViewFixture fixture) => _fixture = fixture;

    [LiveFact]
    public async Task Run_utility_source_file_io_checklist()
    {
        var runner = new LiveUtilitySourceFileIoRunner(_fixture.Host);
        UtilitySourceFileIoReport report;
        try
        {
            report = await runner.RunAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Assert.True(
                File.Exists(UtilitySourceFileIoReport.ReportTextPath),
                $"Utility source I/O runner crashed but report was not written: {ex.Message}");
            throw;
        }

        Assert.True(File.Exists(UtilitySourceFileIoReport.ReportJsonPath), "JSON report was not written");
        Assert.True(File.Exists(UtilitySourceFileIoReport.ReportTextPath), "Text report was not written");
        Assert.NotEmpty(report.Steps);

        Assert.True(
            report.PassedCount > 0 || report.FailedCount > 0,
            "Checklist produced no steps.");

        Assert.Equal(0, report.FailedCount);
    }

    [LiveFact]
    public async Task Run_utility_source_file_io_e2e_checklist()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(LiveUtilitySourceFileIoRunner.E2eEnvVar), "1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Environment.GetEnvironmentVariable(LiveUtilitySourceFileIoRunner.E2eEnvVar), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(true, $"Skipped: set {LiveUtilitySourceFileIoRunner.E2eEnvVar}=1 to run E2E gate.");
            return;
        }

        var runner = new LiveUtilitySourceFileIoRunner(_fixture.Host);
        var report = await runner.RunAsync();

        Assert.True(File.Exists(UtilitySourceFileIoReport.ReportJsonPath));
        Assert.Equal(0, report.FailedCount);
        Assert.Equal("pass", report.E2eClassification);
        Assert.False(string.IsNullOrWhiteSpace(report.ConversationId));
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LiveUtilitySourceFileIoRunner.ConversationIdEnvVar)))
            Assert.True(report.EphemeralThreadDeleted, "Ephemeral utility thread was not deleted after capture.");
    }
}
