using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ProjectRemoteListCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private static readonly Dictionary<string, (DateTimeOffset At, List<GizmoFileRef> Files)> Cache =
        new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public static bool TryGet(string gizmoId, out IReadOnlyList<GizmoFileRef> files)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(gizmoId, out var entry)
                && DateTimeOffset.UtcNow - entry.At < Ttl)
            {
                files = entry.Files;
                return true;
            }
        }

        files = [];
        return false;
    }

    public static void Set(string gizmoId, IReadOnlyList<GizmoFileRef> files)
    {
        lock (Gate)
        {
            Cache[gizmoId] = (DateTimeOffset.UtcNow, files.ToList());
        }
    }

    public static void Invalidate(string? gizmoId = null)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(gizmoId))
                Cache.Clear();
            else
                Cache.Remove(gizmoId);
        }
    }
}
