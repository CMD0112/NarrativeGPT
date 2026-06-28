using ChatGPTWrapper;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(DiagnosticsTestCollection.Name)]
public sealed class DiagnosticsLogTests : IDisposable
{
    private readonly string _root;

    public DiagnosticsLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgw-diagnostics", Guid.NewGuid().ToString("N"));
        ResetState();
        AppDirectories.EnsureCreated();
    }

    public void Dispose()
    {
        ResetState();
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

    private void ResetState()
    {
        DiagnosticsOptions.ResetForTests();
        DiagnosticsLog.ResetSessionCountsForTests();
        AppDirectories.ResetStoresForTests();
        AppDirectories.TestRootOverride = _root;
        AppDirectories.EnsureCreated();
        TryDelete(DiagnosticsLog.PlaySendLegacyPath);
        TryDelete(DiagnosticsLog.UnifiedTracePath);
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

    [Fact]
    public void Standard_mode_writes_info_to_play_send_legacy_only()
    {
        DiagnosticsOptions.Initialize([]);

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
        DiagnosticsOptions.Initialize([]);

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
        DiagnosticsOptions.Initialize(["--extended-diagnostics"]);

        DiagnosticsLog.Write(
            DiagnosticsChannel.Ui,
            DiagnosticsLevel.Debug,
            "ui_debug",
            "verbose");

        Assert.True(File.Exists(DiagnosticsLog.UnifiedTracePath));
        var line = File.ReadAllLines(DiagnosticsLog.UnifiedTracePath).Last();
        Assert.Contains("ui_debug", line);
        Assert.Contains(DiagnosticsLog.SessionId, line);
    }

    [Fact]
    public void Log_ui_events_flag_enables_ui_info_without_extended()
    {
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
        DiagnosticsOptions.Initialize(["--extended-diagnostics"]);
        DiagnosticsSession.WriteExtendedHeader(["--extended-diagnostics"]);

        ProjectLinkDiagnostics.Log("test mirror line");

        Assert.True(File.Exists(DiagnosticsLog.UnifiedTracePath));
        var lines = File.ReadAllLines(DiagnosticsLog.UnifiedTracePath);
        Assert.Contains(lines, l => l.Contains("session_start", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("link_project", StringComparison.Ordinal) && l.Contains("test mirror line", StringComparison.Ordinal));
    }

    [Fact]
    public void Extended_mode_session_end_includes_fault_counts()
    {
        DiagnosticsOptions.Initialize(["--extended-diagnostics"]);
        DiagnosticsSession.WriteExtendedHeader(["--extended-diagnostics"]);

        DiagnosticsLog.Write(DiagnosticsChannel.Ui, DiagnosticsLevel.Warn, "warn_event", "warn");
        DiagnosticsLog.Write(DiagnosticsChannel.Ui, DiagnosticsLevel.Error, "error_event", "err");
        DiagnosticsSession.WriteExtendedShutdown();

        var lines = File.ReadAllLines(DiagnosticsLog.UnifiedTracePath);
        var end = lines.Last(l => l.Contains("session_end", StringComparison.Ordinal));
        Assert.Contains("\"warnings\":1", end);
        Assert.Contains("\"errors\":1", end);
        Assert.Contains("\"hadFaults\":true", end);
    }

    [Fact]
    public async Task UiAsyncTasks_logs_faulted_discarded_tasks()
    {
        DiagnosticsOptions.Initialize(["--extended-diagnostics"]);

        UiAsyncTasks.Run(
            () => Task.FromException(new InvalidOperationException("boom")),
            "test_operation");

        await Task.Delay(50);

        var lines = File.ReadAllLines(DiagnosticsLog.UnifiedTracePath);
        Assert.Contains(lines, l => l.Contains("async_task_failed", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("test_operation", StringComparison.Ordinal));
    }

    [Fact]
    public void Log_ui_events_writes_exceptions_at_error_level()
    {
        DiagnosticsOptions.Initialize(["--log-ui-events"]);

        DiagnosticsMirror.LogException("unit_test", new InvalidOperationException("visible"));

        Assert.True(File.Exists(DiagnosticsLog.UnifiedTracePath));
        var line = File.ReadAllLines(DiagnosticsLog.UnifiedTracePath)
            .Last(l => l.Contains("exception", StringComparison.Ordinal));
        Assert.Contains("\"level\":\"error\"", line);
    }
}
