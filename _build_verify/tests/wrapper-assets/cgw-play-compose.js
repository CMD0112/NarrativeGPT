(function () {
  "use strict";

  var kernel = globalThis.__cgwPageKernel;
  var composerDom = globalThis.__cgwComposerDom;
  var COMPOSE_VERSION = 17;
  var sendLockTimer = null;

  function sendLog(level, eventName, message, data) {
    if (kernel && kernel.bus && typeof kernel.bus.playSendLog === "function") {
      kernel.bus.playSendLog(level, eventName, message, data, "play-compose");
    }
  }
  var sendInFlight = false;
  var inputDebounce = null;
  var mountPollTimer = null;
  var focusRetryTimer = null;
  var focusWanted = false;
  var focusStartedAt = 0;
  var nativeFocusGuardBound = false;
  var enterGuardBound = false;
  var mountedRoot = null;
  var domUnsubscribe = null;

  var FOCUS_MAX_MS = 3000;

  function postToHost(msg) {
    if (kernel && kernel.bus) kernel.bus.post(msg);
    else {
      try {
        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
          window.chrome.webview.postMessage(JSON.stringify(msg));
        }
      } catch (_e) {
        /* ignore */
      }
    }
  }

  function composer() {
    return composerDom || null;
  }

  function findComposerAnchor() {
    var cd = composer();
    if (cd) return cd.findComposerAnchor(getMounted());
    return { node: document.body, mode: "fixed" };
  }

  function relocateNativeComposerChrome(anchor, root) {
    var cd = composer();
    if (cd) cd.relocateNativeComposerChrome(anchor, root);
  }

  function restoreNativeFromOffscreen() {
    var cd = composer();
    if (cd) cd.restoreNativeFromOffscreen();
  }

  function localSetWrapperComposer(enabled) {
    globalThis.__cgwWrapperComposer = !!enabled;
    var root = document.documentElement;
    if (!root) return;
    if (enabled) root.setAttribute("data-cgw-wrapper-composer", "1");
    else root.removeAttribute("data-cgw-wrapper-composer");
    if (enabled) {
      ensureNativeFocusGuard();
      ensureEnterGuard();
      ensureDomSubscription();
      scheduleMount();
      if (kernel && kernel.features) kernel.features.activate("play-compose");
    } else {
      stopMountPoll();
      cancelFocusRetries();
      focusWanted = false;
      teardownDomSubscription();
      unmountWrapperComposer();
      if (kernel && kernel.features) kernel.features.deactivate("play-compose");
    }
  }

  function ensureDomSubscription() {
    if (domUnsubscribe || !kernel || !kernel.dom) return;
    domUnsubscribe = kernel.dom.subscribe(
      "play-compose-mount",
      { debounceMs: 150 },
      function () {
        if (!globalThis.__cgwWrapperComposer) return;
        var mounted = getMounted();
        var current = findComposerAnchor();
        if (needsRemount(mounted, current)) {
          scheduleMount();
          return;
        }
        if (current && current.node && mounted) {
          relocateNativeComposerChrome(current.node, mounted);
        }
      }
    );
  }

  function teardownDomSubscription() {
    if (domUnsubscribe) {
      domUnsubscribe();
      domUnsubscribe = null;
    } else if (kernel && kernel.dom) {
      kernel.dom.unsubscribe("play-compose-mount");
    }
  }

  function startMountPoll() {
    stopMountPoll();
    if (getMounted()) return;
    var attempts = 0;
    mountPollTimer = setInterval(function () {
      if (!globalThis.__cgwWrapperComposer) {
        stopMountPoll();
        return;
      }
      mountWrapperComposer();
      attempts++;
      if (attempts >= 20 || getMounted()) stopMountPoll();
    }, 150);
  }

  function stopMountPoll() {
    if (mountPollTimer) {
      clearInterval(mountPollTimer);
      mountPollTimer = null;
    }
  }

  function sendIconSvg() {
    return (
      '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">' +
      '<path d="M12 3.5l7 7.5h-4.5V20h-5v-8.5H5l7-8z" fill="currentColor"/>' +
      "</svg>"
    );
  }

  function resizeInput(input) {
    if (!input) return;
    input.style.height = "auto";
    var next = Math.min(input.scrollHeight, 200);
    input.style.height = Math.max(24, next) + "px";
  }

  function getMounted() {
    if (mountedRoot && mountedRoot.isConnected) return mountedRoot;
    var byId = document.getElementById("cgw-play-composer-root");
    if (byId) {
      mountedRoot = byId;
      return byId;
    }
    return mountedRoot;
  }

  function getComposeInput() {
    var root = getMounted();
    return root ? root.querySelector(".cgw-compose-input") : null;
  }

  function readInputText() {
    var input = getComposeInput();
    return input ? (input.value || "").trim() : "";
  }

  function isNativeComposerElement(node) {
    var cd = composer();
    if (cd && cd.isInsideWrapper(node)) return false;
    if (!node || !node.closest) return false;
    if (node.closest("#cgw-play-composer-root")) return false;
    if (
      node.closest('[data-testid="composer"]') ||
      node.closest("form:has(#prompt-textarea)") ||
      node.closest("#cgw-native-composer-offscreen")
    ) {
      if (node.id === "prompt-textarea") return true;
      if (node.closest('[data-testid="composer-text-input"]')) return true;
      if (node.closest('div.ProseMirror[contenteditable="true"]')) return true;
      return node.isContentEditable || node.tagName === "TEXTAREA";
    }
    return false;
  }

  function isolateNativeComposerFocus() {
    var scopedSelectors = [
      '[data-testid="composer"] #prompt-textarea',
      '[data-testid="composer"] [data-testid="composer-text-input"]',
      'form:has(#prompt-textarea) #prompt-textarea',
      '#cgw-native-composer-offscreen #prompt-textarea',
      '[data-testid="composer"] div.ProseMirror[contenteditable="true"]',
      '#cgw-native-composer-offscreen div.ProseMirror[contenteditable="true"]',
    ];
    scopedSelectors.forEach(function (sel) {
      document.querySelectorAll(sel).forEach(function (el) {
        if (el.closest("#cgw-play-composer-root")) return;
        try {
          el.tabIndex = -1;
          el.setAttribute("aria-hidden", "true");
        } catch (_e) {
          /* ignore */
        }
      });
    });
  }

  function ensureEnterGuard() {
    if (enterGuardBound) return;
    enterGuardBound = true;
    document.addEventListener(
      "keydown",
      function (ev) {
        if (!globalThis.__cgwWrapperComposer) return;
        if (globalThis.__cgwBridgeAutomationActive) return;
        if (ev.key !== "Enter" || ev.shiftKey || ev.isComposing) return;

        var state = globalThis.__cgwPlayComposeState || {};
        var fromWrapper =
          ev.target && ev.target.closest && ev.target.closest("#cgw-play-composer-root");
        var fromNative = isNativeComposerElement(ev.target);

        if (!fromWrapper && !fromNative) return;

        if (fromNative) {
          ev.preventDefault();
          ev.stopPropagation();
          ev.stopImmediatePropagation();
          if (!state.busy && !sendInFlight) requestComposeFocus("native-enter");
          return;
        }

        if (state.busy || sendInFlight || globalThis.__cgwComposeSendInFlight) {
          ev.preventDefault();
          ev.stopPropagation();
          ev.stopImmediatePropagation();
          return;
        }

        var root = getMounted();
        var sendBtn = root && root.querySelector(".cgw-compose-send");
        if (sendBtn && sendBtn.disabled) {
          ev.preventDefault();
          ev.stopPropagation();
          ev.stopImmediatePropagation();
        }
      },
      true
    );
  }

  function ensureNativeFocusGuard() {
    if (nativeFocusGuardBound) return;
    nativeFocusGuardBound = true;
    document.addEventListener(
      "focusin",
      function (ev) {
        if (!globalThis.__cgwWrapperComposer) return;
        if (globalThis.__cgwBridgeAutomationActive) return;
        var state = globalThis.__cgwPlayComposeState || {};
        if (state.busy) return;
        if (!isNativeComposerElement(ev.target)) return;
        requestComposeFocus("native-steal");
      },
      true
    );
  }

  function unmountWrapperComposer() {
    stopMountPoll();
    var root = getMounted();
    if (root) root.remove();
    mountedRoot = null;
    restoreNativeFromOffscreen();
  }

  function releaseSendLock() {
    sendInFlight = false;
    globalThis.__cgwComposeSendInFlight = false;
    if (sendLockTimer) {
      clearTimeout(sendLockTimer);
      sendLockTimer = null;
    }
    var sendBtn = getMounted()?.querySelector(".cgw-compose-send");
    var state = globalThis.__cgwPlayComposeState || {};
    if (sendBtn) sendBtn.disabled = !!state.busy || sendInFlight;
  }

  function armSendLockTimeout() {
    if (sendLockTimer) clearTimeout(sendLockTimer);
    sendLockTimer = setTimeout(function () {
      sendLockTimer = null;
      if (!sendInFlight && !globalThis.__cgwComposeSendInFlight) return;
      var state = globalThis.__cgwPlayComposeState || {};
      if (state.busy) return;
      releaseSendLock();
    }, 12000);
  }

  function cancelFocusRetries() {
    if (focusRetryTimer) {
      clearTimeout(focusRetryTimer);
      focusRetryTimer = null;
    }
  }

  function blurNativeComposerFocus() {
    var active = document.activeElement;
    if (!active || !active.closest) return;
    if (active.closest("#cgw-play-composer-root")) return;
    if (!isNativeComposerElement(active)) return;
    try {
      active.blur();
    } catch (_e) {
      /* ignore */
    }
  }

  function placeCaretAtEnd(input) {
    if (!input) return;
    var len = (input.value || "").length;
    try {
      input.setSelectionRange(len, len);
    } catch (_e) {
      /* ignore */
    }
  }

  function focusComposeInput() {
    var root = getMounted();
    if (!root) return false;
    var input = root.querySelector(".cgw-compose-input");
    if (!input) return false;

    var state = globalThis.__cgwPlayComposeState || {};
    if (state.busy) return false;

    var sendBtn = root.querySelector(".cgw-compose-send");
    if (sendBtn) sendBtn.disabled = !!state.busy || sendInFlight;

    blurNativeComposerFocus();

    try {
      input.focus({ preventScroll: true });
    } catch (_focus) {
      try {
        input.focus();
      } catch (_e2) {
        return false;
      }
    }

    if (document.activeElement === input) {
      placeCaretAtEnd(input);
      return true;
    }
    return false;
  }

  function scheduleFocusAttempt(delayMs) {
    cancelFocusRetries();
    focusRetryTimer = setTimeout(function () {
      focusRetryTimer = null;
      ensureFocused();
    }, delayMs);
  }

  function ensureFocused() {
    if (!focusWanted) return;
    if (!globalThis.__cgwWrapperComposer) {
      focusWanted = false;
      return;
    }

    var state = globalThis.__cgwPlayComposeState || {};
    if (state.busy) {
      scheduleFocusAttempt(80);
      return;
    }

    if (focusComposeInput()) {
      focusWanted = false;
      cancelFocusRetries();
      return;
    }

    if (Date.now() - focusStartedAt >= FOCUS_MAX_MS) {
      focusWanted = false;
      cancelFocusRetries();
      return;
    }

    scheduleFocusAttempt(60);
  }

  function requestComposeFocus(_reason) {
    focusWanted = true;
    focusStartedAt = Date.now();
    cancelFocusRetries();
    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        ensureFocused();
      });
    });
  }

  function triggerSend(input, sendBtn) {
    if (sendInFlight || globalThis.__cgwComposeSendInFlight) {
      sendLog("warn", "compose_send_blocked", "Send blocked while in flight", {
        sendInFlight: sendInFlight,
        globalInFlight: !!globalThis.__cgwComposeSendInFlight,
      });
      return;
    }
    if (!input || !sendBtn || sendBtn.disabled) {
      sendLog("warn", "compose_send_blocked", "Send blocked by disabled controls", {
        hasInput: !!input,
        hasSendBtn: !!sendBtn,
        sendDisabled: sendBtn ? sendBtn.disabled : null,
      });
      return;
    }
    var text = (input.value || "").trim();
    if (!text) {
      sendLog("debug", "compose_send_empty", "Send ignored for empty text");
      return;
    }

    sendLog("info", "compose_send_start", "Wrapper composer send triggered", {
      textLength: text.length,
      preview: text.length > 120 ? text.slice(0, 120) + "…" : text,
    });

    sendInFlight = true;
    globalThis.__cgwComposeSendInFlight = true;
    sendBtn.disabled = true;
    armSendLockTimeout();

    postToHost({ type: "cgwComposeInput", text: text });
    postToHost({ type: "cgwComposeSend", text: text });

    sendLog("info", "compose_send_posted", "Posted compose send messages to host", {
      textLength: text.length,
    });

    input.value = "";
    resizeInput(input);
    if (inputDebounce) {
      clearTimeout(inputDebounce);
      inputDebounce = null;
    }

    requestComposeFocus("after-send");
  }

  function buildComposerDom() {
    var root = document.createElement("div");
    root.id = "cgw-play-composer-root";

    root.innerHTML =
      '<div class="cgw-compose-wrap">' +
      '<div class="cgw-compose-shell">' +
      '<div class="cgw-compose-main">' +
      '<textarea class="cgw-compose-input" rows="1" aria-label="Message ChatGPT" placeholder="Message ChatGPT"></textarea>' +
      '<button type="button" class="cgw-compose-send" aria-label="Send message" title="Send (Enter)">' +
      sendIconSvg() +
      "</button>" +
      "</div>" +
      '<div class="cgw-compose-footer" aria-live="polite"></div>' +
      "</div>" +
      "</div>";

    var input = root.querySelector(".cgw-compose-input");
    var sendBtn = root.querySelector(".cgw-compose-send");

    function notifyInput() {
      if (inputDebounce) clearTimeout(inputDebounce);
      inputDebounce = setTimeout(function () {
        postToHost({ type: "cgwComposeInput", text: input.value || "" });
      }, 120);
    }

    input.addEventListener("input", function () {
      resizeInput(input);
      notifyInput();
    });

    input.addEventListener("keydown", function (ev) {
      if (ev.key !== "Enter" || ev.shiftKey || ev.isComposing) return;
      ev.preventDefault();
      ev.stopPropagation();
      var state = globalThis.__cgwPlayComposeState || {};
      if (state.busy || sendInFlight || sendBtn.disabled) return;
      if (!(input.value || "").trim()) return;
      triggerSend(input, sendBtn);
    });

    sendBtn.addEventListener("click", function (ev) {
      ev.preventDefault();
      triggerSend(input, sendBtn);
    });

    mountedRoot = root;
    return root;
  }

  function needsRemount(mounted, anchorInfo) {
    if (!mounted) return true;
    if (!mounted.isConnected) return true;
    if (!anchorInfo || !anchorInfo.node) return false;
    if (!anchorInfo.node.contains(mounted)) return true;
    var host = mounted.closest('[data-testid="composer"]');
    if (host && host !== anchorInfo.node) return true;
    return false;
  }

  function mountWrapperComposer() {
    if (!globalThis.__cgwWrapperComposer) return;

    var anchorInfo = findComposerAnchor();
    if (!anchorInfo || !anchorInfo.node) return;

    var anchor = anchorInfo.node;
    var existing = getMounted();

    if (existing && anchor.contains(existing)) {
      if (anchorInfo.mode === "fixed") {
        existing.classList.add("cgw-compose-fixed");
      } else {
        existing.classList.remove("cgw-compose-fixed");
      }
      relocateNativeComposerChrome(anchor, existing);
      isolateNativeComposerFocus();
      return;
    }

    var root = existing;
    if (!root) {
      root = buildComposerDom();
    }

    if (anchorInfo.mode === "fixed") {
      root.classList.add("cgw-compose-fixed");
    } else {
      root.classList.remove("cgw-compose-fixed");
    }

    if (root.parentElement !== anchor) {
      anchor.insertBefore(root, anchor.firstChild);
    }
    mountedRoot = root;

    paintComposeDomFromState(globalThis.__cgwPlayComposeState || {}, {});
    relocateNativeComposerChrome(anchor, root);
    isolateNativeComposerFocus();
    if (focusWanted) requestComposeFocus("remount");
    ensureDomSubscription();
  }

  function scheduleMount() {
    if (!globalThis.__cgwWrapperComposer) return;
    mountWrapperComposer();
    if (getMounted()) return;
    setTimeout(mountWrapperComposer, 0);
    setTimeout(mountWrapperComposer, 80);
    setTimeout(mountWrapperComposer, 250);
    startMountPoll();
  }

  function paintComposeDomFromState(state, patch) {
    if (!state || typeof state !== "object") return;
    var root = getMounted();
    if (!root) return;

    var input = root.querySelector(".cgw-compose-input");
    var sendBtn = root.querySelector(".cgw-compose-send");
    var footer = root.querySelector(".cgw-compose-footer");

    patch = patch || {};
    if (
      input &&
      (Object.prototype.hasOwnProperty.call(patch, "text") || patch.clear)
    ) {
      input.value = typeof state.text === "string" ? state.text : "";
      resizeInput(input);
    }

    if (typeof state.placeholder === "string" && input) {
      input.placeholder = state.placeholder;
    }

    var busy = !!state.busy;
    root.classList.toggle("cgw-compose-busy", busy);
    if (sendBtn) sendBtn.disabled = busy || sendInFlight;

    if (typeof state.status === "string" && footer) {
      footer.textContent = state.status;
    }
  }

  function applyComposeState(patch) {
    if (!patch || typeof patch !== "object") return;

    var prev = globalThis.__cgwPlayComposeState || {};
    var next = Object.assign({}, prev, patch);
    if (patch.clear) {
      next.text = "";
      delete next.clear;
    }
    if (patch.busy === false) {
      sendLog("info", "compose_busy_false", "Compose busy released by host", {
        status: next.status || null,
      });
      releaseSendLock();
    }
    if (patch.busy === true) {
      sendLog("debug", "compose_busy_true", "Compose busy set by host", {
        status: next.status || null,
      });
      sendInFlight = false;
      globalThis.__cgwComposeSendInFlight = false;
      if (sendLockTimer) {
        clearTimeout(sendLockTimer);
        sendLockTimer = null;
      }
      cancelFocusRetries();
    }
    globalThis.__cgwPlayComposeState = next;

    paintComposeDomFromState(next, patch);

    if (patch.clear) {
      if (inputDebounce) {
        clearTimeout(inputDebounce);
        inputDebounce = null;
      }
      postToHost({ type: "cgwComposeInput", text: "" });
    }

    if (patch.busy === true) {
      return;
    }

    if (patch.focus || patch.clear || patch.busy === false) {
      requestComposeFocus("state");
    }
  }

  if (kernel && kernel.features) {
    kernel.features.register("play-compose", {
      onActivate: function () {
        ensureNativeFocusGuard();
        ensureEnterGuard();
        ensureDomSubscription();
      },
      onDeactivate: unmountWrapperComposer,
    });
  }

  globalThis.__cgwPlayComposeApplyState = applyComposeState;
  globalThis.__cgwPlayComposeGetText = readInputText;
  globalThis.__cgwPlayComposeRequestFocus = requestComposeFocus;
  globalThis.__cgwPlayComposeScheduleMount = scheduleMount;
  globalThis.__cgwPlayComposeUnmount = unmountWrapperComposer;
  globalThis.__cgwPlayComposeVersion = COMPOSE_VERSION;
  globalThis.__cgwSetWrapperComposer = localSetWrapperComposer;

  if (globalThis.__cgwWrapperComposer) {
    localSetWrapperComposer(true);
  }
})();
