using System.Windows;

namespace ChatGPTWrapper.Views;

public enum ResponseReviewAction
{
    None,
    Accept,
    AcceptEdited,
    Reject,
    Retry,
}

public partial class ResponseReviewDialog : Window
{
    public ResponseReviewAction ResultAction { get; private set; } = ResponseReviewAction.None;

    public string EditedText => ResponseBox.Text;

    public ResponseReviewDialog(string narratorText, string? error = null)
    {
        InitializeComponent();
        ResponseBox.Text = narratorText;
        if (!string.IsNullOrWhiteSpace(error))
            ErrorLine.Text = error;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = ResponseReviewAction.Accept;
        DialogResult = true;
        Close();
    }

    private void AcceptEdited_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = ResponseReviewAction.AcceptEdited;
        DialogResult = true;
        Close();
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = ResponseReviewAction.Reject;
        DialogResult = false;
        Close();
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        ResultAction = ResponseReviewAction.Retry;
        DialogResult = true;
        Close();
    }
}
