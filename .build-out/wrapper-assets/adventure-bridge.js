(function () {
  "use strict";

  var BRIDGE_VERSION = 21;
  var DOM_FALLBACK_STASH_KEY = "__cgwDomFallbackAttachmentStash";
  var kernel = globalThis.__cgwPageKernel;
  var composerDom = globalThis.__cgwComposerDom;

  function sendLog(level, eventName, message, data) {
    if (kernel && kernel.bus && typeof kernel.bus.playSendLog === "function") {
      kernel.bus.playSendLog(level, eventName, message, data, "adventure-bridge");
    }
  }

  var STABLE_TEXT_MS = 600;
  var POLL_MS = 350;

  function post(msg) {
    if (kernel && kernel.bus) {
      kernel.bus.post(msg);
      return;
    }
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(
          typeof msg === "string" ? msg : JSON.stringify(msg)
        );
      }
    } catch (_e) {
      /* ignore */
    }
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

  function isInsideWrapperComposer(node) {
    if (composerDom) return composerDom.isInsideWrapper(node);
    return !!(node && node.closest && node.closest("#cgw-play-composer-root"));
  }

  function findComposerElement() {
    if (composerDom) {
      return composerDom.findComposerInput({
        preferOffscreen: true,
        skipWrapper: true,
      });
    }
    var el = document.querySelector("#prompt-textarea");
    if (el && !isInsideWrapperComposer(el)) return el;
    return (
      document.querySelector('[data-testid="composer-text-input"]') ||
      document.querySelector('div.ProseMirror[contenteditable="true"]')
    );
  }

  function findComposerRoot() {
    if (composerDom) {
      return composerDom.findComposerRoot({
        preferOffscreen: true,
        skipWrapper: true,
      });
    }
    var el = findComposerElement();
    if (el && el.closest) {
      return (
        el.closest('[data-testid="composer"]') ||
        el.closest("form") ||
        document
      );
    }
    return document.querySelector('[data-testid="composer"]') || document;
  }

  function findComposerSubmitButton(allowDisabled) {
    if (composerDom) {
      return composerDom.findComposerSubmitButton(allowDisabled);
    }
    var root = findComposerRoot();
    return root.querySelector('button[data-testid="composer-submit-button"]');
  }

  function probeComposer() {
    var base = composerDom
      ? composerDom.probeComposer()
      : {
          composerFound: !!findComposerElement(),
          submitFound: !!findComposerSubmitButton(true),
        };
    return Object.assign(base, {
      conversationId: getConversationKey(),
      url: location.href,
    });
  }

  function listComposerFileUiFallback() {
    var fileInputs = [];
    var nodes = document.querySelectorAll('input[type="file"]');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      fileInputs.push({
        accept: el.getAttribute("accept") || "",
        multiple: !!el.multiple,
        hidden: !el.offsetParent,
        id: el.id || "",
        name: el.name || "",
        testId: el.getAttribute("data-testid") || "",
      });
    }
    return { href: location.href, fileInputs: fileInputs, attachButtons: [] };
  }

  function isInsideWrapperComposer(node) {
    return !!(node && node.closest && node.closest("#cgw-play-composer-root"));
  }

  function findNativeComposerFileInput() {
    var nodes = document.querySelectorAll('input[type="file"]');
    var i;
    for (i = nodes.length - 1; i >= 0; i--) {
      if (!isInsideWrapperComposer(nodes[i])) return nodes[i];
    }
    return nodes[0] || null;
  }

  function base64ToUint8Array(b64) {
    var binary = atob(b64 || "");
    var out = new Uint8Array(binary.length);
    var i;
    for (i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
    return out;
  }

  function readDomFallbackStash() {
    if (typeof globalThis.__cgwPlayComposePeekDomFallbackAttachments === "function") {
      var fromCompose = globalThis.__cgwPlayComposePeekDomFallbackAttachments() || [];
      if (fromCompose.length) return fromCompose;
    }
    var stash = globalThis[DOM_FALLBACK_STASH_KEY];
    return stash && stash.length ? stash : [];
  }

  function resolveDomAttachments(options) {
    var list = (options && options.attachments) || [];
    if ((!list || !list.length) && options && options.useWrapperAttachmentStash) {
      list = readDomFallbackStash();
    }
    return list;
  }

  function stageAttachmentsOnNativeInput(attachments, requireAttachments) {
    if (!attachments || !attachments.length) {
      if (requireAttachments) {
        return { ok: false, error: "no_attachments_available" };
      }
      return { ok: true, count: 0 };
    }
    var input = findNativeComposerFileInput();
    if (!input) return { ok: false, error: "file_input_not_found" };
    try {
      var dt = new DataTransfer();
      var skippedNoBase64 = 0;
      var addFailed = 0;
      var a;
      for (a = 0; a < attachments.length; a++) {
        var item = attachments[a];
        if (!item || !item.base64) {
          skippedNoBase64++;
          continue;
        }
        var bytes = base64ToUint8Array(item.base64);
        var mimeType = item.mimeType || "application/octet-stream";
        var blob = new Blob([bytes], { type: mimeType });
        var file = new File([blob], item.name || "attachment", { type: mimeType });
        var added = dt.items.add(file);
        if (!added) addFailed++;
      }
      if (!dt.files.length) {
        return {
          ok: false,
          error: "no_files_staged",
          skippedNoBase64: skippedNoBase64,
          addFailed: addFailed,
          attachmentCount: attachments.length,
        };
      }
      input.files = dt.files;
      input.dispatchEvent(new Event("change", { bubbles: true }));
      input.dispatchEvent(new Event("input", { bubbles: true }));
      return { ok: true, count: dt.files.length };
    } catch (e) {
      return {
        ok: false,
        error: e && e.message ? String(e.message) : "attach_stage_failed",
      };
    }
  }

  function getOffscreenComposerBucket() {
    return document.getElementById("cgw-native-composer-offscreen");
  }

  function nodeShowsAttachmentPreview(node) {
    if (!node || isInsideWrapperComposer(node)) return false;
    if (
      node.matches &&
      node.matches(
        '[data-testid="composer-footer-attachments"], [data-testid*="attachment"], [data-testid*="file-thumbnail"], [class*="attachment-preview"], [data-testid*="image-preview"], [data-testid*="file-preview"]'
      )
    ) {
      return true;
    }
    if (node.querySelector) {
      if (
        node.querySelector(
          '[data-testid="composer-footer-attachments"], [data-testid*="attachment"], [data-testid*="file-thumbnail"], [class*="attachment-preview"], [data-testid*="image-preview"], [data-testid*="file-preview"], button[aria-label*="Remove file"], button[aria-label*="remove file"]'
        )
      ) {
        return true;
      }
      var imgs = node.querySelectorAll("img");
      var i;
      for (i = 0; i < imgs.length; i++) {
        var src = imgs[i].getAttribute("src") || "";
        if (
          src.indexOf("blob:") >= 0 ||
          src.indexOf("oaiusercontent") >= 0 ||
          src.indexOf("/files/") >= 0
        ) {
          return true;
        }
      }
    }
    return false;
  }

  function nativeComposerShowsAttachments() {
    var selectors = [
      '[data-testid="composer-footer-attachments"]',
      '[data-testid*="attachment"]',
      '[data-testid*="file-thumbnail"]',
      '[class*="attachment-preview"]',
      '[data-testid*="composer"] img[src*="blob:"]',
      '[data-testid*="composer"] img[src*="oaiusercontent"]',
    ];
    var s;
    for (s = 0; s < selectors.length; s++) {
      var match = document.querySelector(selectors[s]);
      if (match && !isInsideWrapperComposer(match)) return true;
    }
    var composer = getNativeComposerRoot();
    if (composer && nodeShowsAttachmentPreview(composer)) return true;
    var bucket = getOffscreenComposerBucket();
    if (bucket && nodeShowsAttachmentPreview(bucket)) return true;
    return false;
  }

  function nativeComposerAttachmentReady() {
    if (nativeComposerShowsAttachments()) {
      return { ready: true, via: "preview" };
    }
    var input = findNativeComposerFileInput();
    if (input && input.files && input.files.length > 0) {
      return { ready: true, via: "file_input", count: input.files.length };
    }
    return { ready: false };
  }

  function nativeComposerUploadInProgress() {
    var scopes = collectUploadFailureScopes();
    var bucket = getOffscreenComposerBucket();
    if (bucket) scopes.push(bucket);
    var s;
    for (s = 0; s < scopes.length; s++) {
      if (
        scopes[s].querySelector(
          '[class*="animate-spin"], [data-testid*="uploading"], [aria-label*="Uploading"], [aria-label*="uploading"]'
        )
      ) {
        return true;
      }
    }
    return false;
  }

  function getNativeComposerRoot() {
    return (
      document.querySelector('[data-testid="composer"]') ||
      document.querySelector('main form') ||
      null
    );
  }

  function collectUploadFailureScopes() {
    var scopes = [];
    var composer = getNativeComposerRoot();
    if (composer && !isInsideWrapperComposer(composer)) scopes.push(composer);
    var alerts = document.querySelectorAll('[role="alert"], [data-testid*="toast"], [class*="toast"]');
    var a;
    for (a = 0; a < alerts.length; a++) {
      if (alerts[a].offsetParent) scopes.push(alerts[a]);
    }
    return scopes;
  }

  function nativeComposerShowsUploadFailure() {
    var scopes = collectUploadFailureScopes();
    var s;
    for (s = 0; s < scopes.length; s++) {
      var text = (scopes[s].textContent || "").toLowerCase();
      if (text.indexOf("files.oaiusercontent.com") >= 0) return "oaiusercontent_blocked";
      if (text.indexOf("failed upload") >= 0) return "upload_failed_banner";
    }
    return null;
  }

  function dismissComposerUploadToasts() {
    var dismissed = false;
    var alerts = document.querySelectorAll('[role="alert"], [data-testid*="toast"], [class*="toast"]');
    var a;
    for (a = 0; a < alerts.length; a++) {
      var text = (alerts[a].textContent || "").toLowerCase();
      if (
        text.indexOf("files.oaiusercontent.com") >= 0 ||
        text.indexOf("failed upload") >= 0
      ) {
        var closeBtn =
          alerts[a].querySelector('button[aria-label*="Close"]') ||
          alerts[a].querySelector('button[aria-label*="Dismiss"]') ||
          alerts[a].querySelector("button");
        if (closeBtn) {
          closeBtn.click();
          dismissed = true;
        }
      }
    }
    return dismissed;
  }

  function dismissComposerBlockingModals() {
    var dismissed = false;
    var nodes = document.querySelectorAll('[role="dialog"], [role="alertdialog"]');
    var i;
    for (i = 0; i < nodes.length; i++) {
      var dialog = nodes[i];
      if (isInsideWrapperComposer(dialog)) continue;
      var label = (dialog.textContent || "").toLowerCase();
      if (
        label.indexOf("already uploaded") >= 0 ||
        label.indexOf("upload something new") >= 0
      ) {
        var buttons = dialog.querySelectorAll("button");
        var b;
        for (b = 0; b < buttons.length; b++) {
          var btnText = (buttons[b].textContent || "").trim().toLowerCase();
          if (btnText === "ok" || btnText === "close" || btnText === "dismiss") {
            buttons[b].click();
            dismissed = true;
            break;
          }
        }
      }
    }
    return dismissed;
  }

  function waitForNativeAttachmentPreview(
    deadlineMs,
    onDone,
    requireReady,
    uploadFailureGraceMs,
    hostCdpStaged
  ) {
    dismissComposerUploadToasts();
    dismissComposerBlockingModals();
    var readyNow = nativeComposerAttachmentReady();
    if (readyNow.ready) {
      onDone({ ok: true, via: readyNow.via });
      return;
    }
    var deadline = Date.now() + (deadlineMs || 30000);
    var startedAt = Date.now();
    var failureGraceUntil = startedAt + (uploadFailureGraceMs || 0);
    var uploadSettledMinMs = (uploadFailureGraceMs || 0) + 5000;
    function poll() {
      dismissComposerBlockingModals();
      dismissComposerUploadToasts();
      if (Date.now() >= failureGraceUntil) {
        var uploadFailure = nativeComposerShowsUploadFailure();
        if (uploadFailure) {
          onDone({ ok: false, error: uploadFailure });
          return;
        }
      }
      readyNow = nativeComposerAttachmentReady();
      if (readyNow.ready) {
        onDone({ ok: true, via: readyNow.via });
        return;
      }
      if (
        hostCdpStaged &&
        requireReady &&
        Date.now() - startedAt >= uploadSettledMinMs &&
        !nativeComposerUploadInProgress()
      ) {
        onDone({ ok: true, via: "upload_settled" });
        return;
      }
      if (Date.now() >= deadline) {
        onDone({
          ok: false,
          error: requireReady ? "attachment_not_ready" : "attachment_preview_timeout",
        });
        return;
      }
      setTimeout(poll, 250);
    }
    poll();
  }

  function tryStartProjectChat(onDone) {
    var selectors = [
      'button[data-testid="create-new-chat-button"]',
      'a[data-testid="create-new-chat-button"]',
      'button[data-testid*="new-chat"]',
      'a[data-testid*="new-chat"]',
    ];
    var i;
    for (i = 0; i < selectors.length; i++) {
      var el = document.querySelector(selectors[i]);
      if (el) {
        el.click();
        waitForProjectChatReady(onDone);
        return true;
      }
    }
    var links = document.querySelectorAll("a, button");
    for (i = 0; i < links.length; i++) {
      var t = (links[i].textContent || "").trim();
      var lower = t.toLowerCase();
      if (
        lower.indexOf("new chat in") >= 0 ||
        lower.indexOf("+ new chat") === 0 ||
        lower === "new chat"
      ) {
        links[i].click();
        waitForProjectChatReady(onDone);
        return true;
      }
    }
    onDone({ ok: false, error: "project_new_chat_not_found", probe: probeComposer() });
    return false;
  }

  function waitForProjectChatReady(onDone, attempts) {
    attempts = attempts || 0;
    var probe = probeComposer();
    if (probe.composerFound) {
      onDone({ ok: true, probe: probe, conversationId: probe.conversationId });
      return;
    }
    if (attempts >= 80) {
      onDone({ ok: false, error: "project_chat_not_ready", probe: probe });
      return;
    }
    setTimeout(function () {
      waitForProjectChatReady(onDone, attempts + 1);
    }, 250);
  }

  function tryStartNewChat(onDone) {
    var selectors = [
      'a[href="/"]',
      'button[data-testid="create-new-chat-button"]',
      'nav a[href*="chatgpt.com"]',
    ];
    var i;
    for (i = 0; i < selectors.length; i++) {
      var el = document.querySelector(selectors[i]);
      if (el) {
        el.click();
        setTimeout(function () {
          onDone(probeComposer());
        }, 800);
        return true;
      }
    }
    var links = document.querySelectorAll("a, button");
    for (i = 0; i < links.length; i++) {
      var t = (links[i].textContent || "").trim().toLowerCase();
      if (t === "new chat" || t.indexOf("new chat") >= 0) {
        links[i].click();
        setTimeout(function () {
          onDone(probeComposer());
        }, 800);
        return true;
      }
    }
    onDone(probeComposer());
    return false;
  }

  function clearComposer() {
    var el = findComposerElement();
    if (!el) return false;
    var wrapperActive = !!globalThis.__cgwWrapperComposer;
    if (el.tagName === "TEXTAREA") {
      el.value = "";
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
      return true;
    }
    try {
      if (!wrapperActive) el.focus();
      document.execCommand("selectAll", false, null);
      document.execCommand("delete", false, null);
    } catch (_e) {
      el.textContent = "";
    }
    el.dispatchEvent(
      new InputEvent("input", { bubbles: true, inputType: "deleteContentBackward" })
    );
    return true;
  }

  function clearStaleInjectionComposer(onDone) {
    onDone = onDone || function () {};
    waitForComposer(
      5000,
      function (probe) {
        if (!probe.composerFound) {
          onDone({ ok: false, error: "composer_not_found" });
          return;
        }
        var text = readComposerText();
        if (!isInjectedContextUserMessage(text)) {
          onDone({ ok: true, cleared: false, skipped: true });
          return;
        }
        clearComposer();
        if (typeof globalThis.__cgwPlayComposeApplyState === "function") {
          globalThis.__cgwPlayComposeApplyState({ clear: true });
        }
        post({ type: "cgwComposeInput", text: "" });
        onDone({ ok: true, cleared: true, textLength: text.length });
      },
      { requireSubmit: false }
    );
  }

  function setBridgeAutomation(active) {
    globalThis.__cgwBridgeAutomationActive = !!active;
  }

  function fillComposer(text) {
    var el = findComposerElement();
    if (!el) return false;
    var wrapperActive = !!globalThis.__cgwWrapperComposer;
    var automating = !!globalThis.__cgwBridgeAutomationActive;

    if (!wrapperActive || automating) {
      try {
        el.scrollIntoView({ block: "nearest", behavior: "auto" });
      } catch (_scroll) {
        /* ignore */
      }
      try {
        el.focus({ preventScroll: true });
      } catch (_focus) {
        try {
          el.focus();
        } catch (_focus2) {
          /* ignore */
        }
      }
    }

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
      document.execCommand("selectAll", false, null);
      document.execCommand("insertText", false, text);
    } catch (_e) {
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

  function dispatchComposerEnter() {
    var el = findComposerElement();
    if (!el) return false;
    try {
      el.focus({ preventScroll: true });
    } catch (_focus) {
      try {
        el.focus();
      } catch (_focus2) {
        /* ignore */
      }
    }
    var opts = {
      key: "Enter",
      code: "Enter",
      keyCode: 13,
      which: 13,
      bubbles: true,
      cancelable: true,
    };
    el.dispatchEvent(new KeyboardEvent("keydown", opts));
    el.dispatchEvent(new KeyboardEvent("keypress", opts));
    el.dispatchEvent(new KeyboardEvent("keyup", opts));
    return true;
  }

  function countUserTurns() {
    return document.querySelectorAll('[data-message-author-role="user"]').length;
  }

  function readComposerText() {
    var el = findComposerElement();
    if (!el) return "";
    if (el.tagName === "TEXTAREA") return (el.value || "").trim();
    return (el.innerText || el.textContent || "").trim();
  }

  function waitForSubmitVerification(baselineUserCount, filledText, timeoutMs, onDone) {
    var deadline = Date.now() + (timeoutMs || 8000);
    var filledLen = (filledText || "").trim().length;
    function poll() {
      var userCount = countUserTurns();
      var composerText = readComposerText();
      if (userCount > baselineUserCount) {
        onDone({ ok: true, verifiedBy: "user_message", userCount: userCount });
        return;
      }
      if (filledLen > 0 && composerText.length === 0) {
        onDone({ ok: true, verifiedBy: "composer_empty" });
        return;
      }
      if (filledLen > 12 && composerText.length < Math.max(8, filledLen * 0.2)) {
        onDone({ ok: true, verifiedBy: "composer_shortened" });
        return;
      }
      if (Date.now() >= deadline) {
        onDone({
          ok: false,
          error: "submit_not_verified",
          probe: probeComposer(),
          userCount: userCount,
          composerLength: composerText.length,
        });
        return;
      }
      setTimeout(poll, 120);
    }
    poll();
  }

  function tryDispatchSubmit(attempts, wrapperActive) {
    var btn = findComposerSubmitButton(false);
    if (btn) {
      btn.click();
      return "click";
    }
    btn = findComposerSubmitButton(true);
    if (btn && !btn.disabled) {
      btn.click();
      return "click";
    }
    if (attempts >= 4) {
      var root = findComposerRoot();
      var form =
        (root && root.closest && root.closest("form")) ||
        (root && root.tagName === "FORM" ? root : null);
      if (form && typeof form.requestSubmit === "function") {
        try {
          form.requestSubmit();
          return "form_submit";
        } catch (_form) {
          /* ignore */
        }
      }
    }
    var enterAfter = wrapperActive ? 8 : 20;
    if (attempts >= enterAfter && dispatchComposerEnter()) {
      return "enter";
    }
    return null;
  }

  function sanitizeTranscriptText(text) {
    if (typeof globalThis.__cgwSanitizeExtractedMessageText === "function") {
      return globalThis.__cgwSanitizeExtractedMessageText(text);
    }
    if (!text) return "";
    return String(text)
      .replace(/\r\n/g, "\n")
      .replace(/\r/g, "\n")
      .replace(/[\uE000-\uF8FF]/g, "")
      .replace(/filecite[\w-]*/gi, "")
      .replace(/[ \t\f\v]{2,}/g, " ")
      .trim();
  }

  function extractTurnText(node) {
    if (!node) return "";
    var md = node.querySelector(".markdown, .prose");
    var raw = md ? md.innerText || md.textContent || "" : node.innerText || node.textContent || "";
    return sanitizeTranscriptText(raw);
  }

  function isInjectedContextUserMessage(text) {
    if (!text) return false;
    var trimmed = text.replace(/^\s+/, "");
    return (
      trimmed.indexOf("[[cgw:") === 0 ||
      text.indexOf("=== PROJECT SOURCES") >= 0 ||
      text.indexOf("=== PLOT ESSENTIALS ===") >= 0 ||
      text.indexOf("=== PLAYER TURN ===") >= 0 ||
      text.indexOf("=== STORY SO FAR") >= 0 ||
      text.indexOf("=== ROLLING SUMMARY ===") >= 0 ||
      text.indexOf("=== STATE ===") >= 0 ||
      text.indexOf("=== CURRENT STATE ===") >= 0
    );
  }

  function stripContextTags(text) {
    if (!text) return "";
    return String(text)
      .replace(/\[\[cgw:[^\]/\]]+[^\]]*\]\][\s\S]*?\[\[\/cgw:[^\]]+\]\]/g, "")
      .trim();
  }

  function isUtilityUserMessage(text) {
    if (!text) return false;
    return String(text).indexOf("[[cgw:utility") >= 0;
  }

  function extractTranscriptPlayerText(text) {
    if (!text) return "";
    if (isUtilityUserMessage(text)) return "";
    if (!isInjectedContextUserMessage(text)) return sanitizeTranscriptText(text);
    var marker = "=== PLAYER TURN ===";
    var markerIdx = text.indexOf(marker);
    if (markerIdx >= 0) {
      var afterMarker = text.slice(markerIdx + marker.length).replace(/^[\r\n]+/, "");
      return sanitizeTranscriptText(afterMarker);
    }
    return sanitizeTranscriptText(stripContextTags(text));
  }

  function getLastAssistantNode() {
    var nodes = document.querySelectorAll('[data-message-author-role="assistant"]');
    if (!nodes.length) return null;
    return nodes[nodes.length - 1];
  }

  function getLastAssistantText() {
    return extractTurnText(getLastAssistantNode());
  }

  function isAssistantPlaceholderText(text) {
    var t = String(text || "").trim();
    if (!t) return true;
    if (t === "Thinking" || t === "Searching" || t === "Searching…") return true;
    if (/^Thinking(\u2026|\.{3})?$/i.test(t)) return true;
    return false;
  }

  function countAssistantTurns() {
    return document.querySelectorAll('[data-message-author-role="assistant"]').length;
  }

  function waitForStableAssistantText(baselineCount, timeoutMs, onDone, options) {
    var deadline = Date.now() + (timeoutMs || 120000);
    var minStableMs =
      options && typeof options.minStableMs === "number" ? options.minStableMs : STABLE_TEXT_MS;
    var lastText = "";
    var stableSince = 0;

    function poll() {
      var count = countAssistantTurns();
      var text = getLastAssistantText();

      if (count > baselineCount && text.length > 0 && !isAssistantPlaceholderText(text)) {
        if (text === lastText) {
          if (stableSince === 0) stableSince = Date.now();
          if (Date.now() - stableSince >= minStableMs) {
            onDone({ ok: true, text: text });
            return;
          }
        } else {
          lastText = text;
          stableSince = 0;
        }
      } else {
        lastText = "";
        stableSince = 0;
      }

      if (Date.now() >= deadline) {
        if (text.length > 0) {
          onDone({ ok: true, text: text });
        } else {
          onDone({ ok: false, error: "timeout" });
        }
        return;
      }
      setTimeout(poll, POLL_MS);
    }
    poll();
  }

  function setWrapperComposer(enabled) {
    if (typeof globalThis.__cgwPlayComposeScheduleMount === "function") {
      globalThis.__cgwWrapperComposer = !!enabled;
      var docRoot = document.documentElement;
      if (docRoot) {
        if (enabled) docRoot.setAttribute("data-cgw-wrapper-composer", "1");
        else docRoot.removeAttribute("data-cgw-wrapper-composer");
      }
      if (enabled) globalThis.__cgwPlayComposeScheduleMount();
      else if (typeof globalThis.__cgwPlayComposeUnmount === "function") {
        globalThis.__cgwPlayComposeUnmount();
      }
      return;
    }
    globalThis.__cgwWrapperComposer = !!enabled;
    var root = document.documentElement;
    if (!root) return;
    if (enabled) root.setAttribute("data-cgw-wrapper-composer", "1");
    else root.removeAttribute("data-cgw-wrapper-composer");
  }

  globalThis.__cgwSetWrapperComposer = setWrapperComposer;

  function waitForComposer(timeoutMs, onReady, options) {
    var requireSubmit = !options || options.requireSubmit !== false;
    var deadline = Date.now() + (timeoutMs || 5000);
    function poll() {
      var probe = probeComposer();
      if (probe.composerFound && (!requireSubmit || probe.submitFound)) {
        onReady(probe);
        return;
      }
      if (Date.now() >= deadline) {
        onReady(probe);
        return;
      }
      setTimeout(poll, 120);
    }
    poll();
  }

  function temporarilyShowNativeComposer() {
    var wasWrapper = !!globalThis.__cgwWrapperComposer;
    if (wasWrapper) setWrapperComposer(false);
    return function restore() {
      if (wasWrapper) setWrapperComposer(true);
    };
  }

  function runComposerPrompt(text, requireProjectContext, options, messageType) {
    var wrapperActive = !!globalThis.__cgwWrapperComposer;
    var restoreExpose = function () {};
    var waitMs = wrapperActive
      ? 400
      : (options && options.composerWaitMs) || 2500;
    var unhideDelayMs = wrapperActive ? 0 : (options && options.unhideDelayMs) || 100;
    var onSubmitted = options && options.onSubmitted;
    var maxSubmitAttempts =
      options && options.maxSubmitAttempts
        ? options.maxSubmitAttempts
        : wrapperActive
          ? 80
          : 25;

    function finish(result) {
      setBridgeAutomation(false);
      restoreExpose();
      if (
        result.ok &&
        typeof globalThis.__cgwPlayComposeClearDomFallbackAttachments === "function"
      ) {
        globalThis.__cgwPlayComposeClearDomFallbackAttachments();
      }
      sendLog(
        result.ok ? "info" : "error",
        "bridge_prompt_finish",
        "Composer prompt finished",
        {
          messageType: messageType,
          ok: !!result.ok,
          error: result.error || null,
          textLength: (result.text || "").length,
        }
      );
      post({
        type: messageType,
        ok: result.ok,
        text: result.text || "",
        error: result.error || null,
        conversationId: getConversationKey(),
      });
    }

    function doSend() {
      setBridgeAutomation(true);
      sendLog("info", "bridge_do_send", "Starting native composer fill/submit", {
        messageType: messageType,
        wrapperActive: wrapperActive,
        textLength: (text || "").length,
        attachmentCount: resolveDomAttachments(options).length,
        requireProjectContext: !!requireProjectContext,
        probe: probeComposer(),
      });
      var baseline = countAssistantTurns();

      function continueFillAndSubmit() {
        var filled = fillComposer(text);
        if (!filled) {
          filled = fillComposer(text);
        }
        if (!filled) {
          sendLog("error", "bridge_fill_failed", "fillComposer returned false", {
            probe: probeComposer(),
          });
          finish({ ok: false, error: "composer_not_found" });
          return;
        }
        sendLog("debug", "bridge_fill_ok", "Composer filled", { probe: probeComposer() });
        scheduleSubmitAttempts();
      }

      function prepareNativeComposer(onReady) {
        if (options && options.hostCdpStaged) {
          if (wrapperActive && composerDom) {
            var exposeCdp = composerDom.temporarilyExposeOffscreenComposer();
            if (exposeCdp.exposed) restoreExpose = exposeCdp.restore;
          }
          var cdpInput = findNativeComposerFileInput();
          sendLog(
            "info",
            "bridge_prepare_cdp_expose",
            "Exposed native composer for CDP attachment wait",
            {
              previewVisible: nativeComposerShowsAttachments(),
              fileInputCount: cdpInput && cdpInput.files ? cdpInput.files.length : 0,
            }
          );
          setTimeout(onReady, wrapperActive ? 200 : 0);
          return;
        }
        if (wrapperActive && composerDom) {
          var wrapperRoot = document.getElementById("cgw-play-composer-root");
          var restoreAnchor = composerDom.temporarilyRestoreNativeToAnchor(wrapperRoot);
          restoreExpose = restoreAnchor.restore;
          sendLog("info", "bridge_restore_anchor", "Restored native composer to anchor for submit", {
            restored: restoreAnchor.restored,
            probe: probeComposer(),
          });
          if (!restoreAnchor.restored) {
            var expose = composerDom.temporarilyExposeOffscreenComposer();
            restoreExpose = expose.restore;
            sendLog("info", "bridge_expose_composer", "Fell back to offscreen expose for submit", {
              exposed: expose.exposed,
              probe: probeComposer(),
            });
          }
        }
        setTimeout(onReady, wrapperActive ? 120 : 0);
      }

      function stageAttachmentsThen(onReady) {
        var domAttachments = resolveDomAttachments(options);
        var requireAttachments = !!(
          options &&
          (options.useWrapperAttachmentStash ||
            (options.attachments && options.attachments.length) ||
            (options.attachmentsPreStaged && nativeComposerShowsAttachments()))
        );
        if (!domAttachments.length && !requireAttachments) {
          onReady();
          return;
        }
        if (options && options.attachmentsPreStaged) {
          sendLog(
            "info",
            "bridge_attach_prestaged",
            "Attachments already on native composer",
            {
              attachmentCount: domAttachments.length,
              previewVisible: nativeComposerShowsAttachments(),
            }
          );
          if (nativeComposerShowsAttachments()) {
            waitForNativeAttachmentPreview(
              30000,
              function (preview) {
                if (!preview.ok) {
                  sendLog(
                    "warn",
                    "bridge_attach_prestaged_wait",
                    "Native attachment preview not confirmed; continuing",
                    preview
                  );
                }
                onReady();
              },
              false,
              0,
              false
            );
          } else {
            setTimeout(onReady, 200);
          }
          return;
        }
        if (options && options.hostCdpStaged) {
          sendLog("info", "bridge_attach_cdp_skip", "Skipping in-page staging; host CDP staged files", {
            attachmentCount: domAttachments.length,
            previewVisible: nativeComposerShowsAttachments(),
          });
          waitForNativeAttachmentPreview(
            options.attachmentWaitMs || 120000,
            function (preview) {
              if (!preview.ok) {
                sendLog(
                  "error",
                  "bridge_attach_not_ready",
                  "Attachment not ready after host CDP staging",
                  preview
                );
                finish({
                  ok: false,
                  error: preview.error || "attachment_not_ready",
                });
                return;
              }
              sendLog("info", "bridge_attach_ready", "Attachment ready after CDP staging", preview);
              onReady();
            },
            true,
            4000,
            true
          );
          return;
        }
        var staged = stageAttachmentsOnNativeInput(domAttachments, requireAttachments);
        if (!staged.ok || !staged.count) {
          sendLog("error", "bridge_attach_failed", "Could not stage attachments on native composer", staged);
          finish({ ok: false, error: staged.error || "attach_failed" });
          return;
        }
        sendLog("info", "bridge_attach_staged", "Staged files on native composer input", staged);
        waitForNativeAttachmentPreview(
          options.attachmentWaitMs || 60000,
          function (preview) {
            if (!preview.ok) {
              sendLog(
                "warn",
                "bridge_attach_preview_timeout",
                "Attachment preview not detected; continuing",
                preview
              );
            }
            onReady();
          },
          false,
          0,
          false
        );
      }

      function scheduleSubmitAttempts() {
      var submitDelay = wrapperActive ? 180 : 30;
      setTimeout(function () {
        var attempts = 0;
        var submitHandled = false;
        var maxAttempts = maxSubmitAttempts;
        function trySubmit() {
          if (submitHandled) return;
          var dispatched = tryDispatchSubmit(attempts, wrapperActive);
          if (dispatched) {
            var baselineUser = countUserTurns();
            var filledText = readComposerText();
            sendLog("debug", "bridge_submit_dispatched", "Submit action dispatched", {
              method: dispatched,
              attempt: attempts,
              probe: probeComposer(),
            });
            waitForSubmitVerification(baselineUser, filledText, 4000, function (verify) {
              if (verify.ok) {
                submitHandled = true;
                sendLog("info", "bridge_submit_verified", "Submit verified in ChatGPT UI", verify);
                if (
                  options &&
                  options.displayUserLine &&
                  typeof globalThis.__cgwStampUserTurnDisplay === "function"
                ) {
                  globalThis.__cgwStampUserTurnDisplay(
                    options.displayUserLine,
                    options.packetHash || ""
                  );
                }
                onSubmitted(baseline, finish);
                return;
              }
              sendLog("warn", "bridge_submit_unverified", "Submit action did not verify", verify);
              attempts++;
              if (attempts >= maxAttempts) {
                finish({ ok: false, error: verify.error || "submit_not_verified" });
                return;
              }
              setTimeout(trySubmit, wrapperActive ? 50 : 80);
            });
            return;
          }
          attempts++;
          if (attempts >= maxAttempts) {
            sendLog("error", "bridge_submit_not_found", "Submit button/enter fallback failed", {
              attempts: attempts,
              probe: probeComposer(),
            });
            finish({ ok: false, error: "submit_not_found" });
            return;
          }
          setTimeout(trySubmit, wrapperActive ? 50 : 80);
        }
        trySubmit();
      }, submitDelay);
      }

      prepareNativeComposer(function () {
        stageAttachmentsThen(continueFillAndSubmit);
      });
    }

    var composerWaitOptions =
      options && options.composerWaitOptions ? options.composerWaitOptions : null;

    function startSend() {
      if (wrapperActive) {
        var probe = probeComposer();
        if (probe.composerFound) {
          doSend();
          return;
        }
      }
      waitForComposer(
        waitMs,
        function (probe) {
          if (!probe.composerFound) {
            if (requireProjectContext) {
              finish({ ok: false, error: "project_context_required" });
              return;
            }
            finish({ ok: false, error: "composer_not_found" });
            return;
          }
          doSend();
        },
        composerWaitOptions
      );
    }

    setTimeout(startSend, unhideDelayMs);
  }

  function sendPrompt(text, timeoutMs, requireProjectContext) {
    runComposerPrompt(
      text,
      requireProjectContext,
      {
        composerWaitMs: 5000,
        unhideDelayMs: 250,
        composerWaitOptions: { requireSubmit: false },
        onSubmitted: function (baseline, finish) {
          waitForStableAssistantText(baseline, timeoutMs || 120000, finish, {
            minStableMs: 1500,
          });
        },
      },
      "turnComplete"
    );
  }

  function submitPrompt(
    text,
    requireProjectContext,
    displayUserLine,
    packetHash,
    attachments,
    useWrapperAttachmentStash,
    hostCdpStaged,
    attachmentsPreStaged
  ) {
    runComposerPrompt(
      text,
      requireProjectContext,
      {
        composerWaitMs: 1500,
        unhideDelayMs: 50,
        composerWaitOptions: { requireSubmit: false },
        maxSubmitAttempts: 20,
        displayUserLine: displayUserLine || "",
        packetHash: packetHash || "",
        attachments: attachments || [],
        useWrapperAttachmentStash: !!useWrapperAttachmentStash,
        hostCdpStaged: !!hostCdpStaged,
        attachmentsPreStaged: !!attachmentsPreStaged,
        attachmentWaitMs: 120000,
        onSubmitted: function (_baseline, finish) {
          try {
            if (globalThis.__cgwWrapperComposer) clearComposer();
          } catch (_clear) {
            /* ignore */
          }
          finish({ ok: true });
        },
      },
      "promptSubmitted"
    );
  }

  function regenerateLast(timeoutMs) {
    var last = getLastAssistantNode();
    if (!last) {
      post({ type: "turnComplete", ok: false, error: "no_assistant_turn" });
      return;
    }
    var baseline = countAssistantTurns();
    var regenBtn =
      last.querySelector('button[data-testid*="regenerate"]') ||
      last.querySelector('button[aria-label*="Regenerate"]');
    if (!regenBtn) {
      post({ type: "turnComplete", ok: false, error: "regenerate_not_found" });
      return;
    }
    regenBtn.click();
    waitForStableAssistantText(baseline - 1, timeoutMs || 120000, function (result) {
      if (result.ok) {
        post({
          type: "turnInvalidated",
          ok: true,
          turnId: String(baseline),
          reason: "regenerate",
          text: result.text || "",
          conversationId: getConversationKey(),
        });
      }
      post({
        type: "turnComplete",
        ok: result.ok,
        text: result.text || "",
        error: result.error || null,
        fromRegenerate: true,
        conversationId: getConversationKey(),
      });
    });
  }

  function handleCommand(cmd) {
    if (!cmd || !cmd.action) return;
    sendLog("debug", "bridge_command", "Bridge command received", {
      action: cmd.action,
      textLength: typeof cmd.text === "string" ? cmd.text.length : 0,
      useWrapperAttachmentStash: !!cmd.useWrapperAttachmentStash,
      hostCdpStaged: !!cmd.hostCdpStaged,
      attachmentCount: cmd.attachments && cmd.attachments.length ? cmd.attachments.length : 0,
      stashCount: cmd.useWrapperAttachmentStash ? readDomFallbackStash().length : 0,
    });
    switch (cmd.action) {
      case "sendPrompt":
        sendPrompt(cmd.text || "", cmd.timeoutMs, !!cmd.requireProjectContext);
        break;
      case "submitPrompt":
        submitPrompt(
          cmd.text || "",
          !!cmd.requireProjectContext,
          cmd.displayUserLine || "",
          cmd.packetHash || "",
          cmd.attachments || [],
          !!cmd.useWrapperAttachmentStash,
          !!cmd.hostCdpStaged,
          !!cmd.attachmentsPreStaged
        );
        break;
      case "fillComposer": {
        var wrapperActive = !!globalThis.__cgwWrapperComposer;
        var restoreExpose = function () {};
        setTimeout(function () {
          waitForComposer(5000, function (probe) {
            if (wrapperActive && composerDom) {
              var expose = composerDom.temporarilyExposeOffscreenComposer();
              restoreExpose = expose.restore;
            }
            var filled = probe.composerFound && fillComposer(cmd.text || "");
            restoreExpose();
            if (!filled) {
              post({ type: "composerFilled", ok: false, error: "composer_not_found" });
            } else {
              post({
                type: "composerFilled",
                ok: true,
                conversationId: getConversationKey(),
              });
            }
          }, { requireSubmit: false });
        }, wrapperActive ? 0 : 250);
        break;
      }
      case "clearComposer":
        setTimeout(function () {
          clearComposer();
          if (typeof globalThis.__cgwPlayComposeApplyState === "function") {
            globalThis.__cgwPlayComposeApplyState({ clear: true });
          }
          post({ type: "cgwComposeInput", text: "" });
          post({ type: "composerCleared", ok: true, cleared: true });
        }, 250);
        break;
      case "clearComposerIfInjection":
        setTimeout(function () {
          clearStaleInjectionComposer(function (result) {
            post({ type: "composerCleared", ok: !!result.ok, cleared: !!result.cleared, skipped: !!result.skipped, error: result.error || null });
          });
        }, 250);
        break;
      case "captureLastAssistant": {
        var captured = getLastAssistantText();
        post({
          type: "captureResult",
          ok: !!captured,
          text: captured || "",
          conversationId: getConversationKey(),
        });
        break;
      }
      case "captureStableAssistant": {
        var baseline = typeof cmd.baselineCount === "number" ? cmd.baselineCount : 0;
        var stableTimeoutMs = cmd.timeoutMs || 120000;
        waitForStableAssistantText(baseline, stableTimeoutMs, function (result) {
          post({
            type: "captureResult",
            ok: !!result.ok,
            text: result.text || "",
            error: result.error || null,
            conversationId: getConversationKey(),
          });
        });
        break;
      }
      case "getAssistantTurnCount": {
        post({
          type: "assistantTurnCount",
          ok: true,
          count: countAssistantTurns(),
          conversationId: getConversationKey(),
        });
        break;
      }
      case "getUserTurnCount": {
        post({
          type: "userTurnCount",
          ok: true,
          count: countUserTurns(),
          conversationId: getConversationKey(),
        });
        break;
      }
      case "captureThreadTranscript": {
        var maxPairs = cmd.maxPairs || 0;
        var nodes = document.querySelectorAll("[data-message-author-role]");
        var pairs = [];
        var pendingUser = null;
        for (var ti = 0; ti < nodes.length; ti++) {
          var role = nodes[ti].getAttribute("data-message-author-role");
          var turnText = extractTurnText(nodes[ti]);
          if (!turnText) continue;
          if (role === "user") {
            if (isUtilityUserMessage(turnText)) {
              pendingUser = null;
              continue;
            }
            var playerText = extractTranscriptPlayerText(turnText);
            if (!playerText && isInjectedContextUserMessage(turnText)) continue;
            if (playerText) pendingUser = playerText;
          } else if (role === "assistant") {
            if (
              turnText.indexOf(UTILITY_RESPONSE_TAG_MARKER) >= 0 ||
              isUtilityUserMessage(turnText)
            ) {
              pendingUser = null;
              continue;
            }
            if (pendingUser || turnText) {
              pairs.push({ player: pendingUser || "", narrator: turnText });
            }
            pendingUser = null;
          }
        }
        if (maxPairs > 0 && pairs.length > maxPairs) {
          pairs = pairs.slice(pairs.length - maxPairs);
        }
        post({
          type: "transcriptResult",
          ok: pairs.length > 0,
          pairs: pairs,
          conversationId: getConversationKey(),
        });
        break;
      }
      case "regenerateLast":
        regenerateLast(cmd.timeoutMs);
        break;
      case "getConversationId":
        post({
          type: "conversationId",
          conversationId: getConversationKey(),
        });
        break;
      case "probe":
        post({ type: "probeResult", probe: probeComposer() });
        break;
      case "listComposerFileUi":
        post({
          type: "composerFileUi",
          ok: true,
          ui: composerDom && composerDom.listComposerFileUi
            ? composerDom.listComposerFileUi()
            : listComposerFileUiFallback(),
        });
        break;
      case "startProjectChat":
        tryStartProjectChat(function (result) {
          post({
            type: "projectChatReady",
            ok: !!result.ok,
            error: result.error || null,
            conversationId: (result.probe && result.probe.conversationId) || getConversationKey(),
            probe: result.probe || probeComposer(),
          });
        });
        break;
      case "setWrapperComposer":
        setWrapperComposer(!!cmd.enabled);
        post({
          type: "wrapperComposerSet",
          ok: true,
          enabled: !!cmd.enabled,
        });
        break;
      case "ping":
        post({
          type: "pong",
          ok: true,
          conversationId: getConversationKey(),
          probe: probeComposer(),
        });
        break;
      default:
        post({ type: "error", error: "unknown_action" });
    }
  }

  globalThis.__cgwAdventureBridgeVersion = BRIDGE_VERSION;
  globalThis.__cgwAdventureHandleCommand = handleCommand;
  globalThis.__cgwAdventureClearStaleInjectionComposer = clearStaleInjectionComposer;
  globalThis.__cgwAdventureSendPrompt = function (text, timeoutMs, requireProjectContext) {
    sendPrompt(text, timeoutMs, !!requireProjectContext);
  };
  globalThis.__cgwAdventureSubmitPrompt = function (
    text,
    requireProjectContext,
    displayUserLine,
    packetHash,
    attachments,
    useWrapperAttachmentStash,
    hostCdpStaged,
    attachmentsPreStaged
  ) {
    submitPrompt(
      text || "",
      !!requireProjectContext,
      displayUserLine || "",
      packetHash || "",
      attachments || [],
      !!useWrapperAttachmentStash,
      !!hostCdpStaged,
      !!attachmentsPreStaged
    );
  };
  globalThis.__cgwNativeComposerHasAttachments = function () {
    return nativeComposerShowsAttachments();
  };
  globalThis.__cgwNativeComposerReadText = function () {
    var el = null;
    if (composerDom) {
      el = composerDom.findComposerInput({
        preferOffscreen: !!globalThis.__cgwWrapperComposer,
        skipWrapper: true,
      });
    } else {
      el = findComposerElement();
    }
    if (!el) return "";
    if (el.tagName === "TEXTAREA") return (el.value || "").trim();
    return (el.textContent || el.innerText || "").trim();
  };
  globalThis.__cgwAdventurePollUploadFailure = function () {
    return nativeComposerShowsUploadFailure();
  };
  globalThis.__cgwAdventurePollAttachmentReady = function (startedAtMs) {
    var ready = nativeComposerAttachmentReady();
    if (ready.ready) return ready;
    var startedAt = Number(startedAtMs) || 0;
    if (startedAt > 0 && Date.now() - startedAt >= 9000) {
      if (!nativeComposerShowsUploadFailure() && !nativeComposerUploadInProgress()) {
        return { ready: true, via: "upload_settled" };
      }
    }
    return { ready: false };
  };
  globalThis.__cgwPrepareNativeComposerForAttach = function () {
    var wrapperActive = !!globalThis.__cgwWrapperComposer;
    if (wrapperActive && composerDom) {
      var wrapperRoot = document.getElementById("cgw-play-composer-root");
      var restoreAnchor = composerDom.temporarilyRestoreNativeToAnchor(wrapperRoot);
      if (typeof restoreAnchor.restore === "function") {
        globalThis.__cgwUploadPrepareRestore = restoreAnchor.restore;
      }
      return { restored: !!restoreAnchor.restored };
    }
    return { restored: true };
  };
  globalThis.__cgwAdventureGetConversationId = getConversationKey;
  globalThis.__cgwAdventureProbe = probeComposer;

  var UTILITY_HIDE_STORAGE_PREFIX = "cgw-utility-hide:";

  function loadUtilityHideQueue() {
    var key = getConversationKey();
    if (!key) return;
    try {
      var raw = sessionStorage.getItem(UTILITY_HIDE_STORAGE_PREFIX + key);
      if (raw) globalThis.__cgwUtilityHideQueue = JSON.parse(raw);
    } catch (_e) {
      globalThis.__cgwUtilityHideQueue = [];
    }
  }

  function persistUtilityHideQueue() {
    var key = getConversationKey();
    if (!key) return;
    try {
      sessionStorage.setItem(
        UTILITY_HIDE_STORAGE_PREFIX + key,
        JSON.stringify(globalThis.__cgwUtilityHideQueue || [])
      );
    } catch (_e) {
      /* ignore */
    }
  }

  globalThis.__cgwRegisterUtilityHide = function (jobId) {
    loadUtilityHideQueue();
    var queue = globalThis.__cgwUtilityHideQueue || [];
    queue.push({ jobId: String(jobId || ""), utilityMarker: "[[cgw:utility" });
    globalThis.__cgwUtilityHideQueue = queue;
    persistUtilityHideQueue();
    if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
      globalThis.__cgwContinuousViewSchedule();
    }
    if (typeof globalThis.__cgwApplyNativeUtilityTurnHide === "function") {
      globalThis.__cgwApplyNativeUtilityTurnHide();
    }
  };

  var UTILITY_RESPONSE_TAG_MARKER = "[[cgw:utility-response";

  function getNativeTurnPlainText(turnRoot) {
    if (!turnRoot) return "";
    var pre = turnRoot.querySelector(
      '.whitespace-pre-wrap, [class*="whitespace-pre-wrap"]'
    );
    if (pre) return String(pre.textContent || "").trim();
    return String(turnRoot.textContent || "").trim();
  }

  function findNativeUtilityTurnRoots() {
    var byRole = Array.prototype.slice.call(
      document.querySelectorAll(
        '[data-message-author-role="user"], [data-message-author-role="assistant"]'
      )
    );
    if (byRole.length) return byRole;
    return Array.prototype.slice.call(
      document.querySelectorAll('[data-testid^="conversation-turn-"]')
    );
  }

  function nativeUtilityTurnWrapper(turnRoot) {
    if (!turnRoot) return null;
    var testTurn = turnRoot.closest('[data-testid^="conversation-turn-"]');
    if (testTurn) return testTurn;
    if (
      turnRoot.matches &&
      turnRoot.matches("[data-message-author-role]")
    ) {
      return turnRoot;
    }
    return turnRoot;
  }

  globalThis.__cgwApplyNativeUtilityTurnHide = function () {
    if (globalThis.__cgwContinuousViewEnabled) return;
    var root = document.documentElement;
    var enabled =
      globalThis.__cgwHideInlineUtilityDuringPlay !== false &&
      !globalThis.__cgwShowInlineUtilityTraffic;
    if (enabled) {
      root.setAttribute("data-cgw-hide-inline-utility", "1");
    } else {
      root.removeAttribute("data-cgw-hide-inline-utility");
    }

    var turns = findNativeUtilityTurnRoots();
    var hideNextAssistant = false;
    var composerRoot = findComposerRoot();

    turns.forEach(function (turn) {
      var wrap = nativeUtilityTurnWrapper(turn);
      if (!wrap) return;
      if (
        composerRoot &&
        composerRoot !== document &&
        typeof wrap.contains === "function" &&
        wrap.contains(composerRoot)
      ) {
        return;
      }
      var role =
        turn.getAttribute && turn.getAttribute("data-message-author-role");
      if (!role && wrap.querySelector) {
        if (wrap.querySelector('[data-message-author-role="user"]')) role = "user";
        else if (wrap.querySelector('[data-message-author-role="assistant"]'))
          role = "assistant";
      }
      var text = getNativeTurnPlainText(turn);
      var hide = false;
      if (!enabled) {
        hide = false;
        hideNextAssistant = false;
      } else if (role === "user" && text.indexOf("[[cgw:utility") >= 0) {
        hide = true;
        hideNextAssistant = true;
      } else if (
        role === "assistant" &&
        (text.indexOf(UTILITY_RESPONSE_TAG_MARKER) >= 0 || hideNextAssistant)
      ) {
        hide = true;
        hideNextAssistant = false;
      } else {
        hideNextAssistant = false;
      }

      if (hide) {
        wrap.classList.add("cgw-turn-suppressed");
        wrap.setAttribute("data-cgw-utility-suppressed", "1");
      } else if (wrap.getAttribute("data-cgw-utility-suppressed") === "1") {
        wrap.classList.remove("cgw-turn-suppressed");
        wrap.removeAttribute("data-cgw-utility-suppressed");
      }
    });
  };

  globalThis.__cgwSetInlineUtilityPreferences = function (hideDuringPlay, showTraffic) {
    globalThis.__cgwHideInlineUtilityDuringPlay = hideDuringPlay !== false;
    globalThis.__cgwShowInlineUtilityTraffic = !!showTraffic;
    var root = document.documentElement;
    if (globalThis.__cgwShowInlineUtilityTraffic) {
      root.setAttribute("data-cgw-show-utility-traffic", "1");
    } else {
      root.removeAttribute("data-cgw-show-utility-traffic");
    }
    if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
      globalThis.__cgwContinuousViewSchedule();
    }
    if (typeof globalThis.__cgwApplyNativeUtilityTurnHide === "function") {
      globalThis.__cgwApplyNativeUtilityTurnHide();
    }
    if (typeof globalThis.__cgwApplyContextTagDisplay === "function") {
      globalThis.__cgwApplyContextTagDisplay();
    }
  };

  if (globalThis.__cgwHideInlineUtilityDuringPlay === undefined) {
    globalThis.__cgwHideInlineUtilityDuringPlay = true;
  }

  if (window.chrome && window.chrome.webview && !globalThis.__cgwAdventureBridgeListener) {
    globalThis.__cgwAdventureBridgeListener = true;
    window.chrome.webview.addEventListener("message", function (ev) {
      var data = ev.data;
      if (typeof data === "string") {
        try {
          data = JSON.parse(data);
        } catch (_e) {
          return;
        }
      }
      var handler = globalThis.__cgwAdventureHandleCommand;
      if (typeof handler === "function") handler(data);
    });
  }

  post({ type: "bridgeReady", version: BRIDGE_VERSION });
})();
