using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>DOM turn-invalidation handling for WinUI play bridges.</summary>
internal static class WinUiTurnInvalidationBridge
{
    private static readonly HashSet<ChatGptAdventureBridgeInjection> Wired = [];

    public static void Wire(ChatGptAdventureBridgeInjection bridge, WinUiPlaySessionService session)
    {
        if (!Wired.Add(bridge))
            return;

        bridge.MessageReceived += (_, e) => OnMessage(e, session);
    }

    private static void OnMessage(AdventureBridgeMessage e, WinUiPlaySessionService session)
    {
        if (!string.Equals(e.Type, "turnInvalidated", StringComparison.Ordinal))
            return;

        var bundle = session.CurrentBundle;
        if (bundle is null)
            return;

        TurnInvalidationService.HandleDomTurnInvalidated(
            bundle,
            e.LogTurnIndex,
            e.DomTurnId,
            e.Reason,
            e.Text,
            e.EditRole,
            e.RevisionGroupId,
            e.RevisionPrompt,
            e.AssistantDomTurnId);

        AdventureStore.Save(bundle);
        session.ReloadBundle(bundle.Metadata.Id);
        session.NotifyStatusChanged();
    }
}
