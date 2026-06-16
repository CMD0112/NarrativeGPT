using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ProjectSidebarCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static (DateTimeOffset At, List<GizmoSummary> Projects)? _entry;
    private static readonly object Gate = new();

    public static bool TryGet(out IReadOnlyList<GizmoSummary> projects)
    {
        lock (Gate)
        {
            if (_entry is { } entry && DateTimeOffset.UtcNow - entry.At < Ttl)
            {
                projects = entry.Projects;
                return true;
            }
        }

        projects = [];
        return false;
    }

    public static void Set(IReadOnlyList<GizmoSummary> projects)
    {
        lock (Gate)
        {
            _entry = (DateTimeOffset.UtcNow, projects.ToList());
        }
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            _entry = null;
        }
    }
}
