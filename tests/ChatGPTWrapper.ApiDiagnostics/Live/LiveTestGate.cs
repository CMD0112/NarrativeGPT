namespace ChatGPTWrapper.ApiDiagnostics.Live;

internal static class LiveTestGate
{
    public const string EnvVar = "CGW_RUN_LIVE_API_TESTS";

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvVar),
            "1",
            StringComparison.Ordinal);
}
