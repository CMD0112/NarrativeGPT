using ChatGPTWrapper.ApiDiagnostics.Reporting;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

[Collection("LiveWebView")]
[Trait("Category", "Live")]
public sealed class LiveApiDiagnosticTests
{
    private readonly LiveWebViewFixture _fixture;

    public LiveApiDiagnosticTests(LiveWebViewFixture fixture) => _fixture = fixture;

    [LiveFact]
    public async Task Run_full_api_diagnostic_checklist()
    {
        var runner = new LiveApiDiagnosticRunner(_fixture.Host);
        ApiDiagnosticReport report;
        try
        {
            report = await runner.RunAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Assert.True(
                File.Exists(ApiDiagnosticReport.ReportTextPath),
                $"Diagnostic crashed but report was not written: {ex.Message}");
            throw;
        }

        Assert.True(File.Exists(ApiDiagnosticReport.ReportJsonPath), "JSON report was not written");
        Assert.True(File.Exists(ApiDiagnosticReport.ReportTextPath), "Text report was not written");
        Assert.NotEmpty(report.Steps);

        // Soft pass: report is the deliverable even when API steps fail.
        // Failures are listed in api-diagnostic-report.txt for investigation.
    }
}
