using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChatGPTWrapper.Views;

internal partial class TextPromptDialog : ShellDialogWindow
{
    protected override bool ApplyDesignSizeOnOpen => ResizeMode == ResizeMode.CanResizeWithGrip;

    public string ResultText { get; private set; } = "";

    public TextPromptDialog(
        string title,
        string prompt,
        string defaultText = "",
        string confirmButtonText = "OK",
        bool multiline = false)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        ConfirmButton.Content = confirmButtonText;
        InputBox.Text = defaultText;

        if (multiline)
        {
            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = 520;
            MinWidth = 440;
            MinHeight = 280;
            Height = 320;
            InputBox.AcceptsReturn = true;
            InputBox.TextWrapping = TextWrapping.Wrap;
            InputBox.MinHeight = 96;
            InputBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        InputBox.TextChanged += (_, _) => HideValidation();
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public static bool TryPrompt(
        Window? owner,
        string title,
        string prompt,
        string defaultText,
        out string result,
        string confirmButtonText = "OK",
        bool multiline = false)
    {
        var dialog = new TextPromptDialog(title, prompt, defaultText, confirmButtonText, multiline);
        if (owner is not null)
            dialog.Owner = owner;

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResultText))
        {
            result = string.Empty;
            return false;
        }

        result = dialog.ResultText;
        return true;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var trimmed = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ShowValidation("Enter a value to continue.");
            InputBox.Focus();
            return;
        }

        ResultText = trimmed;
        DialogResult = true;
        Close();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || InputBox.AcceptsReturn)
            return;

        ConfirmButton_Click(sender, e);
        e.Handled = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationText.Visibility = Visibility.Collapsed;
        ValidationText.Text = "";
    }
}
