using ChatGPTWrapper;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Trait("Diagnostics", "Logged")]
public sealed class DiagnosticTestParadigmTests : IDisposable
{
    private readonly DiagnosticTestSession _session;

    public DiagnosticTestParadigmTests() =>
        _session = DiagnosticTestSession.Enter(typeof(DiagnosticTestParadigmTests));

    public void Dispose() => _session.Dispose();

    [Fact]
    public void Session_bootstraps_extended_unified_trace()
    {
        _session.ReloadTraces();
        _session.Traces.Unified.ContainsEvent("session_start", channel: "program");
    }

    [Fact]
    public void PlaySend_trace_sequence_is_assertable()
    {
        var adventureId = Guid.NewGuid();
        using (var scope = PlaySendTrace.BeginSend(adventureId, "hello", "https://chatgpt.com/c/test"))
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.PacketPrepared,
                PlaySendCategory.Host,
                PlaySendLevel.Info,
                "packet ready",
                data: new { mergedLength = 42 });
            scope.Complete("ok");
        }

        _session.ReloadTraces();
        _session.Traces.PlaySend.Sequence(
            PlaySendTraceEvents.SendRunStart,
            PlaySendTraceEvents.PacketPrepared,
            PlaySendTraceEvents.SendRunEnd);
        _session.Traces.PlaySend.NoErrors();
    }

    [Fact]
    public void Failure_digest_includes_recent_trace_lines()
    {
        DiagnosticsLog.Write(
            DiagnosticsChannel.Ui,
            DiagnosticsLevel.Error,
            "unit_probe",
            "synthetic failure for excerpt");

        _session.ReloadTraces();
        var digest = _session.Traces.FormatFailureDigest("probe");
        Assert.Contains("unit_probe", digest, StringComparison.Ordinal);
        Assert.Contains(DiagnosticsLog.SessionId, digest, StringComparison.Ordinal);
    }
}
