using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PlaySendTraceTests : IDisposable
{
    private readonly string _root;

    public PlaySendTraceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-play-send-trace", Guid.NewGuid().ToString("N"));
        AppDirectories.TestRootOverride = _root;
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        AppDirectories.TestRootOverride = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void Event_writes_jsonl_line()
    {
        PlaySendTrace.Event(
            PlaySendTraceEvents.SendGate,
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            "test event",
            data: new { ok = true });

        Assert.True(File.Exists(PlaySendTrace.TracePath));
        var line = File.ReadAllLines(PlaySendTrace.TracePath).Last();
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("send_gate", doc.RootElement.GetProperty("event").GetString());
        Assert.Equal("test event", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void BeginSend_writes_run_summary_on_complete()
    {
        var adventureId = Guid.NewGuid();
        using var scope = PlaySendTrace.BeginSend(adventureId, "hello", "https://chatgpt.com/c/test");
        scope.Complete("ok", data: new { conversationId = "abc" });

        Assert.True(File.Exists(PlaySendTrace.TracePath));
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

        var line = File.ReadAllLines(PlaySendTrace.TracePath).Last();
        using var logged = JsonDocument.Parse(line);
        Assert.Equal("bridge_submit_not_found", logged.RootElement.GetProperty("event").GetString());
        Assert.Equal("error", logged.RootElement.GetProperty("level").GetString());
    }
}
