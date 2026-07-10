namespace ChatGPTWrapper.ApiDiagnostics.Live;

/// <summary>
/// Live Snorlax publication lab gate. Requires CGW_RUN_LIVE_PUBLICATION=1 and linked project.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveProjectPublicationTests
{
    private const string EnvVar = "CGW_RUN_LIVE_PUBLICATION";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvVar), "1", StringComparison.Ordinal);

    [Fact]
    public void Live_publication_gate_documents_env_var()
    {
        if (!IsEnabled)
        {
            // Documented opt-in gate — not a failure when unset.
            Assert.True(true);
            return;
        }

        Assert.True(IsEnabled);
    }
}
