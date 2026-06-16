using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class DebouncedAdventureSaver : IDisposable
{
    private readonly Func<AdventureBundle?> _getBundle;
    private readonly Action<DateTimeOffset>? _onSaved;
    private readonly DispatcherTimer _timer;

    public DebouncedAdventureSaver(Func<AdventureBundle?> getBundle, Action<DateTimeOffset>? onSaved = null, int delayMs = 300)
    {
        _getBundle = getBundle;
        _onSaved = onSaved;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMs),
        };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            SaveNow();
        };
    }

    public void ScheduleSave() => _timer.Start();

    public void SaveNow()
    {
        _timer.Stop();
        var bundle = _getBundle();
        if (bundle is null)
            return;

        AdventureStore.Save(bundle);
        _onSaved?.Invoke(DateTimeOffset.Now);
    }

    public void Dispose() => _timer.Stop();
}
