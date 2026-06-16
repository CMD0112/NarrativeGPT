/**
 * ChatGPT Wrapper — collapses turn bubbles into readable plain-text segments.
 */
(function () {
  function refreshInjectedCss() {
    var css = globalThis.__cgwContinuousViewCss;
    if (!css) return;
    var el = document.getElementById("cgw-continuous-view-css");
    if (el) el.textContent = css;
  }

  refreshInjectedCss();

  if (globalThis.__cgwContinuousViewBooted) {
    if (
      globalThis.__cgwContinuousViewEnabled &&
      typeof globalThis.__cgwContinuousViewSchedule === "function"
    ) {
      globalThis.__cgwContinuousViewSchedule();
    }
    return;
  }
  globalThis.__cgwContinuousViewBooted = true;

  if (globalThis.__cgwContinuousViewEnabled === undefined) {
    globalThis.__cgwContinuousViewEnabled = false;
  }

  if (globalThis.__cgwProseEnhancementsEnabled === undefined) {
    globalThis.__cgwProseEnhancementsEnabled = false;
  }

  if (globalThis.__cgwHideAssistantEditArtifacts === undefined) {
    globalThis.__cgwHideAssistantEditArtifacts = false;
  }

  var REVISION_PROMPT_PREFIX =
    "Please replace your previous response with the following text exactly:";
  var REVISION_HIDE_STORAGE_PREFIX = "cgw-revision-hide:";
  var STICK_TO_BOTTOM_THRESHOLD_PX = 48;

  var CONTAINER_ID = "cgw-continuous-view";
  var STYLE_ID = "cgw-continuous-view-css";
  var CONTEXT_MENU_ID = "cgw-continuous-context-menu";
  var SURROGATE_EDIT_PANEL_ID = "cgw-surrogate-edit-panel";
  var SURROGATE_EDIT_BACKDROP_ID = "cgw-surrogate-edit-backdrop";
  var SUPPRESS_CLASS = "cgw-turn-suppressed";
  var SCROLL_HOST_CLASS = "cgw-transcript-scroll-host";
  var PEEK_TARGET_CLASS = "cgw-continuous-peek-target";
  var INTERACTIVE_SEGMENT_CLASS = "cgw-continuous-segment--interactive";
  var DEBOUNCE_MS = 350;
  var STREAM_DEBOUNCE_MS = 60;
  var PEEK_TIMEOUT_MS = 60000;
  var NATIVE_EDIT_TIMEOUT_MS = 2000;

  var contextMenuState = { segment: null, turnId: null, role: null, open: false };
  var surrogateEditState = {
    open: false,
    turnId: null,
    segment: null,
    initialText: "",
    submitting: false,
  };
  var peekState = {
    turnId: null,
    wrap: null,
    observer: null,
    docObserver: null,
    timeoutId: null,
    clickHandler: null,
  };
  var contextMenuListenersBound = false;
  var nextTurnId = 0;
  var mutationObsTarget = null;
  var transcriptObserver = null;
  var applyRetryTimer = null;
  var navWatcherBound = false;
  var composerClearanceBound = false;
  var composerResizeObserver = null;
  var loadedConversationKey = null;

  function isConversationUrl(href) {
    try {
      var u = new URL(href || location.href);
      if (u.protocol !== "https:") return false;
      var host = (u.hostname || "").toLowerCase();
      if (host !== "chatgpt.com" && host !== "www.chatgpt.com") return false;
      var path = (u.pathname || "").replace(/\\/g, "/");
      if (path.indexOf("/c/") >= 0 || /\/c$/i.test(path)) return true;
      var frag = u.hash || "";
      if (frag.charAt(0) === "#") frag = frag.slice(1);
      if (frag && frag.charAt(0) !== "/") frag = "/" + frag;
      return frag.indexOf("/c/") >= 0 || /\/c$/i.test(frag);
    } catch (_e) {
      return false;
    }
  }

  function ensureStyles() {
    if (!globalThis.__cgwContinuousViewEnabled) return;
    refreshInjectedCss();
    if (document.getElementById(STYLE_ID)) return;
    var css = globalThis.__cgwContinuousViewCss;
    if (!css) return;
    var el = document.createElement("style");
    el.id = STYLE_ID;
    el.setAttribute("data-source", "chatgpt-wrapper");
    el.textContent = css;
    document.documentElement.appendChild(el);
  }

  function removeStyles() {
    var el = document.getElementById(STYLE_ID);
    if (el) el.remove();
  }

  function findLeafTurnGroups() {
    var all = Array.prototype.slice.call(
      document.querySelectorAll('[class*="group/turn-messages"]')
    );
    return all.filter(function (el) {
      return !all.some(function (other) {
        return other !== el && el.contains(other);
      });
    });
  }

  function findTurnRoots() {
    var byRole = Array.prototype.slice.call(
      document.querySelectorAll(
        '[data-message-author-role="user"], [data-message-author-role="assistant"]'
      )
    );
    if (byRole.length) return byRole;

    var byTestId = Array.prototype.slice.call(
      document.querySelectorAll('[data-testid^="conversation-turn-"]')
    );
    if (byTestId.length) return byTestId;

    return findLeafTurnGroups();
  }

  function stripChrome(root) {
    root
      .querySelectorAll(
        'button, [role="button"], nav, aside, svg, ' +
          '[aria-label*="Copy"], [aria-label*="copy"], ' +
          '[aria-label*="message actions"], [aria-label*="Regenerate"], ' +
          '[aria-label*="Read aloud"], [data-testid*="copy"], ' +
          '[class*="message-actions"], [class*="action-buttons"]'
      )
      .forEach(function (el) {
        el.remove();
      });
  }

  function isLeafCandidate(el, all) {
    return !Array.prototype.some.call(all, function (other) {
      return other !== el && el.contains(other);
    });
  }

  function isWrapperNode(el) {
    if (!el || el.nodeType !== 1) return false;
    if (
      el.id === CONTAINER_ID ||
      el.id === CONTEXT_MENU_ID ||
      el.id === STYLE_ID ||
      el.id === SURROGATE_EDIT_PANEL_ID ||
      el.id === SURROGATE_EDIT_BACKDROP_ID
    ) {
      return true;
    }
    if (typeof el.closest === "function") {
      return !!el.closest(
        "#" +
          CONTAINER_ID +
          ", #" +
          CONTEXT_MENU_ID +
          ", #" +
          STYLE_ID +
          ", #" +
          SURROGATE_EDIT_PANEL_ID +
          ", #" +
          SURROGATE_EDIT_BACKDROP_ID
      );
    }
    return false;
  }

  function mutationShouldSchedule(mutations) {
    for (var i = 0; i < mutations.length; i++) {
      var m = mutations[i];
      if (isWrapperNode(m.target)) continue;
      var j;
      if (m.addedNodes) {
        for (j = 0; j < m.addedNodes.length; j++) {
          if (!isWrapperNode(m.addedNodes[j])) return true;
        }
      }
      if (m.removedNodes) {
        for (j = 0; j < m.removedNodes.length; j++) {
          if (!isWrapperNode(m.removedNodes[j])) return true;
        }
      }
      if (!m.addedNodes && !m.removedNodes && !isWrapperNode(m.target)) return true;
    }
    return false;
  }

  function bindTranscriptObserver(scrollHost) {
    var target = scrollHost || document.querySelector("main") || document.body;
    if (!transcriptObserver) {
      transcriptObserver = new MutationObserver(function (mutations) {
        if (!globalThis.__cgwContinuousViewEnabled) return;
        if (contextMenuState.open || peekState.turnId || surrogateEditState.open) return;
        if (!mutationShouldSchedule(mutations)) return;
        schedule();
      });
    }
    if (mutationObsTarget === target) return;
    transcriptObserver.disconnect();
    mutationObsTarget = target;
    transcriptObserver.observe(target, { childList: true, subtree: true });
  }

  function disconnectTranscriptObserver() {
    if (transcriptObserver) transcriptObserver.disconnect();
    mutationObsTarget = null;
  }

  function findUserContentHostIn(root) {
    if (!root) return null;
    var actions = root.querySelector('[aria-label="Your message actions"]');
    if (actions && actions.parentElement && actions.parentElement.previousElementSibling) {
      return actions.parentElement.previousElementSibling;
    }
    var pre = root.querySelector(
      '.whitespace-pre-wrap, [class*="whitespace-pre-wrap"]'
    );
    if (pre) return pre;
    var dirs = root.querySelectorAll('[dir="auto"]');
    if (dirs.length === 1) return dirs[0];
    var group = root.querySelector('[class*="group/turn-messages"]');
    if (group && group.firstElementChild) return group.firstElementChild;
    return null;
  }

  function getOrAssignTurnId(wrap) {
    var existing = wrap.getAttribute("data-cgw-turn-id");
    if (existing) return existing;
    var id = String(nextTurnId++);
    wrap.setAttribute("data-cgw-turn-id", id);
    return id;
  }

  function computeSegmentsFingerprint(segments) {
    return segments
      .map(function (s) {
        return segmentFingerprint(s);
      })
      .join("\x03");
  }

  function blockFingerprint(block) {
    return block.kind + "\x00" + blockSignature(block);
  }

  function phraseHighlightFingerprintSuffix() {
    if (!globalThis.__cgwPhraseHighlightsEnabled) return "\x04off";
    return "\x04" + (globalThis.__cgwPhraseHighlightStyleFp || "on");
  }

  function blocksFingerprint(blocks) {
    return blocks
      .map(function (b) {
        return blockFingerprint(b);
      })
      .join("\x02");
  }

  function segmentFingerprint(segData) {
    return (
      String(segData.turnId) +
      "\x01" +
      segData.role +
      "\x01" +
      blocksFingerprint(segData.blocks) +
      phraseHighlightFingerprintSuffix()
    );
  }

  function isNativeStreaming() {
    var turns = findTurnRoots();
    if (!turns.length) return false;
    var last = turns[turns.length - 1];
    if (getTurnRole(last) !== "assistant") return false;
    var wrap = turnWrapper(last);
    if (!wrap) return false;
    if (
      wrap.querySelector(
        '[class*="streaming"], [data-testid*="streaming"], .result-streaming'
      )
    ) {
      return true;
    }
    if (
      wrap.querySelector(
        'button[data-testid*="stop"], button[aria-label*="Stop"], button[aria-label*="stop"]'
      )
    ) {
      return true;
    }
    return false;
  }

  function applyContainerScroll(container, scrollTop, stickToBottom) {
    if (stickToBottom) {
      container.scrollTop = container.scrollHeight;
    } else if (typeof scrollTop === "number") {
      container.scrollTop = scrollTop;
    }
  }

  function createSegmentElement(segData) {
    var seg = document.createElement("div");
    seg.className =
      "cgw-continuous-segment " +
      INTERACTIVE_SEGMENT_CLASS +
      " cgw-continuous-segment--" +
      (segData.role === "user" ? "user" : "assistant");
    seg.setAttribute("data-cgw-turn-id", String(segData.turnId));
    seg.setAttribute("data-cgw-turn-role", segData.role);
    return seg;
  }

  function decoratePhraseHighlights(root) {
    if (typeof globalThis.__cgwDecoratePhraseHighlightsInElement === "function") {
      globalThis.__cgwDecoratePhraseHighlightsInElement(root);
    }
  }

  function normalizeSegmentTypography(seg) {
    if (!seg) return;
    seg.normalize();
    if (globalThis.__cgwProseEnhancementsEnabled) {
      seg.classList.add("cgw-continuous-segment--formatted");
    } else {
      seg.classList.remove("cgw-continuous-segment--formatted");
    }
  }

  function finalizeContinuousViewFormatting(container) {
    if (!container || !globalThis.__cgwContinuousViewEnabled) return;
    container.querySelectorAll(".cgw-continuous-segment").forEach(function (seg) {
      normalizeSegmentTypography(seg);
      decoratePhraseHighlights(seg);
    });
  }

  function fillSegmentBlocks(segEl, blocks) {
    while (segEl.firstChild) segEl.removeChild(segEl.firstChild);
    blocks.forEach(function (block) {
      appendRichBlock(segEl, block);
    });
  }

  function updateSegmentBlocksIncremental(segEl, blocks, prevBlockFps) {
    if (
      prevBlockFps &&
      blocks.length > 0 &&
      prevBlockFps.length === blocks.length
    ) {
      var prefixSame = true;
      var i;
      for (i = 0; i < blocks.length - 1; i++) {
        if (blockFingerprint(blocks[i]) !== prevBlockFps[i]) {
          prefixSame = false;
          break;
        }
      }
      if (prefixSame) {
        var lastIdx = blocks.length - 1;
        if (blockFingerprint(blocks[lastIdx]) === prevBlockFps[lastIdx]) {
          return false;
        }
        var lastChild = segEl.lastElementChild;
        if (lastChild) lastChild.remove();
        appendRichBlock(segEl, blocks[lastIdx]);
        return true;
      }
    }
    fillSegmentBlocks(segEl, blocks);
    return true;
  }

  function syncSegments(container, segments, scrollTop, stickToBottom) {
    var prevFps = globalThis.__cgwSegmentFingerprints || {};
    var prevBlockFps = globalThis.__cgwSegmentBlockFingerprints || {};
    var nextFps = {};
    var nextBlockFps = {};
    var existing = Array.prototype.slice.call(
      container.querySelectorAll(".cgw-continuous-segment")
    );
    var canSync =
      existing.length > 0 &&
      segments.length >= existing.length &&
      existing.every(function (el, i) {
        return (
          i < segments.length &&
          el.getAttribute("data-cgw-turn-id") === String(segments[i].turnId)
        );
      });

    if (!canSync) {
      renderSegments(container, segments, scrollTop, stickToBottom);
      segments.forEach(function (s) {
        var tid = String(s.turnId);
        nextFps[tid] = segmentFingerprint(s);
        nextBlockFps[tid] = s.blocks.map(blockFingerprint);
      });
      globalThis.__cgwSegmentFingerprints = nextFps;
      globalThis.__cgwSegmentBlockFingerprints = nextBlockFps;
      return;
    }

    var changed = false;
    segments.forEach(function (segData, i) {
      var tid = String(segData.turnId);
      var fp = segmentFingerprint(segData);
      var blockFps = segData.blocks.map(blockFingerprint);
      nextFps[tid] = fp;
      nextBlockFps[tid] = blockFps;

      var el = existing[i];
      if (!el) {
        el = createSegmentElement(segData);
        fillSegmentBlocks(el, segData.blocks);
        container.appendChild(el);
        changed = true;
        return;
      }

      if (prevFps[tid] !== fp) {
        if (
          updateSegmentBlocksIncremental(el, segData.blocks, prevBlockFps[tid])
        ) {
          changed = true;
        }
      }
    });

    while (existing.length > segments.length) {
      container.removeChild(existing.pop());
      changed = true;
    }

    globalThis.__cgwSegmentFingerprints = nextFps;
    globalThis.__cgwSegmentBlockFingerprints = nextBlockFps;

    if (changed || stickToBottom) {
      applyContainerScroll(container, scrollTop, stickToBottom);
    } else if (typeof scrollTop === "number") {
      container.scrollTop = scrollTop;
    }
  }

  function collectContentRoots(root) {
    var all = root.querySelectorAll(
      '.markdown, [class*="markdown"], .whitespace-pre-wrap, [class*="whitespace-pre-wrap"], [dir="auto"]'
    );
    var candidates = Array.prototype.filter.call(all, function (c) {
      return isLeafCandidate(c, all);
    });

    var userHost = findUserContentHostIn(root);
    if (userHost) {
      var already = false;
      Array.prototype.forEach.call(candidates, function (c) {
        if (c === userHost || c.contains(userHost) || userHost.contains(c)) {
          already = true;
        }
      });
      if (!already) candidates.push(userHost);
    }

    if (candidates.length) {
      return candidates.sort(function (a, b) {
        var pos = a.compareDocumentPosition(b);
        if (pos & Node.DOCUMENT_POSITION_FOLLOWING) return -1;
        if (pos & Node.DOCUMENT_POSITION_PRECEDING) return 1;
        return 0;
      });
    }
    return [root];
  }

  function getRichFormat() {
    return globalThis.__cgwContinuousRichFormat || null;
  }

  function blockSignature(block) {
    var rf = getRichFormat();
    if (rf) return rf.blockSignature(block);
    if (block.kind === "table" && block.rows) {
      return block.rows
        .map(function (row) {
          return row.join("\t");
        })
        .join("\n");
    }
    return block.text || block.html || "";
  }

  function blocksFromRoot(root) {
    var rf = getRichFormat();
    if (rf) return rf.blocksFromRoot(root);
    return [];
  }

  function splitParagraphFallback(text) {
    var rf = getRichFormat();
    if (rf) return rf.splitParagraphFallback(text);
    return [];
  }

  function applyPhraseHighlightsToBlocks(blocks) {
    if (typeof globalThis.__cgwApplyPhraseHighlightsToBlocks === "function") {
      return globalThis.__cgwApplyPhraseHighlightsToBlocks(blocks);
    }
    return blocks;
  }

  function formatTurnToBlocks(turnRoot) {
    if (!turnRoot) return [];

    var clone = turnRoot.cloneNode(true);
    stripChrome(clone);
    var roots = collectContentRoots(clone);
    var blocks = [];
    var seen = new Set();

    roots.forEach(function (root) {
      var part = blocksFromRoot(root);
      part.forEach(function (block) {
        var sig = block.kind + "\x00" + blockSignature(block);
        if (seen.has(sig)) return;
        seen.add(sig);
        blocks.push(block);
      });
    });

    if (!blocks.length) {
      var userHost = findUserContentHostIn(clone);
      if (userHost) {
        var hostClone = userHost.cloneNode(true);
        stripChrome(hostClone);
        var hostText = (hostClone.innerText || "").trim();
        if (hostText) return applyPhraseHighlightsToBlocks(splitParagraphFallback(hostText));
      }
      var wrapClone = turnWrapper(turnRoot);
      if (wrapClone) {
        wrapClone = wrapClone.cloneNode(true);
        stripChrome(wrapClone);
        var wrapText = (wrapClone.innerText || "").trim();
        if (wrapText) return applyPhraseHighlightsToBlocks(splitParagraphFallback(wrapText));
      }
    }

    return applyPhraseHighlightsToBlocks(blocks);
  }

  function turnWrapper(turnRoot) {
    var testTurn = turnRoot.closest('[data-testid^="conversation-turn-"]');
    if (testTurn) return testTurn;

    if (turnRoot.matches("[data-message-author-role]")) return turnRoot;

    var article = turnRoot.closest("article");
    if (article) {
      var roles = article.querySelectorAll(
        '[data-message-author-role="user"], [data-message-author-role="assistant"]'
      );
      var groups = article.querySelectorAll('[class*="group/turn-messages"]');
      if (roles.length <= 1 && groups.length <= 1) return article;
    }

    var group = turnRoot.matches('[class*="group/turn-messages"]')
      ? turnRoot
      : turnRoot.querySelector('[class*="group/turn-messages"]');
    if (group && group.parentElement) return group.parentElement;

    return turnRoot;
  }

  function findScrollHost(turns) {
    if (!turns.length) return null;
    var node = turnWrapper(turns[0]);
    var scrollHost = null;
    while (node && node !== document.body) {
      var style = window.getComputedStyle(node);
      var oy = style.overflowY;
      var o = style.overflow;
      if (
        oy === "auto" ||
        oy === "scroll" ||
        o === "auto" ||
        o === "scroll"
      ) {
        scrollHost = node;
      }
      node = node.parentElement;
    }
    if (scrollHost) return scrollHost;

    var main = document.querySelector("main");
    if (main) return main;

    return turnWrapper(turns[0]).parentElement;
  }

  function clearScrollHostMark() {
    document.querySelectorAll("." + SCROLL_HOST_CLASS).forEach(function (el) {
      el.classList.remove(SCROLL_HOST_CLASS);
    });
  }

  function clearSuppressed() {
    document.querySelectorAll("." + SUPPRESS_CLASS).forEach(function (el) {
      el.classList.remove(SUPPRESS_CLASS);
    });
  }

  function getTurnRole(turn) {
    if (!turn) return "assistant";
    var role = turn.getAttribute && turn.getAttribute("data-message-author-role");
    if (role === "user" || role === "assistant") return role;
    var inner = turn.querySelector(
      '[data-message-author-role="user"], [data-message-author-role="assistant"]'
    );
    if (inner) {
      role = inner.getAttribute("data-message-author-role");
      if (role === "user" || role === "assistant") return role;
    }
    return "assistant";
  }

  function findActionBar(wrap, role) {
    var label = role === "user" ? "Your message actions" : "Response actions";
    return wrap.querySelector('[aria-label="' + label + '"]');
  }

  function buttonMatchesAction(btn, kind) {
    if (!btn) return false;
    var aria = (btn.getAttribute("aria-label") || "").toLowerCase();
    var tid = (btn.getAttribute("data-testid") || "").toLowerCase();
    if (kind === "edit") return aria.indexOf("edit") >= 0 || tid.indexOf("edit") >= 0;
    if (kind === "regenerate") {
      return aria.indexOf("regenerate") >= 0 || tid.indexOf("regenerate") >= 0;
    }
    return false;
  }

  function findTurnActionButton(wrap, role, kind) {
    var bar = findActionBar(wrap, role);
    if (!bar) return null;
    var buttons = bar.querySelectorAll("button");
    var i;
    for (i = 0; i < buttons.length; i++) {
      if (buttonMatchesAction(buttons[i], kind)) return buttons[i];
    }
    var copyIdx = -1;
    for (i = 0; i < buttons.length; i++) {
      var a = (buttons[i].getAttribute("aria-label") || "").toLowerCase();
      if (a.indexOf("copy") >= 0) {
        copyIdx = i;
        break;
      }
    }
    if (copyIdx >= 0 && buttons.length > copyIdx + 1) {
      if (kind === "edit" && role === "user") return buttons[copyIdx + 1];
      if (kind === "regenerate" && role === "assistant") return buttons[copyIdx + 1];
    }
    return null;
  }

  function segmentPlainText(segEl) {
    return (segEl.innerText || segEl.textContent || "").trim();
  }

  function hideContextMenu() {
    var menu = document.getElementById(CONTEXT_MENU_ID);
    if (menu) {
      menu.hidden = true;
      menu.setAttribute("aria-hidden", "true");
    }
    contextMenuState.segment = null;
    contextMenuState.turnId = null;
    contextMenuState.role = null;
    contextMenuState.open = false;
  }

  function ensureContextMenu() {
    var menu = document.getElementById(CONTEXT_MENU_ID);
    if (!menu) {
      menu = document.createElement("div");
      menu.id = CONTEXT_MENU_ID;
      menu.setAttribute("role", "menu");
      menu.hidden = true;
      menu.setAttribute("aria-hidden", "true");

      var copyBtn = document.createElement("button");
      copyBtn.type = "button";
      copyBtn.setAttribute("role", "menuitem");
      copyBtn.className = "cgw-continuous-context-menu__item";
      copyBtn.setAttribute("data-action", "copy");
      copyBtn.textContent = "Copy";

      var editBtn = document.createElement("button");
      editBtn.type = "button";
      editBtn.setAttribute("role", "menuitem");
      editBtn.className = "cgw-continuous-context-menu__item";
      editBtn.setAttribute("data-action", "edit");
      editBtn.textContent = "Edit message";

      var editRespBtn = document.createElement("button");
      editRespBtn.type = "button";
      editRespBtn.setAttribute("role", "menuitem");
      editRespBtn.className = "cgw-continuous-context-menu__item";
      editRespBtn.setAttribute("data-action", "edit-response");
      editRespBtn.textContent = "Edit response";

      var regenBtn = document.createElement("button");
      regenBtn.type = "button";
      regenBtn.setAttribute("role", "menuitem");
      regenBtn.className = "cgw-continuous-context-menu__item";
      regenBtn.setAttribute("data-action", "regenerate");
      regenBtn.textContent = "Regenerate response";

      menu.appendChild(copyBtn);
      menu.appendChild(editBtn);
      menu.appendChild(editRespBtn);
      menu.appendChild(regenBtn);
      document.body.appendChild(menu);

      menu.addEventListener("click", function (e) {
        e.stopPropagation();
        var item = e.target.closest("[data-action]");
        if (!item || menu.hidden) return;
        var action = item.getAttribute("data-action");
        var turnId = contextMenuState.turnId;
        var segment = contextMenuState.segment;
        hideContextMenu();
        if (action === "copy" && segment) {
          var text = "";
          var sel = window.getSelection && window.getSelection();
          if (sel && !sel.isCollapsed && sel.toString().trim()) {
            text = sel.toString();
          } else {
            text = segmentPlainText(segment);
          }
          if (text && navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).catch(function () {
              copyViaExecCommand(text);
            });
          } else if (text) {
            copyViaExecCommand(text);
          }
          return;
        }
        if (action === "edit" && turnId) enterPeekMode(turnId, "edit");
        if (action === "edit-response" && turnId && segment) {
          openSurrogateEditPanel(turnId, segment);
        }
        if (action === "regenerate" && turnId) enterPeekMode(turnId, "regenerate");
      });
    }

    if (!contextMenuListenersBound) {
      contextMenuListenersBound = true;
      document.addEventListener("click", function () {
        hideContextMenu();
      });
      document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
          if (surrogateEditState.open) closeSurrogateEditPanel();
          else if (peekState.turnId) exitPeekMode();
          hideContextMenu();
        }
      });
      document.addEventListener(
        "scroll",
        function () {
          hideContextMenu();
        },
        true
      );
    }

    return menu;
  }

  function copyViaExecCommand(text) {
    var ta = document.createElement("textarea");
    ta.value = text;
    ta.setAttribute("readonly", "");
    ta.style.position = "fixed";
    ta.style.left = "-9999px";
    document.body.appendChild(ta);
    ta.select();
    try {
      document.execCommand("copy");
    } catch (_e) {
      /* ignore */
    }
    document.body.removeChild(ta);
  }

  function populateEditSurface(surface, text) {
    if (!surface) return false;
    surface.focus();
    if (surface.tagName === "TEXTAREA") {
      surface.value = text;
      surface.dispatchEvent(new Event("input", { bubbles: true }));
      return true;
    }
    try {
      document.execCommand("selectAll", false, null);
      document.execCommand("insertText", false, text);
      surface.dispatchEvent(
        new InputEvent("input", { bubbles: true, inputType: "insertText" })
      );
      return true;
    } catch (_e) {
      surface.innerText = text;
      surface.dispatchEvent(new InputEvent("input", { bubbles: true }));
      return true;
    }
  }

  function findNativeSendButton(wrap) {
    if (!wrap) return null;
    var turn =
      wrap.closest('[data-testid^="conversation-turn-"]') ||
      wrap.closest("[data-message-author-role]") ||
      wrap;
    var buttons = turn.querySelectorAll('button, [role="button"]');
    var i;
    for (i = 0; i < buttons.length; i++) {
      var btn = buttons[i];
      if (isComposerElement(btn)) continue;
      var text = (btn.textContent || "").trim().toLowerCase();
      var aria = (btn.getAttribute("aria-label") || "").toLowerCase();
      var testid = (btn.getAttribute("data-testid") || "").toLowerCase();
      if (text === "cancel" || aria.indexOf("cancel") >= 0) continue;
      if (text === "send" || aria.indexOf("send") >= 0) return btn;
      if (
        testid.indexOf("confirm") >= 0 ||
        testid.indexOf("save") >= 0
      ) {
        return btn;
      }
    }
    return null;
  }

  function findComposerElement() {
    var el = document.querySelector("#prompt-textarea");
    if (el) {
      if (el.getAttribute("contenteditable") === "true") return el;
      var inner = el.querySelector(
        'textarea, [contenteditable="true"], [role="textbox"]'
      );
      if (inner) return inner;
      return el;
    }
    return (
      document.querySelector('[data-testid="composer-text-input"]') ||
      document.querySelector('div.ProseMirror[contenteditable="true"]') ||
      document.querySelector('[contenteditable="true"].ProseMirror')
    );
  }

  function findComposerRoot() {
    var el = findComposerElement();
    if (el && el.closest) {
      var root =
        el.closest('[data-testid="composer"]') ||
        el.closest("form") ||
        el.closest('[class*="composer"]');
      if (root) return root;
    }
    var anchor = document.querySelector("#prompt-textarea");
    if (anchor) {
      var node = anchor.parentElement;
      while (node && node !== document.body) {
        if (
          node.querySelector(
            'button[data-testid*="submit"], button[data-testid*="publish"]'
          )
        ) {
          return node;
        }
        node = node.parentElement;
      }
    }
    return (
      document.querySelector('[data-testid="composer"]') ||
      document.querySelector('[class*="composer"]') ||
      document
    );
  }

  function fillComposer(text) {
    var el = findComposerElement();
    if (!el) return false;

    try {
      el.scrollIntoView({ block: "nearest", behavior: "auto" });
    } catch (_scroll) {
      /* ignore */
    }
    el.focus();

    if (el.tagName === "TEXTAREA") {
      el.value = text;
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
      return true;
    }

    var sel = window.getSelection && window.getSelection();
    if (sel) {
      try {
        var range = document.createRange();
        range.selectNodeContents(el);
        sel.removeAllRanges();
        sel.addRange(range);
      } catch (_range) {
        /* ignore */
      }
    }

    try {
      document.execCommand("insertText", false, text);
    } catch (_insert) {
      try {
        el.textContent = text;
      } catch (_text) {
        el.innerText = text;
      }
    }

    el.dispatchEvent(
      new InputEvent("input", { bubbles: true, inputType: "insertText" })
    );
    el.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  }

  function findComposerSubmitButton(allowDisabled) {
    var root = findComposerRoot();
    var selectors = [
      'button[data-testid="composer-submit-button"]',
      'button[data-testid="composer-publish-button"]',
      'button[data-testid*="submit"]',
      'button[data-testid*="publish"]',
    ];
    var i;
    var btn;
    for (i = 0; i < selectors.length; i++) {
      btn = root.querySelector(selectors[i]);
      if (btn && (allowDisabled || !btn.disabled)) return btn;
    }
    var buttons = root.querySelectorAll("button");
    for (i = 0; i < buttons.length; i++) {
      btn = buttons[i];
      if (!allowDisabled && btn.disabled) continue;
      var aria = (btn.getAttribute("aria-label") || "").toLowerCase();
      var testid = (btn.getAttribute("data-testid") || "").toLowerCase();
      if (aria.indexOf("send") >= 0 || testid.indexOf("send") >= 0) return btn;
    }
    return null;
  }

  function buildRevisionPrompt(editedText) {
    return REVISION_PROMPT_PREFIX + "\n\n" + editedText;
  }

  function getConversationKey() {
    try {
      var u = new URL(location.href);
      var path = (u.pathname || "").replace(/\\/g, "/");
      var match = path.match(/\/c\/([^/]+)/i);
      if (match) return match[1];
      var frag = u.hash || "";
      if (frag.charAt(0) === "#") frag = frag.slice(1);
      match = frag.match(/\/c\/([^/]+)/i);
      if (match) return match[1];
    } catch (_e) {
      /* ignore */
    }
    return null;
  }

  function loadRevisionHideQueue() {
    var key = getConversationKey();
    if (key !== loadedConversationKey) {
      loadedConversationKey = key;
      globalThis.__cgwRevisionHideQueue = [];
    }
    if (!key) return;
    try {
      var raw = sessionStorage.getItem(REVISION_HIDE_STORAGE_PREFIX + key);
      if (raw) globalThis.__cgwRevisionHideQueue = JSON.parse(raw);
    } catch (_e) {
      globalThis.__cgwRevisionHideQueue = [];
    }
  }

  function persistRevisionHideQueue() {
    var key = getConversationKey();
    if (!key) return;
    try {
      sessionStorage.setItem(
        REVISION_HIDE_STORAGE_PREFIX + key,
        JSON.stringify(globalThis.__cgwRevisionHideQueue || [])
      );
    } catch (_e) {
      /* ignore */
    }
  }

  function recordRevisionHide(assistantTurnId) {
    if (!globalThis.__cgwHideAssistantEditArtifacts || !assistantTurnId) return;
    var queue = globalThis.__cgwRevisionHideQueue || [];
    queue.push({
      assistantTurnId: String(assistantTurnId),
      promptPrefix: REVISION_PROMPT_PREFIX,
    });
    globalThis.__cgwRevisionHideQueue = queue;
    persistRevisionHideQueue();
  }

  function blocksPlainText(blocks) {
    return blocks
      .map(function (b) {
        return blockSignature(b);
      })
      .join("\n")
      .trim();
  }

  function shouldHideTurn(turnId, role, blocks) {
    if (!globalThis.__cgwHideAssistantEditArtifacts) return false;
    var queue = globalThis.__cgwRevisionHideQueue || [];
    if (!queue.length) return false;
    var id = String(turnId);
    var text = blocksPlainText(blocks);
    var i;
    for (i = 0; i < queue.length; i++) {
      var entry = queue[i];
      if (String(entry.assistantTurnId) === id) return true;
      if (
        role === "user" &&
        entry.promptPrefix &&
        text.indexOf(entry.promptPrefix) === 0
      ) {
        return true;
      }
    }
    return false;
  }

  function updateComposerClearance() {
    var root = findComposerRoot();
    var height = 96;
    if (root && root !== document) {
      var rect = root.getBoundingClientRect();
      if (rect.height > 0) height = Math.ceil(rect.height);
    }
    document.documentElement.style.setProperty(
      "--cgw-composer-clearance",
      height + "px"
    );
  }

  function ensureComposerClearanceWatcher() {
    updateComposerClearance();
    if (composerClearanceBound) return;
    composerClearanceBound = true;

    if (typeof ResizeObserver !== "undefined") {
      composerResizeObserver = new ResizeObserver(function () {
        updateComposerClearance();
      });
      var root = findComposerRoot();
      if (root && root !== document) composerResizeObserver.observe(root);
    }

    var composerMutationObserver = new MutationObserver(function () {
      updateComposerClearance();
      if (!composerResizeObserver) return;
      var observed = findComposerRoot();
      if (observed && observed !== document) {
        try {
          composerResizeObserver.observe(observed);
        } catch (_e) {
          /* already observed */
        }
      }
    });
    composerMutationObserver.observe(document.body, {
      childList: true,
      subtree: true,
    });
  }

  function isNearBottom(container) {
    if (!container) return false;
    return (
      container.scrollHeight -
        container.scrollTop -
        container.clientHeight <=
      STICK_TO_BOTTOM_THRESHOLD_PX
    );
  }

  function setSurrogateError(message) {
    var panel = document.getElementById(SURROGATE_EDIT_PANEL_ID);
    if (!panel) return;
    var err = panel.querySelector(".cgw-surrogate-edit-panel__error");
    if (!err) return;
    if (message) {
      err.textContent = message;
      err.hidden = false;
    } else {
      err.textContent = "";
      err.hidden = true;
    }
  }

  function setSurrogateSubmitting(submitting) {
    surrogateEditState.submitting = submitting;
    var panel = document.getElementById(SURROGATE_EDIT_PANEL_ID);
    if (!panel) return;
    var sendBtn = panel.querySelector('[data-action="send"]');
    var cancelBtn = panel.querySelector('[data-action="cancel"]');
    var input = panel.querySelector(".cgw-surrogate-edit-panel__input");
    if (sendBtn) sendBtn.disabled = submitting;
    if (cancelBtn) cancelBtn.disabled = submitting;
    if (input) input.disabled = submitting;
  }

  function ensureSurrogateEditPanel() {
    var panel = document.getElementById(SURROGATE_EDIT_PANEL_ID);
    if (panel) return panel;

    var backdrop = document.createElement("div");
    backdrop.id = SURROGATE_EDIT_BACKDROP_ID;
    backdrop.className = "cgw-surrogate-edit-backdrop";
    backdrop.hidden = true;
    backdrop.setAttribute("aria-hidden", "true");

    panel = document.createElement("div");
    panel.id = SURROGATE_EDIT_PANEL_ID;
    panel.className = "cgw-surrogate-edit-panel";
    panel.hidden = true;
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-modal", "true");
    panel.setAttribute("aria-label", "Edit response");
    panel.setAttribute("aria-hidden", "true");

    var header = document.createElement("div");
    header.className = "cgw-surrogate-edit-panel__header";
    header.textContent = "Edit response";

    var input = document.createElement("textarea");
    input.className = "cgw-surrogate-edit-panel__input";
    input.setAttribute("rows", "12");
    input.setAttribute("spellcheck", "true");

    var error = document.createElement("div");
    error.className = "cgw-surrogate-edit-panel__error";
    error.hidden = true;

    var footer = document.createElement("div");
    footer.className = "cgw-surrogate-edit-panel__footer";

    var cancelBtn = document.createElement("button");
    cancelBtn.type = "button";
    cancelBtn.className = "cgw-surrogate-edit-panel__btn cgw-surrogate-edit-panel__btn--cancel";
    cancelBtn.setAttribute("data-action", "cancel");
    cancelBtn.textContent = "Cancel";

    var sendBtn = document.createElement("button");
    sendBtn.type = "button";
    sendBtn.className = "cgw-surrogate-edit-panel__btn cgw-surrogate-edit-panel__btn--send";
    sendBtn.setAttribute("data-action", "send");
    sendBtn.textContent = "Send";

    footer.appendChild(cancelBtn);
    footer.appendChild(sendBtn);
    panel.appendChild(header);
    panel.appendChild(input);
    panel.appendChild(error);
    panel.appendChild(footer);
    document.body.appendChild(backdrop);
    document.body.appendChild(panel);

    cancelBtn.addEventListener("click", function (e) {
      e.stopPropagation();
      if (surrogateEditState.submitting) return;
      closeSurrogateEditPanel();
    });

    sendBtn.addEventListener("click", function (e) {
      e.stopPropagation();
      if (surrogateEditState.submitting || !surrogateEditState.turnId) return;
      var editedText = (input.value || "").trim();
      if (!editedText) {
        setSurrogateError("Response text cannot be empty.");
        return;
      }
      setSurrogateError("");
      setSurrogateSubmitting(true);
      submitSurrogateAssistantEdit(editedText, surrogateEditState.turnId);
    });

    input.addEventListener("keydown", function (e) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        if (!surrogateEditState.submitting) closeSurrogateEditPanel();
      }
    });

    backdrop.addEventListener("click", function () {
      if (!surrogateEditState.submitting) closeSurrogateEditPanel();
    });

    return panel;
  }

  function openSurrogateEditPanel(turnId, segment, initialTextOverride) {
    hideContextMenu();
    var panel = ensureSurrogateEditPanel();
    var backdrop = document.getElementById(SURROGATE_EDIT_BACKDROP_ID);
    var input = panel.querySelector(".cgw-surrogate-edit-panel__input");
    var initialText =
      initialTextOverride != null
        ? initialTextOverride
        : segment
          ? segmentPlainText(segment)
          : surrogateEditState.initialText || "";

    surrogateEditState.open = true;
    surrogateEditState.turnId = turnId;
    surrogateEditState.segment = segment;
    surrogateEditState.initialText = initialText;
    surrogateEditState.submitting = false;

    if (input) {
      input.value = initialText;
      input.disabled = false;
    }
    setSurrogateError("");
    setSurrogateSubmitting(false);

    document.documentElement.setAttribute("data-cgw-surrogate-edit", "1");
    panel.hidden = false;
    panel.setAttribute("aria-hidden", "false");
    if (backdrop) {
      backdrop.hidden = false;
      backdrop.setAttribute("aria-hidden", "false");
    }

    requestAnimationFrame(function () {
      if (input) {
        input.focus();
        input.setSelectionRange(0, input.value.length);
      }
    });
  }

  function closeSurrogateEditPanel(restoreOverlay) {
    if (!surrogateEditState.open && !document.getElementById(SURROGATE_EDIT_PANEL_ID)) {
      return;
    }

    var panel = document.getElementById(SURROGATE_EDIT_PANEL_ID);
    var backdrop = document.getElementById(SURROGATE_EDIT_BACKDROP_ID);
    if (panel) {
      panel.hidden = true;
      panel.setAttribute("aria-hidden", "true");
    }
    if (backdrop) {
      backdrop.hidden = true;
      backdrop.setAttribute("aria-hidden", "true");
    }

    document.documentElement.removeAttribute("data-cgw-surrogate-edit");
    surrogateEditState.open = false;
    surrogateEditState.turnId = null;
    surrogateEditState.segment = null;
    surrogateEditState.initialText = "";
    surrogateEditState.submitting = false;
    setSurrogateError("");

    scheduleContinuousViewRebuild();
  }

  function scheduleContinuousViewRebuild() {
    if (!globalThis.__cgwContinuousViewEnabled) return;
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    if (typeof schedule === "function") schedule();
  }

  function scanEditSurfaceInWrap(wrap) {
    var inWrap = findEditSurface(wrap);
    if (inWrap) return inWrap;
    var turn =
      wrap.closest('[data-testid^="conversation-turn-"]') ||
      wrap.closest("[data-message-author-role]") ||
      wrap;
    return findEditSurface(turn);
  }

  function tryNativeAssistantEdit(turnId, editedText, onComplete) {
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = registry[turnId];
    if (!entry || !entry.wrap) {
      onComplete(false);
      return;
    }

    var wrap = entry.wrap;
    var editBtn = findTurnActionButton(wrap, "assistant", "edit");
    if (!editBtn) {
      onComplete(false);
      return;
    }

    hideContextMenu();
    if (peekState.turnId) exitPeekMode(false);

    peekState.turnId = turnId;
    peekState.wrap = wrap;

    document.documentElement.setAttribute("data-cgw-continuous-peek", "1");

    var container = document.getElementById(CONTAINER_ID);
    if (container) {
      container.style.visibility = "hidden";
      container.setAttribute("aria-hidden", "true");
    }

    wrap.classList.remove(SUPPRESS_CLASS);
    wrap.classList.add(PEEK_TARGET_CLASS);
    wrap.scrollIntoView({ block: "center", behavior: "auto" });
    editBtn.click();

    var deadline = Date.now() + NATIVE_EDIT_TIMEOUT_MS;

    function attemptPopulate() {
      var surface = scanEditSurfaceInWrap(wrap);
      if (surface && populateEditSurface(surface, editedText)) {
        var sendBtn = findNativeSendButton(wrap);
        if (sendBtn) {
          sendBtn.click();
          startPeekExitWatch(wrap);
          onComplete(true);
          return;
        }
      }
      if (Date.now() < deadline) {
        requestAnimationFrame(attemptPopulate);
        return;
      }
      exitPeekMode(false);
      onComplete(false);
    }

    requestAnimationFrame(function () {
      requestAnimationFrame(attemptPopulate);
    });
  }

  function submitComposerRevision(editedText, assistantTurnId, onComplete) {
    if (typeof onComplete !== "function") {
      if (typeof assistantTurnId === "function") {
        onComplete = assistantTurnId;
        assistantTurnId = null;
      } else {
        onComplete = function () {};
      }
    }

    var prompt = buildRevisionPrompt(editedText);
    if (!fillComposer(prompt)) {
      onComplete(false);
      return;
    }

    var attempts = 0;
    function trySubmit() {
      var submitBtn = findComposerSubmitButton(false);
      if (!submitBtn) submitBtn = findComposerSubmitButton(true);
      if (submitBtn && !submitBtn.disabled) {
        submitBtn.click();
        recordRevisionHide(assistantTurnId);
        delete globalThis.__cgwContinuousViewFingerprint;
        setTimeout(function () {
          if (globalThis.__cgwContinuousViewEnabled) schedule();
        }, 1500);
        setTimeout(function () {
          if (globalThis.__cgwContinuousViewEnabled) schedule();
        }, 3500);
        onComplete(true);
        return;
      }
      attempts++;
      if (attempts === 24) {
        var composerEl = findComposerElement();
        if (composerEl) {
          composerEl.dispatchEvent(
            new KeyboardEvent("keydown", {
              key: "Enter",
              code: "Enter",
              keyCode: 13,
              which: 13,
              bubbles: true,
            })
          );
        }
      }
      if (attempts >= 30) {
        onComplete(false);
        return;
      }
      setTimeout(trySubmit, 80);
    }

    requestAnimationFrame(function () {
      requestAnimationFrame(trySubmit);
    });
  }

  function submitSurrogateAssistantEdit(editedText, turnId) {
    closeSurrogateEditPanel(false);

    tryNativeAssistantEdit(turnId, editedText, function (nativeOk) {
      if (nativeOk) return;

      submitComposerRevision(editedText, turnId, function (composerOk) {
        if (composerOk) return;

        openSurrogateEditPanel(turnId, null, editedText);
        setSurrogateError(
          "Could not submit. Check that the composer is available and try again."
        );
        setSurrogateSubmitting(false);
      });
    });
  }

  function showContextMenu(x, y, segment) {
    var turnId = segment.getAttribute("data-cgw-turn-id");
    var role = segment.getAttribute("data-cgw-turn-role") || "assistant";
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = turnId != null ? registry[turnId] : null;
    var wrap = entry && entry.wrap;

    var menu = ensureContextMenu();
    contextMenuState.segment = segment;
    contextMenuState.turnId = turnId;
    contextMenuState.role = role;

    var editBtn = menu.querySelector('[data-action="edit"]');
    var editRespBtn = menu.querySelector('[data-action="edit-response"]');
    var regenBtn = menu.querySelector('[data-action="regenerate"]');

    if (role === "user") {
      editBtn.hidden = false;
      editBtn.textContent = "Edit message";
      editRespBtn.hidden = true;
      regenBtn.hidden = true;
      var editTarget = wrap ? findTurnActionButton(wrap, "user", "edit") : null;
      editBtn.disabled = !editTarget;
    } else {
      editBtn.hidden = true;
      editRespBtn.hidden = false;
      editRespBtn.disabled = false;
      regenBtn.hidden = false;
      var regenTarget = wrap ? findTurnActionButton(wrap, "assistant", "regenerate") : null;
      regenBtn.disabled = !regenTarget;
    }

    menu.style.left = x + "px";
    menu.style.top = y + "px";
    menu.hidden = false;
    menu.setAttribute("aria-hidden", "false");
    contextMenuState.open = true;

    requestAnimationFrame(function () {
      var rect = menu.getBoundingClientRect();
      var pad = 8;
      var left = x;
      var top = y;
      if (rect.right > window.innerWidth - pad) {
        left = Math.max(pad, window.innerWidth - rect.width - pad);
      }
      if (rect.bottom > window.innerHeight - pad) {
        top = Math.max(pad, window.innerHeight - rect.height - pad);
      }
      menu.style.left = left + "px";
      menu.style.top = top + "px";
    });
  }

  function bindContextMenuOnContainer(container) {
    if (!container || container.getAttribute("data-cgw-context-bound")) return;
    container.setAttribute("data-cgw-context-bound", "1");
    container.addEventListener(
      "contextmenu",
      function (e) {
        if (!globalThis.__cgwContinuousViewEnabled || peekState.turnId || surrogateEditState.open) {
          return;
        }
        var segment = e.target.closest(".cgw-continuous-segment");
        if (!segment || !container.contains(segment)) return;
        e.preventDefault();
        e.stopPropagation();
        hideContextMenu();
        showContextMenu(e.clientX, e.clientY, segment);
      },
      true
    );
  }

  function isComposerElement(el) {
    if (!el || !el.closest) return false;
    return !!el.closest(
      '#prompt-textarea, [data-testid="composer"], [data-testid*="composer"], [class*="composer"]'
    );
  }

  function isEditDismissButton(el, wrap) {
    if (!el || !wrap) return false;
    if (isComposerElement(el)) return false;
    var btn = el.closest ? el.closest("button, [role=\"button\"]") : null;
    if (!btn) return false;

    var turn =
      wrap.closest('[data-testid^="conversation-turn-"]') ||
      wrap.closest("[data-message-author-role]") ||
      wrap;
    if (!turn.contains(btn) && !wrap.contains(btn)) return false;

    var aria = (btn.getAttribute("aria-label") || "").toLowerCase();
    var testid = (btn.getAttribute("data-testid") || "").toLowerCase();
    var text = (btn.textContent || "").trim().toLowerCase();

    if (text === "cancel" || text === "send") return true;
    if (aria.indexOf("cancel") >= 0) return true;
    if (aria.indexOf("send") >= 0) return true;
    if (
      testid.indexOf("cancel") >= 0 ||
      testid.indexOf("confirm") >= 0 ||
      testid.indexOf("save") >= 0
    ) {
      return true;
    }
    return false;
  }

  function findEditSurface(root) {
    if (!root) return null;
    return root.querySelector(
      '[contenteditable="true"], textarea, [role="textbox"], form textarea'
    );
  }

  function clearPeekObserver() {
    if (peekState.observer) {
      peekState.observer.disconnect();
      peekState.observer = null;
    }
    if (peekState.docObserver) {
      peekState.docObserver.disconnect();
      peekState.docObserver = null;
    }
    if (peekState.timeoutId != null) {
      clearTimeout(peekState.timeoutId);
      peekState.timeoutId = null;
    }
    if (peekState.clickHandler) {
      document.removeEventListener("click", peekState.clickHandler, true);
      peekState.clickHandler = null;
    }
  }

  function exitPeekMode(restoreContinuousView) {
    if (!peekState.turnId) return;

    clearPeekObserver();

    var registry = globalThis.__cgwTurnRegistry || {};
    clearSuppressed();
    Object.keys(registry).forEach(function (turnId) {
      var entry = registry[turnId];
      if (entry && entry.wrap) {
        entry.wrap.classList.add(SUPPRESS_CLASS);
        entry.wrap.classList.remove(PEEK_TARGET_CLASS);
      }
    });

    document.documentElement.removeAttribute("data-cgw-continuous-peek");
    peekState.turnId = null;
    peekState.wrap = null;

    var container = document.getElementById(CONTAINER_ID);
    if (container) {
      container.style.visibility = "";
      container.setAttribute("aria-hidden", "false");
    }

    scheduleContinuousViewRebuild();
  }

  function schedulePeekExitAfterDismiss() {
    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        exitPeekMode();
      });
    });
  }

  function startPeekExitWatch(wrap) {
    clearPeekObserver();
    var hadEditor = false;

    function onEditEnded() {
      if (peekState.turnId) exitPeekMode();
    }

    function scanEditor() {
      var inWrap = findEditSurface(wrap);
      if (inWrap) return inWrap;
      var turn =
        wrap.closest('[data-testid^="conversation-turn-"]') ||
        wrap.closest("[data-message-author-role]") ||
        wrap;
      return findEditSurface(turn);
    }

    peekState.observer = new MutationObserver(function () {
      var editor = scanEditor();
      if (editor) hadEditor = true;
      else if (hadEditor) onEditEnded();
    });

    peekState.observer.observe(wrap, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["class", "hidden", "aria-hidden", "contenteditable"],
    });

    var turnRoot =
      wrap.closest('[data-testid^="conversation-turn-"]') ||
      wrap.closest("article") ||
      wrap.parentElement;
    if (turnRoot && turnRoot !== wrap) {
      peekState.docObserver = new MutationObserver(function () {
        var editor = scanEditor();
        if (editor) hadEditor = true;
        else if (hadEditor) onEditEnded();
      });
      peekState.docObserver.observe(turnRoot, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["class", "hidden", "aria-hidden", "contenteditable"],
      });
    }

    peekState.clickHandler = function (e) {
      if (!peekState.turnId || !peekState.wrap) return;
      if (!isEditDismissButton(e.target, peekState.wrap)) return;
      schedulePeekExitAfterDismiss();
    };
    document.addEventListener("click", peekState.clickHandler, true);

    peekState.timeoutId = setTimeout(function () {
      exitPeekMode();
    }, PEEK_TIMEOUT_MS);
  }

  function enterPeekMode(turnId, actionKind) {
    if (surrogateEditState.open) return;
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = registry[turnId];
    if (!entry || !entry.wrap) return;

    hideContextMenu();
    if (peekState.turnId) exitPeekMode(false);

    var wrap = entry.wrap;
    var role = entry.role || getTurnRole(entry.turn);

    peekState.turnId = turnId;
    peekState.wrap = wrap;

    document.documentElement.setAttribute("data-cgw-continuous-peek", "1");

    var container = document.getElementById(CONTAINER_ID);
    if (container) {
      container.style.visibility = "hidden";
      container.setAttribute("aria-hidden", "true");
    }

    wrap.classList.remove(SUPPRESS_CLASS);
    wrap.classList.add(PEEK_TARGET_CLASS);
    wrap.scrollIntoView({ block: "center", behavior: "smooth" });

    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        var btn = findTurnActionButton(wrap, role, actionKind);
        if (btn) {
          btn.click();
          if (actionKind === "edit") startPeekExitWatch(wrap);
          else setTimeout(exitPeekMode, 800);
        } else {
          exitPeekMode();
        }
      });
    });
  }

  function scheduleApplyRetry(delayMs) {
    if (!globalThis.__cgwContinuousViewEnabled) return;
    if (applyRetryTimer != null) clearTimeout(applyRetryTimer);
    applyRetryTimer = setTimeout(function () {
      applyRetryTimer = null;
      schedule();
    }, delayMs || 250);
  }

  function ensureNavigationWatcher() {
    if (navWatcherBound) return;
    navWatcherBound = true;
    window.addEventListener("popstate", function () {
      schedule();
    });
    var origPush = history.pushState;
    var origReplace = history.replaceState;
    history.pushState = function () {
      var result = origPush.apply(history, arguments);
      schedule();
      return result;
    };
    history.replaceState = function () {
      var result = origReplace.apply(history, arguments);
      schedule();
      return result;
    };
  }

  function hideContinuousOverlay() {
    document.documentElement.removeAttribute("data-cgw-continuous-view");
    clearSuppressed();
    var container = document.getElementById(CONTAINER_ID);
    if (container) {
      container.style.visibility = "hidden";
      container.setAttribute("aria-hidden", "true");
    }
  }

  function teardown() {
    if (applyRetryTimer != null) {
      clearTimeout(applyRetryTimer);
      applyRetryTimer = null;
    }
    closeSurrogateEditPanel(false);
    exitPeekMode(false);
    hideContextMenu();
    globalThis.__cgwTurnRegistry = {};
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    disconnectTranscriptObserver();
    document.documentElement.removeAttribute("data-cgw-continuous-view");
    document.documentElement.removeAttribute("data-cgw-continuous-peek");
    document.documentElement.removeAttribute("data-cgw-surrogate-edit");
    var menu = document.getElementById(CONTEXT_MENU_ID);
    if (menu) menu.remove();
    var panel = document.getElementById(SURROGATE_EDIT_PANEL_ID);
    if (panel) panel.remove();
    var backdrop = document.getElementById(SURROGATE_EDIT_BACKDROP_ID);
    if (backdrop) backdrop.remove();
    var existing = document.getElementById(CONTAINER_ID);
    if (existing) existing.remove();
    clearSuppressed();
    document.querySelectorAll("." + PEEK_TARGET_CLASS).forEach(function (el) {
      el.classList.remove(PEEK_TARGET_CLASS);
    });
    clearScrollHostMark();
    removeStyles();
  }

  function appendTableBlock(parent, block) {
    var rf = getRichFormat();
    if (rf) {
      rf.appendTableBlock(parent, block);
      return;
    }
  }

  function appendCodeBlock(parent, block) {
    var rf = getRichFormat();
    if (rf) {
      rf.appendCodeBlock(parent, block);
      return;
    }
  }

  function appendRichBlock(parent, block) {
    var rf = getRichFormat();
    if (rf) {
      rf.appendRichBlock(parent, block);
      return;
    }
    var el = document.createElement("div");
    el.className = "cgw-continuous-block";
    el.textContent = block.text || "";
    parent.appendChild(el);
  }

  function renderSegments(container, segments, scrollTop, stickToBottom) {
    while (container.firstChild) container.removeChild(container.firstChild);
    segments.forEach(function (segData) {
      var blocks = segData.blocks;
      if (!blocks.length) return;
      var seg = createSegmentElement(segData);
      blocks.forEach(function (block) {
        appendRichBlock(seg, block);
      });
      container.appendChild(seg);
    });
    applyContainerScroll(container, scrollTop, stickToBottom);
  }

  function applyContinuousView() {
    if (!globalThis.__cgwContinuousViewEnabled) {
      teardown();
      return;
    }

    ensureNavigationWatcher();
    loadRevisionHideQueue();

    if (!isConversationUrl(location.href)) {
      hideContinuousOverlay();
      bindTranscriptObserver(document.querySelector("main") || document.body);
      scheduleApplyRetry(400);
      return;
    }

    if (peekState.turnId || contextMenuState.open || surrogateEditState.open) return;

    ensureStyles();

    var turns = findTurnRoots();
    if (!turns.length) {
      hideContinuousOverlay();
      bindTranscriptObserver(document.querySelector("main") || document.body);
      scheduleApplyRetry(300);
      return;
    }

    var registry = {};
    var segments = [];
    var hiddenWraps = [];
    var seenTurn = new Set();

    turns.forEach(function (turn) {
      var wrap = turnWrapper(turn);
      if (wrap) getOrAssignTurnId(wrap);
    });

    turns.forEach(function (turn) {
      if (seenTurn.has(turn)) return;
      seenTurn.add(turn);

      var wrap = turnWrapper(turn);
      if (!wrap) return;

      var blocks = formatTurnToBlocks(turn);
      if (!blocks.length) return;

      var turnId = wrap.getAttribute("data-cgw-turn-id") || getOrAssignTurnId(wrap);
      var role = getTurnRole(turn);

      if (shouldHideTurn(turnId, role, blocks)) {
        hiddenWraps.push(wrap);
        return;
      }

      registry[turnId] = { turn: turn, wrap: wrap, role: role };
      segments.push({ turnId: turnId, role: role, blocks: blocks, turn: turn });
    });

    if (!segments.length) {
      hideContinuousOverlay();
      bindTranscriptObserver(document.querySelector("main") || document.body);
      scheduleApplyRetry(300);
      return;
    }

    globalThis.__cgwTurnRegistry = registry;

    var scrollHost = findScrollHost(turns);
    if (!scrollHost) {
      hideContinuousOverlay();
      bindTranscriptObserver(document.querySelector("main") || document.body);
      scheduleApplyRetry(300);
      return;
    }

    bindTranscriptObserver(scrollHost);

    document.documentElement.setAttribute("data-cgw-continuous-view", "1");

    clearSuppressed();
    segments.forEach(function (seg) {
      var entry = registry[seg.turnId];
      if (entry && entry.wrap) entry.wrap.classList.add(SUPPRESS_CLASS);
    });
    hiddenWraps.forEach(function (wrap) {
      wrap.classList.add(SUPPRESS_CLASS);
    });

    clearScrollHostMark();
    scrollHost.classList.add(SCROLL_HOST_CLASS);

    var container = document.getElementById(CONTAINER_ID);
    if (!container) {
      container = document.createElement("div");
      container.id = CONTAINER_ID;
      container.className = "cgw-continuous-view";
      scrollHost.appendChild(container);
    } else if (container.parentElement !== scrollHost) {
      scrollHost.appendChild(container);
    }

    container.style.visibility = "";
    container.setAttribute("aria-hidden", "false");

    ensureComposerClearanceWatcher();

    var fingerprint = computeSegmentsFingerprint(segments);
    var stickToBottom = isNearBottom(container) || isNativeStreaming();
    var prevFingerprint = globalThis.__cgwContinuousViewFingerprint;
    var unchanged =
      container.childElementCount > 0 &&
      fingerprint === prevFingerprint &&
      container.childElementCount === segments.length;

    if (!unchanged) {
      var savedScrollTop = container.scrollTop;
      syncSegments(
        container,
        segments,
        stickToBottom ? null : savedScrollTop,
        stickToBottom
      );
      globalThis.__cgwContinuousViewFingerprint = fingerprint;
    } else if (stickToBottom) {
      container.scrollTop = container.scrollHeight;
    }

    finalizeContinuousViewFormatting(container);

    bindContextMenuOnContainer(container);
    ensureContextMenu();
    ensureSurrogateEditPanel();
  }

  var debounceTimer = null;
  var scheduled = null;

  function schedule() {
    if (!globalThis.__cgwContinuousViewEnabled) {
      teardown();
      return;
    }

    if (contextMenuState.open || peekState.turnId || surrogateEditState.open) return;

    if (debounceTimer != null) clearTimeout(debounceTimer);
    var delay = isNativeStreaming() ? STREAM_DEBOUNCE_MS : DEBOUNCE_MS;
    debounceTimer = setTimeout(function () {
      debounceTimer = null;
      if (scheduled != null) cancelAnimationFrame(scheduled);
      scheduled = requestAnimationFrame(function () {
        scheduled = null;
        applyContinuousView();
      });
    }, delay);
  }

  globalThis.__cgwSetContinuousView = function (enabled) {
    globalThis.__cgwContinuousViewEnabled = !!enabled;
    if (!enabled) {
      if (debounceTimer != null) clearTimeout(debounceTimer);
      debounceTimer = null;
      teardown();
      return;
    }
    schedule();
  };

  globalThis.__cgwSetHideAssistantEditArtifacts = function (enabled) {
    globalThis.__cgwHideAssistantEditArtifacts = !!enabled;
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    if (globalThis.__cgwContinuousViewEnabled) schedule();
  };

  globalThis.__cgwContinuousViewSchedule = schedule;
})();
