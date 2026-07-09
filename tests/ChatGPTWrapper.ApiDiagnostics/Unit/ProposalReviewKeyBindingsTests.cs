using System.Windows.Input;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ProposalReviewKeyBindingsTests
{
    [Theory]
    [InlineData(Key.Y, ModifierKeys.None, ProposalReviewKeyBindings.ActionKind.Accept)]
    [InlineData(Key.N, ModifierKeys.None, ProposalReviewKeyBindings.ActionKind.Dismiss)]
    [InlineData(Key.Enter, ModifierKeys.None, ProposalReviewKeyBindings.ActionKind.Accept)]
    [InlineData(Key.Y, ModifierKeys.Control | ModifierKeys.Shift, ProposalReviewKeyBindings.ActionKind.AcceptAll)]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Shift, ProposalReviewKeyBindings.ActionKind.DismissAll)]
    public void TryMatchKey_maps_review_chords(Key key, ModifierKeys modifiers, ProposalReviewKeyBindings.ActionKind expected)
    {
        var matched = ProposalReviewKeyBindings.TryMatchKey(key, modifiers, out var action);

        Assert.True(matched);
        Assert.Equal(expected, action);
    }

    [Theory]
    [InlineData(Key.A, ModifierKeys.None)]
    [InlineData(Key.Y, ModifierKeys.Control)]
    [InlineData(Key.N, ModifierKeys.Alt)]
    public void TryMatchKey_ignores_unbound_chords(Key key, ModifierKeys modifiers)
    {
        var matched = ProposalReviewKeyBindings.TryMatchKey(key, modifiers, out _);

        Assert.False(matched);
    }

    [Fact]
    public void ShellShortcutCatalog_includes_play_review_chords()
    {
        var accept = ShellShortcutCatalog.Defaults.Single(shortcut => shortcut.Id == ShellShortcutCatalog.ReviewAcceptProposal);
        var dismiss = ShellShortcutCatalog.Defaults.Single(shortcut => shortcut.Id == ShellShortcutCatalog.ReviewDismissProposal);

        Assert.Equal(Key.Y, accept.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, accept.Modifiers);
        Assert.Equal(ShellShortcutScope.Play, accept.Scope);
        Assert.True(accept.AllowWhenWebViewFocused);

        Assert.Equal(Key.X, dismiss.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, dismiss.Modifiers);
        Assert.Equal(ShellShortcutScope.Play, dismiss.Scope);
        Assert.True(dismiss.AllowWhenWebViewFocused);
    }
}
