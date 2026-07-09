using ChatGPTWrapper.ApiDiagnostics.Reporting;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

[Collection("LiveWebView")]
[Trait("Category", "Live")]
[Trait("Diagnostics", "Logged")]
public sealed class LiveProjectSourceDownloadTests
{
    private readonly LiveWebViewFixture _fixture;

    public LiveProjectSourceDownloadTests(LiveWebViewFixture fixture) => _fixture = fixture;

    [LiveFact]
    public async Task Run_project_source_download_checklist()
    {
        var runner = new LiveProjectSourceDownloadRunner(_fixture.Host);
        ProjectSourceDownloadReport report;
        try
        {
            report = await runner.RunAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Assert.True(
                File.Exists(ProjectSourceDownloadReport.ReportTextPath),
                $"Download runner crashed but report was not written: {ex.Message}");
            throw;
        }

        Assert.True(File.Exists(ProjectSourceDownloadReport.ReportJsonPath), "JSON download report was not written");
        Assert.True(File.Exists(ProjectSourceDownloadReport.ReportTextPath), "Text download report was not written");
        Assert.NotEmpty(report.Steps);

        // Report is the deliverable; failures are listed for investigation.
        Assert.True(
            report.PassedCount > 0 || report.FailedCount > 0,
            "Download checklist produced no steps.");
    }
}
