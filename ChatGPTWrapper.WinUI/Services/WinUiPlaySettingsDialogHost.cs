using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Wires play settings dialog callbacks for the WinUI host.</summary>
internal static class WinUiPlaySettingsDialogHost
{
    public static void Wire(IPlaySettingsHost dialog, Guid adventureId, WinUiPlaySessionService? session = null) =>
        WinUiPlaySettingsBridge.Wire(dialog, adventureId, session);
}
