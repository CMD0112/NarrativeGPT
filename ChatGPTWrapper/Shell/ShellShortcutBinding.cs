using System.Text.Json.Serialization;
using System.Windows.Input;

namespace ChatGPTWrapper.Shell;

public sealed class ShellShortcutBinding
{
    public int Key { get; set; }

    public int Modifiers { get; set; }

    [JsonIgnore]
    public bool IsValid
    {
        get
        {
            if (!Enum.IsDefined(typeof(Key), Key))
                return false;

            if (ResolvedKey == System.Windows.Input.Key.None || ShellShortcutCatalog.IsModifierOnlyKey(ResolvedKey))
                return false;

            var modifiers = ResolvedModifiers;
            if (!ShellShortcutCatalog.HasRequiredModifiers(modifiers))
                return false;

            return ((int)modifiers & ~(int)(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) == 0;
        }
    }

    public static ShellShortcutBinding From(Key key, ModifierKeys modifiers) =>
        new()
        {
            Key = (int)key,
            Modifiers = (int)modifiers,
        };

    public Key ResolvedKey => (Key)Key;

    public ModifierKeys ResolvedModifiers => (ModifierKeys)Modifiers;
}
