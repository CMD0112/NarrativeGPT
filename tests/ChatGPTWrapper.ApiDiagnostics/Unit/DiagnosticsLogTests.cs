using ChatGPTWrapper;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Trait("Diagnostics", "Logged")]
public sealed class DiagnosticsLogTests : IDisposable
{
    private readonly DiagnosticTestSession _session;

    public DiagnosticsLogTests() =>
        _session = DiagnosticTestSession.Enter(typeof(DiagnosticsLogTests));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void Standard_mode_writes_info_to_play_send_legacy_only()
    {
        DiagnosticsOptions.ResetForTests();
        DiagnosticsOptions.Initialize([]);
        TryDelete(DiagnosticsLog.UnifiedTracePath);
        TryDelete(DiagnosticsLog.PlaySendLegacyPath);

        DiagnosticsLog.Write(
            DiagnosticsChannel.PlaySend,
            DiagnosticsLevel.Info,
            "test_event",
            "hello");

        Assert.True(File.Exists(DiagnosticsLog.PlaySendLegacyPath));
        Assert.False(File.Exists(DiagnosticsLog.UnifiedTracePath));
    }

    [Fact]
    public void Standard_mode_skips_debug_events()
    {
        DiagnosticsOptions.ResetForTests();
        DiagnosticsOptions.Initialize([]);
        TryDelete(DiagnosticsLog.UnifiedTracePath);
        TryDelete(DiagnosticsLog.PlaySendLegacyPath);

        DiagnosticsLog.Write(
            DiagnosticsChannel.PlaySend,
            DiagnosticsLevel.Debug,
            "debug_event",
            "hidden");

        Assert.False(File.Exists(DiagnosticsLog.PlaySendLegacyPath));
    }

    [Fact]
    public void Extended_mode_writes_unified_and_debug()
    {
        DiagnosticsLog.Write(
            DiagnosticsChannel.Ui,
            DiagnosticsLevel.Debug,
            "ui_debug",
            "verbose");

        _session.ReloadTraces();
        _session.Traces.Unified.ContainsEvent("ui_debug", channel: "ui");
        var line = _session.Traces.Unified.Lines.Last();
        Assert.Contains(DiagnosticsLog.SessionId, line.RawLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_ui_events_flag_enables_ui_info_without_extended()
    {
        DiagnosticsOptions.ResetForTests();
        DiagnosticsOptions.Initialize(["--log-ui-events"]);

        UiEventLogger.Info("tab_selected", "tab changed", new { key = "abc" });
        Assert.True(File.Exists(DiagnosticsLog.UnifiedTracePath));
        var line = File.ReadAllLines(DiagnosticsLog.UnifiedTracePath)
            .Last(l => l.Contains("tab_selected", StringComparison.Ordinal));
        Assert.Contains("\"channel\":\"ui\"", line);
    }

    [Fact]
    public void Extended_mode_writes_session_start_and_mirrors_link_project_log()
    {
        ProjectLinkDiagnostics.Log("test mirror line");

        _session.ReloadTraces();
        _session.Traces.Unified.ContainsEvent("session_start", channel: "program");
        Assert.True(
            _session.Traces.Unified.Lines.Any(l =>
                l.Event.Contains("link_project", StringComparison.Ordinal)
                && l.RawLine.Contains("test mirror line", StringComparison.Ordinal)),
            _session.Traces.Unified.FormatExcerpt());
    }

    [Fact]
    public void Extended_mode_session_end_includes_fault_counts()
    {
        DiagnosticsLog.Write(DiagnosticsChannel.Ui, DiagnosticsLevel.Warn, "warn_event", "warn");
        DiagnosticsLog.Write(DiagnosticsChannel.Ui, DiagnosticsLevel.Error, "error_event", "err");
        DiagnosticsSession.WriteExtendedShutdown();

        _session.ReloadTraces();
        var end = _session.Traces.Unified.LastEvent("session_end", channel: "program");
        Assert.NotNull(end);
        Assert.Contains("\"warnings\":1", end!.RawLine, StringComparison.Ordinal);
        Assert.Contains("\"errors\":1", end.RawLine, StringComparison.Ordinal);
        Assert.Contains("\"hadFaults\":true", end.RawLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiAsyncTasks_logs_faulted_discarded_tasks()
    {
        UiAsyncTasks.Run(
            () => Task.FromException(new InvalidOperationException("boom")),
            "test_operation");

        await Task.Delay(50);

        _session.ReloadTraces();
        _session.Traces.Unified.ContainsEvent("async_task_failed", channel: "program");
        _session.Traces.Unified.ContainsEvent(
            "async_task_failed",
            channel: "program",
            predicate: l => l.Message.Contains("test_operation", StringComparison.Ordinal));
    }

    [Fact]
    public void Log_ui_events_writes_exceptions_at_error_level()
    {
        DiagnosticsOptions.ResetForTests();
        DiagnosticsOptions.Initialize(["--log-ui-events"]);

        DiagnosticsMirror.LogException("unit_test", new InvalidOperationException("visible"));

        Assert.True(File.Exists(DiagnosticsLog.UnifiedTracePath));
        var line = File.ReadAllLines(DiagnosticsLog.UnifiedTracePath)
            .Last(l => l.Contains("exception", StringComparison.Ordinal));
        Assert.Contains("\"level\":\"error\"", line);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }
}
