using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private async Task CheckThreadLogDriftOnLoadAsync(Guid adventureId) =>
        await SyncActiveThreadLogAsync(
            adventureId,
            AdventureThreadKind.Play,
            ThreadConversationLogCaptureSource.Api,
            snapshotTrigger: ThreadConversationLogSnapshotTrigger.SessionLoad);

    private Task PromptThreadLogSyncFromMenuAsync(Guid adventureId) =>
        SyncActiveThreadLogAsync(adventureId, AdventureThreadKind.Play, ThreadConversationLogCaptureSource.Api);

    private Task PromptThreadLogDumpFromMenuAsync(Guid adventureId) =>
        DumpActiveThreadLogAsync(adventureId, AdventureThreadKind.Play);

    private Task PromptThreadLogSnapshotFromMenuAsync(Guid adventureId) =>
        SaveActiveThreadSnapshotAsync(adventureId, AdventureThreadKind.Play);
}
