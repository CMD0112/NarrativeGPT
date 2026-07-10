using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChatGPTWrapper.Shell;

/// <summary>
/// Shared keyboard chords for triaging AI proposals in review surfaces.
/// </summary>
public static class ProposalReviewKeyBindings
{
    public const string AcceptGesture = "Y";
    public const string DismissGesture = "N";
    public const string AcceptAllGesture = "Ctrl+Shift+Y";
    public const string DismissAllGesture = "Ctrl+Shift+N";
    public const string ShellAcceptGesture = "Ctrl+Alt+Y";
    public const string ShellDismissGesture = "Ctrl+Alt+X";
    public const string HintText = "Y accept · N dismiss · Ctrl+Shift+Y accept all · Ctrl+Shift+N dismiss all";

    public enum ActionKind
    {
        Accept,
        Dismiss,
        AcceptAll,
        DismissAll,
    }

    public static bool TryHandlePreviewKeyDown(
        KeyEventArgs e,
        bool canAccept,
        bool canDismiss,
        Action accept,
        Action dismiss,
        Action? acceptAll = null,
        Action? dismissAll = null,
        bool allowEnterAccept = true,
        bool canAcceptAll = true,
        bool canDismissAll = true)
    {
        if (e.Handled)
            return false;

        if (!TryMatch(e, out var action))
            return false;

        switch (action)
        {
            case ActionKind.Accept when canAccept && allowEnterAccept:
                accept();
                e.Handled = true;
                return true;
            case ActionKind.Dismiss when canDismiss:
                dismiss();
                e.Handled = true;
                return true;
            case ActionKind.AcceptAll when canAcceptAll && acceptAll is not null:
                acceptAll();
                e.Handled = true;
                return true;
            case ActionKind.DismissAll when canDismissAll && dismissAll is not null:
                dismissAll();
                e.Handled = true;
                return true;
            default:
                return false;
        }
    }

    public static bool TryMatch(KeyEventArgs e, out ActionKind action)
    {
        action = default;

        if (IsEditableTextFocused())
            return false;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return TryMatchKey(key, Keyboard.Modifiers, out action);
    }

    public static bool TryMatchKey(Key key, ModifierKeys modifiers, out ActionKind action)
    {
        action = default;

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Y)
        {
            action = ActionKind.AcceptAll;
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.N)
        {
            action = ActionKind.DismissAll;
            return true;
        }

        if (modifiers == ModifierKeys.None && key == Key.Y)
        {
            action = ActionKind.Accept;
            return true;
        }

        if (modifiers == ModifierKeys.None && key == Key.N)
        {
            action = ActionKind.Dismiss;
            return true;
        }

        if (modifiers == ModifierKeys.None && key == Key.Enter)
        {
            action = ActionKind.Accept;
            return true;
        }

        return false;
    }

    public static bool IsEditableTextFocused()
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
            return false;

        if (focused is TextBox textBox && !textBox.IsReadOnly)
            return true;

        if (focused is ComboBox comboBox && (comboBox.IsDropDownOpen || comboBox.IsEditable))
            return true;

        return false;
    }

    public static string FormatAcceptButton(string label = "Accept") =>
        $"{label} ({AcceptGesture})";

    public static string FormatDismissButton(string label = "Dismiss") =>
        $"{label} ({DismissGesture})";
}
