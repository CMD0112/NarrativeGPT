namespace ChatGPTWrapper.ApiDiagnostics.Live;

internal static class OllamaLiveGate
{
    public const string EnvVar = "CGW_RUN_OLLAMA_TESTS";

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnvVar),
            "1",
            StringComparison.Ordinal);
}
