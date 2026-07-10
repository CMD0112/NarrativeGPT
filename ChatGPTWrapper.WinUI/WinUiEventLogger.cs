using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.WinUI;

/// <summary>
/// WinUI shell UI events — enabled with <c>--log-ui-events</c> or extended diagnostics.
/// </summary>
internal static class WinUiEventLogger
{
    public static void Info(string eventName, string message, object? data = null, Guid? adventureId = null) =>
        Write(DiagnosticsLevel.Info, eventName, message, data, adventureId);

    public static void Debug(string eventName, string message, object? data = null, Guid? adventureId = null) =>
        Write(DiagnosticsLevel.Debug, eventName, message, data, adventureId);

    public static void Warn(string eventName, string message, object? data = null, Guid? adventureId = null) =>
        Write(DiagnosticsLevel.Warn, eventName, message, data, adventureId);

    public static void Error(string eventName, string message, object? data = null, Guid? adventureId = null) =>
        Write(DiagnosticsLevel.Error, eventName, message, data, adventureId);

    private static void Write(
        DiagnosticsLevel level,
        string eventName,
        string message,
        object? data,
        Guid? adventureId)
    {
        if (!DiagnosticsOptions.Extended && !DiagnosticsOptions.LogUiEvents)
            return;

        if (!DiagnosticsOptions.Extended && level == DiagnosticsLevel.Debug)
            return;

        DiagnosticsLog.Write(
            DiagnosticsChannel.Ui,
            level,
            eventName,
            message,
            adventureId: adventureId,
            source: "winui",
            data: data);
    }
}
