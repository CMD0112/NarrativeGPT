using ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Thin adapter delegating to <see cref="ProjectFilePublicationService"/>.
/// </summary>
public sealed class ProjectSourcePublicationPipeline
{
    private readonly ProjectFilePublicationService _service;

    public ProjectSourcePublicationPipeline(ChatGptProjectApiService api) =>
        _service = new ProjectFilePublicationService(api);

    public Task<ProjectSourcePublicationResult> PublishAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        _service.PublishAsync(
            core,
            request,
            ProjectPublicationProfile.Lab,
            progress,
            cancellationToken);

    public Task<ProjectSourcePublicationResult> PublishBatchAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        _service.PublishAsync(
            core,
            request,
            ProjectPublicationProfile.BatchSync,
            progress,
            cancellationToken);

    public Task<ProjectSourcePublicationResult> PublishUtilityFastAsync(
        CoreWebView2 core,
        ProjectSourcePublicationRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        _service.PublishAsync(
            core,
            request,
            ProjectPublicationProfile.UtilityFast,
            progress,
            cancellationToken);

    public Task<IReadOnlyList<ProjectSourcePublicationResult>> PublishUtilityFastBatchAsync(
        CoreWebView2 core,
        IReadOnlyList<ProjectSourcePublicationRequest> requests,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        _service.PublishUtilityFastBatchAsync(core, requests, progress, cancellationToken);
}
