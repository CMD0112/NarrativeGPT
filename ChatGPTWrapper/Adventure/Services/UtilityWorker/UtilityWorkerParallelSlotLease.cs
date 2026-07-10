using ChatGPTWrapper.Adventure.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>Rented parallel worker slot with dedicated WebView and turn service.</summary>
internal sealed class UtilityWorkerParallelSlotLease
{
    public required int SlotId { get; init; }

    public required WebView2 WebView { get; init; }

    public required CoreWebView2 Core { get; init; }

    public required AdventureTurnService TurnService { get; init; }

    public required UtilityWorkerParallelSlotHost Host { get; init; }
}
