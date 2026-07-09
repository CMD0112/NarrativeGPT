using ChatGPTWrapper.WinUI.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>
/// Unified WinUI dialog routing: T1 <see cref="ContentDialog"/>, T2–T4 <see cref="WinUiShellDialogWindow"/>.
/// </summary>
internal static class WinUiDialogService
{
    public static Task<bool?> ShowWorkbenchAsync(
        Window? owner,
        string title,
        UIElement body,
        string layoutKey,
        double designWidth,
        double designHeight,
        Action<WinUiShellDialogHostWindow>? configure = null)
    {
        var window = new WinUiShellDialogHostWindow(title, body, layoutKey, designWidth, designHeight);

        configure?.Invoke(window);
        return window.ShowDialogAsync(owner ?? App.CurrentMainWindow);
    }

    public static void AddSaveCancelFooter(
        WinUiShellDialogHostWindow window,
        Action onSave,
        Action onCancel)
    {
        var cancel = new Button
        {
            Content = "Cancel",
            Style = GetStyle("ShellGhostButtonStyle"),
        };
        cancel.Click += (_, _) => onCancel();

        var save = new Button
        {
            Content = "Save",
            Style = GetStyle("ShellPrimaryButtonStyle"),
        };
        save.Click += (_, _) => onSave();

        window.AddFooterButton(cancel);
        window.AddFooterButton(save);
    }

    public static void AddCloseFooter(WinUiShellDialogHostWindow window, Action? onClose = null)
    {
        var close = new Button
        {
            Content = "Close",
            Style = GetStyle("ShellGhostButtonStyle"),
        };
        close.Click += (_, _) =>
        {
            onClose?.Invoke();
            window.CloseDialog(null);
        };
        window.AddFooterButton(close);
    }

    private static Style? GetStyle(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Style style
            ? style
            : null;

    public static async Task<bool> ShowConfirmAsync(
        Window? owner,
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel") =>
        await WinUiDialogHelper.ConfirmAsync(owner, title, message, confirmText, cancelText);

    public static Task ShowInfoAsync(Window? owner, string title, string message) =>
        WinUiDialogHelper.ShowInfoAsync(owner, title, message);

    public static Task<ContentDialogResult> ShowAlertAsync(
        Window? owner,
        string title,
        UIElement content,
        string closeText = "Close")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = closeText,
        };

        return WinUiDialogHelper.ShowAsync(dialog, owner);
    }

    public static async Task<(bool Success, string Result)> PromptAsync(
        Window? owner,
        string title,
        string label,
        string defaultText = "")
    {
        var box = new TextBox
        {
            Text = defaultText,
            SelectionStart = 0,
            SelectionLength = defaultText.Length,
            MinWidth = 320,
        };

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label },
                box,
            },
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await WinUiDialogHelper.ShowAsync(dialog, owner) != ContentDialogResult.Primary)
            return (false, string.Empty);

        return (true, box.Text.Trim());
    }
}
