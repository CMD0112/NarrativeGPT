namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayComposeAssetTests
{
    private static string WrapperAssetsDir =>
        Path.Combine(
            Path.GetDirectoryName(typeof(ChatGptPlayComposeInjection).Assembly.Location)!,
            "wrapper-assets");

    private static string ComposeJsPath => Path.Combine(WrapperAssetsDir, "cgw-play-compose.js");
    private static string ComposeCssPath => Path.Combine(WrapperAssetsDir, "cgw-play-compose.css");
    private static string BridgeJsPath => Path.Combine(WrapperAssetsDir, "adventure-bridge.js");

    [Fact]
    public void Compose_assets_exist_in_main_app_output()
    {
        Assert.True(File.Exists(ComposeJsPath), $"Missing: {ComposeJsPath}");
        Assert.True(File.Exists(ComposeCssPath), $"Missing: {ComposeCssPath}");
    }

    [Fact]
    public void Compose_script_exports_required_host_api()
    {
        var text = File.ReadAllText(ComposeJsPath);
        Assert.Contains("__cgwPlayComposeApplyState", text);
        Assert.Contains("__cgwPlayComposeGetText", text);
        Assert.Contains("__cgwPlayComposeRequestFocus", text);
        Assert.Contains("__cgwPlayComposeScheduleMount", text);
        Assert.Contains("__cgwPlayComposeUnmount", text);
        Assert.Contains("__cgwPlayComposeVersion", text);
        Assert.Contains("__cgwSetWrapperComposer", text);
        Assert.Contains("__cgwSetNativeComposePassthrough", text);
        Assert.Contains("__cgwNativeComposePassthrough", text);
        Assert.Contains("__cgwPlayComposeEnsureHooks", text);
        Assert.Contains("triggerNativeSend", text);
        Assert.DoesNotContain("cgw-compose-accept", text);
        Assert.DoesNotContain("cgwComposeAccept", text);
    }

    [Fact]
    public void Kernel_assets_exist_in_main_app_output()
    {
        Assert.True(File.Exists(Path.Combine(WrapperAssetsDir, "cgw-page-kernel.js")), "Missing kernel JS");
        Assert.True(File.Exists(Path.Combine(WrapperAssetsDir, "cgw-composer-dom.js")), "Missing composer DOM JS");
    }

    [Fact]
    public void Compose_script_uses_stable_reparent_and_focus_manager()
    {
        var text = File.ReadAllText(ComposeJsPath);
        Assert.Contains("insertBefore(root, anchor.firstChild)", text);
        Assert.Contains("needsRemount", text);
        Assert.Contains("focusWanted", text);
        Assert.Contains("FOCUS_MAX_MS", text);
        Assert.Contains("ensureEnterGuard", text);
        Assert.Contains("__cgwPageKernel", text);
        Assert.Contains("__cgwComposerDom", text);
        Assert.Contains("kernel.dom.subscribe", text);
        Assert.DoesNotContain("input.readOnly = true", text);
    }

    [Fact]
    public void Compose_script_only_syncs_text_when_patch_includes_text_or_clear()
    {
        var text = File.ReadAllText(ComposeJsPath);
        Assert.Contains("Object.prototype.hasOwnProperty.call(patch, \"text\")", text);
        Assert.Contains("patch.clear", text);
    }

    [Fact]
    public void Compose_css_hides_native_input_but_keeps_submit_clickable()
    {
        var css = File.ReadAllText(ComposeCssPath);
        Assert.Contains("data-cgw-wrapper-composer=\"1\"", css);
        Assert.Contains("#prompt-textarea", css);
        Assert.Contains("pointer-events: none", css);
        Assert.Contains("pointer-events: auto !important", css);
        Assert.Contains("user-select: none", css);
    }

    [Fact]
    public void Compose_css_does_not_hide_global_prosemirror()
    {
        var css = File.ReadAllText(ComposeCssPath);
        Assert.DoesNotContain(
            "html[data-cgw-wrapper-composer=\"1\"] div.ProseMirror[contenteditable=\"true\"]",
            css);
        Assert.Contains("[data-testid=\"composer\"] div.ProseMirror", css);
    }

    [Fact]
    public void Bridge_submit_prompt_supports_native_attachment_staging()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        Assert.Contains("stageAttachmentsOnNativeInput", bridge);
        Assert.Contains("findNativeComposerFileInput", bridge);
        Assert.Contains("useWrapperAttachmentStash", bridge);
        Assert.Contains("resolveDomAttachments", bridge);
        Assert.Contains("__cgwDomFallbackAttachmentStash", bridge);
        Assert.Contains("hostCdpStaged", bridge);
        Assert.Contains("__cgwPrepareNativeComposerForAttach", bridge);
    }

    [Fact]
    public void Compose_script_supports_pre_upload_progress_and_send_gating()
    {
        var text = File.ReadAllText(ComposeJsPath);
        Assert.Contains("__cgwPlayComposeSetUploadStatus", text);
        Assert.Contains("cgwComposeUploadRequest", text);
        Assert.Contains("attachmentsPreStaged", text);
        Assert.Contains("allAttachmentsReady", text);
        Assert.Contains("cgw-compose-upload-spinner", text);
        Assert.Contains("COMPOSE_VERSION = 29", text);
    }

    [Fact]
    public void Bridge_exposes_compose_upload_poll_helpers()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        Assert.Contains("__cgwAdventurePollAttachmentReady", bridge);
        Assert.Contains("__cgwAdventurePollUploadFailure", bridge);
        Assert.Contains("attachmentsPreStaged", bridge);
        Assert.Contains("bridge_attach_prestaged", bridge);
        Assert.Contains("__cgwNativeComposerHasAttachments", bridge);
        Assert.Contains("__cgwNativeComposerReadText", bridge);
    }

    [Fact]
    public void Adventure_turn_service_routes_native_prestaged_attachments()
    {
        var turnServicePath = FindRepoFile("ChatGPTWrapper", "Adventure", "Services", "AdventureTurnService.cs");
        var text = File.ReadAllText(turnServicePath);
        Assert.Contains("if (attachmentsPreStaged || domAttachments is { Count: > 0 })", text);
        Assert.Contains("attachmentsPreStaged: attachmentsPreStaged", text);
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(new[] { dir }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relativeParts));
    }

    [Fact]
    public void Compose_script_persists_dom_fallback_stash_on_globalThis()
    {
        var text = File.ReadAllText(ComposeJsPath);
        Assert.Contains("__cgwDomFallbackAttachmentStash", text);
        Assert.Contains("compose_dom_stash", text);
        Assert.Contains("__cgwPlayComposePeekDomFallbackAttachments", text);
    }

    [Fact]
    public void Bridge_submitPrompt_does_not_request_wrapper_focus_early()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        var submitSection = bridge[
            (bridge.IndexOf("function submitPrompt", StringComparison.Ordinal) + 1)..];
        var end = submitSection.IndexOf("function regenerateLast", StringComparison.Ordinal);
        if (end > 0)
            submitSection = submitSection[..end];

        Assert.DoesNotContain("__cgwPlayComposeRequestFocus", submitSection);
    }

    [Fact]
    public void Bridge_submit_uses_automation_guard_and_verified_submit()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        var composerDom = File.ReadAllText(Path.Combine(WrapperAssetsDir, "cgw-composer-dom.js"));
        Assert.Contains("__cgwBridgeAutomationActive", bridge);
        Assert.Contains("tryDispatchSubmit", bridge);
        Assert.Contains("waitForSubmitVerification", bridge);
        Assert.Contains("bridge_restore_anchor", bridge);
        Assert.Contains("temporarilyRestoreNativeToAnchor", composerDom);
        Assert.Contains("collectSubmitSearchRoots", composerDom);
        Assert.Contains("syncComposeThemeFromNative", composerDom);
    }

    [Fact]
    public void Bridge_fillComposer_skips_native_focus_in_wrapper_mode()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        Assert.Contains("if (!wrapperActive)", bridge);
        Assert.Contains("if (!wrapperActive) el.focus();", bridge);
    }

    [Fact]
    public void Bridge_exports_stable_assistant_capture_actions()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        Assert.Contains("case \"captureStableAssistant\":", bridge);
        Assert.Contains("waitForStableAssistantText", bridge);
        Assert.Contains("case \"getAssistantTurnCount\":", bridge);
        Assert.Contains("assistantTurnCount", bridge);
    }

    [Fact]
    public void Bridge_getUserTurnCount_counts_play_turns_not_injection_packets()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        Assert.Contains("function countPlayUserTurns", bridge);
        Assert.Contains("count: countPlayUserTurns()", bridge);
        Assert.Contains("if (!playerText && isInjectedContextUserMessage(text)) continue;", bridge);
    }

    [Fact]
    public void Bridge_sendPrompt_wires_submit_verification_and_stable_capture()
    {
        var bridge = File.ReadAllText(BridgeJsPath);
        var sendSection = bridge[
            (bridge.IndexOf("function sendPrompt", StringComparison.Ordinal) + 1)..];
        var end = sendSection.IndexOf("function submitPrompt", StringComparison.Ordinal);
        if (end > 0)
            sendSection = sendSection[..end];

        Assert.Contains("waitForSubmitVerification", bridge);
        Assert.Contains("waitForStableAssistantText", sendSection);
        Assert.Contains("\"turnComplete\"", sendSection);
    }
}
