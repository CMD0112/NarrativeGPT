using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>Validates republish against the user's real adventure folder when present.</summary>
[Trait("Category", "Unit")]
public sealed class KingInRedRepublishTests
{
    private static readonly Guid KingInRedId = Guid.Parse("b9233735-fdfa-47fe-8f2c-e7122d562f83");

    [Fact]
    public void Republish_all_core_lore_restores_send_readiness_on_custom_adventures_path()
    {
        var settingsPath = Path.Combine(AppDirectories.ConfigRoot, "wrapper-settings.json");
        if (!File.Exists(settingsPath))
            return;

        WrapperSettingsStore.Initialize();
        var adventureDir = AppDirectories.AdventureDirectory(KingInRedId);
        if (!Directory.Exists(adventureDir))
            return;

        var bundle = AdventureStore.Load(KingInRedId);
        Assert.NotNull(bundle);

        var before = ProjectSourceInjectionService.Evaluate(bundle!);
        if (before.CanDelegateStaticContent)
            return;

        Assert.True(
            before.NeedsRepublishCount > 0,
            "Expected stale publish hashes, not a different blocking reason");

        var republished = SourceManifestHelper.RepublishAllCoreLore(bundle!);
        Assert.Equal(4, republished);

        AdventureStore.SaveSourceManifestOnly(bundle!);

        var reloaded = AdventureStore.Load(KingInRedId)!;
        var after = ProjectSourceInjectionService.Evaluate(reloaded);
        Assert.True(after.CanDelegateStaticContent, after.BlockingReason ?? "still blocked after republish");
    }
}
