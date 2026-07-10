namespace ChatGPTWrapper.ApiDiagnostics.Infrastructure;

/// <summary>
/// Serializes in-process appdata mutations and cross-process WebView2 profile access.
/// </summary>
internal static class FileLockGate
{
    private static readonly SemaphoreSlim AppDataGate = new(1, 1);
    private static readonly Mutex WebViewProfileMutex = new(false, @"Global\ChatGPTWrapper.WebView2Profile");

    public static IDisposable AcquireAppData(string? label = null) =>
        new SemaphoreScope(AppDataGate, label ?? "appdata");

    public static IDisposable AcquireWebViewProfile(string? label = null) =>
        new MutexScope(WebViewProfileMutex, label ?? "webview-profile");

    private sealed class SemaphoreScope : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _held;

        public SemaphoreScope(SemaphoreSlim gate, string label)
        {
            _gate = gate;
            _held = _gate.Wait(TimeSpan.FromMinutes(5));
            if (!_held)
            {
                throw new TimeoutException(
                    $"Timed out waiting for in-process test file lock '{label}'.");
            }
        }

        public void Dispose()
        {
            if (!_held)
                return;

            _gate.Release();
            _held = false;
        }
    }

    private sealed class MutexScope : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly string _label;
        private bool _held;

        public MutexScope(Mutex mutex, string label)
        {
            _mutex = mutex;
            _label = label;
            try
            {
                _held = _mutex.WaitOne(TimeSpan.FromMinutes(5));
            }
            catch (AbandonedMutexException)
            {
                _held = true;
            }

            if (!_held)
            {
                throw new TimeoutException(
                    $"Timed out waiting for WebView2 profile lock '{_label}'. " +
                    "Close ChatGPT Wrapper or other live diagnostic windows.");
            }
        }

        public void Dispose()
        {
            if (!_held)
                return;

            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                /* ignore release races on shutdown */
            }

            _held = false;
        }
    }
}
