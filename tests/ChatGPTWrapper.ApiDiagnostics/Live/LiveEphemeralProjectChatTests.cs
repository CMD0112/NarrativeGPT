using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

/// <summary>
/// Full create → send → capture → delete cycle against a real linked project.
/// Requires CGW_RUN_LIVE_API_TESTS=1 and CGW_EPHEMERAL_GIZMO_ID.
/// </summary>
[Collection("LiveWebView")]
[Trait("Category", "Live")]
public sealed class LiveEphemeralProjectChatTests
{
    private readonly LiveWebViewFixture _fixture;

    public LiveEphemeralProjectChatTests(LiveWebViewFixture fixture) => _fixture = fixture;

    [LiveFact]
    public async Task Run_once_create_send_capture_delete()
    {
        var gizmoId = Environment.GetEnvironmentVariable("CGW_EPHEMERAL_GIZMO_ID");
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            Assert.True(true, "Skipped: set CGW_EPHEMERAL_GIZMO_ID to run ephemeral chat live test.");
            return;
        }

        EphemeralProjectChatResult? result = null;

        await _fixture.Host.RunOnUiAsync(async () =>
        {
            var webView = _fixture.Host.WebView
                          ?? throw new InvalidOperationException("WebView not ready.");
            var core = _fixture.Host.Core
                       ?? throw new InvalidOperationException("WebView core not ready.");
            var bridge = _fixture.Host.Bridge
                         ?? throw new InvalidOperationException("Bridge not ready.");

            await EnsureSignedInOnChatGptAsync(core);
            await EnsureOnProjectAsync(core, gizmoId);
            await WaitForPageReadyAsync(core);

            var adventureBridge = new ChatGptAdventureBridgeInjection(webView);
            adventureBridge.Register();
            await adventureBridge.InjectAsync(core);

            var turnService = new AdventureTurnService(adventureBridge);
            Assert.True(
                await turnService.EnsureUtilityBridgeReadyAsync(core),
                "Adventure bridge not ready on project page.");

            var composerAlreadyOpen = await EnsureProjectComposerReadyAsync(turnService, core, gizmoId);

            var projectApi = new ChatGptProjectApiService(bridge);
            var conversationSend = new ChatGptConversationSendService(bridge);
            var service = new EphemeralProjectChatService(projectApi, conversationSend);

            result = await service.RunOnceAsync(
                new EphemeralProjectChatRequest
                {
                    Core = core,
                    GizmoId = gizmoId,
                    MessageText = "Reply with exactly: EPHEMERAL_OK",
                    TurnService = turnService,
                    ComposerAlreadyOpen = composerAlreadyOpen,
                    TryUiCreate = composerAlreadyOpen
                        ? null
                        : (c, ct) => TryUiOpenProjectChatAsync(turnService, c, ct),
                    WarmSession = true,
                    DeleteAfterCapture = true,
                    DeleteInBackground = false,
                    CaptureMaxAttempts = 4,
                    CapturePollDelay = TimeSpan.FromSeconds(1),
                    MaxComposerWaitSeconds = 8,
                });
        });

        Assert.NotNull(result);
        Assert.True(
            result!.Success,
            $"Ephemeral chat failed at {result.FailedPhase}: {result.Error} delete={result.Deleted} deleteError={result.DeleteError}");
        Assert.False(string.IsNullOrWhiteSpace(result.ResponseText));
        Assert.True(result.Deleted, $"Hide failed: {result.DeleteError}");
    }

    private static async Task<bool> EnsureProjectComposerReadyAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        string gizmoId)
    {
        for (var warmup = 0; warmup < 40; warmup++)
        {
            var href = await UtilityConversationPageService.GetPageHrefAsync(core);
            var health = await turnService.GetAdventureComposerHealthAsync(core);
            if (health.ComposerFound
                && EphemeralProjectChatService.CanSendFromProjectHome(href, gizmoId))
            {
                return true;
            }

            await Task.Delay(250);
        }

        var ui = await turnService.StartProjectChatAsync(core);
        if (ui.ComposerReady || ui.Success)
        {
            var href = await UtilityConversationPageService.GetPageHrefAsync(core);
            return EphemeralProjectChatService.CanSendFromProjectHome(href, gizmoId);
        }

        if (string.Equals(ui.Error, "project_new_chat_not_found", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ui.Error, "project_chat_not_ready", StringComparison.OrdinalIgnoreCase))
        {
            var health = await turnService.GetAdventureComposerHealthAsync(core);
            var href = await UtilityConversationPageService.GetPageHrefAsync(core);
            if (health.ComposerFound
                && EphemeralProjectChatService.CanSendFromProjectHome(href, gizmoId))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(ui.Error)
            && !string.Equals(ui.Error, "project_chat_not_ready", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ui.Error, "project_new_chat_not_found", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"New chat in project failed: {ui.Error} (source={core.Source})");
        }

        await EnsureOnProjectAsync(core, gizmoId);
        var finalHealth = await turnService.GetAdventureComposerHealthAsync(core);
        Assert.True(
            finalHealth.ComposerFound,
            $"Project composer not found (source={core.Source}).");
        return true;
    }

    private static async Task<string?> TryUiOpenProjectChatAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
        var conversationId = ui.ConversationId ?? await turnService.GetConversationIdAsync(core);
        if (!string.IsNullOrWhiteSpace(conversationId))
            return conversationId;

        var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
        return health.ComposerFound ? string.Empty : null;
    }

    private static async Task EnsureOnProjectAsync(CoreWebView2 core, string gizmoId)
    {
        var href = await UtilityConversationPageService.GetPageHrefAsync(core);
        if (EphemeralProjectChatService.CanSendFromProjectHome(href, gizmoId))
            return;

        var url = ChatGptUrls.BuildProjectUrl(gizmoId);
        core.Navigate(url);
        await WaitForSourceContainsAsync(core, "/project", TimeSpan.FromSeconds(45));
        await WaitForPageReadyAsync(core);
    }

    private static async Task EnsureSignedInOnChatGptAsync(CoreWebView2 core)
    {
        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            if (Uri.TryCreate(core.Source, UriKind.Absolute, out var u)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(u))
            {
                tcs.TrySetResult();
            }
        }

        core.NavigationCompleted += Handler;
        try
        {
            core.Navigate("https://chatgpt.com");
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(90));
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static async Task WaitForPageReadyAsync(CoreWebView2 core)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var raw = await core.ExecuteScriptAsync(
                "JSON.stringify({ready:document.readyState==='complete'||document.readyState==='interactive'," +
                "body:!!document.body,state:document.readyState,href:location.href})");

            if (TryParsePageProbe(raw, out var probe) && (probe.Ready || probe.HasBody))
                return;

            await Task.Delay(500);
        }

        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            return;
        }

        throw new TimeoutException($"Page did not become ready within 60s (source={core.Source})");
    }

    private static bool TryParsePageProbe(string raw, out PageProbe probe)
    {
        probe = default;
        try
        {
            var json = BridgeScriptJson.Normalize(raw);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            probe = new PageProbe(
                root.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True,
                root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.True,
                root.TryGetProperty("state", out var s) ? s.GetString() : null,
                root.TryGetProperty("href", out var h) ? h.GetString() : null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct PageProbe(bool Ready, bool HasBody, string? State, string? Href);

    private static async Task WaitForSourceContainsAsync(
        CoreWebView2 core,
        string fragment,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (core.Source.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(250);
        }

        throw new TimeoutException($"Timed out waiting for source to contain {fragment}: {core.Source}");
    }
}
