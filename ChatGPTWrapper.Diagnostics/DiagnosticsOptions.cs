namespace ChatGPTWrapper.Diagnostics;

/// <summary>
/// Runtime diagnostics flags — parsed once at startup from CLI args and environment.
/// </summary>
internal static class DiagnosticsOptions
{
    private static bool _initialized;

    public static bool Extended { get; private set; }

    /// <summary>Log shell UI events (tabs, navigation, compose arm state, dialogs).</summary>
    public static bool LogUiEvents { get; private set; }

    public static void Initialize(string[]? startupArgs = null)
    {
        if (_initialized)
            return;

        _initialized = true;
        startupArgs ??= [];

        Extended = HasFlag(startupArgs, "--extended-diagnostics", "--extended-log", "--verbose-diagnostics")
                     || EnvTruthy("CGW_EXTENDED_DIAGNOSTICS")
                     || EnvTruthy("CGW_EXTENDED_LOG");

        LogUiEvents = HasFlag(startupArgs, "--log-ui-events")
                      || EnvTruthy("CGW_LOG_UI_EVENTS")
                      || Extended;
    }

    internal static void ResetForTests()
    {
        _initialized = false;
        Extended = false;
        LogUiEvents = false;
    }

    private static bool HasFlag(string[] args, params string[] flags)
    {
        foreach (var arg in args)
        {
            foreach (var flag in flags)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (arg.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
                    return ParseTruthy(arg[(flag.Length + 1)..]);
            }
        }

        return false;
    }

    private static bool EnvTruthy(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return ParseTruthy(value);
    }

    private static bool ParseTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase));
}
