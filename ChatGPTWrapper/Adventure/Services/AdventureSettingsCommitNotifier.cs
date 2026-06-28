namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Broadcasts when play/transport settings are committed so all bundle holders stay in sync.
/// </summary>
internal static class AdventureSettingsCommitNotifier
{
    public static event EventHandler<Guid>? SettingsCommitted;

    internal static void Notify(Guid adventureId) =>
        SettingsCommitted?.Invoke(null, adventureId);
}
