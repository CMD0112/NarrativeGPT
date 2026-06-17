namespace ChatGPTWrapper.ApiDiagnostics.Unit;



[Trait("Category", "Unit")]

public sealed class PacketDisplayAssetTests

{

    [Fact]

    public void Packet_display_module_exposes_parse_and_transform()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-packet-display.js");

        Assert.Contains("parsePacket", text);

        Assert.Contains("transformUserBlocks", text);

        Assert.Contains("__cgwStampUserTurnDisplay", text);

        Assert.Contains("MutationObserver", text);

        Assert.Contains("schedulePacketDisplayPass", text);

        Assert.Contains("togglePacketContextUiVisible", text);

        Assert.Contains("data-cgw-show-packet-context", text);

    }



    [Fact]

    public void Packet_display_exposes_pending_fallback_for_cv_off()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-packet-display.js");

        Assert.Contains("PENDING_FALLBACK_MS", text);

        Assert.Contains("releasePendingFallback", text);

    }



    [Fact]

    public void Packet_display_refuses_empty_native_mount_and_restores_source()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-packet-display.js");

        Assert.Contains("if (!playerLine) return false", text);

        Assert.Contains("reconcileOrphanedPacketSources", text);

        Assert.Contains("rewriteNativeHostContent", text);

        Assert.Contains("leaf.textContent", text);

        Assert.Contains("findNativePlayerTextLeaf", text);

        Assert.Contains("sourceBackupByTurnId", text);

        Assert.Contains("ensureSourceBackup", text);

        Assert.Contains("if (mounted > 0) clearPendingAttr()", text);

        Assert.Contains("else releasePendingFallback()", text);

        Assert.Contains("display.blocks.player", text);

    }



    [Fact]

    public void Context_tags_script_delegates_to_packet_display()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-context-tags.js");

        Assert.Contains("__cgwApplyContextTagDisplay", text);

        Assert.DoesNotContain("MutationObserver", text);

    }



    [Fact]

    public void Continuous_format_renders_packetContext_blocks()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-format.js");

        Assert.Contains("packetContext", text);

        Assert.Contains("cgw-continuous-packet-context__sections", text);

        Assert.Contains("appendSectionProse", text);

        Assert.DoesNotContain("cgw-continuous-packet-context__text", text);

    }



    [Fact]

    public void Continuous_view_uses_turn_extract_cache()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("extractTurnBlocks", text);

        Assert.Contains("rawBlocks", text);

        Assert.Contains("patchStreamingProseBlock", text);

        Assert.Contains("updateStreamingStickObserver", text);

    }



    [Fact]

    public void Continuous_transcript_context_menu_toggles_packet_context()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("toggle-packet-context", text);

        Assert.Contains("Show adventure context", text);

    }



    [Fact]

    public void Continuous_view_strips_show_more_controls()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("sanitizeExtractedMessageText", text);

        Assert.Contains("stripExpandCollapseControls", text);

    }



    [Fact]

    public void Continuous_view_stabilizes_chat_open_transition()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("enterConversationTransition", text);

        Assert.Contains("commitConversationOverlay", text);

        Assert.Contains("stabilizeContinuousLayout", text);

        Assert.Contains("data-cgw-cv-pending", text);

        Assert.Contains("syncOverlayGeometry", text);

        Assert.Contains("ensureScrollHostResizeObserver", text);

        Assert.Contains("__cgwContinuousViewNavigate", text);

        Assert.Contains("cgw-cv-transition-shell", text);

        Assert.Contains("waitForTranscriptDomReady", text);

    }



    [Fact]

    public void Continuous_view_pending_css_hides_native_during_load()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.css");

        Assert.Contains("data-cgw-continuous-view=\"1\"][data-cgw-cv-pending=\"1\"]", text);

        Assert.Contains("[data-message-author-role]", text);

        Assert.Contains("#cgw-continuous-view", text);

        Assert.Contains("#cgw-cv-transition-shell", text);

        Assert.Contains("position: absolute", text);
        Assert.Contains("overflow: hidden !important", text);
        Assert.Contains("[data-testid^=\"conversation-turn-\"]", text);
        Assert.Contains("overscroll-behavior: contain", text);
    }



    [Fact]

    public void Packet_display_uses_batch_open_pipeline()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-packet-display.js");

        Assert.Contains("__cgwPacketDisplayBooted", text);

        Assert.Contains("__cgwPacketDisplayNavigate", text);

        Assert.Contains("enterConversationPacketPass", text);

        Assert.Contains("batchApplyAllTurns", text);

        Assert.Contains("processDeltaTurns", text);

        Assert.Contains("turnDisplayCache", text);

        Assert.Contains("turnRegistry", text);

        Assert.Contains("data-cgw-packet-pending", text);

        Assert.Contains("mountPacketDisplay", text);

        Assert.Contains("rewriteNativeHostContent", text);

        Assert.Contains("buildNativePlayerLine", text);

        Assert.DoesNotContain("restoreNativeUserMessage", text);

        Assert.DoesNotContain("rewriteNativeUserMessage", text);

        Assert.DoesNotContain("cgw-native-packet-viewport", text);

    }



    [Fact]

    public void Context_tags_css_scopes_legacy_native_layers()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-context-tags.css");

        Assert.DoesNotContain(
            "main [data-message-author-role=\"user\"]",
            text);

        Assert.Contains(".cgw-native-packet-display", text);

        Assert.Contains("display: none", text);

    }



    [Fact]

    public void Native_packet_display_skips_expandable_context_panels()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-packet-display.js");

        Assert.Contains("buildNativePlayerLine", text);

        Assert.DoesNotContain("cgw-packet-context__details", text);

    }



    [Fact]

    public void Continuous_view_syncs_packet_display_on_toggle()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("__cgwApplyContextTagDisplay", text);

    }



    [Fact]

    public void Continuous_view_toggle_cancels_pending_apply_and_soft_resumes()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("cancelPendingApply", text);

        Assert.Contains("canResumeContinuousViewWithoutTransition", text);

        Assert.Contains("resumeContinuousView", text);

        Assert.Contains("__cgwSetContinuousView(!!globalThis.__cgwContinuousViewEnabled)", text);

    }



    [Fact]

    public void Continuous_view_dedupes_turn_roots_by_wrapper()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("dedupeTurnRootsByWrapper", text);

        Assert.Contains("seenByWrap", text);

        Assert.Contains("segmentOrderKey", text);

        Assert.Contains("teardownAllPacketShells", text);

        Assert.Contains(".cgw-native-packet-display", text);

    }



    [Fact]

    public void Packet_context_visible_by_default_unless_user_hid_it()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-packet-display.js");

        Assert.Contains("stored === \"0\"", text);

        Assert.Contains("var visible = true", text);

    }



    [Fact]

    public void Continuous_view_sizes_overlay_to_visible_viewport()

    {

        var js = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        var css = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.css");

        Assert.Contains("ensureOverlayInScrollHost", js);

        Assert.Contains("assignTurnIdsInDocumentOrder", js);

        Assert.Contains("compareTurnRootsDocumentOrder", js);

        Assert.Contains("computeComposerBottomInset", js);

        Assert.Contains("computeVisibleHeightInViewport", js);

        Assert.Contains("__cgwApplyChromePreferences", WrapperAssetTestHelpers.ReadAsset("chrome-preferences.js"));

        Assert.Contains("cgw-cv-scroll-anchor", css);

        Assert.Contains("flex: 0 0 0", css);

        Assert.Contains("bindScrollHostWheelForward", js);

        Assert.Contains("maxScrollTop", js);

        Assert.Contains("preserveScroll", js);

        Assert.Contains("position: absolute", css);

        Assert.Contains("overflow-y: scroll", css);

        Assert.Contains("touch-action: pan-y", css);

        Assert.Contains("max-width: none", css);

        Assert.DoesNotContain(".cgw-continuous-view {\r\n  max-width", css);

    }



    [Fact]

    public void Continuous_view_stick_to_bottom_respects_user_scroll_away()

    {

        var js = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");

        Assert.Contains("function shouldStickToBottom", js);

        Assert.Contains("userDetachedFromBottom", js);

        Assert.Contains("bindContainerScrollIntent", js);

        Assert.Contains("globalThis.__cgwShouldStickToBottom = shouldStickToBottom", js);

        Assert.DoesNotContain("scrollSurfaceNearBottom(scrollHost, container) || isNativeStreaming()", js);

    }



    [Fact]

    public void Packet_display_sanitizes_player_line()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("cgw-packet-display.js");

        Assert.Contains("sanitizeExtractedMessageText", text);

        Assert.Contains("getPacketSourceText", text);

        Assert.Contains("displayFingerprint", text);

        Assert.Contains("isPacketTurn", text);

        Assert.Contains("computeSourceFingerprint", text);

        Assert.Contains("expandSourcesV2Blocks", text);

        Assert.Contains("sources-always", text);

    }



    [Fact]

    public void Adventure_bridge_stamps_user_display_on_submit()

    {

        var text = WrapperAssetTestHelpers.ReadAsset("adventure-bridge.js");

        Assert.Contains("__cgwStampUserTurnDisplay", text);

        Assert.Contains("displayUserLine", text);

    }

}



internal static class WrapperAssetTestHelpers

{

    public static string ReadAsset(string fileName)

    {

        var path = Path.Combine(AppContext.BaseDirectory, "wrapper-assets", fileName);

        Assert.True(File.Exists(path), $"Missing wrapper asset: {path}");

        return File.ReadAllText(path);

    }

}


