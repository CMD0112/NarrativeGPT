using System.Windows.Input;

namespace ChatGPTWrapper.Shell;

public sealed class ShellShortcutDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Category { get; init; }

    public required Key Key { get; init; }

    public required ModifierKeys Modifiers { get; init; }

    public required ShellShortcutScope Scope { get; init; }

    /// <summary>
    /// When false, the shortcut is suppressed while the active chat WebView has keyboard focus
    /// (avoids stealing browser chords such as Ctrl+F).
    /// </summary>
    public bool AllowWhenWebViewFocused { get; init; }

    public string GestureText => ShellShortcutCatalog.FormatGesture(Key, Modifiers);

    public ShellShortcutDefinition WithBinding(Key key, ModifierKeys modifiers) =>
        new()
        {
            Id = Id,
            DisplayName = DisplayName,
            Category = Category,
            Key = key,
            Modifiers = modifiers,
            Scope = Scope,
            AllowWhenWebViewFocused = AllowWhenWebViewFocused,
        };
}
