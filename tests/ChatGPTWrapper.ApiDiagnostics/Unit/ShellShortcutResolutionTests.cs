using System.Windows.Input;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ShellShortcutResolutionTests
{
    [Fact]
    public void ResolveEffectiveShortcuts_applies_override_binding()
    {
        var overrides = new Dictionary<string, ShellShortcutBinding>
        {
            [ShellShortcutCatalog.Format] = ShellShortcutBinding.From(Key.G, ModifierKeys.Control | ModifierKeys.Shift),
        };

        var resolved = ShellShortcutCatalog.ResolveEffectiveShortcuts(overrides);
        var format = resolved.Single(shortcut => shortcut.Id == ShellShortcutCatalog.Format);

        Assert.Equal(Key.G, format.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, format.Modifiers);
        Assert.Equal("Ctrl+Shift+G", format.GestureText);
    }

    [Fact]
    public void FindBindingConflict_detects_duplicate_chord()
    {
        var overrides = new Dictionary<string, ShellShortcutBinding>
        {
            [ShellShortcutCatalog.Format] = ShellShortcutBinding.From(Key.G, ModifierKeys.Control | ModifierKeys.Shift),
        };

        var conflict = ShellShortcutCatalog.FindBindingConflict(
            ShellShortcutCatalog.Preferences,
            Key.G,
            ModifierKeys.Control | ModifierKeys.Shift,
            overrides);

        Assert.NotNull(conflict);
        Assert.Equal(ShellShortcutCatalog.Format, conflict!.ShortcutId);
        Assert.Equal("Open Format options", conflict.DisplayName);
    }

    [Fact]
    public void FindBindingConflict_ignores_same_shortcut()
    {
        var overrides = new Dictionary<string, ShellShortcutBinding>
        {
            [ShellShortcutCatalog.Format] = ShellShortcutBinding.From(Key.G, ModifierKeys.Control | ModifierKeys.Shift),
        };

        var conflict = ShellShortcutCatalog.FindBindingConflict(
            ShellShortcutCatalog.Format,
            Key.G,
            ModifierKeys.Control | ModifierKeys.Shift,
            overrides);

        Assert.Null(conflict);
    }

    [Fact]
    public void ValidateBinding_requires_modifier()
    {
        var message = ShellShortcutCatalog.ValidateBinding(Key.F, ModifierKeys.None);

        Assert.Equal("Include at least one modifier (Ctrl, Alt, Shift, or Win).", message);
    }

    [Fact]
    public void NormalizeOverrides_removes_unknown_and_invalid_entries()
    {
        var overrides = new Dictionary<string, ShellShortcutBinding>
        {
            ["missing-shortcut"] = ShellShortcutBinding.From(Key.A, ModifierKeys.Control),
            [ShellShortcutCatalog.Format] = new ShellShortcutBinding { Key = (int)Key.None, Modifiers = (int)ModifierKeys.Control },
        };

        ShellShortcutCatalog.NormalizeOverrides(overrides);

        Assert.Empty(overrides);
    }
}
