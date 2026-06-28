namespace ChatGPTWrapper;

public partial class MainWindow
{
    public event EventHandler? PhraseHighlightRulesChanged;

    private void CommitPhraseHighlightRules(IReadOnlyList<PhraseHighlightRule> rules)
    {
        _chrome.PhraseHighlightRules = rules.Select(r => r.Clone()).ToList();
        ChromePreferencesApplier.ApplyChromeToTrustedTabs(this, _chrome, persist: true);
        PhraseHighlightRulesChanged?.Invoke(this, EventArgs.Empty);
    }
}
