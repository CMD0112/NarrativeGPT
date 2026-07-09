using System.Windows.Input;

namespace ChatGPTWrapper.Shell;

public static class ShellShortcutCatalog
{
    public const string Preferences = "preferences";
    public const string Format = "format";
    public const string PlaySettings = "play-settings";
    public const string FocusChat = "focus-chat";
    public const string ToggleLeftPanel = "toggle-left-panel";
    public const string ToggleRightPanel = "toggle-right-panel";
    public const string TabReference = "tab-reference";
    public const string TabWarnings = "tab-warnings";
    public const string TabState = "tab-state";
    public const string TabNotes = "tab-notes";
    public const string ShowShortcuts = "show-shortcuts";
    public const string ReviewAcceptProposal = "review-accept-proposal";
    public const string ReviewDismissProposal = "review-dismiss-proposal";

    public static IReadOnlyList<ShellShortcutDefinition> Defaults { get; } =
    [
        new()
        {
            Id = Preferences,
            DisplayName = "Open Preferences",
            Category = "Shell",
            Key = Key.P,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
            Scope = ShellShortcutScope.Global,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = Format,
            DisplayName = "Open Format options",
            Category = "Shell",
            Key = Key.F,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
            Scope = ShellShortcutScope.Global,
        },
        new()
        {
            Id = PlaySettings,
            DisplayName = "Open Play settings",
            Category = "Adventure session",
            Key = Key.OemComma,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
            Scope = ShellShortcutScope.AdventureSession,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = FocusChat,
            DisplayName = "Focus chat / restore panels",
            Category = "Play layout",
            Key = Key.D0,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = ToggleLeftPanel,
            DisplayName = "Toggle left companion panel",
            Category = "Play layout",
            Key = Key.L,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = ToggleRightPanel,
            DisplayName = "Toggle notes / right panel",
            Category = "Play layout",
            Key = Key.N,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = TabReference,
            DisplayName = "Open Reference tab",
            Category = "Play navigation",
            Key = Key.D1,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = TabWarnings,
            DisplayName = "Open Warnings tab",
            Category = "Play navigation",
            Key = Key.D2,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = TabState,
            DisplayName = "Open State tab",
            Category = "Play navigation",
            Key = Key.D3,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = TabNotes,
            DisplayName = "Open Notes",
            Category = "Play navigation",
            Key = Key.D4,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = ShowShortcuts,
            DisplayName = "Show keyboard shortcuts",
            Category = "Shell",
            Key = Key.OemQuestion,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
            Scope = ShellShortcutScope.Global,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = ReviewAcceptProposal,
            DisplayName = "Accept selected review proposal",
            Category = "Play review",
            Key = Key.Y,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
        new()
        {
            Id = ReviewDismissProposal,
            DisplayName = "Dismiss selected review proposal",
            Category = "Play review",
            Key = Key.X,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            Scope = ShellShortcutScope.Play,
            AllowWhenWebViewFocused = true,
        },
    ];

    public static IReadOnlyList<ShellShortcutDefinition> All => Defaults;

    public static IReadOnlyList<ShellShortcutDefinition> ResolveEffectiveShortcuts(
        IReadOnlyDictionary<string, ShellShortcutBinding>? overrides)
    {
        overrides ??= new Dictionary<string, ShellShortcutBinding>();
        var resolved = new List<ShellShortcutDefinition>(Defaults.Count);

        foreach (var definition in Defaults)
        {
            if (!overrides.TryGetValue(definition.Id, out var binding) || !binding.IsValid)
            {
                resolved.Add(definition);
                continue;
            }

            resolved.Add(definition.WithBinding(binding.ResolvedKey, binding.ResolvedModifiers));
        }

        return resolved;
    }

    public static ShellShortcutDefinition? TryGetDefault(string shortcutId) =>
        Defaults.FirstOrDefault(definition => definition.Id == shortcutId);

    public static ShellShortcutDefinition? TryGetEffective(
        string shortcutId,
        IReadOnlyDictionary<string, ShellShortcutBinding>? overrides) =>
        ResolveEffectiveShortcuts(overrides).FirstOrDefault(definition => definition.Id == shortcutId);

    public static bool TryMatch(
        KeyEventArgs e,
        IReadOnlyList<ShellShortcutDefinition> shortcuts,
        out ShellShortcutDefinition shortcut)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        foreach (var candidate in shortcuts)
        {
            if (candidate.Key == key && candidate.Modifiers == modifiers)
            {
                shortcut = candidate;
                return true;
            }
        }

        shortcut = null!;
        return false;
    }

    public static bool TryMatch(KeyEventArgs e, out ShellShortcutDefinition shortcut) =>
        TryMatch(e, Defaults, out shortcut);

    public static ShellShortcutConflict? FindBindingConflict(
        string shortcutId,
        Key key,
        ModifierKeys modifiers,
        IReadOnlyDictionary<string, ShellShortcutBinding>? overrides)
    {
        foreach (var candidate in ResolveEffectiveShortcuts(overrides))
        {
            if (candidate.Id == shortcutId)
                continue;

            if (candidate.Key == key && candidate.Modifiers == modifiers)
                return new ShellShortcutConflict(candidate.Id, candidate.DisplayName);
        }

        return null;
    }

    public static string? ValidateBinding(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None)
            return "Choose a key.";

        if (IsModifierOnlyKey(key))
            return "Modifier keys alone cannot be assigned.";

        if (!HasRequiredModifiers(modifiers))
            return "Include at least one modifier (Ctrl, Alt, Shift, or Win).";

        return null;
    }

    public static bool IsModifierOnlyKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System;

    public static bool HasRequiredModifiers(ModifierKeys modifiers) =>
        modifiers != ModifierKeys.None;

    public static void NormalizeOverrides(Dictionary<string, ShellShortcutBinding> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        var knownIds = Defaults.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        var staleIds = overrides.Keys.Where(id => !knownIds.Contains(id)).ToList();
        foreach (var staleId in staleIds)
            overrides.Remove(staleId);

        var invalidIds = overrides
            .Where(pair => !pair.Value.IsValid)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var invalidId in invalidIds)
            overrides.Remove(invalidId);
    }

    public static string FormatGesture(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    private static string FormatKey(Key key) =>
        key switch
        {
            Key.OemComma => ",",
            Key.OemQuestion => "?",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            _ => key.ToString(),
        };

    public static IEnumerable<IGrouping<string, ShellShortcutDefinition>> GroupedForDisplay(
        IReadOnlyDictionary<string, ShellShortcutBinding>? overrides = null) =>
        ResolveEffectiveShortcuts(overrides).GroupBy(definition => definition.Category);
}
