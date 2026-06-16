(function () {
  "use strict";

  var BRIDGE_VERSION = 11;
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

  function extractTurnText(node) {
    if (!node) return "";
    var md = node.querySelector(".markdown, .prose");
    if (md) return (md.innerText || md.textContent || "").trim();
    return (node.innerText || node.textContent || "").trim();
  }

  function getLastAssistantNode() {
    var nodes = document.querySelectorAll('[data-message-author-role="assistant"]');
    if (!nodes.length) return null;
    return nodes[nodes.length - 1];
  }

  function getLastAssistantText() {
    return extractTurnText(getLastAssistantNode());
  }

  function countAssistantTurns() {
    return document.querySelectorAll('[data-message-author-role="assistant"]').length;
  }

  function waitForStableAssistantText(baselineCount, timeoutMs, onDone) {
    var deadline = Date.now() + (timeoutMs || 120000);
    var lastText = "";
    var stableSince = 0;

    function poll() {
      var count = countAssistantTurns();
      var text = getLastAssistantText();

      if (count > baselineCount && text.length > 0) {
        if (text === lastText) {
          if (stableSince === 0) stableSince = Date.now();
          if (Date.now() - stableSince >= STABLE_TEXT_MS) {
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
        requireProjectContext: !!requireProjectContext,
        probe: probeComposer(),
      });
      var baseline = countAssistantTurns();
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
        onSubmitted: function (baseline, finish) {
          waitForStableAssistantText(baseline, timeoutMs || 120000, finish);
        },
      },
      "turnComplete"
    );
  }

  function submitPrompt(text, requireProjectContext, displayUserLine, packetHash) {
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
          cmd.packetHash || ""
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
  globalThis.__cgwAdventureSendPrompt = function (text, timeoutMs, requireProjectContext) {
    sendPrompt(text, timeoutMs, !!requireProjectContext);
  };
  globalThis.__cgwAdventureSubmitPrompt = function (
    text,
    requireProjectContext,
    displayUserLine,
    packetHash
  ) {
    submitPrompt(
      text || "",
      !!requireProjectContext,
      displayUserLine || "",
      packetHash || ""
    );
  };
  globalThis.__cgwAdventureGetConversationId = getConversationKey;
  globalThis.__cgwAdventureProbe = probeComposer;

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
