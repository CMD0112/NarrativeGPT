using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayCompanionRestoreTests
{
    [Fact]
    public void ApplyEnterPlayPreferences_AlwaysCollapsed_forces_panel_closed()
    {
        var settings = new AdventureSettings { PlaySidePanelCollapsed = false };
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionOnEnter = PlayCompanionOnEnterModes.AlwaysCollapsed };

        PlayCompanionRestoreService.ApplyEnterPlayPreferences(settings, chrome);

        Assert.True(settings.PlaySidePanelCollapsed);
    }

    [Fact]
    public void ApplyEnterPlayPreferences_AlwaysOpen_forces_panel_open()
    {
        var settings = new AdventureSettings { PlaySidePanelCollapsed = true };
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionOnEnter = PlayCompanionOnEnterModes.AlwaysOpen };

        PlayCompanionRestoreService.ApplyEnterPlayPreferences(settings, chrome);

        Assert.False(settings.PlaySidePanelCollapsed);
    }

    [Fact]
    public void ApplyEnterPlayPreferences_RememberLast_keeps_stored_collapse()
    {
        var settings = new AdventureSettings { PlaySidePanelCollapsed = true };
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionOnEnter = PlayCompanionOnEnterModes.RememberLast };

        PlayCompanionRestoreService.ApplyEnterPlayPreferences(settings, chrome);

        Assert.True(settings.PlaySidePanelCollapsed);
    }

    [Fact]
    public void ResolveTab_uses_last_tab_when_present()
    {
        var settings = new AdventureSettings { PlayCompanionLastTab = "Warnings" };
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionDefaultTab = "Reference" };

        Assert.Equal("Warnings", PlayCompanionRestoreService.ResolveTab(settings, chrome));
    }

    [Fact]
    public void ResolveTab_falls_back_to_default_tab_on_first_visit()
    {
        var settings = new AdventureSettings();
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionDefaultTab = "State" };

        Assert.Equal("State", PlayCompanionRestoreService.ResolveTab(settings, chrome));
    }

    [Fact]
    public void ResolveSection_uses_last_section_when_remember_enabled()
    {
        var settings = new AdventureSettings { PlayCompanionLastSection = "Tools" };
        var chrome = new PlaySurfaceChromeDefaults
        {
            PlayCompanionDefaultSection = "Session",
            PlayCompanionRememberExpanders = true,
        };

        Assert.Equal("Tools", PlayCompanionRestoreService.ResolveSection(settings, chrome));
    }

    [Fact]
    public void ResolveSection_ignores_last_section_when_remember_disabled()
    {
        var settings = new AdventureSettings { PlayCompanionLastSection = "Tools" };
        var chrome = new PlaySurfaceChromeDefaults
        {
            PlayCompanionDefaultSection = "Session",
            PlayCompanionRememberExpanders = false,
        };

        Assert.Equal("Session", PlayCompanionRestoreService.ResolveSection(settings, chrome));
    }

    [Fact]
    public void ResolveSection_falls_back_to_default_on_first_visit()
    {
        var settings = new AdventureSettings();
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionDefaultSection = "Narrator" };

        Assert.Equal("Narrator", PlayCompanionRestoreService.ResolveSection(settings, chrome));
    }

    [Fact]
    public void TryGetExpanderState_respects_remember_expanders_flag()
    {
        var settings = new AdventureSettings
        {
            PlayCompanionExpanderState = new Dictionary<string, bool> { ["Narrator"] = true },
        };
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionRememberExpanders = false };

        var found = PlayCompanionRestoreService.TryGetExpanderState(
            settings,
            chrome,
            "Narrator",
            defaultExpanded: false,
            out var expanded);

        Assert.False(found);
        Assert.False(expanded);
    }

    [Fact]
    public void TryGetExpanderState_restores_saved_state_when_enabled()
    {
        var settings = new AdventureSettings
        {
            PlayCompanionExpanderState = new Dictionary<string, bool> { ["Narrator"] = true },
        };
        var chrome = new PlaySurfaceChromeDefaults { PlayCompanionRememberExpanders = true };

        var found = PlayCompanionRestoreService.TryGetExpanderState(
            settings,
            chrome,
            "Narrator",
            defaultExpanded: false,
            out var expanded);

        Assert.True(found);
        Assert.True(expanded);
    }
}
