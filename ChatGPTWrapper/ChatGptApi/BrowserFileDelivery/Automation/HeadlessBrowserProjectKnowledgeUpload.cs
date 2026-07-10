using System.IO;
using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Playwright;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery.Automation;

/// <summary>
/// Project knowledge upload via headless Chrome (Playwright). API download verify runs before session release.
/// </summary>
internal static class HeadlessBrowserProjectKnowledgeUpload
{
    private const int PrepareMaxAttempts = 2;
    private static readonly TimeSpan PrepareRetryDelay = TimeSpan.FromMilliseconds(750);

    internal readonly record struct StageResult(bool Success, string? Error, GizmoFileRef? DownloadableFile);

    public static Task<StageResult> StageUploadAsync(
        CoreWebView2 cookieSourceCore,
        ProjectSourcePublicationRequest request,
        HashSet<string> baselineRemoteIds,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default) =>
        StageUploadAsync(
            cookieSourceCore,
            request.GizmoId,
            request.RemoteFileName,
            request.Content,
            request.MimeType,
            baselineRemoteIds,
            progress,
            cancellationToken);

    public static async Task<StageResult> StageUploadAsync(
        CoreWebView2 cookieSourceCore,
        string gizmoId,
        string remoteFileName,
        byte[] content,
        string mimeType,
        HashSet<string> baselineRemoteIds,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
            return new StageResult(false, "empty_content", null);

        var stagingDir = DomFileStagingUtilities.GetStagingDirectory(DomFileInputTarget.ProjectKnowledge);
        var sanitized = ProjectKnowledgeFileStaging.SanitizeFileName(remoteFileName);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitized)))
        {
            sanitized += DomFileStagingUtilities.GuessExtension(
                mimeType,
                DomFileInputTarget.ProjectKnowledge);
        }

        var runDir = Path.Combine(stagingDir, $"cgw-headless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runDir);
        var localPath = Path.Combine(runDir, sanitized);
        try
        {
            await File.WriteAllBytesAsync(localPath, content, cancellationToken);
            localPath = Path.GetFullPath(localPath);

            return await RunPlaywrightUploadAsync(
                cookieSourceCore,
                gizmoId,
                remoteFileName,
                content,
                localPath,
                baselineRemoteIds,
                progress,
                cancellationToken);
        }
        finally
        {
            try { File.Delete(localPath); }
            catch { /* best-effort */ }

            try { Directory.Delete(runDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private static async Task<StageResult> RunPlaywrightUploadAsync(
        CoreWebView2 cookieSourceCore,
        string gizmoId,
        string remoteFileName,
        byte[] expectedContent,
        string localPath,
        HashSet<string> baselineRemoteIds,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Launching headless browser…");
        ProjectLinkDiagnostics.Log(
            $"Headless browser publication starting gizmo={gizmoId} file={Path.GetFileName(localPath)}");

        var invalidateSession = false;
        IPage? page = null;
        try
        {
            AppDirectories.EnsureCreated();
            var normalizedGizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
            var projectUrl = ChatGptUrls.BuildProjectUrl(normalizedGizmoId);

            progress?.Report("Syncing session cookies…");
            page = await HeadlessBrowserSessionPool.AcquirePageAsync(
                cookieSourceCore,
                cancellationToken);

            progress?.Report("Opening linked project Sources…");
            var sourcesNavError = await NavigateToProjectSourcesAsync(
                page,
                normalizedGizmoId,
                projectUrl,
                cancellationToken);
            if (sourcesNavError is not null)
            {
                invalidateSession = IsSessionInvalidatingError(sourcesNavError);
                return new StageResult(false, sourcesNavError, null);
            }

            progress?.Report("Preparing project files UI…");
            var sourcesBaseline = await SnapshotSourcesListAsync(page, remoteFileName);
            ProjectLinkDiagnostics.Log(
                $"Headless browser sources baseline names={sourcesBaseline.FileNames.Count} "
                + $"remotePresentBefore={sourcesBaseline.RemoteNamePresentBeforeUpload}");

            string? prepareFailure = null;
            for (var prepareAttempt = 1; prepareAttempt <= PrepareMaxAttempts; prepareAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsOnProjectHome(page.Url, normalizedGizmoId))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Headless browser re-navigating before prepare attempt={prepareAttempt} href={page.Url}");
                    var reNav = await NavigateToProjectSourcesAsync(
                        page,
                        normalizedGizmoId,
                        projectUrl,
                        cancellationToken);
                    if (reNav is not null)
                    {
                        prepareFailure = reNav;
                        invalidateSession = IsSessionInvalidatingError(reNav);
                        break;
                    }
                }

                var sourcesUi = await OpenSourcesUploadSurfaceAsync(
                    page,
                    normalizedGizmoId,
                    projectUrl,
                    localPath,
                    prepareAttempt,
                    cancellationToken);
                if (sourcesUi.Success)
                {
                    prepareFailure = null;
                    break;
                }

                prepareFailure = sourcesUi.Error ?? "automation_prepare_failed";
                ProjectLinkDiagnostics.Log(
                    $"Headless browser prepare attempt={prepareAttempt} failed: {prepareFailure}");
                if (prepareAttempt < PrepareMaxAttempts)
                    await Task.Delay(PrepareRetryDelay, cancellationToken);
            }

            if (prepareFailure is not null)
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser prepare exhausted href={page.Url}: {prepareFailure}");
                invalidateSession = IsSessionInvalidatingError(prepareFailure);
                return new StageResult(false, prepareFailure, null);
            }

            ProjectLinkDiagnostics.Log(
                $"Headless browser file staged via chooser file={Path.GetFileName(localPath)} remote={remoteFileName} "
                + $"href={page.Url}");

            progress?.Report("Waiting for upload to finish in headless browser…");
            var cdnFailures = new List<string>();
            void OnRequestFailed(object? _, IRequest request)
            {
                if (!request.Url.Contains("oaiusercontent.com", StringComparison.OrdinalIgnoreCase))
                    return;

                var detail = request.Failure ?? "request_failed";
                cdnFailures.Add($"{detail} url={request.Url}");
                ProjectLinkDiagnostics.Log($"Headless browser CDN request failed: {detail} url={request.Url}");
            }

            page.RequestFailed += OnRequestFailed;
            try
            {
                var uploadWait = await WaitForUploadFinishInBrowserAsync(
                    page,
                    remoteFileName,
                    sourcesBaseline,
                    cdnFailures,
                    progress,
                    cancellationToken);
                if (!uploadWait.Success)
                {
                    invalidateSession = true;
                    return new StageResult(false, uploadWait.Error, null);
                }
            }
            finally
            {
                page.RequestFailed -= OnRequestFailed;
            }

            progress?.Report("Waiting for blob download (headless session open)…");
            try
            {
                var downloadableFile = await HeadlessBrowserProjectApi.WaitForDownloadableFileAsync(
                    page,
                    normalizedGizmoId,
                    remoteFileName,
                    baselineRemoteIds,
                    expectedContent,
                    progress,
                    cancellationToken);

                ProjectLinkDiagnostics.Log(
                    $"Headless browser upload + download verified file_id={downloadableFile.FileId} "
                    + $"name={downloadableFile.Name}");
                progress?.Report("Headless upload verified via API download.");
                return new StageResult(true, null, downloadableFile);
            }
            catch (ChatGptApiException ex)
            {
                var listCandidate = await HeadlessBrowserProjectApi.TryResolveListCandidateAsync(
                    page,
                    normalizedGizmoId,
                    remoteFileName,
                    baselineRemoteIds,
                    cancellationToken);
                if (listCandidate is not null)
                {
                    ProjectLinkDiagnostics.Log(
                        $"Headless browser upload listed file_id={listCandidate.FileId} name={listCandidate.Name} "
                        + $"blob pending ({ex.Message}) — deferring verify to WebView");
                    progress?.Report("Upload listed on project; verifying download…");
                    return new StageResult(true, null, listCandidate);
                }

                invalidateSession = true;
                ProjectLinkDiagnostics.Log($"Headless browser API download verify failed: {ex.Message}");
                return new StageResult(false, ex.Message, null);
            }
        }
        catch (PlaywrightException ex)
        {
            invalidateSession = true;
            var hint = ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                ? " Install Playwright browsers: pwsh -ExecutionPolicy Bypass -File \"<app output>\\playwright.ps1\" install chrome (build the app first so playwright.ps1 is copied to bin\\Debug\\net9.0-windows)."
                : "";
            ProjectLinkDiagnostics.Log($"Headless browser Playwright error: {ex.Message}{hint}");
            return new StageResult(false, $"automation_playwright_failed:{ex.Message}{hint}", null);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("automation_", StringComparison.Ordinal))
        {
            invalidateSession = true;
            ProjectLinkDiagnostics.Log($"Headless browser session error: {ex.Message}");
            return new StageResult(false, ex.Message, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            invalidateSession = true;
            ProjectLinkDiagnostics.Log($"Headless browser upload failed: {ex.Message}");
            return new StageResult(false, $"automation_failed:{ex.Message}", null);
        }
        finally
        {
            if (page is not null)
                HeadlessBrowserSessionPool.Release(invalidateSession);
        }
    }

    private static bool IsSessionInvalidatingError(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && (error.Contains("login_required", StringComparison.OrdinalIgnoreCase)
            || error.Contains("not_on_project_page", StringComparison.OrdinalIgnoreCase)
            || error.Contains("session_not_ready", StringComparison.OrdinalIgnoreCase)
            || error.Contains("no_session_cookies", StringComparison.OrdinalIgnoreCase)
            || error.Contains("stage_off_project", StringComparison.OrdinalIgnoreCase)
            || error.Contains("oaiusercontent_blocked", StringComparison.OrdinalIgnoreCase));

    private static async Task<string?> NavigateToProjectSourcesAsync(
        IPage page,
        string gizmoId,
        string projectUrl,
        CancellationToken cancellationToken)
    {
        var sourcesUrl = BuildProjectSourcesUrl(projectUrl);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectLinkDiagnostics.Log(
                $"Headless browser navigating to Sources attempt={attempt} url={sourcesUrl} from={page.Url}");

            try
            {
                await page.GotoAsync(
                    sourcesUrl,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 60_000,
                    });
            }
            catch (PlaywrightException ex)
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser Sources navigation attempt={attempt} failed: {ex.Message}");
                if (attempt == 2)
                    return $"automation_not_on_project_page: expected={sourcesUrl} href={page.Url}";
                continue;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await LooksLikeLoginWallAsync(page))
                    return "automation_login_required: sign in via ChatGPT Wrapper, then retry";

                if (IsOnProjectHome(page.Url, gizmoId)
                    && await WaitForSelectedSourcesTabAsync(page, cancellationToken))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Headless browser on Sources tab attempt={attempt} href={page.Url}");
                    return null;
                }

                if (!IsOnProjectHome(page.Url, gizmoId))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Headless browser drifted off project during Sources nav href={page.Url}; retrying");
                    break;
                }

                await Task.Delay(200, cancellationToken);
            }
        }

        return $"automation_not_on_project_page: expected={sourcesUrl} href={page.Url}";
    }

    private static async Task<bool> WaitForSelectedSourcesTabAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        var selectedSourcesTab = page
            .Locator("main [role='tablist'] [role='tab'][aria-selected='true']")
            .Filter(new LocatorFilterOptions
            {
                HasTextRegex = new Regex("^\\s*Sources\\s*$", RegexOptions.IgnoreCase),
            });

        try
        {
            await selectedSourcesTab.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000,
            });
            return true;
        }
        catch (TimeoutException)
        {
            var sourcesTab = page.Locator("main [role='tablist'] [role='tab']").Filter(new LocatorFilterOptions
            {
                HasTextRegex = new Regex("^\\s*Sources\\s*$", RegexOptions.IgnoreCase),
            });
            if (await sourcesTab.CountAsync() == 0)
                return false;

            await sourcesTab.First.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            try
            {
                await selectedSourcesTab.First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 10_000,
                });
                return true;
            }
            catch (TimeoutException)
            {
                return page.Url.Contains("tab=sources", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private readonly record struct SourcesUploadSurfaceResult(bool Success, string? Error);

    private static string BuildProjectSourcesUrl(string projectUrl) =>
        projectUrl.Contains('?', StringComparison.Ordinal)
            ? $"{projectUrl}&tab=sources"
            : $"{projectUrl}?tab=sources";

    private static async Task<SourcesUploadSurfaceResult> OpenSourcesUploadSurfaceAsync(
        IPage page,
        string gizmoId,
        string projectUrl,
        string localFilePath,
        int attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsSourcesPanelReadyAsync(page, gizmoId))
            {
                var sourcesUrl = BuildProjectSourcesUrl(projectUrl);
                ProjectLinkDiagnostics.Log(
                    $"Headless browser opening Sources tab attempt={attempt} url={sourcesUrl}");
                await page.GotoAsync(
                    sourcesUrl,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 60_000,
                    });

                if (!await WaitForSelectedSourcesTabAsync(page, cancellationToken))
                {
                    await LogPrepareDiagnosticsAsync(page, attempt);
                    return new SourcesUploadSurfaceResult(false, "sources_tab_not_found");
                }
            }
            else
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser Sources panel already ready attempt={attempt} href={page.Url}");
            }

            ProjectLinkDiagnostics.Log(
                $"Headless browser Sources tab selected attempt={attempt} href={page.Url}");

            var addButton = await ResolveSourcesAddButtonAsync(page);
            if (addButton is null)
            {
                await LogPrepareDiagnosticsAsync(page, attempt);
                return new SourcesUploadSurfaceResult(false, "add_sources_not_found");
            }

            await addButton.ScrollIntoViewIfNeededAsync();
            await addButton.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000,
            });

            await addButton.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            ProjectLinkDiagnostics.Log(
                $"Headless browser Sources add button clicked attempt={attempt} href={page.Url}");

            var uploadOption = await ResolveSourcesUploadPopupButtonAsync(page, attempt);
            if (uploadOption is null)
            {
                await LogPrepareDiagnosticsAsync(page, attempt);
                return new SourcesUploadSurfaceResult(false, "upload_option_not_found");
            }

            await uploadOption.ScrollIntoViewIfNeededAsync();
            await uploadOption.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000,
            });

            var chooserTask = page.WaitForFileChooserAsync(new PageWaitForFileChooserOptions
            {
                Timeout = 8_000,
            });
            await uploadOption.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
            ProjectLinkDiagnostics.Log(
                $"Headless browser Sources upload option clicked attempt={attempt}");
            try
            {
                var chooser = await chooserTask;
                await chooser.SetFilesAsync(localFilePath);
                ProjectLinkDiagnostics.Log(
                    $"Headless browser upload option opened file chooser attempt={attempt}");
                return new SourcesUploadSurfaceResult(true, null);
            }
            catch (TimeoutException)
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser upload option no file chooser attempt={attempt} href={page.Url}");
                return new SourcesUploadSurfaceResult(false, "file_chooser_not_opened");
            }
        }
        catch (TimeoutException ex)
        {
            await LogPrepareDiagnosticsAsync(page, attempt);
            return new SourcesUploadSurfaceResult(false, $"sources_ui_timeout: {ex.Message}");
        }
        catch (PlaywrightException ex)
        {
            await LogPrepareDiagnosticsAsync(page, attempt);
            var message = ex.Message;
            if (message.Contains("Sources", StringComparison.OrdinalIgnoreCase))
                return new SourcesUploadSurfaceResult(false, $"sources_tab_not_found: {message}");
            if (message.Contains("Add", StringComparison.OrdinalIgnoreCase)
                && message.Contains("source", StringComparison.OrdinalIgnoreCase))
            {
                return new SourcesUploadSurfaceResult(false, $"add_sources_not_found: {message}");
            }

            return new SourcesUploadSurfaceResult(false, $"sources_ui_failed: {message}");
        }
    }

    private static async Task<bool> IsSourcesPanelReadyAsync(IPage page, string gizmoId)
    {
        if (!IsOnProjectHome(page.Url, gizmoId))
            return false;
        if (!page.Url.Contains("tab=sources", StringComparison.OrdinalIgnoreCase))
            return false;

        var addButton = await ResolveSourcesAddButtonAsync(page);
        return addButton is not null && await addButton.IsVisibleAsync();
    }

    private static async Task<ILocator?> ResolveSourcesAddButtonAsync(IPage page)
    {
        ILocator[] structural =
        [
            page.Locator("[id*='content-sources'] section section button"),
            page.Locator("main [id*='content-sources'] section section button"),
            page.Locator("main section section > button"),
        ];

        foreach (var candidate in structural)
        {
            if (await candidate.CountAsync() == 0)
                continue;

            var button = candidate.First;
            if (await button.IsVisibleAsync())
            {
                ProjectLinkDiagnostics.Log("Headless browser Sources add button matched structural section>section>button");
                return button;
            }
        }

        var main = page.Locator("main");
        string[] labels =
        [
            "Add files and more",
            "Add sources",
            "Add Sources",
            "Upload file",
            "Add file",
        ];

        foreach (var label in labels)
        {
            var button = main.GetByRole(
                AriaRole.Button,
                new LocatorGetByRoleOptions { Name = label, Exact = true });
            if (await button.CountAsync() > 0)
            {
                ProjectLinkDiagnostics.Log($"Headless browser Sources add button matched label={label}");
                return button.First;
            }
        }

        var fuzzy = main.GetByRole(
            AriaRole.Button,
            new LocatorGetByRoleOptions
            {
                NameRegex = new Regex("add.*(source|file)", RegexOptions.IgnoreCase),
            });
        if (await fuzzy.CountAsync() > 0)
        {
            ProjectLinkDiagnostics.Log("Headless browser Sources add button matched fuzzy add/source|file");
            return fuzzy.First;
        }

        return null;
    }

    private static async Task<ILocator?> ResolveSourcesUploadPopupButtonAsync(IPage page, int attempt)
    {
        var popupRoot = page.Locator(
            "[role='dialog'], [data-radix-popper-content-wrapper], [id^='radix-']:not([id*='content-sources'])");
        var popupGrid = page.Locator("div.mt-4.grid, div.grid.grid-cols-2, div.grid.sm\\:grid-cols-4");
        try
        {
            await popupGrid.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 8_000,
            });
        }
        catch (TimeoutException)
        {
            try
            {
                await popupRoot.First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 3_000,
                });
            }
            catch (TimeoutException)
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser Sources upload popup not visible attempt={attempt} href={page.Url}");
                return null;
            }
        }

        var uploadByRole = page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "Upload", Exact = true });
        if (await uploadByRole.CountAsync() > 0 && await uploadByRole.First.IsVisibleAsync())
        {
            ProjectLinkDiagnostics.Log("Headless browser Sources upload option matched role=Upload");
            return uploadByRole.First;
        }

        ILocator[] structural =
        [
            page.Locator("div.mt-4.grid button").First,
            page.Locator("div.grid.grid-cols-2 button, div.grid.sm\\:grid-cols-4 button").First,
            page.Locator("div.mt-4.grid button").Filter(new LocatorFilterOptions
            {
                HasTextRegex = new Regex("^\\s*Upload\\s*$", RegexOptions.IgnoreCase),
            }),
            page.Locator("[role='dialog'] div.grid button").First,
        ];

        foreach (var candidate in structural)
        {
            if (await candidate.CountAsync() == 0)
                continue;

            var button = candidate.First;
            if (!await button.IsVisibleAsync())
                continue;

            var text = (await button.InnerTextAsync()).Trim();
            if (Regex.IsMatch(text, "upload", RegexOptions.IgnoreCase) || string.IsNullOrWhiteSpace(text))
            {
                ProjectLinkDiagnostics.Log(
                    $"Headless browser Sources upload option matched structural grid button text={text}");
                return button;
            }
        }

        var gridUpload = page.Locator("div.grid button").Filter(new LocatorFilterOptions
        {
            HasTextRegex = new Regex("upload", RegexOptions.IgnoreCase),
        });
        if (await gridUpload.CountAsync() > 0)
        {
            ProjectLinkDiagnostics.Log("Headless browser Sources upload option matched grid has-text upload");
            return gridUpload.First;
        }

        return null;
    }

    private readonly record struct SourcesListSnapshot(
        HashSet<string> FileNames,
        string PanelText,
        bool RemoteNamePresentBeforeUpload);

    private static async Task<SourcesListSnapshot> SnapshotSourcesListAsync(IPage page, string remoteFileName)
    {
        var remoteBasename = ProjectKnowledgeFileStaging.Basename(remoteFileName);
        var panel = page.Locator("[id*='content-sources']");
        if (await panel.CountAsync() == 0)
            panel = page.Locator("main");

        var text = await panel.First.InnerTextAsync();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length > 200)
                continue;
            if (Regex.IsMatch(line, @"\.[a-z0-9]{1,10}$", RegexOptions.IgnoreCase))
                names.Add(line);
        }

        var presentBefore = names.Contains(remoteBasename)
                            || text.Contains(remoteBasename, StringComparison.OrdinalIgnoreCase);
        return new SourcesListSnapshot(names, text, presentBefore);
    }

    private static async Task<(bool Success, string? Error)> WaitForUploadFinishInBrowserAsync(
        IPage page,
        string remoteFileName,
        SourcesListSnapshot baseline,
        IReadOnlyList<string> cdnFailures,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var remoteBasename = ProjectKnowledgeFileStaging.Basename(remoteFileName);
        var fileLocator = page.Locator("[id*='content-sources']")
            .GetByText(remoteBasename, new LocatorGetByTextOptions { Exact = false });
        if (await fileLocator.CountAsync() == 0)
        {
            fileLocator = page.Locator("main")
                .GetByText(remoteBasename, new LocatorGetByTextOptions { Exact = false });
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        var pollCount = 0;
        var pollDelayMs = 200;
        var sawPending = false;
        var uploadStartedUtc = DateTime.UtcNow;
        var panelLocator = page.Locator("[id*='content-sources']");
        if (await panelLocator.CountAsync() == 0)
            panelLocator = page.Locator("main");

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pollCount++;

            try
            {
                var hostFailure = await DetectUploadHostFailureAsync(page);
                if (hostFailure is not null)
                    return (false, hostFailure);

                if (cdnFailures.Count > 0)
                {
                    return (false, BuildOaiUploadBlockedError(cdnFailures[0]));
                }

                var pending = await page.Locator("[aria-busy='true'], [role='progressbar']").CountAsync() > 0;
                if (pending)
                    sawPending = true;

                var nameVisible = await fileLocator.CountAsync() > 0
                                  && await fileLocator.First.IsVisibleAsync();
                var panelText = await panelLocator.First.InnerTextAsync();
                var textGrew = panelText.Length > baseline.PanelText.Length + 8;
                var elapsed = DateTime.UtcNow - uploadStartedUtc;
                var uploadEvidence = nameVisible
                                     && (!baseline.RemoteNamePresentBeforeUpload || sawPending || textGrew);
                var settled = uploadEvidence
                              && !pending
                              && (sawPending || elapsed >= TimeSpan.FromSeconds(4))
                              && pollCount >= 3;

                if (pollCount == 1 || pollCount % 20 == 0)
                {
                    ProjectLinkDiagnostics.Log(
                        $"Headless browser upload poll #{pollCount} file={remoteFileName} visible={nameVisible} "
                        + $"pending={pending} sawPending={sawPending} textGrew={textGrew} "
                        + $"elapsedSec={elapsed.TotalSeconds:F1} remotePresentBefore={baseline.RemoteNamePresentBeforeUpload} "
                        + $"href={page.Url}");
                }

                if (settled)
                {
                    hostFailure = await DetectUploadHostFailureAsync(page);
                    if (hostFailure is not null)
                        return (false, hostFailure);

                    ProjectLinkDiagnostics.Log(
                        $"Headless browser upload finished in-page file={remoteFileName} polls={pollCount} href={page.Url}");
                    return (true, null);
                }
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "automation_browser_closed_during_upload_wait");
            }

            if (pollCount % 40 == 0)
                progress?.Report($"Headless browser upload in progress… (~{pollCount * pollDelayMs / 1000}s)");

            await Task.Delay(pollDelayMs, cancellationToken);
            if (pollCount == 15 && pollDelayMs < 400)
                pollDelayMs = 400;
        }

        if (cdnFailures.Count > 0)
            return (false, BuildOaiUploadBlockedError(cdnFailures[0]));

        var lateFailure = await DetectUploadHostFailureAsync(page);
        if (lateFailure is not null)
            return (false, lateFailure);

        ProjectLinkDiagnostics.Log(
            $"Headless browser upload wait timeout file={remoteFileName} polls={pollCount} href={page.Url}");
        return (false, $"automation_upload_timeout: file={remoteFileName}");
    }

    private static async Task<string?> DetectUploadHostFailureAsync(IPage page)
    {
        var alerts = page.Locator("[role='alert'], [data-testid*='toast'], [class*='toast']");
        var count = await alerts.CountAsync();
        for (var i = 0; i < count && i < 8; i++)
        {
            var alert = alerts.Nth(i);
            if (!await alert.IsVisibleAsync())
                continue;

            var text = await alert.InnerTextAsync();
            if (!text.Contains("files.oaiusercontent.com", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("failed upload", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ProjectLinkDiagnostics.Log($"Headless browser upload failure banner: {text.Trim()}");
            return BuildOaiUploadBlockedError(text.Trim());
        }

        return null;
    }

    private static string BuildOaiUploadBlockedError(string detail) =>
        "automation_oaiusercontent_blocked: ChatGPT could not upload to files.oaiusercontent.com from headless Chrome. "
        + "Allow this host through AdGuard, firewall, VPN, or corporate proxy (headless Chrome may be filtered differently than the embedded WebView). "
        + $"Detail: {detail}";

    private static async Task LogPrepareDiagnosticsAsync(IPage page, int attempt)
    {
        try
        {
            var tabSummaries = new List<string>();
            var tabLoc = page.Locator("main [role='tablist'] [role='tab']");
            var tabCount = await tabLoc.CountAsync();
            for (var i = 0; i < tabCount && tabSummaries.Count < 8; i++)
            {
                var tab = tabLoc.Nth(i);
                var text = (await tab.InnerTextAsync()).Trim();
                var selected = await tab.GetAttributeAsync("aria-selected");
                tabSummaries.Add($"{text} selected={selected}");
            }

            var buttonSummaries = new List<string>();
            var buttonLoc = page.Locator("main button, main [role='button']");
            var buttonCount = await buttonLoc.CountAsync();
            for (var i = 0; i < buttonCount && buttonSummaries.Count < 12; i++)
            {
                var button = buttonLoc.Nth(i);
                if (!await button.IsVisibleAsync())
                    continue;

                var text = ((await button.InnerTextAsync()) + " " + (await button.GetAttributeAsync("aria-label") ?? "")).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (!Regex.IsMatch(text, "source|upload|add|file", RegexOptions.IgnoreCase))
                    continue;

                buttonSummaries.Add(text.Length > 60 ? text[..60] : text);
            }

            ProjectLinkDiagnostics.Log(
                $"Headless browser prepare diagnostics attempt={attempt} href={page.Url} "
                + $"tabs=[{string.Join("; ", tabSummaries)}] buttons=[{string.Join("; ", buttonSummaries)}]");
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"Headless browser prepare diagnostics failed: {ex.Message}");
        }
    }

    private static bool IsOnProjectHome(string? url, string gizmoId)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return ChatGptUrls.IsCanonicalProjectHome(uri, gizmoId);
    }

    private static async Task<bool> LooksLikeLoginWallAsync(IPage page)
    {
        try
        {
            return await page.Locator("button:has-text(\"Log in\"), a:has-text(\"Log in\")").CountAsync() > 0
                   && await page.Locator("[data-testid=\"composer\"], textarea").CountAsync() == 0;
        }
        catch
        {
            return false;
        }
    }
}
