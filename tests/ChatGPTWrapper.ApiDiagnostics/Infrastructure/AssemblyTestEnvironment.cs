namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure;

/// <summary>
/// Helpers for per-class isolated appdata directories under %TEMP%.
/// </summary>
internal static class AssemblyTestEnvironment
{
    internal static string CreateClassRoot(string className) =>
        Path.Combine(
            Path.GetTempPath(),
            "ChatGPTWrapper-" + className + "-" + Guid.NewGuid().ToString("N"));
}
