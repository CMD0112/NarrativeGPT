using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlaySurfaceActionSendHelper
{
    public static readonly string[] DefaultActionKeys = ["continue", "regenerate", "retry"];

    public static string ApplyInjectedOnly(AdventureBundle bundle, string playerLine)
    {
        var trimmed = playerLine.Trim();
        foreach (var (actionKey, mode) in EnumerateActions(bundle))
        {
            if (!IsInjectedOnly(mode))
                continue;

            if (string.IsNullOrWhiteSpace(trimmed) || EndsWithActionToken(trimmed, actionKey))
                return string.IsNullOrWhiteSpace(trimmed)
                    ? BuildActionPacket(actionKey)
                    : BuildActionPacket(actionKey) + Environment.NewLine + Environment.NewLine + trimmed;
        }

        return playerLine;
    }

    public static string BuildActionPacket(string actionKey)
    {
        var name = actionKey.Trim().ToUpperInvariant();
        return $"[[cgw:action name=\"{name}\"]]\n[[/cgw:action]]";
    }

    private static IEnumerable<KeyValuePair<string, string>> EnumerateActions(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings.PlaySurfaceActions;
        if (settings.Count > 0)
        {
            foreach (var entry in settings)
                yield return entry;
            yield break;
        }

        foreach (var key in DefaultActionKeys)
            yield return new KeyValuePair<string, string>(key, "Visible");
    }

    private static bool IsInjectedOnly(string? mode) =>
        string.Equals(mode, "InjectedOnly", StringComparison.OrdinalIgnoreCase);

    private static bool EndsWithActionToken(string text, string actionKey)
    {
        var lower = text.ToLowerInvariant();
        var token = actionKey.Trim().ToLowerInvariant();
        return lower.EndsWith(token, StringComparison.Ordinal)
               || lower.EndsWith($"[[cgw:action name=\"{token}\"]]", StringComparison.OrdinalIgnoreCase);
    }
}
