using Xunit;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(int timeout = 1_200_000)
    {
        Timeout = timeout;
        if (!LiveTestGate.IsEnabled)
            Skip = $"Set {LiveTestGate.EnvVar}=1 to run live WebView2 API diagnostics.";
    }
}
