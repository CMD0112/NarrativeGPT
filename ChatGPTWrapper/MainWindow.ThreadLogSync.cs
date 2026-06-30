using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private async Task CheckThreadLogDriftOnLoadAsync(Guid adventureId) =>
        await SyncActiveThreadLogAsync(adventureId, AdventureThreadKind.Play, ThreadConversationLogCaptureSource.Api);

    private Task PromptThreadLogSyncFromMenuAsync(Guid adventureId) =>
        SyncActiveThreadLogAsync(adventureId, AdventureThreadKind.Play, ThreadConversationLogCaptureSource.Api);

    private Task PromptThreadLogDumpFromMenuAsync(Guid adventureId) =>
        DumpActiveThreadLogAsync(adventureId, AdventureThreadKind.Play);
}
