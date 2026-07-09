namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Suppresses play navigation recovery while project-knowledge DOM publication owns the WebView.
/// </summary>
internal static class ProjectDomPublicationGuard
{
    private static int _inFlight;

    public static bool IsActive => Volatile.Read(ref _inFlight) > 0;

    public static IDisposable Begin() => new Scope();

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public Scope() => Interlocked.Increment(ref _inFlight);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Interlocked.Decrement(ref _inFlight);
        }
    }
}
