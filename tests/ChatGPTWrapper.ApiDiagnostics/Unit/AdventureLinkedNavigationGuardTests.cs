using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class AdventureLinkedNavigationGuardTests
{
    [Fact]
    public void TryBeginRecovery_allows_first_attempt()
    {
        var adventureId = Guid.NewGuid();
        Assert.True(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 42));
    }

    [Fact]
    public void TryBeginRecovery_blocks_immediate_repeat()
    {
        var adventureId = Guid.NewGuid();
        Assert.True(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 42));
        Assert.False(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 42));
    }

    [Fact]
    public void Reset_clears_cooldown_for_adventure()
    {
        var adventureId = Guid.NewGuid();
        Assert.True(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 42));
        Assert.False(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 42));

        AdventureLinkedNavigationGuard.Reset(adventureId);

        Assert.True(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 42));
    }

    [Fact]
    public void TryBeginRecovery_isolated_per_webview()
    {
        var adventureId = Guid.NewGuid();
        Assert.True(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 1));
        Assert.True(AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, 2));
    }
}
