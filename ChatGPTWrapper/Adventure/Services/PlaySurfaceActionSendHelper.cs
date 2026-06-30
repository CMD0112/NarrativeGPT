using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlaySurfaceActionSendHelper
{
    public static readonly string[] DefaultActionKeys = ["continue", "regenerate", "retry"];

    /// <summary>Wrapper quick-action bar: continue is supported; regenerate/retry are native turn actions.</summary>
    public static readonly string[] WrapperQuickActionKeys = ["continue"];

    public static bool AllowsEmptyComposerSend(AdventureBundle bundle) =>
        EnumerateActions(bundle).Any(e => IsInjectedOnly(e.Value));

    public static bool ShouldShowWrapperQuickAction(string actionKey, string? mode) =>
        WrapperQuickActionKeys.Any(k =>
            string.Equals(k, actionKey, StringComparison.OrdinalIgnoreCase))
        && (IsHidden(mode) || IsInjectedOnly(mode));

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
        var body = ResolveActionDirectiveBody(actionKey);
        return string.IsNullOrWhiteSpace(body)
            ? $"[[cgw:action name=\"{name}\"]]\n[[/cgw:action]]"
            : $"[[cgw:action name=\"{name}\"]]\n{body}\n[[/cgw:action]]";
    }

    internal static string ResolveActionDirectiveBody(string actionKey) =>
        actionKey.Trim().ToLowerInvariant() switch
        {
            "continue" =>
                "The player requests you continue narrating — advance the scene with fresh prose "
                + "without requiring a new player action.",
            "regenerate" =>
                "The player requests you regenerate your previous response with a different approach "
                + "while preserving story continuity.",
            "retry" =>
                "The player requests you retry your previous response.",
            _ => $"The player requests the {actionKey.Trim().ToUpperInvariant()} action.",
        };

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

    private static bool IsHidden(string? mode) =>
        string.Equals(mode, "Hidden", StringComparison.OrdinalIgnoreCase);

    private static bool EndsWithActionToken(string text, string actionKey)
    {
        var lower = text.ToLowerInvariant();
        var token = actionKey.Trim().ToLowerInvariant();
        return lower.EndsWith(token, StringComparison.Ordinal)
               || lower.EndsWith($"[[cgw:action name=\"{token}\"]]", StringComparison.OrdinalIgnoreCase);
    }
}
