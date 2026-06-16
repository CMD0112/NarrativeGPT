using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private readonly HashSet<ChatGptAdventureBridgeInjection> _wiredTurnInvalidationBridges = [];

    private void WireTurnInvalidationBridge(ChatGptAdventureBridgeInjection bridge)
    {
        if (!_wiredTurnInvalidationBridges.Add(bridge))
            return;

        bridge.MessageReceived += OnAdventureBridgeTurnInvalidation;
    }

    private void OnAdventureBridgeTurnInvalidation(object? sender, AdventureBridgeMessage e)
    {
        if (!string.Equals(e.Type, "turnInvalidated", StringComparison.Ordinal))
            return;

        if (_activeAdventureId is not { } adventureId)
            return;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        TurnInvalidationService.HandleDomTurnInvalidated(
            bundle,
            e.DomTurnId,
            e.Reason,
            e.Text);

        AdventureStore.Save(bundle);
    }

    private async Task ApplyThreadOrdinalMapToPlayTabAsync()
    {
        if (_playWebView?.CoreWebView2 is not { } core || _activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var map = ThreadMetadataService.BuildOrdinalMap(bundle);
        await ChatGptAdventureBridgeInjection.ApplyThreadOrdinalMapAsync(core, map);
    }
}
