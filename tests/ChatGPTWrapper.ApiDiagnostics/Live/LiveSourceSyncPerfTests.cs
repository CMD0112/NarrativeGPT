using ChatGPTWrapper.ApiDiagnostics.Reporting;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

[Collection("LiveWebView")]
[Trait("Category", "Live")]
[Trait("Category", "Performance")]
public sealed class LiveSourceSyncPerfTests
{
    private readonly LiveWebViewFixture _fixture;

    public LiveSourceSyncPerfTests(LiveWebViewFixture fixture) => _fixture = fixture;

    [LiveFact]
    public async Task Run_live_source_sync_performance_checklist()
    {
        var runner = new LiveSourceSyncPerfRunner(_fixture.Host);
        SourceSyncPerfReport report;
        try
        {
            report = await runner.RunAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Assert.True(
                File.Exists(SourceSyncPerfReport.ReportTextPath),
                $"Perf runner crashed but report was not written: {ex.Message}");
            throw;
        }

        Assert.True(File.Exists(SourceSyncPerfReport.ReportJsonPath), "JSON perf report was not written");
        Assert.True(File.Exists(SourceSyncPerfReport.ReportTextPath), "Text perf report was not written");
        Assert.NotEmpty(report.Steps);
    }
}
