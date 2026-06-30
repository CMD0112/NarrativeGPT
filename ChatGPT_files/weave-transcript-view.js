/**
 * ChatGPT Wrapper — weave transcript view (narrator body + embedded player lines).
 */
(function () {
  function refreshWeaveCss() {
    var css = globalThis.__cgwWeaveViewCss;
    if (!css) return;
    var el = document.getElementById("cgw-weave-view-css");
    if (el) el.textContent = css;
  }

  refreshWeaveCss();

  function kernel() {
    return globalThis.__cgwTranscriptKernel || {};
  }

  function blocksToPlainText(blocks) {
    if (!blocks || !blocks.length) return "";
    var rf = globalThis.__cgwContinuousRichFormat;
    if (rf && typeof rf.blockPlainText === "function") {
      return blocks
        .map(function (b) {
          return rf.blockPlainText(b);
        })
        .join("\n")
        .trim();
    }
    return blocks
      .map(function (b) {
        if (b.text) return b.text;
        if (b.html) {
          var tmp = document.createElement("div");
          tmp.innerHTML = b.html;
          return tmp.textContent || "";
        }
        return "";
      })
      .join("\n")
      .trim();
  }

  function isPlayerFacingBlock(block) {
    return block && block.kind !== "packetContext";
  }

  function buildPlayerBlocksFromLine(line) {
    var pd = globalThis.__cgwPacketDisplay;
    if (pd && typeof pd.buildPlayerBlocks === "function") {
      return pd.buildPlayerBlocks(line) || [];
    }
    var trimmed = (line || "").trim();
    if (!trimmed) return [];
    return [{ kind: "prose", html: "<p>" + trimmed + "</p>" }];
  }

  function resolveWeaveEmbedBlocks(seg) {
    var blocks = (seg.blocks || []).filter(isPlayerFacingBlock);
    if (blocks.length) return blocks;

    var turnId = seg.turnId;
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = turnId != null ? registry[turnId] : null;

    if (entry && entry.playerSnippet) {
      blocks = buildPlayerBlocksFromLine(entry.playerSnippet);
      if (blocks.length) return blocks;
    }

    if (entry && entry.wrap) {
      var stamped = entry.wrap.getAttribute("data-cgw-user-line");
      if (stamped) {
        blocks = buildPlayerBlocksFromLine(stamped);
        if (blocks.length) return blocks;
      }
    }

    return blocks;
  }

  function packetContextBlocks(blocks) {
    return (blocks || []).filter(function (block) {
      return block && block.kind === "packetContext";
    });
  }

  function composeWeaveEmbedBlocks(seg) {
    var playerBlocks = resolveWeaveEmbedBlocks(seg);
    return playerBlocks.concat(packetContextBlocks(seg.blocks));
  }

  function readWeaveEmbedPreset() {
    var fmt = globalThis.__cgwContinuousViewFormat || {};
    var raw =
      fmt.weaveEmbedKind != null
        ? fmt.weaveEmbedKind
        : fmt.WeaveEmbedKind != null
          ? fmt.WeaveEmbedKind
          : "blockquote";
    return String(raw).toLowerCase();
  }

  function resolveEmbedKind(seg) {
    var preset = readWeaveEmbedPreset();
    if (preset !== "auto") {
      if (preset === "aside" || preset === "pull-quote" || preset === "run-in") {
        return preset;
      }
      return "blockquote";
    }

    var text = blocksToPlainText(seg.blocks || []);
    if (!text) return "blockquote";

    if (
      /^["\u201c\u201d']/.test(text) &&
      /["\u201c\u201d']$/.test(text)
    ) {
      return "blockquote";
    }

    if (/^\s*>\s/.test(text)) return "aside";
    if (/^\s*\*[^*]+\*\s*$/.test(text) || /^\s*_[^_]+_\s*$/.test(text)) {
      return "aside";
    }

    var oneLine = text.indexOf("\n") < 0;
    var shortLine = text.length <= 72;
    var noTerminalPeriod = !/[.!?]["'\u201d]?\s*$/.test(text);
    if (oneLine && shortLine && noTerminalPeriod) {
      return text.length <= 36 ? "run-in" : "pull-quote";
    }

    return "blockquote";
  }

  function buildFlow(segments, streamingTurnId) {
    var flow = [];
    var bodyRun = null;

    segments.forEach(function (seg) {
      if (seg.role === "assistant") {
        var streamingThis =
          streamingTurnId != null &&
          String(seg.turnId) === String(streamingTurnId);
        if (
          bodyRun &&
          streamingThis &&
          bodyRun.turnIds.length &&
          String(bodyRun.turnIds[bodyRun.turnIds.length - 1]) !==
            String(seg.turnId)
        ) {
          bodyRun = null;
        }
        if (!bodyRun) {
          bodyRun = {
            kind: "bodyRun",
            turnIds: [],
            blocks: [],
            streaming: false,
          };
          flow.push(bodyRun);
        }
        bodyRun.turnIds.push(seg.turnId);
        bodyRun.blocks = bodyRun.blocks.concat(seg.blocks);
        if (streamingThis) bodyRun.streaming = true;
      } else if (seg.role === "user") {
        bodyRun = null;
        var embedBlocks = composeWeaveEmbedBlocks(seg);
        if (!embedBlocks.length) return;
        var playerBlocks = resolveWeaveEmbedBlocks(seg);
        flow.push({
          kind: "embed",
          turnId: seg.turnId,
          role: "user",
          blocks: embedBlocks,
          embedKind: resolveEmbedKind({ blocks: playerBlocks }),
        });
      }
    });

    return flow;
  }

  function flowFingerprint(flow) {
    var k = kernel();
    return flow
      .map(function (item) {
        if (item.kind === "bodyRun") {
          return (
            "body:" +
            item.turnIds.join(",") +
            "\x01" +
            (k.blocksFingerprint ? k.blocksFingerprint(item.blocks) : "")
          );
        }
        return (
          "embed:" +
          item.turnId +
          "\x01" +
          item.embedKind +
          "\x01" +
          (k.blocksFingerprint ? k.blocksFingerprint(item.blocks) : "")
        );
      })
      .join("\x03");
  }

  function createWeaveBodyElement(item) {
    var k = kernel();
    var body = document.createElement("div");
    body.className =
      "cgw-weave-body " + (k.INTERACTIVE_SEGMENT_CLASS || "cgw-continuous-segment--interactive");
    var primaryTurnId =
      item.turnIds && item.turnIds.length
        ? item.turnIds[item.turnIds.length - 1]
        : "";
    body.setAttribute("data-cgw-turn-id", String(primaryTurnId));
    body.setAttribute("data-cgw-turn-ids", (item.turnIds || []).join(","));
    body.setAttribute("data-cgw-turn-role", "assistant");
    if (item.streaming) body.setAttribute("data-cgw-streaming", "1");
    return body;
  }

  function applyWeaveEmbedClasses(embed, item) {
    var k = kernel();
    embed.className =
      "cgw-weave-embed cgw-weave-embed--" +
      item.embedKind +
      " " +
      (k.INTERACTIVE_SEGMENT_CLASS || "cgw-continuous-segment--interactive");
    embed.classList.remove(
      "cgw-has-packet-context",
      "cgw-weave-embed--has-hidden-packet"
    );
    if (
      item.role === "user" &&
      k.segmentHasPacketContextBlock &&
      k.segmentHasPacketContextBlock(item.blocks)
    ) {
      embed.classList.add("cgw-has-packet-context");
      if (k.isPacketContextUiVisible && !k.isPacketContextUiVisible()) {
        embed.classList.add("cgw-weave-embed--has-hidden-packet");
      }
    }
  }

  function createWeaveEmbedElement(item) {
    var embed = document.createElement("div");
    embed.setAttribute("data-cgw-turn-id", String(item.turnId));
    embed.setAttribute("data-cgw-turn-role", "user");
    embed.setAttribute("data-cgw-voice", "player");
    applyWeaveEmbedClasses(embed, item);
    return embed;
  }

  function fillWeaveBlocks(parent, blocks, turnId) {
    var k = kernel();
    while (parent.firstChild) parent.removeChild(parent.firstChild);
    blocks.forEach(function (block) {
      if (k.appendRichBlock) k.appendRichBlock(parent, block);
    });
    if (k.syncPacketContextExpandState) {
      k.syncPacketContextExpandState(parent, turnId);
    }
  }

  function patchWeaveStreamingBody(bodyEl, block) {
    var k = kernel();
    if (k.patchStreamingProseBlock) {
      return k.patchStreamingProseBlock(bodyEl, block);
    }
    return false;
  }

  function renderWeaveFlow(scrollHost, container, flow, scrollTop, stickToBottom) {
    var k = kernel();
    while (container.firstChild) container.removeChild(container.firstChild);
    if (k.ensureScrollAnchor) k.ensureScrollAnchor(container);

    flow.forEach(function (item) {
      if (item.kind === "bodyRun") {
        var body = createWeaveBodyElement(item);
        fillWeaveBlocks(body, item.blocks, item.turnIds[item.turnIds.length - 1]);
        container.appendChild(body);
      } else if (item.kind === "embed") {
        var embed = createWeaveEmbedElement(item);
        fillWeaveBlocks(embed, item.blocks, item.turnId);
        container.appendChild(embed);
      }
    });

    if (k.applyScrollSurface) {
      k.applyScrollSurface(scrollHost, container, scrollTop, stickToBottom);
    }
  }

  function syncWeaveFlow(scrollHost, container, flow, scrollTop, stickToBottom) {
    var k = kernel();
    var prevFps = globalThis.__cgwWeaveFlowFingerprints || {};
    var nextFps = {};
    var changedTurnIds = [];
    var streamingPatch = k.isNativeStreaming ? k.isNativeStreaming() : false;

    var existing = Array.prototype.slice.call(
      container.querySelectorAll(".cgw-weave-body, .cgw-weave-embed")
    );

    function flowOrderKey(item) {
      if (item.kind === "bodyRun") {
        return "body:" + item.turnIds.join(",");
      }
      return "embed:" + item.turnId;
    }

    function existingOrderKey(el) {
      if (el.classList.contains("cgw-weave-body")) {
        return (
          "body:" +
          (el.getAttribute("data-cgw-turn-ids") ||
            el.getAttribute("data-cgw-turn-id") ||
            "")
        );
      }
      return "embed:" + (el.getAttribute("data-cgw-turn-id") || "");
    }

    var canSync =
      existing.length > 0 &&
      flow.length >= existing.length &&
      existing.every(function (el, i) {
        return i < flow.length && existingOrderKey(el) === flowOrderKey(flow[i]);
      });

    if (!canSync) {
      renderWeaveFlow(scrollHost, container, flow, scrollTop, stickToBottom);
      flow.forEach(function (item) {
        nextFps[flowOrderKey(item)] = flowFingerprint([item]);
        if (item.kind === "embed") changedTurnIds.push(item.turnId);
        else if (item.turnIds) changedTurnIds = changedTurnIds.concat(item.turnIds);
      });
      globalThis.__cgwWeaveFlowFingerprints = nextFps;
      return changedTurnIds;
    }

    flow.forEach(function (item, index) {
      var key = flowOrderKey(item);
      var fp = flowFingerprint([item]);
      nextFps[key] = fp;
      var el = existing[index];
      if (!el) return;
      if (prevFps[key] === fp) return;

      if (item.kind === "bodyRun") {
        var lastBlock = item.blocks[item.blocks.length - 1];
        if (
          item.streaming &&
          lastBlock &&
          lastBlock.kind === "prose" &&
          patchWeaveStreamingBody(el, lastBlock)
        ) {
          el.setAttribute("data-cgw-streaming", "1");
        } else {
          el.removeAttribute("data-cgw-streaming");
          fillWeaveBlocks(el, item.blocks, item.turnIds[item.turnIds.length - 1]);
        }
        changedTurnIds = changedTurnIds.concat(item.turnIds);
      } else {
        applyWeaveEmbedClasses(el, item);
        fillWeaveBlocks(el, item.blocks, item.turnId);
        changedTurnIds.push(item.turnId);
      }
    });

    while (existing.length > flow.length) {
      existing.pop().remove();
    }

    if (flow.length > existing.length) {
      for (var i = existing.length; i < flow.length; i++) {
        var itemAdd = flow[i];
        if (itemAdd.kind === "bodyRun") {
          var bodyAdd = createWeaveBodyElement(itemAdd);
          fillWeaveBlocks(
            bodyAdd,
            itemAdd.blocks,
            itemAdd.turnIds[itemAdd.turnIds.length - 1]
          );
          container.appendChild(bodyAdd);
        } else {
          var embedAdd = createWeaveEmbedElement(itemAdd);
          fillWeaveBlocks(embedAdd, itemAdd.blocks, itemAdd.turnId);
          container.appendChild(embedAdd);
        }
        if (itemAdd.kind === "embed") changedTurnIds.push(itemAdd.turnId);
        else changedTurnIds = changedTurnIds.concat(itemAdd.turnIds);
      }
    }

    globalThis.__cgwWeaveFlowFingerprints = nextFps;
    if (k.applyScrollSurface) {
      k.applyScrollSurface(scrollHost, container, scrollTop, stickToBottom);
    }
    return changedTurnIds;
  }

  function containerHasWeaveMarkup(container) {
    return (
      !!container &&
      !!container.querySelector(".cgw-weave-body, .cgw-weave-embed")
    );
  }

  function applyWeaveViewCore() {
    var k = kernel();
    if (!k.collectSegmentsFromTurns || !k.isOverlayTranscriptMode) return;
    if (k.getTranscriptViewMode && k.getTranscriptViewMode() !== "weave") return;

    var collected = k.collectSegmentsFromTurns();
    if (!collected) return;
    if (collected.notReady) {
      if (collected.reason === "url" && k.hideContinuousOverlay) {
        k.hideContinuousOverlay();
        if (k.bindTranscriptObserver) {
          k.bindTranscriptObserver(document.querySelector("main") || document.body);
        }
        if (k.scheduleApplyRetry) k.scheduleApplyRetry(400);
      } else if (collected.reason === "dom" && k.waitForTranscriptDomReady) {
        k.waitForTranscriptDomReady(function (ready) {
          if (!k.isOverlayTranscriptMode()) return;
          if (ready && globalThis.__cgwContinuousViewSchedule) {
            globalThis.__cgwContinuousViewSchedule({ immediate: true });
          } else if (k.handleApplyNotReady) {
            k.handleApplyNotReady(document.querySelector("main") || document.body);
          }
        });
      } else if (k.handleApplyNotReady) {
        k.handleApplyNotReady(document.querySelector("main") || document.body);
      }
      return;
    }

    if (k.ensureStyles) k.ensureStyles();
    ensureWeaveStyles();

    var segments = collected.segments;
    var registry = collected.registry;
    var hiddenWraps = collected.hiddenWraps;
    var scrollHost = collected.scrollHost;
    var streamingTurnId = collected.streamingTurnId;

    if (k.bindTranscriptObserver) k.bindTranscriptObserver(scrollHost);

    var flow = buildFlow(segments, streamingTurnId);
    var fingerprint = flowFingerprint(flow);
    var containerId = k.CONTAINER_ID || "cgw-continuous-view";
    var container = document.getElementById(containerId);
    var prevFingerprint = globalThis.__cgwWeaveViewFingerprint;
    var unchanged =
      containerHasWeaveMarkup(container) && fingerprint === prevFingerprint;

    if (
      unchanged &&
      k.isContinuousViewStableActive &&
      k.isContinuousViewStableActive() &&
      container.parentElement === scrollHost &&
      k.isNativeStreaming &&
      !k.isNativeStreaming()
    ) {
      if (k.syncOverlayGeometry) {
        k.syncOverlayGeometry(scrollHost, container, { preserveScroll: true });
      }
      if (k.updateStreamingStickObserver) {
        k.updateStreamingStickObserver(scrollHost, container, false);
      }
      return;
    }

    var needsAtomicSwap =
      k.isContinuousViewStableActive &&
      !k.isContinuousViewStableActive() &&
      document.documentElement.hasAttribute("data-cgw-cv-pending");

    if (needsAtomicSwap && k.setTransitionAttributes) {
      k.setTransitionAttributes();
    }

    if (k.markScrollHost) k.markScrollHost(scrollHost);
    if (k.bindScrollHostScrollLock) k.bindScrollHostScrollLock(scrollHost);
    if (k.ensureTransitionShell) k.ensureTransitionShell(scrollHost);

    var reparented = false;
    if (!container) {
      container = document.createElement("div");
      container.id = containerId;
      container.className = "cgw-continuous-view cgw-weave-view";
      scrollHost.appendChild(container);
      reparented = true;
    } else {
      container.classList.add("cgw-weave-view");
      if (k.ensureOverlayInScrollHost && k.ensureOverlayInScrollHost(scrollHost, container)) {
        reparented = true;
      }
    }

    if (needsAtomicSwap) {
      container.style.visibility = "hidden";
      container.setAttribute("aria-hidden", "true");
    } else {
      container.style.visibility = "";
      container.setAttribute("aria-hidden", "false");
    }

    if (k.ensureComposerClearanceWatcher) k.ensureComposerClearanceWatcher();
    if (k.ensureScrollHostResizeObserver) {
      k.ensureScrollHostResizeObserver(scrollHost, container);
    }
    if (k.ensureScrollHostWheelBinding) {
      k.ensureScrollHostWheelBinding(scrollHost, container);
    }
    if (k.bindContainerScrollClamp) k.bindContainerScrollClamp(container);
    if (k.bindContainerScrollIntent) k.bindContainerScrollIntent(container);

    if (k.trimTurnExtractCache && k.turnExtractCacheKey) {
      k.trimTurnExtractCache(
        segments.map(function (s) {
          return k.turnExtractCacheKey(s.turnId, s.role);
        })
      );
    }

    var stickToBottom =
      k.shouldStickToBottom && k.shouldStickToBottom(scrollHost, container);
    var savedScrollTop =
      k.readScrollTop && !stickToBottom
        ? k.readScrollTop(scrollHost, container)
        : null;

    var changedTurnIds = syncWeaveFlow(
      scrollHost,
      container,
      flow,
      stickToBottom ? null : savedScrollTop,
      !!stickToBottom
    );
    globalThis.__cgwWeaveViewFingerprint = fingerprint;

    if (k.updateStreamingStickObserver) {
      k.updateStreamingStickObserver(
        scrollHost,
        container,
        k.isNativeStreaming ? k.isNativeStreaming() : false
      );
    }
    if (k.noteStreamingLifecycle) k.noteStreamingLifecycle(container);

    if (needsAtomicSwap && k.commitConversationOverlay) {
      k.commitConversationOverlay(
        container,
        segments,
        registry,
        hiddenWraps,
        scrollHost
      );
      requestAnimationFrame(function () {
        if (k.finalizeContinuousViewFormatting) {
          k.finalizeContinuousViewFormatting(container, changedTurnIds);
        }
      });
      if (k.stabilizeContinuousLayout) {
        k.stabilizeContinuousLayout(scrollHost, container, true);
      }
    } else {
      if (k.finalizeContinuousViewFormatting) {
        k.finalizeContinuousViewFormatting(container, changedTurnIds);
      }
      if (k.applyTurnSuppressions) {
        k.applyTurnSuppressions(segments, registry, hiddenWraps);
      }
      document.documentElement.setAttribute("data-cgw-continuous-view", "1");
      document.documentElement.removeAttribute("data-cgw-cv-pending");
      if (k.disconnectScrollHostScrollLock) k.disconnectScrollHostScrollLock();
      if (k.syncOverlayGeometry) {
        k.syncOverlayGeometry(scrollHost, container, { preserveScroll: true });
      }
      if (reparented && k.stabilizeContinuousLayout) {
        k.stabilizeContinuousLayout(scrollHost, container, true);
      }
    }

    if (k.bindContextMenuOnContainer) k.bindContextMenuOnContainer(container);
    if (k.ensureContextMenu) k.ensureContextMenu();
    if (k.ensureSurrogateEditPanel) k.ensureSurrogateEditPanel();
  }

  function ensureWeaveStyles() {
    var css = globalThis.__cgwWeaveViewCss;
    if (!css) return;
    var id = "cgw-weave-view-css";
    var el = document.getElementById(id);
    if (!el) {
      el = document.createElement("style");
      el.id = id;
      document.head.appendChild(el);
    }
    el.textContent = css;
  }

  globalThis.__cgwApplyWeaveView = applyWeaveViewCore;
  globalThis.__cgwBuildWeaveFlow = buildFlow;
  globalThis.__cgwRenderWeaveFlow = renderWeaveFlow;
  globalThis.__cgwSyncWeaveFlow = syncWeaveFlow;
  globalThis.__cgwResolveWeaveEmbedKind = resolveEmbedKind;
  globalThis.__cgwResolveWeaveEmbedBlocks = resolveWeaveEmbedBlocks;

  if (typeof globalThis.__cgwRegisterTranscriptRenderer === "function") {
    globalThis.__cgwRegisterTranscriptRenderer("weave", {
      apply: applyWeaveViewCore,
    });
  }
})();
