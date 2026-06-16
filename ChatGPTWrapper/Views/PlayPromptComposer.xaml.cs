using System.Windows;
using System.Windows.Controls;

namespace ChatGPTWrapper.Views;

/// <summary>
/// Hidden adapter for prompt text and merged-preview state synced with the in-page wrapper composer.
/// </summary>
public partial class PlayPromptComposer : UserControl
{
    public event EventHandler? PromptTextChanged;

    public PlayPromptComposer()
    {
        InitializeComponent();
        PromptBox.TextChanged += (_, _) => PromptTextChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetPromptText() => PromptBox.Text.Trim();

    public void SetPromptText(string text) => PromptBox.Text = text ?? "";

    public void AppendPromptText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var existing = PromptBox.Text;
        PromptBox.Text = string.IsNullOrWhiteSpace(existing) ? text : existing + " " + text;
        PromptBox.CaretIndex = PromptBox.Text.Length;
    }

    public void ClearPrompt() => PromptBox.Clear();

    public void SetMergedPreview(string? text) => MergedPreviewBox.Text = text ?? "";

    public void SetStatus(string? text) => StatusLine.Text = text ?? "";

    public void SetBusy(bool busy, string? busyMessage = null)
    {
        PromptBox.IsReadOnly = busy;
        if (busy && !string.IsNullOrWhiteSpace(busyMessage))
            StatusLine.Text = busyMessage;
    }
}
