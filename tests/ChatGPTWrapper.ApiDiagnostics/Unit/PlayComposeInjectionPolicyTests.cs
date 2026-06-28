using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayComposeInjectionPolicyTests
{
    [Fact]
    public void Schema6_regression_legacy_metadata_pin_misses_registry_pinned_tab()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        const string candidate = "registry-pin-tab";

        var legacyWouldRegister = PlayComposeInjectionPolicy.WouldLegacyMetadataPinMatch(bundle, candidate);
        var registryWouldRegister = PlayComposeInjectionPolicy.ShouldRegisterIntercept(
            new PlayComposeRegistrationContext(
                IsPlayMode: true,
                Bundle: bundle,
                CandidateTabKey: candidate,
                PlayWebViewTabKey: "stale-play-ref",
                ActiveWebViewTabKey: "unrelated-active",
                SuppressPlayAutomation: false));

        Assert.False(legacyWouldRegister);
        Assert.True(registryWouldRegister);
        Assert.True(PlayTabPinService.IsTabKeyPlayPin(bundle, candidate));
    }

    [Fact]
    public void ShouldRegisterIntercept_registry_pin_when_metadata_cleared_and_stale_play_webview()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var ctx = new PlayComposeRegistrationContext(
            IsPlayMode: true,
            Bundle: bundle,
            CandidateTabKey: "registry-pin-tab",
            PlayWebViewTabKey: "stale-play-ref",
            ActiveWebViewTabKey: "unrelated-active",
            SuppressPlayAutomation: false);

        Assert.True(PlayComposeInjectionPolicy.ShouldRegisterIntercept(ctx));
        Assert.False(PlayComposeInjectionPolicy.WouldLegacyMetadataPinMatch(bundle, "registry-pin-tab"));
    }

    [Fact]
    public void ShouldRegisterIntercept_legacy_metadata_pin_still_works()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { PinnedPlayTabKey = "legacy-pin" },
        };

        var ctx = new PlayComposeRegistrationContext(
            IsPlayMode: true,
            Bundle: bundle,
            CandidateTabKey: "legacy-pin",
            PlayWebViewTabKey: null,
            ActiveWebViewTabKey: null,
            SuppressPlayAutomation: false);

        Assert.True(PlayComposeInjectionPolicy.ShouldRegisterIntercept(ctx));
        Assert.True(PlayComposeInjectionPolicy.WouldLegacyMetadataPinMatch(bundle, "legacy-pin"));
    }

    [Fact]
    public void ShouldRegisterIntercept_rejects_unpinned_background_tab()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var ctx = new PlayComposeRegistrationContext(
            IsPlayMode: true,
            Bundle: bundle,
            CandidateTabKey: "background-chat",
            PlayWebViewTabKey: "stale-play-ref",
            ActiveWebViewTabKey: "unrelated-active",
            SuppressPlayAutomation: false);

        Assert.False(PlayComposeInjectionPolicy.ShouldRegisterIntercept(ctx));
    }

    [Fact]
    public void ShouldRegisterIntercept_allows_active_tab_when_not_pinned()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var ctx = new PlayComposeRegistrationContext(
            IsPlayMode: true,
            Bundle: bundle,
            CandidateTabKey: "active-now",
            PlayWebViewTabKey: "stale-play-ref",
            ActiveWebViewTabKey: "active-now",
            SuppressPlayAutomation: false);

        Assert.True(PlayComposeInjectionPolicy.ShouldRegisterIntercept(ctx));
    }

    [Fact]
    public void ShouldRegisterIntercept_honors_suppress_on_active_only()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var ctx = new PlayComposeRegistrationContext(
            IsPlayMode: true,
            Bundle: bundle,
            CandidateTabKey: "draft-active",
            PlayWebViewTabKey: "stale-play-ref",
            ActiveWebViewTabKey: "draft-active",
            SuppressPlayAutomation: false,
            SuppressPlayAutomationOnActiveOnly: true);

        Assert.False(PlayComposeInjectionPolicy.ShouldRegisterIntercept(ctx));
    }

    [Fact]
    public void ShouldRegisterIntercept_still_registers_pinned_tab_when_suppress_on_active_only()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var ctx = new PlayComposeRegistrationContext(
            IsPlayMode: true,
            Bundle: bundle,
            CandidateTabKey: "registry-pin-tab",
            PlayWebViewTabKey: "stale-play-ref",
            ActiveWebViewTabKey: "draft-active",
            SuppressPlayAutomation: false,
            SuppressPlayAutomationOnActiveOnly: true);

        Assert.True(PlayComposeInjectionPolicy.ShouldRegisterIntercept(ctx));
    }

    [Fact]
    public void ResolveInjectionTabKey_prefers_resolved_pin_over_stale_play_webview()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var registered = new[] { "registry-pin-tab", "stale-play-ref" };

        var key = PlayComposeInjectionPolicy.ResolveInjectionTabKey(
            bundle,
            stalePlayWebViewTabKey: "stale-play-ref",
            resolvedPlayWebViewTabKey: "registry-pin-tab",
            registered);

        Assert.Equal("registry-pin-tab", key);
    }

    [Fact]
    public void ResolveInjectionTabKey_falls_back_to_registry_pin_when_stale_play_webview_wrong()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var registered = new[] { "registry-pin-tab" };

        var key = PlayComposeInjectionPolicy.ResolveInjectionTabKey(
            bundle,
            stalePlayWebViewTabKey: "stale-play-ref",
            resolvedPlayWebViewTabKey: null,
            registered);

        Assert.Equal("registry-pin-tab", key);
    }

    [Fact]
    public void ResolveInjectionTabKey_legacy_metadata_only_fails_after_schema6_save()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("registry-pin-tab");
        var registered = new[] { "registry-pin-tab" };

        var legacyWouldMatch = PlayComposeInjectionPolicy.WouldLegacyMetadataPinMatch(bundle, "registry-pin-tab");
        var resolved = PlayComposeInjectionPolicy.ResolveInjectionTabKey(
            bundle,
            stalePlayWebViewTabKey: "stale-play-ref",
            resolvedPlayWebViewTabKey: null,
            registered);

        Assert.False(legacyWouldMatch);
        Assert.Equal("registry-pin-tab", resolved);
    }

    [Fact]
    public void IsTabKeyPlayPin_matches_registry_pin_key()
    {
        var bundle = PlayTabPinHarness.CreateInMemoryRegistryPinnedBundle("pin-abc");

        Assert.True(PlayTabPinService.IsTabKeyPlayPin(bundle, "pin-abc"));
        Assert.False(PlayTabPinService.IsTabKeyPlayPin(bundle, "other"));
        Assert.Null(bundle.Metadata.PinnedPlayTabKey);
    }
}

[CollectionDefinition("PlayTabHarness", DisableParallelization = true)]
public sealed class PlayTabHarnessCollection : ICollectionFixture<IsolatedAppRootFixture>;

[Collection("PlayTabHarness")]
[Trait("Category", "Unit")]
public sealed class PlayTabPinHarnessTests : IAsyncLifetime
{
    private readonly PlayTabPinHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TryFindWebViewForPlaySession_finds_registry_pin_after_schema6_save()
    {
        var (pinnedWv, pinKey) = await _harness.AddTabAsync("registry-pin-tab");
        await _harness.AddTabAsync("other-tab");

        var bundle = PlayTabPinHarness.CreateRegistryPinnedBundle(pinKey);

        var found = await _harness.OnUiAsync(() =>
            PlayTabPinService.TryFindWebViewForPlaySession(_harness.Tabs, bundle));
        Assert.Same(pinnedWv, found);
    }

    [Fact]
    public async Task FindWebViewByPinKey_uses_GetPlayPinKey_not_metadata()
    {
        var (pinnedWv, pinKey) = await _harness.AddTabAsync("registry-pin-tab");
        var bundle = PlayTabPinHarness.CreateRegistryPinnedBundle(pinKey);

        var found = await _harness.OnUiAsync(() =>
            PlayTabPinService.FindWebViewByPinKey(
                _harness.Tabs,
                PlayTabPinService.GetPlayPinKey(bundle)));

        Assert.Same(pinnedWv, found);
        Assert.Null(bundle.Metadata.PinnedPlayTabKey);
    }

    [Fact]
    public async Task Registration_and_lookup_scenario_stale_play_ref_vs_registry_pin()
    {
        var (pinnedWv, pinKey) = await _harness.AddTabAsync("registry-pin-tab");
        var (staleWv, staleKey) = await _harness.AddTabAsync("stale-play-ref");

        var bundle = PlayTabPinHarness.CreateRegistryPinnedBundle(pinKey);
        var registered = new[] { pinKey, staleKey };

        // Pinned tab registers even when stale _playWebView points elsewhere and user is on pinned tab.
        Assert.True(PlayComposeInjectionPolicy.ShouldRegisterIntercept(
            new PlayComposeRegistrationContext(
                IsPlayMode: true,
                Bundle: bundle,
                CandidateTabKey: pinKey,
                PlayWebViewTabKey: staleKey,
                ActiveWebViewTabKey: pinKey,
                SuppressPlayAutomation: false)));

        // Unrelated background tab — no intercept.
        Assert.False(PlayComposeInjectionPolicy.ShouldRegisterIntercept(
            new PlayComposeRegistrationContext(
                IsPlayMode: true,
                Bundle: bundle,
                CandidateTabKey: "unrelated-background",
                PlayWebViewTabKey: staleKey,
                ActiveWebViewTabKey: pinKey,
                SuppressPlayAutomation: false)));

        // Stale _playWebView ref stays eligible (hooks may remain from earlier session).
        Assert.True(PlayComposeInjectionPolicy.ShouldRegisterIntercept(
            new PlayComposeRegistrationContext(
                IsPlayMode: true,
                Bundle: bundle,
                CandidateTabKey: staleKey,
                PlayWebViewTabKey: staleKey,
                ActiveWebViewTabKey: pinKey,
                SuppressPlayAutomation: false)));

        var resolvedTabKey = PlayComposeInjectionPolicy.ResolveInjectionTabKey(
            bundle,
            stalePlayWebViewTabKey: staleKey,
            resolvedPlayWebViewTabKey: pinKey,
            registered);

        Assert.Equal(pinKey, resolvedTabKey);

        var resolvedWv = await _harness.OnUiAsync(() =>
            PlayTabPinService.TryFindWebViewForPlaySession(_harness.Tabs, bundle));
        Assert.Same(pinnedWv, resolvedWv);
        Assert.NotSame(staleWv, resolvedWv);
    }
}
