using System.Windows;

namespace ChatGPTWrapper;

public partial class PhraseHighlightsDialog : Window
{
    public IReadOnlyList<PhraseHighlightRule> ResultRules { get; private set; } = [];

    public PhraseHighlightsDialog(IEnumerable<PhraseHighlightRule> existingRules)
    {
        InitializeComponent();
        EditorControl.LoadRules(existingRules);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EditorControl.TryValidate(out _))
            return;

        ResultRules = EditorControl.GetRules().Select(r => r.Clone()).ToList();
        DialogResult = true;
        Close();
    }
}
