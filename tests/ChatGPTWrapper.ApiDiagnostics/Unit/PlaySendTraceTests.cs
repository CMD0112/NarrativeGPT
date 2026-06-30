using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Trait("Diagnostics", "Logged")]
public sealed class PlaySendTraceTests : IDisposable
{
    private readonly DiagnosticTestSession _session;

    public PlaySendTraceTests() =>
        _session = DiagnosticTestSession.Enter(typeof(PlaySendTraceTests));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void Event_writes_jsonl_line()
    {
        PlaySendTrace.Event(
            PlaySendTraceEvents.SendGate,
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            "test event",
            data: new { ok = true });

        _session.ReloadTraces();
        _session.Traces.PlaySend.ContainsEvent(PlaySendTraceEvents.SendGate);
    }

    [Fact]
    public void BeginSend_writes_run_summary_on_complete()
    {
        var adventureId = Guid.NewGuid();
        using var scope = PlaySendTrace.BeginSend(adventureId, "hello", "https://chatgpt.com/c/test");
        scope.Complete("ok", data: new { conversationId = "abc" });

        _session.ReloadTraces();
        _session.Traces.PlaySend.ContainsEvent(PlaySendTraceEvents.SendRunEnd);
        var summaryPath = PlaySendTrace.GetRunSummaryPath(scope.Run.RunIdShort);
        Assert.True(File.Exists(summaryPath));
    }

    [Fact]
    public void LogFromPage_records_page_event()
    {
        using var doc = JsonDocument.Parse(
            """
            {"type":"cgwPlaySendLog","level":"error","event":"bridge_submit_not_found","message":"Submit failed","source":"adventure-bridge","data":{"attempts":80}}
            """);
        PlaySendTrace.LogFromPage(doc.RootElement);

        _session.ReloadTraces();
        _session.Traces.PlaySend.ContainsEvent("bridge_submit_not_found");
        var line = _session.Traces.PlaySend.Lines
            .Last(l => l.Event == "bridge_submit_not_found");
        Assert.Equal("error", line.Level);
    }
}
