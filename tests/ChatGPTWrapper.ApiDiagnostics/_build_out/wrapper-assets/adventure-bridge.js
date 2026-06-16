(function () {
  "use strict";

  if (globalThis.__cgwAdventureBridgeBooted) return;
  globalThis.__cgwAdventureBridgeBooted = true;

  var STABLE_TEXT_MS = 600;
  var POLL_MS = 350;

  function post(msg) {
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(msg);
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
    return (
      document.querySelector('[data-testid="composer"]') ||
      document.querySelector('[class*="composer"]') ||
      document
    );
  }

  function probeComposer() {
    return {
      composerFound: !!findComposerElement(),
      submitFound: !!findComposerSubmitButton(true),
      conversationId: getConversationKey(),
      url: location.href,
    };
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

  function fillComposer(text) {
    var el = findComposerElement();
    if (!el) return false;
    el.focus();
    if (el.tagName === "TEXTAREA") {
      el.value = text;
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
      return true;
    }
    try {
      document.execCommand("selectAll", false, null);
      document.execCommand("insertText", false, text);
    } catch (_e) {
      el.textContent = text;
    }
    el.dispatchEvent(
      new InputEvent("input", { bubbles: true, inputType: "insertText" })
    );
    return true;
  }

  function findComposerSubmitButton(allowDisabled) {
    var root = findComposerRoot();
    var selectors = [
      'button[data-testid="composer-submit-button"]',
      'button[data-testid="composer-publish-button"]',
      'button[data-testid*="submit"]',
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
      if (aria.indexOf("send") >= 0) return btn;
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

  function sendPrompt(text, timeoutMs, requireProjectContext) {
    function doSend() {
      var baseline = countAssistantTurns();
      if (!fillComposer(text)) {
        post({ type: "turnComplete", ok: false, error: "composer_not_found" });
        return;
      }
      var attempts = 0;
      function trySubmit() {
        var btn = findComposerSubmitButton(false) || findComposerSubmitButton(true);
        if (btn && !btn.disabled) {
          btn.click();
          waitForStableAssistantText(baseline, timeoutMs || 120000, function (result) {
            post({
              type: "turnComplete",
              ok: result.ok,
              text: result.text || "",
              error: result.error || null,
              conversationId: getConversationKey(),
            });
          });
          return;
        }
        attempts++;
        if (attempts < 40) {
          setTimeout(trySubmit, 100);
          return;
        }
        post({ type: "turnComplete", ok: false, error: "submit_not_found" });
      }
      setTimeout(trySubmit, 50);
    }

    if (!findComposerElement()) {
      if (requireProjectContext) {
        post({
          type: "turnComplete",
          ok: false,
          error: "project_context_required",
        });
        return;
      }
      tryStartNewChat(function () {
        setTimeout(doSend, 300);
      });
      return;
    }
    doSend();
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
    switch (cmd.action) {
      case "sendPrompt":
        sendPrompt(cmd.text || "", cmd.timeoutMs, !!cmd.requireProjectContext);
        break;
      case "fillComposer":
        if (!fillComposer(cmd.text || "")) {
          post({ type: "composerFilled", ok: false, error: "composer_not_found" });
        } else {
          post({
            type: "composerFilled",
            ok: true,
            conversationId: getConversationKey(),
          });
        }
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

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", function (ev) {
      var data = ev.data;
      if (typeof data === "string") {
        try {
          data = JSON.parse(data);
        } catch (_e) {
          return;
        }
      }
      handleCommand(data);
    });
  }

  globalThis.__cgwAdventureSendPrompt = function (text, timeoutMs) {
    sendPrompt(text, timeoutMs);
  };

  globalThis.__cgwAdventureGetConversationId = getConversationKey;
  globalThis.__cgwAdventureProbe = probeComposer;

  post({ type: "bridgeReady" });
})();
