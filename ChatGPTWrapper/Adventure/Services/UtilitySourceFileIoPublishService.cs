using ChatGPTWrapper.Adventure.Models;

using ChatGPTWrapper.ChatGptApi;

using Microsoft.Web.WebView2.Core;



namespace ChatGPTWrapper.Adventure.Services;



/// <summary>Pre-send publish dispatch for jobs in <see cref="UtilitySourceFileIoCatalog"/>.</summary>

internal static class UtilitySourceFileIoPublishService

{

    public static Task<(bool Success, string? Error, IReadOnlyList<string> RemotePaths)> PublishJobInputsAsync(

        ChatGptProjectApiService api,

        CoreWebView2 core,

        AdventureBundle bundle,

        string jobId,

        Guid runId,

        IProgress<string>? progress = null,

        CancellationToken cancellationToken = default) =>

        UtilityPublishSession.PublishJobInputsAsync(

            api,

            core,

            bundle,

            jobId,

            runId,

            progress,

            cancellationToken);



    public static bool IsPublishComplete(

        Guid adventureId,

        Guid runId,

        string jobId,

        AdventureBundle bundle) =>

        UtilityPublishSession.IsPublishComplete(adventureId, runId, jobId, bundle);

}

