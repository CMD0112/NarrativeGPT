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
    if (typeof globalThis.__cgwSetTranscriptViewMode === "function") {
      globalThis.__cgwSetTranscriptViewMode(
        globalThis.__cgwTranscriptViewMode ||
          (globalThis.__cgwContinuousViewEnabled ? "continuous" : "native")
      );
    } else if (typeof globalThis.__cgwSetContinuousView === "function") {
      globalThis.__cgwSetContinuousView(!!globalThis.__cgwContinuousViewEnabled);
    } else if (globalThis.__cgwContinuousViewEnabled) {
      if (typeof globalThis.__cgwContinuousViewNavigate === "function") {
        globalThis.__cgwContinuousViewNavigate();
      } else if (typeof globalThis.__cgwContinuousViewSchedule === "function") {
        globalThis.__cgwContinuousViewSchedule();
      }
    }
    return;
  }
  globalThis.__cgwContinuousViewBooted = true;

  if (globalThis.__cgwContinuousViewEnabled === undefined) {
    globalThis.__cgwContinuousViewEnabled = false;
  }

  if (globalThis.__cgwTranscriptViewMode === undefined) {
    globalThis.__cgwTranscriptViewMode = globalThis.__cgwContinuousViewEnabled
      ? "continuous"
      : "native";
  }

  function normalizeTranscriptViewMode(mode) {
    var m = String(mode || "native").toLowerCase();
    if (m === "continuous" || m === "weave") return m;
    return "native";
  }

  function getTranscriptViewMode() {
    return normalizeTranscriptViewMode(globalThis.__cgwTranscriptViewMode);
  }

  function isOverlayTranscriptMode() {
    return getTranscriptViewMode() !== "native";
  }

  function isContinuousTranscriptMode() {
    return getTranscriptViewMode() === "continuous";
  }

  function syncTranscriptViewDom(mode) {
    var root = document.documentElement;
    if (!root) return;
    root.setAttribute("data-cgw-transcript-mode", mode);
    if (mode === "native") {
      root.removeAttribute("data-cgw-continuous-view");
    } else {
      root.setAttribute("data-cgw-continuous-view", "1");
    }
  }

  var transcriptRenderers = Object.create(null);

  function registerTranscriptRenderer(id, renderer) {
    if (!id || !renderer) return;
    transcriptRenderers[id] = renderer;
  }

  globalThis.__cgwRegisterTranscriptRenderer = registerTranscriptRenderer;

  if (globalThis.__cgwHideAssistantEditArtifacts === undefined) {
    globalThis.__cgwHideAssistantEditArtifacts = false;
  }

  var REVISION_PROMPT_PREFIX = "For play turn ";
  var REVISION_HIDE_STORAGE_PREFIX = "cgw-revision-hide:";
  var UTILITY_HIDE_STORAGE_PREFIX = "cgw-utility-hide:";
  var UTILITY_TAG_MARKER = "[[cgw:utility";
  var UTILITY_RESPONSE_TAG_MARKER = "[[cgw:utility-response";
  var STICK_TO_BOTTOM_THRESHOLD_PX = 48;
  var userDetachedFromBottom = false;
  var preferBottomOnNextApply = false;

  function markPreferBottomScroll() {
    preferBottomOnNextApply = true;
    userDetachedFromBottom = false;
  }
  var containerScrollIntentTarget = null;
  var containerScrollIntentHandler = null;

  var CONTAINER_ID = "cgw-continuous-view";
  var SCROLL_ANCHOR_ID = "cgw-cv-scroll-anchor";
  var TRANSITION_SHELL_ID = "cgw-cv-transition-shell";
  var STYLE_ID = "cgw-continuous-view-css";
  var CONTEXT_MENU_ID = "cgw-continuous-context-menu";
  var SURROGATE_EDIT_PANEL_ID = "cgw-surrogate-edit-panel";
  var SURROGATE_EDIT_BACKDROP_ID = "cgw-surrogate-edit-backdrop";
  var SUPPRESS_CLASS = "cgw-turn-suppressed";
  var SCROLL_HOST_CLASS = "cgw-transcript-scroll-host";
  var PEEK_TARGET_CLASS = "cgw-continuous-peek-target";
  var INTERACTIVE_SEGMENT_CLASS = "cgw-continuous-segment--interactive";
  var DEBOUNCE_MS = 350;
  var STREAM_DEBOUNCE_MS = 0;
  var NAV_FAST_DEBOUNCE_MS = 0;
  var APPLY_RETRY_DELAYS = [50, 100, 200, 400, 800];
  var MAX_APPLY_RETRY_ATTEMPTS = 8;
  var TRANSITION_PHASE_ACTIVE = "active";
  var TRANSITION_PHASE_TRANSITIONING = "transitioning";
  var DOM_READY_MAX_MS = 600;
  var TRANSITION_MIN_HOLD_MS = 32;
  var HREF_POLL_MS = 250;
  var HREF_POLL_DURATION_MS = 3000;
  var PEEK_TIMEOUT_MS = 60000;
  var NATIVE_EDIT_TIMEOUT_MS = 5000;

  var contextMenuState = { segment: null, turnId: null, role: null, open: false };
  var surrogateEditState = {
    open: false,
    turnId: null,
    segment: null,
    initialText: "",
    submitting: false,
    role: "assistant",
  };
  var peekState = {
    turnId: null,
    wrap: null,
    observer: null,
    docObserver: null,
    timeoutId: null,
    clickHandler: null,
    pendingInvalidation: null,
    actionKind: null,
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
  var activeConversationKey = null;
  var cachedScrollHostForKey = null;
  var scrollHostResizeObserver = null;
  var scrollHostResizeObserveTarget = null;
  var scrollHostResizeObserveContainer = null;
  var overlayGeometrySyncTimer = null;
  var overlayGeometrySyncPending = null;
  var userScrollAnchor = null;
  var userScrollAnchorAt = 0;
  var lastResizeBottomInset = null;
  var overlayGeometryPinnedHost = null;
  var scrollHostScrollLockHandler = null;
  var scrollHostScrollLockTarget = null;
  var containerScrollClampHandler = null;
  var containerScrollClampTarget = null;
  var applyRetryAttempts = 0;
  var transitionPhase = null;
  var targetConversationKey = null;
  var hrefPollTimer = null;
  var hrefPollUntil = 0;
  var lastPolledHref = null;
  var domReadyQuietTimer = null;
  var domReadyPendingCallback = null;
  var domReadyLastMutation = 0;
  var turnExtractCache = {};
  var MAX_TURN_EXTRACT_CACHE = 128;
  var packetContextExpandState = new Map();
  globalThis.__cgwPacketContextExpandState = packetContextExpandState;
  var containerResizeObserver = null;
  var composerClearanceUnsubscribe = null;
  var overlayViewportHandler = null;
  var scrollHostWheelHandler = null;
  var scrollHostWheelTarget = null;
  var wasNativeStreaming = false;
  var streamApplyQueued = false;
  var streamApplyFrameId = null;

  function invalidateTurnExtractCache() {
    turnExtractCache = {};
  }

  function stripTurnIdsFromDom() {
    document.querySelectorAll("[data-cgw-turn-id]").forEach(function (el) {
      if (el.id === CONTAINER_ID) return;
      el.removeAttribute("data-cgw-turn-id");
    });
  }

  function disconnectScrollHostResizeObserver() {
    if (scrollHostResizeObserver) {
      scrollHostResizeObserver.disconnect();
      scrollHostResizeObserver = null;
    }
    scrollHostResizeObserveTarget = null;
    scrollHostResizeObserveContainer = null;
  }

  function isContinuousScrollingActive() {
    return document.documentElement.hasAttribute("data-cgw-continuous-scrolling");
  }

  function scheduleOverlayGeometrySync(scrollHost, container, opts) {
    if (!scrollHost || !container) return;
    overlayGeometrySyncPending = {
      scrollHost: scrollHost,
      container: container,
      opts: opts || {},
    };
    if (overlayGeometrySyncTimer != null) return;
    overlayGeometrySyncTimer = setTimeout(function () {
      overlayGeometrySyncTimer = null;
      var pending = overlayGeometrySyncPending;
      overlayGeometrySyncPending = null;
      if (!pending || !globalThis.__cgwContinuousViewEnabled) return;
      syncOverlayGeometry(pending.scrollHost, pending.container, pending.opts);
    }, 16);
  }

  function disconnectOverlayViewportWatcher() {
    if (!overlayViewportHandler) return;
    if (window.visualViewport) {
      window.visualViewport.removeEventListener("resize", overlayViewportHandler);
      window.visualViewport.removeEventListener("scroll", overlayViewportHandler);
    }
    window.removeEventListener("resize", overlayViewportHandler);
    overlayViewportHandler = null;
  }

  function maxScrollTop(surface) {
    if (!surface) return 0;
    return Math.max(0, surface.scrollHeight - surface.clientHeight);
  }

  function ensureOverlayInScrollHost(scrollHost, container) {
    if (!scrollHost || !container) return false;
    if (container.parentElement === scrollHost) return false;
    scrollHost.appendChild(container);
    return true;
  }

  var diagScrollSkipLast = Object.create(null);

  function diagScroll(eventName, message, data) {
    if (!globalThis.__cgwExtendedDiagnostics) return;
    var k = globalThis.__cgwPageKernel;
    if (k && k.bus && typeof k.bus.diagnosticsLog === "function") {
      k.bus.diagnosticsLog("debug", eventName, message, data, "continuous-view", "navigation");
    }
  }

  function diagScrollSkip(reason, data) {
    if (!globalThis.__cgwExtendedDiagnostics) return;
    if (!data || !data.deltaY) return;
    var now = Date.now();
    if (diagScrollSkipLast[reason] && now - diagScrollSkipLast[reason] < 400) return;
    diagScrollSkipLast[reason] = now;
    var payload = data || {};
    payload.reason = reason;
    diagScroll("scroll_wheel_skip", reason, payload);
  }

  function wrapperComposerConsumesWheel(target, e) {
    var root =
      target && target.closest
        ? target.closest("#cgw-play-composer-root")
        : null;
    if (!root) return false;
    var input = root.querySelector(".cgw-compose-input");
    if (!input || (target !== input && !input.contains(target))) return false;
    if (input.scrollHeight <= input.clientHeight + 2) return false;
    var atTop = input.scrollTop <= 0;
    var atBottom =
      input.scrollTop + input.clientHeight >= input.scrollHeight - 2;
    if (e.deltaY < 0 && !atTop) return true;
    if (e.deltaY > 0 && !atBottom) return true;
    return false;
  }

  function shouldForwardWheelToTranscript(target) {
    if (!target || !target.closest) return false;
    if (target.closest("#cgw-play-composer-root")) return true;
    if (target.closest("#cgw-continuous-view")) return true;
    if (target.closest(".cgw-transcript-scroll-host")) return true;
    if (globalThis.__cgwContinuousViewEnabled && isComposerElement(target)) {
      return true;
    }
    return false;
  }

  function applyWheelToContinuousSurface(surface, e, origin) {
    var max = maxScrollTop(surface);
    if (max <= 0) {
      diagScrollSkip("no_scroll_range", {
        deltaY: e.deltaY,
        origin: origin,
        scrollHeight: surface.scrollHeight,
        clientHeight: surface.clientHeight,
      });
      return false;
    }
    var before = surface.scrollTop;
    var after = Math.max(0, Math.min(max, before + e.deltaY));
    if (after === before) {
      diagScrollSkip("at_scroll_limit", {
        deltaY: e.deltaY,
        origin: origin,
        before: before,
        max: max,
      });
      return false;
    }
    surface.scrollTop = after;
    noteUserScrollTop(surface);
    if (!isNearBottom(surface)) {
      userDetachedFromBottom = true;
      preferBottomOnNextApply = false;
    }
    markContinuousScrolling();
    diagScroll(
      origin === "direct" ? "scroll_wheel_direct" : "scroll_wheel_forward",
      "Applied wheel to continuous view",
      {
        deltaY: e.deltaY,
        before: before,
        after: after,
        max: max,
        origin: origin,
        targetTag: e.target && e.target.tagName,
      }
    );
    e.preventDefault();
    e.stopPropagation();
    return true;
  }

  function bindScrollHostWheelForward(scrollHost, container) {
    if (!scrollHost || !container) return;
    if (
      scrollHostWheelTarget &&
      scrollHostWheelHandler &&
      document.contains(scrollHostWheelTarget)
    ) {
      return;
    }
    disconnectScrollHostWheelForward();
    scrollHostWheelTarget = scrollHost;
    scrollHostWheelHandler = function (e) {
      if (!globalThis.__cgwContinuousViewEnabled) {
        diagScrollSkip("continuous_disabled", { deltaY: e.deltaY });
        return;
      }
      var surface = document.getElementById(CONTAINER_ID);
      if (!surface) {
        diagScrollSkip("no_surface", { deltaY: e.deltaY });
        return;
      }
      if (wrapperComposerConsumesWheel(e.target, e)) {
        diagScrollSkip("compose_input", { deltaY: e.deltaY });
        return;
      }
      if (!shouldForwardWheelToTranscript(e.target)) {
        diagScrollSkip("outside_transcript_zone", {
          deltaY: e.deltaY,
          tag: e.target && e.target.tagName,
        });
        return;
      }
      var origin =
        surface === e.target || surface.contains(e.target) ? "direct" : "forward";
      applyWheelToContinuousSurface(surface, e, origin);
    };
    document.addEventListener("wheel", scrollHostWheelHandler, {
      passive: false,
      capture: true,
    });
    diagScroll("scroll_wheel_bound", "Continuous view wheel listener bound", {
      scrollHeight: container.scrollHeight,
      clientHeight: container.clientHeight,
      maxScroll: maxScrollTop(container),
      bottomInset: computeComposerBottomInset(scrollHost),
    });
  }

  function disconnectScrollHostWheelForward() {
    if (scrollHostWheelHandler) {
      document.removeEventListener("wheel", scrollHostWheelHandler, {
        capture: true,
      });
    }
    scrollHostWheelTarget = null;
    scrollHostWheelHandler = null;
  }

  function ensureOverlayViewportWatcher(scrollHost, container) {
    if (overlayViewportHandler) return;
    overlayViewportHandler = function () {
      if (!globalThis.__cgwContinuousViewEnabled) return;
      if (isContinuousScrollingActive()) return;
      var c = document.getElementById(CONTAINER_ID);
      var host = c ? resolveScrollHost(c) : scrollHost;
      if (!c || !host) return;
      var bottomInset = computeComposerBottomInset(host);
      if (userDetachedFromBottom && lastResizeBottomInset === bottomInset) return;
      scheduleOverlayGeometrySync(host, c, { preserveScroll: true });
    };
    if (window.visualViewport) {
      window.visualViewport.addEventListener("resize", overlayViewportHandler);
      window.visualViewport.addEventListener("scroll", overlayViewportHandler);
    }
    window.addEventListener("resize", overlayViewportHandler);
  }

  function disconnectScrollHostScrollLock() {
    if (scrollHostScrollLockTarget && scrollHostScrollLockHandler) {
      scrollHostScrollLockTarget.removeEventListener(
        "scroll",
        scrollHostScrollLockHandler
      );
    }
    scrollHostScrollLockTarget = null;
    scrollHostScrollLockHandler = null;
  }

  function isCvPending() {
    return document.documentElement.hasAttribute("data-cgw-cv-pending");
  }

  function resolveScrollHost(container) {
    if (
      cachedScrollHostForKey &&
      cachedScrollHostForKey.host &&
      document.contains(cachedScrollHostForKey.host)
    ) {
      return cachedScrollHostForKey.host;
    }
    if (
      container &&
      container.parentElement &&
      container.parentElement !== document.body
    ) {
      return container.parentElement;
    }
    return (
      document.querySelector("." + SCROLL_HOST_CLASS) ||
      document.querySelector("main")
    );
  }

  function getScrollSurface(scrollHost, container) {
    return container || scrollHost;
  }

  function clampScrollSurface(surface) {
    if (!surface) return;
    var max = maxScrollTop(surface);
    if (surface.scrollTop > max) surface.scrollTop = max;
    if (surface.scrollTop < 0) surface.scrollTop = 0;
  }

  function disconnectContainerScrollClamp() {
    if (containerScrollClampTarget && containerScrollClampHandler) {
      containerScrollClampTarget.removeEventListener(
        "scroll",
        containerScrollClampHandler
      );
    }
    containerScrollClampTarget = null;
    containerScrollClampHandler = null;
  }

  function disconnectContainerScrollIntent() {
    if (containerScrollIntentTarget && containerScrollIntentHandler) {
      containerScrollIntentTarget.removeEventListener(
        "scroll",
        containerScrollIntentHandler
      );
    }
    containerScrollIntentTarget = null;
    containerScrollIntentHandler = null;
  }

  var continuousScrollIdleTimer = null;

  function markContinuousScrolling() {
    document.documentElement.setAttribute("data-cgw-continuous-scrolling", "1");
    if (continuousScrollIdleTimer) clearTimeout(continuousScrollIdleTimer);
    continuousScrollIdleTimer = setTimeout(function () {
      continuousScrollIdleTimer = null;
      document.documentElement.removeAttribute("data-cgw-continuous-scrolling");
    }, 320);
  }

  function noteUserScrollTop(surface) {
    if (!surface || typeof surface.scrollTop !== "number") return;
    userScrollAnchor = surface.scrollTop;
    userScrollAnchorAt = Date.now();
  }

  function resolvePreservedScrollTop(scrollHost, container) {
    var saved = readScrollTop(scrollHost, container);
    if (
      userScrollAnchor != null &&
      userScrollAnchor > 8 &&
      saved < userScrollAnchor - 8 &&
      Date.now() - userScrollAnchorAt < 800
    ) {
      if (globalThis.__cgwExtendedDiagnostics) {
        diagScroll(
          "scroll_position_anchor_restore",
          "Restoring scroll from user anchor after geometry sync",
          { saved: saved, anchor: userScrollAnchor }
        );
      }
      return userScrollAnchor;
    }
    return saved;
  }

  function bindContainerScrollIntent(container) {
    if (!container) return;
    if (
      containerScrollIntentTarget === container &&
      containerScrollIntentHandler
    ) {
      return;
    }
    disconnectContainerScrollIntent();
    containerScrollIntentTarget = container;
    containerScrollIntentHandler = function () {
      if (!globalThis.__cgwContinuousViewEnabled) return;
      noteUserScrollTop(container);
      markContinuousScrolling();
      if (isNearBottom(container)) {
        userDetachedFromBottom = false;
      } else {
        userDetachedFromBottom = true;
        preferBottomOnNextApply = false;
      }
    };
    container.addEventListener("scroll", containerScrollIntentHandler, {
      passive: true,
    });
  }

  function shouldStickToBottom(scrollHost, container) {
    var surface = getScrollSurface(scrollHost, container);
    if (!surface) return false;
    if (userDetachedFromBottom) return false;
    if (preferBottomOnNextApply) return true;
    if (isNearBottom(surface)) return true;
    if (isNativeStreaming()) return true;
    if (transitionPhase === TRANSITION_PHASE_TRANSITIONING) return true;
    if (isCvPending()) return true;
    return false;
  }

  function bindContainerScrollClamp(container) {
    if (!container) return;
    if (
      containerScrollClampTarget === container &&
      containerScrollClampHandler
    ) {
      return;
    }
    disconnectContainerScrollClamp();
    containerScrollClampTarget = container;
    containerScrollClampHandler = function () {
      if (!globalThis.__cgwContinuousViewEnabled) return;
      clampScrollSurface(container);
    };
    container.addEventListener("scroll", containerScrollClampHandler, {
      passive: true,
    });
  }

  function readScrollTop(scrollHost, container) {
    var surface = getScrollSurface(scrollHost, container);
    return surface ? surface.scrollTop : 0;
  }

  function applyScrollSurface(scrollHost, container, scrollTop, stickToBottom) {
    var surface = getScrollSurface(scrollHost, container);
    if (!surface) return;
    if (stickToBottom) {
      surface.scrollTop = maxScrollTop(surface);
    } else if (typeof scrollTop === "number") {
      surface.scrollTop = Math.max(0, Math.min(maxScrollTop(surface), scrollTop));
    }
    clampScrollSurface(surface);
  }

  function scrollSurfaceNearBottom(scrollHost, container) {
    var surface = getScrollSurface(scrollHost, container);
    return isNearBottom(surface);
  }

  function bindScrollHostScrollLock(scrollHost) {
    if (!scrollHost || !isCvPending()) {
      disconnectScrollHostScrollLock();
      return;
    }
    if (scrollHostScrollLockTarget === scrollHost && scrollHostScrollLockHandler) {
      scrollHost.scrollTop = 0;
      return;
    }
    disconnectScrollHostScrollLock();
    scrollHostScrollLockTarget = scrollHost;
    scrollHostScrollLockHandler = function () {
      if (!globalThis.__cgwContinuousViewEnabled) return;
      if (scrollHost.scrollTop !== 0) scrollHost.scrollTop = 0;
    };
    scrollHost.addEventListener("scroll", scrollHostScrollLockHandler, {
      passive: true,
    });
    scrollHost.scrollTop = 0;
  }

  function removeTransitionShell() {
    var shell = document.getElementById(TRANSITION_SHELL_ID);
    if (shell) shell.remove();
  }

  function ensureTransitionShell(scrollHost) {
    var host = scrollHost || document.querySelector("main");
    if (!host) return null;
    var shell = document.getElementById(TRANSITION_SHELL_ID);
    if (!shell) {
      shell = document.createElement("div");
      shell.id = TRANSITION_SHELL_ID;
      shell.setAttribute("aria-hidden", "true");
    }
    if (shell.parentElement !== host) {
      host.appendChild(shell);
    }
    return shell;
  }

  function markProvisionalScrollHost() {
    var main = document.querySelector("main");
    if (!main) return null;
    if (main.classList.contains(SCROLL_HOST_CLASS)) {
      ensureTransitionShell(main);
      return main;
    }
    clearScrollHostMark();
    main.classList.add(SCROLL_HOST_CLASS);
    ensureTransitionShell(main);
    return main;
  }

  function setTransitionAttributes() {
    document.documentElement.setAttribute("data-cgw-continuous-view", "1");
    document.documentElement.setAttribute("data-cgw-cv-pending", "1");
  }

  function stopHrefPoll() {
    if (hrefPollTimer != null) {
      clearInterval(hrefPollTimer);
      hrefPollTimer = null;
    }
    hrefPollUntil = 0;
  }

  function startHrefPoll() {
    lastPolledHref = location.href;
    hrefPollUntil = Date.now() + HREF_POLL_DURATION_MS;
    if (hrefPollTimer != null) return;
    hrefPollTimer = setInterval(tickHrefPoll, HREF_POLL_MS);
  }

  function tickHrefPoll() {
    if (!globalThis.__cgwContinuousViewEnabled) {
      stopHrefPoll();
      return;
    }
    var href = location.href;
    if (href !== lastPolledHref) {
      lastPolledHref = href;
      var key = getConversationKey();
      if (key !== targetConversationKey) {
        enterConversationTransition(key, { force: true });
        schedule({ immediate: true });
      }
    }
    if (
      Date.now() > hrefPollUntil &&
      !document.documentElement.hasAttribute("data-cgw-cv-pending")
    ) {
      stopHrefPoll();
    }
  }

  function cancelDomReadyWait() {
    if (domReadyQuietTimer != null) {
      clearTimeout(domReadyQuietTimer);
      domReadyQuietTimer = null;
    }
    domReadyPendingCallback = null;
  }

  function isConversationKeyStable() {
    return getConversationKey() === targetConversationKey;
  }

  function isTranscriptDomReady() {
    if (!isConversationKeyStable()) return false;
    if (!isConversationUrl(location.href)) return false;
    if (transitionPhase !== TRANSITION_PHASE_TRANSITIONING) return true;
    var elapsed = Date.now() - domReadyLastMutation;
    if (elapsed < TRANSITION_MIN_HOLD_MS) return false;
    if (findTurnRoots().length > 0) return true;
    return elapsed >= 400;
  }

  function waitForTranscriptDomReady(callback) {
    if (isTranscriptDomReady()) {
      callback(true);
      return;
    }
    cancelDomReadyWait();
    domReadyPendingCallback = callback;
    var started = Date.now();

    function tick() {
      if (!domReadyPendingCallback) return;
      if (isTranscriptDomReady()) {
        var cb = domReadyPendingCallback;
        domReadyPendingCallback = null;
        cancelDomReadyWait();
        cb(true);
        return;
      }
      if (Date.now() - started >= DOM_READY_MAX_MS) {
        var done = domReadyPendingCallback;
        domReadyPendingCallback = null;
        cancelDomReadyWait();
        done(isConversationKeyStable());
        return;
      }
      domReadyQuietTimer = setTimeout(tick, 16);
    }

    domReadyQuietTimer = setTimeout(tick, TRANSITION_MIN_HOLD_MS);
  }

  function enterConversationTransition(targetKey, opts) {
    opts = opts || {};
    targetKey = targetKey != null ? targetKey : getConversationKey();
    if (
      !opts.force &&
      transitionPhase === TRANSITION_PHASE_TRANSITIONING &&
      targetKey === targetConversationKey
    ) {
      return false;
    }
    targetConversationKey = targetKey;
    activeConversationKey = targetKey;
    transitionPhase = TRANSITION_PHASE_TRANSITIONING;
    loadedConversationKey = null;

    invalidateTurnExtractCache();
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    globalThis.__cgwTurnRegistry = {};

    var existing = document.getElementById(CONTAINER_ID);
    if (existing) existing.remove();

    stripTurnIdsFromDom();
    nextTurnId = 0;

    updateStreamingStickObserver(null, null, false);
    disconnectScrollHostResizeObserver();
    disconnectScrollHostScrollLock();
    disconnectContainerScrollClamp();
    disconnectContainerScrollIntent();
    markPreferBottomScroll();
    userScrollAnchor = null;
    userScrollAnchorAt = 0;
    lastResizeBottomInset = null;
    overlayGeometryPinnedHost = null;
    cachedScrollHostForKey = null;
    applyRetryAttempts = 0;
    cancelDomReadyWait();
    domReadyLastMutation = Date.now();

    setTransitionAttributes();
    markProvisionalScrollHost();
    startHrefPoll();
    return true;
  }

  function noteConversationKeyChange() {
    var key = getConversationKey();
    if (
      key === activeConversationKey &&
      transitionPhase !== TRANSITION_PHASE_TRANSITIONING
    ) {
      return false;
    }
    if (
      key === targetConversationKey &&
      transitionPhase === TRANSITION_PHASE_TRANSITIONING
    ) {
      return false;
    }
    enterConversationTransition(key, { force: true });
    return true;
  }

  function trimTurnExtractCache(activeTurnIds) {
    var keys = Object.keys(turnExtractCache);
    if (keys.length <= MAX_TURN_EXTRACT_CACHE) return;
    var keep = {};
    activeTurnIds.forEach(function (id) {
      if (turnExtractCache[id]) keep[id] = turnExtractCache[id];
    });
    keys.forEach(function (id) {
      if (!keep[id] && Object.keys(keep).length < MAX_TURN_EXTRACT_CACHE) {
        keep[id] = turnExtractCache[id];
      }
    });
    turnExtractCache = keep;
  }

  function computeNativeTurnFingerprint(turn) {
    if (!turn) return "";
    var host =
      findUserContentHostIn(turn) ||
      turn.querySelector('.markdown, [class*="markdown"]') ||
      turn;
    var text = (host && (host.textContent || "")) || "";
    var len = text.length;
    var tail = len > 96 ? text.slice(len - 96) : text;
    var streaming = 0;
    if (isNativeStreaming()) {
      var turns = findTurnRoots();
      if (turns.length && turns[turns.length - 1] === turn) streaming = 1;
    }
    return len + "\x01" + tail + "\x01" + streaming;
  }

  function segmentHasPacketContextBlock(blocks) {
    if (!blocks || !blocks.length) return false;
    for (var i = 0; i < blocks.length; i++) {
      if (blocks[i].kind === "packetContext") return true;
    }
    return false;
  }

  function formatSettingsRevision() {
    var rev = globalThis.__cgwFormatSettingsRevision;
    return typeof rev === "number" ? rev : 0;
  }

  function turnExtractCacheKey(turnId, role) {
    return (
      String(turnId) +
      "\x00" +
      (role || "") +
      "\x00" +
      formatSettingsRevision() +
      "\x00" +
      (globalThis.__cgwShowContinuousImages === false ? "0" : "1") +
      "\x00" +
      (globalThis.__cgwHideContextTags === true ? "1" : "0") +
      "\x00" +
      (globalThis.__cgwExpandHiddenContext === false ? "0" : "1")
    );
  }

  function getRawTurnBlocks(turn, turnId, role) {
    var cacheKey = turnExtractCacheKey(turnId, role);
    var nativeFp = computeNativeTurnFingerprint(turn);
    var cached = turnExtractCache[cacheKey];

    if (cached && cached.nativeFp === nativeFp && cached.rawBlocks) {
      return cached.rawBlocks;
    }

    var rawBlocks = formatTurnToBlocks(turn);
    turnExtractCache[cacheKey] = {
      nativeFp: nativeFp,
      rawBlocks: rawBlocks,
      role: role,
    };
    return rawBlocks;
  }

  function extractTurnBlocks(turn, turnId, role, opts) {
    var rawBlocks = getRawTurnBlocks(turn, turnId, role);
    var blocks = rawBlocks;

    if (
      typeof globalThis.__cgwPacketDisplay === "object" &&
      globalThis.__cgwPacketDisplay &&
      typeof globalThis.__cgwPacketDisplay.transformUserBlocks === "function"
    ) {
      blocks =
        globalThis.__cgwPacketDisplay.transformUserBlocks(turn, rawBlocks, role) ||
        rawBlocks;
    }

    return decorateTurnBlocks(blocks, opts);
  }

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
    (document.head || document.documentElement).appendChild(el);
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

  function dedupeTurnRootsByWrapper(turns) {
    if (!turns || !turns.length) return turns;
    var seenByWrap = new Map();
    var out = [];
    turns.forEach(function (turn) {
      var wrap = turnWrapper(turn) || turn;
      var role = getTurnRole(turn);
      if (!seenByWrap.has(wrap)) seenByWrap.set(wrap, new Set());
      var roles = seenByWrap.get(wrap);
      if (roles.has(role)) return;
      roles.add(role);
      out.push(turn);
    });
    return out;
  }

  function findTurnRoots() {
    var byRole = Array.prototype.slice.call(
      document.querySelectorAll(
        '[data-message-author-role="user"], [data-message-author-role="assistant"]'
      )
    );
    if (byRole.length) {
      return sortTurnRootsByOrdinal(dedupeTurnRootsByWrapper(byRole));
    }

    var byTestId = Array.prototype.slice.call(
      document.querySelectorAll('[data-testid^="conversation-turn-"]')
    );
    if (byTestId.length) {
      return sortTurnRootsByOrdinal(dedupeTurnRootsByWrapper(byTestId));
    }

    return sortTurnRootsByOrdinal(dedupeTurnRootsByWrapper(findLeafTurnGroups()));
  }

  function compareTurnRootsDocumentOrder(a, b) {
    if (a === b) return 0;
    var pos = a.compareDocumentPosition(b);
    if (pos & Node.DOCUMENT_POSITION_FOLLOWING) return -1;
    if (pos & Node.DOCUMENT_POSITION_PRECEDING) return 1;
    return 0;
  }

  function turnsAreRoleGrouped(turns) {
    if (!turns || turns.length < 2) return false;
    var lastUserIdx = -1;
    var firstAssistantIdx = turns.length;
    for (var i = 0; i < turns.length; i++) {
      var role = getTurnRole(turns[i]);
      if (role === "user") lastUserIdx = i;
      if (role === "assistant" && firstAssistantIdx === turns.length) firstAssistantIdx = i;
    }
    return lastUserIdx >= 0 && firstAssistantIdx < turns.length && lastUserIdx > firstAssistantIdx;
  }

  function interleaveGroupedTurnRoots(turns) {
    var users = [];
    var assistants = [];
    var other = [];
    turns.forEach(function (turn) {
      var role = getTurnRole(turn);
      if (role === "user") users.push(turn);
      else if (role === "assistant") assistants.push(turn);
      else other.push(turn);
    });
    var out = [];
    var n = Math.max(users.length, assistants.length);
    for (var i = 0; i < n; i++) {
      if (i < users.length) out.push(users[i]);
      if (i < assistants.length) out.push(assistants[i]);
    }
    return out.concat(other);
  }

  function resolveDomOrdinal(turn, docIndex) {
    var map = globalThis.__cgwThreadOrdinalMap;
    if (!map || typeof map !== "object") return docIndex;
    var domKey = "dom:" + String(docIndex + 1);
    if (map[domKey] != null) return map[domKey];
    return docIndex;
  }

  function assignTurnIdsInDocumentOrder(turns) {
    if (!turns || !turns.length) return;
    turns
      .slice()
      .sort(compareTurnRootsDocumentOrder)
      .forEach(function (turn) {
        var wrap = turnWrapper(turn);
        if (wrap && !wrap.getAttribute("data-cgw-turn-id")) {
          getOrAssignTurnId(wrap);
        }
      });
  }

  function sortTurnRootsByOrdinal(turns) {
    if (!turns || !turns.length) return turns;
    assignTurnIdsInDocumentOrder(turns);
    var ordered = turns.slice();
    if (turnsAreRoleGrouped(ordered)) {
      ordered = interleaveGroupedTurnRoots(ordered);
    }
    var map = globalThis.__cgwThreadOrdinalMap;
    if (map && typeof map === "object" && Object.keys(map).length > 0) {
      var ordinalFor = {};
      ordered.forEach(function (turn, idx) {
        ordinalFor[turn] = resolveDomOrdinal(turn, idx);
      });
      ordered.sort(function (a, b) {
        var oa = ordinalFor[a] != null ? ordinalFor[a] : 0;
        var ob = ordinalFor[b] != null ? ordinalFor[b] : 0;
        if (oa !== ob) return oa - ob;
        return compareTurnRootsDocumentOrder(a, b);
      });
    } else {
      ordered.sort(compareTurnRootsDocumentOrder);
    }
    return ordered;
  }

  function stripExpandCollapseControls(root) {
    if (!root || !root.querySelectorAll) return;
    root.querySelectorAll("button, [role='button'], a").forEach(function (el) {
      var label = (
        (el.getAttribute("aria-label") || "") +
        " " +
        (el.textContent || "")
      )
        .trim()
        .toLowerCase();
      if (label === "show more" || label === "show less") el.remove();
    });
    root.querySelectorAll("div, span, p").forEach(function (el) {
      if (el.children.length > 0) return;
      var t = (el.textContent || "").trim();
      if (t === "Show more" || t === "Show less") el.remove();
    });
  }

  function sanitizeExtractedMessageText(text) {
    if (!text) return "";
    var t = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    t = t.replace(/\s*Show more\s*Show less\s*/gi, " ");
    t = t.replace(/\s*Show more\s*/gi, " ");
    t = t.replace(/\s*Show less\s*/gi, " ");
    t = t.replace(/[\uE000-\uF8FF]/g, "");
    t = t.replace(/filecite[\w-]*/gi, "");
    return t.replace(/[ \t\f\v]{2,}/g, " ").trim();
  }

  globalThis.__cgwSanitizeExtractedMessageText = sanitizeExtractedMessageText;

  function stripChrome(root) {
    stripExpandCollapseControls(root);
    root
      .querySelectorAll(
        ".cgw-native-packet-display, [data-cgw-packet-display], " +
          ".cgw-packet-player, [data-cgw-packet-player]"
      )
      .forEach(function (el) {
        el.remove();
      });
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
    stripExpandCollapseControls(root);
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
      el.id === TRANSITION_SHELL_ID ||
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
          TRANSITION_SHELL_ID +
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

  function isPhraseHighlightNode(el) {
    if (!el || el.nodeType !== 1) return false;
    if (el.classList && el.classList.contains("cgw-phrase-highlight")) return true;
    if (typeof el.closest === "function") {
      return !!el.closest(".cgw-phrase-highlight");
    }
    return false;
  }

  function isSuppressedTurnNode(el) {
    if (!el || el.nodeType !== 1) return false;
    if (typeof el.closest === "function") {
      return !!el.closest("." + SUPPRESS_CLASS);
    }
    return false;
  }

  function isContinuousViewStableActive() {
    return (
      transitionPhase === TRANSITION_PHASE_ACTIVE &&
      document.documentElement.hasAttribute("data-cgw-continuous-view") &&
      !document.documentElement.hasAttribute("data-cgw-cv-pending") &&
      !!document.getElementById(CONTAINER_ID)
    );
  }

  function mutationShouldSchedule(mutations) {
    for (var i = 0; i < mutations.length; i++) {
      var m = mutations[i];
      if (isWrapperNode(m.target) || isPhraseHighlightNode(m.target)) continue;
      if (m.type === "attributes") {
        if (
          m.attributeName === "class" ||
          m.attributeName === "data-testid" ||
          m.attributeName === "data-message-author-role"
        ) {
          return true;
        }
        continue;
      }
      if (isSuppressedTurnNode(m.target) && !isNativeStreaming()) continue;
      var j;
      if (m.addedNodes) {
        for (j = 0; j < m.addedNodes.length; j++) {
          if (!isWrapperNode(m.addedNodes[j]) && !isPhraseHighlightNode(m.addedNodes[j]))
            return true;
        }
      }
      if (m.removedNodes) {
        for (j = 0; j < m.removedNodes.length; j++) {
          if (
            !isWrapperNode(m.removedNodes[j]) &&
            !isPhraseHighlightNode(m.removedNodes[j])
          )
            return true;
        }
      }
      if (
        !m.addedNodes &&
        !m.removedNodes &&
        !isWrapperNode(m.target) &&
        !isPhraseHighlightNode(m.target)
      )
        return true;
    }
    return false;
  }

  function bindTranscriptObserver(scrollHost) {
    var target = scrollHost || document.querySelector("main") || document.body;
    if (!transcriptObserver) {
      transcriptObserver = new MutationObserver(function (mutations) {
        if (!globalThis.__cgwContinuousViewEnabled) return;
        if (peekState.turnId || surrogateEditState.open) return;
        if (!mutationShouldSchedule(mutations)) return;
        if (document.documentElement.hasAttribute("data-cgw-cv-pending")) {
          applyRetryAttempts = 0;
        }
        schedule();
      });
    }
    if (mutationObsTarget === target) return;
    transcriptObserver.disconnect();
    mutationObsTarget = target;
    transcriptObserver.observe(target, {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ["class", "data-testid", "data-message-author-role"],
    });
  }

  function disconnectTranscriptObserver() {
    if (transcriptObserver) transcriptObserver.disconnect();
    mutationObsTarget = null;
  }

  function findUserContentHostIn(root) {
    if (!root) return null;
    var pd = globalThis.__cgwPacketDisplay;
    if (pd && typeof pd.findNativePlayerTextLeaf === "function") {
      var marked = pd.findNativePlayerTextLeaf(root);
      if (marked) return marked;
    }
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

  function currentSegmentPhraseHighlightFp() {
    return (
      (globalThis.__cgwPhraseHighlightStyleFp || "off") +
      phraseHighlightFingerprintSuffix()
    );
  }

  function segmentsNeedPhraseHighlightRefresh(container) {
    if (!container || !globalThis.__cgwPhraseHighlightsEnabled) return false;
    var curFp = currentSegmentPhraseHighlightFp();
    var segs = container.querySelectorAll(
      ".cgw-continuous-segment, .cgw-weave-body, .cgw-weave-embed"
    );
    for (var i = 0; i < segs.length; i++) {
      if (segs[i].getAttribute("data-cgw-streaming") === "1") continue;
      if (segs[i].getAttribute("data-cgw-phrase-hl-fp") !== curFp) return true;
    }
    return false;
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

  function getStreamingAssistantTurnId(turns) {
    if (!isNativeStreaming() || !turns || !turns.length) return null;
    var last = turns[turns.length - 1];
    if (getTurnRole(last) !== "assistant") return null;
    var wrap = turnWrapper(last);
    if (!wrap) return null;
    return wrap.getAttribute("data-cgw-turn-id") || getOrAssignTurnId(wrap);
  }

  function clearStreamingSegmentMarkers(container) {
    if (!container) return;
    container.querySelectorAll('[data-cgw-streaming="1"]').forEach(function (seg) {
      seg.removeAttribute("data-cgw-streaming");
    });
  }

  function noteStreamingLifecycle(container) {
    var streamingNow = isNativeStreaming();
    if (wasNativeStreaming && !streamingNow && container) {
      clearStreamingSegmentMarkers(container);
      finalizeContinuousViewFormatting(container, null);
      finalizePendingComposerRevision();
    }
    wasNativeStreaming = streamingNow;
  }

  function applyContainerScroll(scrollHost, container, scrollTop, stickToBottom) {
    applyScrollSurface(scrollHost, container, scrollTop, stickToBottom);
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
    if (
      segData.role === "user" &&
      segmentHasPacketContextBlock(segData.blocks)
    ) {
      seg.classList.add("cgw-has-packet-context");
      if (!isPacketContextUiVisible()) {
        seg.classList.add("cgw-continuous-segment--has-hidden-packet");
      }
    }
    return seg;
  }

  function isPacketContextUiVisible() {
    var pd = globalThis.__cgwPacketDisplay;
    if (pd && typeof pd.isPacketContextUiVisible === "function") {
      return pd.isPacketContextUiVisible();
    }
    return (
      document.documentElement.getAttribute("data-cgw-show-packet-context") ===
      "1"
    );
  }

  function syncPacketContextExpandState(segEl, turnId) {
    if (!segEl || !turnId) return;
    var details = segEl.querySelector(".cgw-continuous-packet-context__details");
    if (!details) return;
    if (packetContextExpandState.get(String(turnId))) {
      details.open = true;
    }
  }

  function decoratePhraseHighlights(root) {
    if (typeof globalThis.__cgwDecoratePhraseHighlightsInElement === "function") {
      globalThis.__cgwDecoratePhraseHighlightsInElement(root);
    }
  }

  function normalizeSegmentTypography(seg) {
    if (!seg) return;
    seg.normalize();
  }

  function scheduleReadingGuides(container) {
    if (typeof globalThis.__cgwScheduleReadingGuides === "function") {
      globalThis.__cgwScheduleReadingGuides(container);
    } else if (typeof globalThis.__cgwApplyReadingGuides === "function") {
      globalThis.__cgwApplyReadingGuides(container);
    }
  }

  function finalizeContinuousViewFormatting(container, changedTurnIds) {
    if (!container || !isOverlayTranscriptMode()) return;
    var changedSet = null;
    if (changedTurnIds && changedTurnIds.length) {
      changedSet = {};
      changedTurnIds.forEach(function (id) {
        changedSet[String(id)] = true;
      });
    }

    var mode = getTranscriptViewMode();
    if (mode === "weave") {
      container.querySelectorAll(".cgw-weave-body, .cgw-weave-embed").forEach(function (seg) {
        if (seg.getAttribute("data-cgw-streaming") === "1") return;
        var tid = seg.getAttribute("data-cgw-turn-id") || "";
        if (changedSet && tid && !changedSet[tid]) {
          var hlFpW = seg.getAttribute("data-cgw-phrase-hl-fp");
          var curFpW =
            (globalThis.__cgwPhraseHighlightStyleFp || "off") +
            phraseHighlightFingerprintSuffix();
          if (hlFpW === curFpW) return;
        }
        seg.normalize();
        decoratePhraseHighlights(seg);
        seg.setAttribute(
          "data-cgw-phrase-hl-fp",
          (globalThis.__cgwPhraseHighlightStyleFp || "off") +
            phraseHighlightFingerprintSuffix()
        );
      });
    } else {
      container.querySelectorAll(".cgw-continuous-segment").forEach(function (seg) {
        if (seg.getAttribute("data-cgw-streaming") === "1") return;
        var tid = seg.getAttribute("data-cgw-turn-id");
        if (changedSet && !changedSet[tid]) {
          var hlFp = seg.getAttribute("data-cgw-phrase-hl-fp");
          var curFp =
            (globalThis.__cgwPhraseHighlightStyleFp || "off") +
            phraseHighlightFingerprintSuffix();
          if (hlFp === curFp) return;
        }
        normalizeSegmentTypography(seg);
        decoratePhraseHighlights(seg);
        seg.setAttribute(
          "data-cgw-phrase-hl-fp",
          (globalThis.__cgwPhraseHighlightStyleFp || "off") +
            phraseHighlightFingerprintSuffix()
        );
      });
    }

    scheduleReadingGuides(container);
  }

  function fillSegmentBlocks(segEl, blocks) {
    while (segEl.firstChild) segEl.removeChild(segEl.firstChild);
    blocks.forEach(function (block) {
      appendRichBlock(segEl, block);
    });
  }

  function patchStreamingProseBlock(segEl, block) {
    if (!block || block.kind !== "prose" || !block.html) return false;
    var lastChild = segEl.lastElementChild;
    if (!lastChild) return false;
    if (
      !lastChild.classList.contains("cgw-continuous-prose") &&
      !lastChild.classList.contains("cgw-continuous-block")
    ) {
      return false;
    }
    if (lastChild.classList.contains("cgw-continuous-packet-context")) return false;
    if (lastChild.innerHTML === block.html) return true;
    lastChild.innerHTML = block.html;
    return true;
  }

  function updateSegmentBlocksIncremental(segEl, blocks, prevBlockFps, turnId, streamingPatch) {
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
        if (
          blocks[lastIdx].kind === "prose" &&
          patchStreamingProseBlock(segEl, blocks[lastIdx])
        ) {
          if (streamingPatch) segEl.setAttribute("data-cgw-streaming", "1");
          syncPacketContextExpandState(segEl, turnId);
          return true;
        }
        var lastChild = segEl.lastElementChild;
        if (lastChild) lastChild.remove();
        appendRichBlock(segEl, blocks[lastIdx]);
        syncPacketContextExpandState(segEl, turnId);
        return true;
      }
    }
    fillSegmentBlocks(segEl, blocks);
    syncPacketContextExpandState(segEl, turnId);
    return true;
  }

  function syncSegments(scrollHost, container, segments, scrollTop, stickToBottom) {
    var prevFps = globalThis.__cgwSegmentFingerprints || {};
    var prevBlockFps = globalThis.__cgwSegmentBlockFingerprints || {};
    var nextFps = {};
    var nextBlockFps = {};
    var changedTurnIds = [];
    var streamingPatch = isNativeStreaming();
    var existing = Array.prototype.slice.call(
      container.querySelectorAll(".cgw-continuous-segment")
    );
    function segmentOrderKey(segData) {
      return String(segData.turnId) + "\x01" + (segData.role || "");
    }

    function existingSegmentOrderKey(el) {
      return (
        (el.getAttribute("data-cgw-turn-id") || "") +
        "\x01" +
        (el.getAttribute("data-cgw-turn-role") || "")
      );
    }

    var canSync =
      existing.length > 0 &&
      segments.length >= existing.length &&
      existing.every(function (el, i) {
        return (
          i < segments.length &&
          existingSegmentOrderKey(el) === segmentOrderKey(segments[i])
        );
      });

    if (!canSync) {
      renderSegments(scrollHost, container, segments, scrollTop, stickToBottom);
      segments.forEach(function (s) {
        var tid = String(s.turnId);
        nextFps[tid] = segmentFingerprint(s);
        nextBlockFps[tid] = s.blocks.map(blockFingerprint);
        changedTurnIds.push(tid);
      });
      globalThis.__cgwSegmentFingerprints = nextFps;
      globalThis.__cgwSegmentBlockFingerprints = nextBlockFps;
      return changedTurnIds;
    }

    ensureScrollAnchor(container);

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
        syncPacketContextExpandState(el, tid);
        container.appendChild(el);
        changed = true;
        changedTurnIds.push(tid);
        return;
      }

      if (prevFps[tid] !== fp) {
        var blocksUpdated = updateSegmentBlocksIncremental(
          el,
          segData.blocks,
          prevBlockFps[tid],
          tid,
          streamingPatch &&
            i === segments.length - 1 &&
            segData.role === "assistant"
        );
        if (blocksUpdated) {
          changed = true;
          changedTurnIds.push(tid);
        } else {
          changedTurnIds.push(tid);
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
      applyScrollSurface(scrollHost, container, scrollTop, stickToBottom);
    } else if (typeof scrollTop === "number") {
      applyScrollSurface(scrollHost, container, scrollTop, false);
    }
    return changedTurnIds;
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

    var imgs = clone.querySelectorAll("img[src]");
    for (var im = 0; im < imgs.length; im++) {
      var src = imgs[im].getAttribute("src") || "";
      if (!src) continue;
      if (globalThis.__cgwShowContinuousImages !== false) {
        blocks.push({
          kind: "image",
          src: src,
          alt: imgs[im].getAttribute("alt") || "",
        });
      } else {
        var label =
          imgs[im].getAttribute("alt") ||
          imgs[im].getAttribute("title") ||
          "attachment";
        blocks.push({ kind: "prose", html: "<p>[" + label + "]</p>" });
      }
    }

        if (!blocks.length) {
      var userHost = findUserContentHostIn(clone);
      if (userHost) {
        var hostClone = userHost.cloneNode(true);
        stripChrome(hostClone);
        var hostText = sanitizeExtractedMessageText((hostClone.innerText || "").trim());
        var pdCollect = globalThis.__cgwPacketDisplay;
        if (
          pdCollect &&
          typeof pdCollect.collectNativeUserMessageText === "function"
        ) {
          var joined = pdCollect.collectNativeUserMessageText(turnRoot);
          if (joined) hostText = joined;
        }
        if (hostText && globalThis.__cgwHideContextTags === true) {
          if (hostText.indexOf("[[cgw:") >= 0) {
            if (typeof globalThis.__cgwStripContextTags === "function") {
              hostText = globalThis.__cgwStripContextTags(hostText);
            } else if (
              typeof globalThis.__cgwPacketDisplay === "object" &&
              globalThis.__cgwPacketDisplay &&
              typeof globalThis.__cgwPacketDisplay.parsePacket === "function"
            ) {
              var parsed = globalThis.__cgwPacketDisplay.parsePacket(hostText);
              if (parsed && parsed.userLine) hostText = parsed.userLine;
            }
          } else if (
            typeof globalThis.__cgwPacketDisplay === "object" &&
            globalThis.__cgwPacketDisplay &&
            typeof globalThis.__cgwPacketDisplay.isStructuredPreviewPacket ===
              "function" &&
            globalThis.__cgwPacketDisplay.isStructuredPreviewPacket(hostText)
          ) {
            var structured = globalThis.__cgwPacketDisplay.parseStructuredPreview(
              hostText
            );
            if (structured && structured.userLine) hostText = structured.userLine;
          }
          if (typeof globalThis.__cgwStripTrailingInjectionBlocks === "function") {
            hostText = globalThis.__cgwStripTrailingInjectionBlocks(hostText);
          }
        }
        if (hostText) {
          var role = getTurnRole(turnRoot);
          if (
            role === "user" &&
            globalThis.__cgwHideContextTags === true &&
            typeof globalThis.__cgwPacketDisplay === "object" &&
            globalThis.__cgwPacketDisplay &&
            typeof globalThis.__cgwPacketDisplay.transformUserBlocks === "function"
          ) {
            var hidden = globalThis.__cgwPacketDisplay.transformUserBlocks(
              turnRoot,
              splitParagraphFallback(hostText),
              role
            );
            if (hidden && hidden.length) return hidden;
          }
          return splitParagraphFallback(hostText);
        }
      }
      var wrapClone = turnWrapper(turnRoot);
      if (wrapClone) {
        wrapClone = wrapClone.cloneNode(true);
        stripChrome(wrapClone);
        var wrapText = sanitizeExtractedMessageText((wrapClone.innerText || "").trim());
        if (wrapText) return splitParagraphFallback(wrapText);
      }
    }

    return blocks;
  }

  function decorateTurnBlocks(blocks, opts) {
    if (opts && opts.skipPhraseHighlights) return blocks;
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

  function scoreScrollHostCandidate(node) {
    if (!node || node === document.body) return -1;
    var style = window.getComputedStyle(node);
    var oy = style.overflowY;
    var o = style.overflow;
    var scrollable =
      oy === "auto" ||
      oy === "scroll" ||
      o === "auto" ||
      o === "scroll";
    if (!scrollable) return -1;

    var score = node.scrollHeight - node.clientHeight;
    var visibleHeight = computeVisibleHeightInViewport(node);
    if (visibleHeight > 0) {
      score -= Math.abs(node.clientHeight - visibleHeight) * 4;
      if (node.clientHeight > visibleHeight + 96) score -= 12000;
    }
    var composer = findComposerRoot();
    if (composer && composer !== document) {
      var compParent = composer.parentElement;
      while (compParent && compParent !== document.body) {
        if (compParent === node) score -= 10000;
        compParent = compParent.parentElement;
      }
    }
    return score;
  }

  function findScrollHost(turns) {
    if (!turns.length) return null;

    var mounted = document.getElementById(CONTAINER_ID);
    if (
      mounted &&
      mounted.isConnected &&
      mounted.parentElement &&
      document.contains(mounted.parentElement)
    ) {
      return mounted.parentElement;
    }
    if (
      overlayGeometryPinnedHost &&
      document.contains(overlayGeometryPinnedHost)
    ) {
      return overlayGeometryPinnedHost;
    }

    if (
      cachedScrollHostForKey &&
      cachedScrollHostForKey.key === activeConversationKey &&
      cachedScrollHostForKey.host &&
      document.contains(cachedScrollHostForKey.host)
    ) {
      return cachedScrollHostForKey.host;
    }

    var best = null;
    var bestScore = -1;
    var node = turnWrapper(turns[0]);
    while (node && node !== document.body) {
      var score = scoreScrollHostCandidate(node);
      if (score > bestScore) {
        bestScore = score;
        best = node;
      }
      node = node.parentElement;
    }
    if (best) return best;

    var main = document.querySelector("main");
    if (main) return main;

    var wrap = turnWrapper(turns[0]);
    return wrap ? wrap.parentElement : null;
  }

  function clearScrollHostMark() {
    document.querySelectorAll("." + SCROLL_HOST_CLASS).forEach(function (el) {
      el.classList.remove(SCROLL_HOST_CLASS);
    });
  }

  function markScrollHost(scrollHost) {
    if (!scrollHost) return;
    document.querySelectorAll("." + SCROLL_HOST_CLASS).forEach(function (el) {
      if (el !== scrollHost) el.classList.remove(SCROLL_HOST_CLASS);
    });
    scrollHost.classList.add(SCROLL_HOST_CLASS);
  }

  function applyTurnSuppressions(segments, registry, hiddenWraps) {
    clearSuppressed();
    segments.forEach(function (seg) {
      var entry = registry[seg.turnId];
      if (entry && entry.wrap) entry.wrap.classList.add(SUPPRESS_CLASS);
    });
    hiddenWraps.forEach(function (wrap) {
      wrap.classList.add(SUPPRESS_CLASS);
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
    if (!wrap) return null;
    var exact = role === "user" ? "Your message actions" : "Response actions";
    var bar = wrap.querySelector('[aria-label="' + exact + '"]');
    if (bar) return bar;

    var partial = role === "user" ? "message actions" : "response actions";
    var labeled = wrap.querySelectorAll("[aria-label]");
    var i;
    for (i = 0; i < labeled.length; i++) {
      var aria = (labeled[i].getAttribute("aria-label") || "").toLowerCase();
      if (aria.indexOf(partial) >= 0) return labeled[i];
    }

    return wrap.querySelector(
      '[class*="message-actions"], [class*="action-buttons"]'
    );
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

  function playerLineFromSegment(segEl) {
    if (!segEl) return "";
    var role = segEl.getAttribute("data-cgw-turn-role") || "assistant";
    if (role !== "user") return segmentPlainText(segEl);

    var turnId = segEl.getAttribute("data-cgw-turn-id");
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = turnId != null ? registry[turnId] : null;
    if (entry && entry.playerSnippet) {
      return sanitizeExtractedMessageText(entry.playerSnippet);
    }

    var clone = segEl.cloneNode(true);
    clone
      .querySelectorAll(".cgw-continuous-packet-context")
      .forEach(function (el) {
        el.remove();
      });
    stripChrome(clone);
    return sanitizeExtractedMessageText(
      (clone.innerText || clone.textContent || "").trim()
    );
  }

  function segmentTextForRole(segEl) {
    if (!segEl) return "";
    var role = segEl.getAttribute("data-cgw-turn-role") || "assistant";
    return role === "user" ? playerLineFromSegment(segEl) : segmentPlainText(segEl);
  }

  globalThis.__cgwPlayerLineFromSegment = playerLineFromSegment;

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

      var togglePacketCtxBtn = document.createElement("button");
      togglePacketCtxBtn.type = "button";
      togglePacketCtxBtn.setAttribute("role", "menuitem");
      togglePacketCtxBtn.className = "cgw-continuous-context-menu__item";
      togglePacketCtxBtn.setAttribute("data-action", "toggle-packet-context");
      togglePacketCtxBtn.textContent = "Show adventure context";

      menu.appendChild(copyBtn);
      menu.appendChild(editBtn);
      menu.appendChild(editRespBtn);
      menu.appendChild(regenBtn);
      menu.appendChild(togglePacketCtxBtn);
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
            text = segmentTextForRole(segment);
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
        if (action === "edit" && turnId && segment) {
          openSurrogateEditPanel(turnId, segment, null, "user");
        }
        if (action === "edit-response" && turnId && segment) {
          openSurrogateEditPanel(turnId, segment, null, "assistant");
        }
        if (action === "regenerate" && turnId) enterPeekMode(turnId, "regenerate");
        if (action === "toggle-packet-context") {
          var pd = globalThis.__cgwPacketDisplay;
          if (pd && typeof pd.togglePacketContextUiVisible === "function") {
            pd.togglePacketContextUiVisible();
          }
        }
      });
    }

    if (!contextMenuListenersBound) {
      contextMenuListenersBound = true;
      document.addEventListener("click", function () {
        hideContextMenu();
      });
      document.addEventListener("keydown", function (e) {
        var menu = document.getElementById(CONTEXT_MENU_ID);
        var menuOpen = menu && !menu.hidden;
        if (menuOpen && (e.key === "ArrowDown" || e.key === "ArrowUp")) {
          e.preventDefault();
          var items = Array.prototype.slice.call(
            menu.querySelectorAll(".cgw-continuous-context-menu__item:not([hidden]):not(:disabled)")
          );
          if (!items.length) return;
          var active = document.activeElement;
          var idx = items.indexOf(active);
          if (e.key === "ArrowDown") {
            idx = idx < 0 ? 0 : (idx + 1) % items.length;
          } else {
            idx = idx <= 0 ? items.length - 1 : idx - 1;
          }
          items[idx].focus();
          return;
        }
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
      if (text === "save" || text === "update" || aria.indexOf("save") >= 0) {
        return btn;
      }
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
    var cd = globalThis.__cgwComposerDom;
    if (cd) {
      return cd.findComposerInput({ preferOffscreen: false, skipWrapper: true });
    }
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
    var cd = globalThis.__cgwComposerDom;
    if (cd) {
      return cd.findComposerRoot({ preferOffscreen: false, skipWrapper: true });
    }
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

  function transcriptIx() {
    return globalThis.__cgwTranscriptInteractions || null;
  }

  function postTurnInvalidated(turnId, reason, revisedText, opts) {
    var ix = transcriptIx();
    if (ix && typeof ix.postTurnInvalidated === "function") {
      ix.postTurnInvalidated(turnId, reason, revisedText, opts);
      return;
    }
    var msg = {
      type: "turnInvalidated",
      turnId: turnId != null ? String(turnId) : null,
      reason: reason || "surrogate_edit",
      text: revisedText || "",
      ok: true,
    };
    var kernel = globalThis.__cgwPageKernel;
    if (kernel && kernel.bus && typeof kernel.bus.post === "function") {
      kernel.bus.post(msg);
      return;
    }
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(JSON.stringify(msg));
      }
    } catch (_e) {
      /* ignore */
    }
  }

  function buildRevisionPrompt(editedText, turnId) {
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = turnId != null ? registry[turnId] : null;
    var ix = transcriptIx();
    var markerTurn =
      ix && typeof ix.resolveInvalidationMarkerTurn === "function"
        ? ix.resolveInvalidationMarkerTurn(entry)
        : turnId;
    var marker =
      markerTurn != null
        ? '[[cgw:invalidation turn="' + String(markerTurn) + '"]]\n'
        : "";
    var turnNum = markerTurn != null ? String(markerTurn) : "?";
    var prefix =
      REVISION_PROMPT_PREFIX +
      turnNum +
      " only: disregard your prior assistant reply for this turn and any later play turns in the thread. Output ONLY the replacement narrator text below with no preamble or commentary.";
    if (entry && entry.playerSnippet) {
      prefix +=
        '\n(Player line: "' +
        String(entry.playerSnippet).replace(/"/g, "'") +
        '")';
    }
    return marker + prefix + "\n\n" + editedText;
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

  function utilityHideEnabled() {
    if (globalThis.__cgwShowInlineUtilityTraffic) return false;
    return globalThis.__cgwHideInlineUtilityDuringPlay !== false;
  }

  function isUtilityUserMessage(text) {
    if (!text) return false;
    return String(text).indexOf(UTILITY_TAG_MARKER) >= 0;
  }

  function isUtilityAssistantMessage(text) {
    if (!text) return false;
    return String(text).indexOf(UTILITY_RESPONSE_TAG_MARKER) >= 0;
  }

  function loadUtilityHideQueue() {
    var key = getConversationKey();
    if (key !== loadedConversationKey) {
      globalThis.__cgwUtilityHideQueue = [];
    }
    if (!key) return;
    try {
      var raw = sessionStorage.getItem(UTILITY_HIDE_STORAGE_PREFIX + key);
      if (raw) globalThis.__cgwUtilityHideQueue = JSON.parse(raw);
    } catch (_e) {
      globalThis.__cgwUtilityHideQueue = [];
    }
  }

  function shouldHideUtilityTurn(turnId, role, blocks, hideNextAssistant) {
    if (!utilityHideEnabled()) return { hide: false, hideNextAssistant: false };
    var text = blocksPlainText(blocks);
    if (role === "user" && isUtilityUserMessage(text)) {
      return { hide: true, hideNextAssistant: true };
    }
    if (role === "assistant" && isUtilityAssistantMessage(text)) {
      return { hide: true, hideNextAssistant: false };
    }
    if (hideNextAssistant && role === "assistant") {
      return { hide: true, hideNextAssistant: false };
    }
    return { hide: false, hideNextAssistant: hideNextAssistant };
  }

  function shouldHideTurn(turnId, role, blocks) {
    if (!globalThis.__cgwHideAssistantEditArtifacts) return false;
    var id = String(turnId);
    var text = blocksPlainText(blocks);
    var entries = globalThis.__cgwRevisionHideEntries || [];
    var i;
    for (i = 0; i < entries.length; i++) {
      var meta = entries[i];
      if (meta.assistantDomTurnId && String(meta.assistantDomTurnId) === id) {
        return true;
      }
      if (role === "user" && meta.promptPrefix) {
        if (text.indexOf(meta.promptPrefix) === 0) return true;
      }
      if (
        role === "user" &&
        meta.messageKind === "narrator_revision_prompt" &&
        text.indexOf(REVISION_PROMPT_PREFIX) === 0
      ) {
        return true;
      }
    }
    var queue = globalThis.__cgwRevisionHideQueue || [];
    if (!queue.length) return false;
    for (i = 0; i < queue.length; i++) {
      var entry = queue[i];
      if (String(entry.assistantTurnId) === id) return true;
      if (role === "user" && entry.promptPrefix) {
        if (text.indexOf(entry.promptPrefix) === 0) return true;
        if (text.indexOf(REVISION_PROMPT_PREFIX) === 0) return true;
      }
    }
    return false;
  }

  function ensureScrollHostResizeObserver(scrollHost, container) {
    if (typeof ResizeObserver === "undefined" || !scrollHost) return;
    if (
      scrollHostResizeObserver &&
      scrollHostResizeObserveTarget === scrollHost &&
      scrollHostResizeObserveContainer === container
    ) {
      return;
    }
    disconnectScrollHostResizeObserver();
    scrollHostResizeObserveTarget = scrollHost;
    scrollHostResizeObserveContainer = container;
    scrollHostResizeObserver = new ResizeObserver(function () {
      if (!globalThis.__cgwContinuousViewEnabled) return;
      if (isContinuousScrollingActive()) return;
      var bottomInset = computeComposerBottomInset(scrollHost);
      if (
        userDetachedFromBottom &&
        lastResizeBottomInset === bottomInset
      ) {
        return;
      }
      lastResizeBottomInset = bottomInset;
      scheduleOverlayGeometrySync(scrollHost, container, { preserveScroll: true });
    });
    scrollHostResizeObserver.observe(scrollHost);
    ensureOverlayViewportWatcher(scrollHost, container);
  }

  function ensureScrollHostWheelBinding(scrollHost, container) {
    bindScrollHostWheelForward(scrollHost, container);
  }

  function stabilizeContinuousLayout(scrollHost, container, force) {
    if (!scrollHost || !container) return;
    if (!force && isContinuousViewStableActive()) return;
    updateComposerClearance();
    requestAnimationFrame(function () {
      if (!globalThis.__cgwContinuousViewEnabled) return;
      if (
        container.clientHeight === 0 ||
        scrollHost.getBoundingClientRect().height === 0
      ) {
        scheduleApplyRetry(50);
        return;
      }
      syncOverlayGeometry(scrollHost, container, { preserveScroll: true });
      if (shouldStickToBottom(scrollHost, container)) {
        applyScrollSurface(scrollHost, container, null, true);
      }
      scheduleReadingGuides(container);
    });
  }

  function handleApplyNotReady(watchTarget) {
    if (globalThis.__cgwContinuousViewEnabled && isConversationUrl(location.href)) {
      setTransitionAttributes();
      markProvisionalScrollHost();
    }
    bindTranscriptObserver(
      watchTarget || document.querySelector("main") || document.body
    );
    scheduleApplyRetry();
  }

  function resolveComposerMeasureNode() {
    var wrapperRoot = document.getElementById("cgw-play-composer-root");
    if (
      wrapperRoot &&
      wrapperRoot.isConnected &&
      globalThis.__cgwWrapperComposer
    ) {
      return wrapperRoot;
    }
    return findComposerRoot();
  }

  function computeComposerBottomInset(scrollHost) {
    if (!scrollHost) return 0;
    var composer = resolveComposerMeasureNode();
    if (
      !composer ||
      composer === document ||
      composer === document.documentElement ||
      composer === document.body
    ) {
      return 0;
    }
    var hostRect = scrollHost.getBoundingClientRect();
    var compRect = composer.getBoundingClientRect();
    if (compRect.top >= hostRect.bottom || compRect.height <= 0) return 0;
    return Math.max(0, Math.ceil(hostRect.bottom - compRect.top));
  }

  function computeVisibleHeightInViewport(node) {
    if (!node) return 0;
    var rect = node.getBoundingClientRect();
    var viewportHeight = window.visualViewport
      ? window.visualViewport.height
      : window.innerHeight;
    return Math.max(
      0,
      Math.min(rect.bottom, viewportHeight) - Math.max(rect.top, 0)
    );
  }

  function shouldSkipOverlayGeometrySync(scrollHost, container, bottomInset) {
    if (!scrollHost || !container) return true;
    return (
      container.getAttribute("data-cgw-overlay-bottom") === bottomInset + "px" &&
      container.parentElement === scrollHost
    );
  }

  function syncOverlayGeometry(scrollHost, container, opts) {
    opts = opts || {};
    if (!scrollHost || !container) return;
    var preserveScroll = !!opts.preserveScroll;
    var bottomInset = computeComposerBottomInset(scrollHost);
    if (shouldSkipOverlayGeometrySync(scrollHost, container, bottomInset)) {
      return;
    }
    ensureOverlayInScrollHost(scrollHost, container);
    overlayGeometryPinnedHost = scrollHost;
    var savedScrollTop = preserveScroll
      ? resolvePreservedScrollTop(scrollHost, container)
      : null;
    var nextBottom = bottomInset + "px";
    lastResizeBottomInset = bottomInset;
    container.setAttribute("data-cgw-overlay-bottom", nextBottom);
    container.style.position = "absolute";
    container.style.top = "0";
    container.style.left = "0";
    container.style.right = "0";
    container.style.bottom = nextBottom;
    container.style.width = "100%";
    container.style.height = "";
    container.style.maxHeight = "";
    container.style.maxWidth = "none";
    container.style.minHeight = "0";
    container.style.margin = "0";
    container.style.zIndex = "5";
    if (
      !preserveScroll ||
      savedScrollTop == null ||
      savedScrollTop <= 8
    ) {
      if (scrollHost.scrollTop !== 0) scrollHost.scrollTop = 0;
    }
    function restoreScroll() {
      if (preserveScroll && typeof savedScrollTop === "number") {
        applyScrollSurface(scrollHost, container, savedScrollTop, false);
        if (globalThis.__cgwExtendedDiagnostics && savedScrollTop > 8) {
          var actual = readScrollTop(scrollHost, container);
          if (actual < savedScrollTop - 8) {
            diagScroll(
              "scroll_position_restore_gap",
              "Overlay geometry restore below saved scroll",
              {
                saved: savedScrollTop,
                actual: actual,
                bottomInset: bottomInset,
              }
            );
          }
        }
      } else {
        clampScrollSurface(container);
      }
    }
    restoreScroll();
    if (preserveScroll && typeof savedScrollTop === "number") {
      requestAnimationFrame(restoreScroll);
    }
  }

  function commitConversationOverlay(
    container,
    segments,
    registry,
    hiddenWraps,
    scrollHost
  ) {
    requestAnimationFrame(function () {
      if (!isOverlayTranscriptMode()) return;
      if (!container || !document.contains(container)) {
        schedule({ immediate: true });
        return;
      }
      syncOverlayGeometry(scrollHost, container);

      clearSuppressed();
      segments.forEach(function (seg) {
        var entry = registry[seg.turnId];
        if (entry && entry.wrap) entry.wrap.classList.add(SUPPRESS_CLASS);
      });
      hiddenWraps.forEach(function (wrap) {
        wrap.classList.add(SUPPRESS_CLASS);
      });

      document.documentElement.setAttribute("data-cgw-continuous-view", "1");
      container.style.visibility = "";
      container.setAttribute("aria-hidden", "false");
      document.documentElement.removeAttribute("data-cgw-cv-pending");
      disconnectScrollHostScrollLock();
      syncOverlayGeometry(scrollHost, container, { preserveScroll: true });
      removeTransitionShell();
      transitionPhase = TRANSITION_PHASE_ACTIVE;
      applyRetryAttempts = 0;
      stopHrefPoll();

      if (shouldStickToBottom(scrollHost, container)) {
        applyScrollSurface(scrollHost, container, null, true);
      }

      scheduleReadingGuides(container);
    });
  }

  var COMPOSER_CLEARANCE_DEFAULT_PX = 96;

  function readComposerClearanceMinPx() {
    return typeof globalThis.__cgwComposerClearanceMinPx === "number" &&
      globalThis.__cgwComposerClearanceMinPx > 0
      ? globalThis.__cgwComposerClearanceMinPx
      : 64;
  }

  function readComposerClearanceMaxPx() {
    return typeof globalThis.__cgwComposerClearanceMaxPx === "number" &&
      globalThis.__cgwComposerClearanceMaxPx > 0
      ? globalThis.__cgwComposerClearanceMaxPx
      : 320;
  }

  function updateComposerClearance() {
    var root = resolveComposerMeasureNode();
    var height = COMPOSER_CLEARANCE_DEFAULT_PX;
    if (root && root !== document && root !== document.documentElement) {
      var rect = root.getBoundingClientRect();
      if (rect.height > 0) height = Math.ceil(rect.height);
    }
    height = Math.max(
      readComposerClearanceMinPx(),
      Math.min(readComposerClearanceMaxPx(), height)
    );
    document.documentElement.style.setProperty(
      "--cgw-composer-clearance",
      height + "px"
    );
  }

  globalThis.__cgwUpdateComposerClearance = updateComposerClearance;
  globalThis.__cgwSyncOverlayGeometry = syncOverlayGeometry;

  function ensureComposerClearanceWatcher() {
    updateComposerClearance();
    if (composerClearanceBound) return;
    composerClearanceBound = true;

    if (typeof ResizeObserver !== "undefined") {
      composerResizeObserver = new ResizeObserver(function () {
        var c = document.getElementById(CONTAINER_ID);
        var host = c ? resolveScrollHost(c) : null;
        if (c && host) {
          if (!isContinuousScrollingActive()) {
            scheduleOverlayGeometrySync(host, c, { preserveScroll: true });
          }
        } else {
          updateComposerClearance();
        }
      });
      var root = findComposerRoot();
      if (root && root !== document) composerResizeObserver.observe(root);
    }

    var kernel = globalThis.__cgwPageKernel;
    if (kernel && kernel.dom && !composerClearanceUnsubscribe) {
      composerClearanceUnsubscribe = kernel.dom.subscribe(
        "continuous-composer-clearance",
        { debounceMs: 120 },
        function () {
          var c = document.getElementById(CONTAINER_ID);
          var host = c ? resolveScrollHost(c) : null;
          if (c && host) {
            if (!isContinuousScrollingActive()) {
              scheduleOverlayGeometrySync(host, c, { preserveScroll: true });
            }
          } else {
            updateComposerClearance();
          }
          if (!composerResizeObserver) return;
          var observed = findComposerRoot();
          if (observed && observed !== document) {
            try {
              composerResizeObserver.observe(observed);
            } catch (_e) {
              /* already observed */
            }
          }
        }
      );
    }
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

    var meta = document.createElement("div");
    meta.className = "cgw-surrogate-edit-panel__meta";
    meta.hidden = true;

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
    panel.appendChild(meta);
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
        setSurrogateError(
          surrogateEditState.role === "user"
            ? "Message text cannot be empty."
            : "Response text cannot be empty."
        );
        return;
      }
      setSurrogateError("");
      setSurrogateSubmitting(true);
      submitSurrogateEdit(editedText, surrogateEditState.turnId, surrogateEditState.role);
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

  function openSurrogateEditPanel(turnId, segment, initialTextOverride, role) {
    hideContextMenu();
    var panel = ensureSurrogateEditPanel();
    var backdrop = document.getElementById(SURROGATE_EDIT_BACKDROP_ID);
    var input = panel.querySelector(".cgw-surrogate-edit-panel__input");
    var header = panel.querySelector(".cgw-surrogate-edit-panel__header");
    var meta = panel.querySelector(".cgw-surrogate-edit-panel__meta");
    var editRole = role === "user" ? "user" : "assistant";
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = turnId != null ? registry[turnId] : null;
    var ix = transcriptIx();
    var initialText =
      initialTextOverride != null
        ? initialTextOverride
        : segment
          ? segmentTextForRole(segment)
          : surrogateEditState.initialText || "";

    surrogateEditState.open = true;
    surrogateEditState.turnId = turnId;
    surrogateEditState.segment = segment;
    surrogateEditState.initialText = initialText;
    surrogateEditState.submitting = false;
    surrogateEditState.role = editRole;

    if (header) {
      header.textContent =
        ix && typeof ix.buildTurnContextLabel === "function"
          ? ix.buildTurnContextLabel(entry, editRole)
          : editRole === "user"
            ? "Edit message"
            : "Edit response";
    }
    if (meta) {
      var warning =
        ix && typeof ix.buildSupersedeWarning === "function"
          ? ix.buildSupersedeWarning(entry)
          : "";
      if (warning) {
        meta.textContent = warning;
        meta.hidden = false;
      } else {
        meta.textContent = "";
        meta.hidden = true;
      }
    }
    panel.setAttribute(
      "aria-label",
      editRole === "user" ? "Edit message" : "Edit response"
    );

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

  function closeSurrogateEditPanel(restoreOverlay, opts) {
    opts = opts || {};
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
    surrogateEditState.role = "assistant";
    setSurrogateError("");

    if (!opts.skipRebuild) {
      scheduleContinuousViewRebuild();
    }
  }

  function scheduleContinuousViewRebuild(opts) {
    opts = opts || {};
    if (!globalThis.__cgwContinuousViewEnabled) return;
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    invalidateTurnExtractCache();
    if (typeof schedule === "function") {
      schedule(opts.immediate ? { immediate: true } : undefined);
    }
  }

  function scheduleContinuousViewDecorationOnly() {
    if (!globalThis.__cgwContinuousViewEnabled) return;
    invalidateTurnExtractCache();
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    if (typeof schedule === "function") schedule();
  }

  globalThis.__cgwScheduleContinuousViewDecorationOnly =
    scheduleContinuousViewDecorationOnly;
  globalThis.__cgwScheduleContinuousViewRebuild = scheduleContinuousViewRebuild;

  function updateStreamingStickObserver(scrollHost, container, active) {
    if (typeof ResizeObserver === "undefined" || !container) return;
    if (active) {
      if (containerResizeObserver) return;
      containerResizeObserver = new ResizeObserver(function () {
        if (!globalThis.__cgwContinuousViewEnabled) return;
        if (shouldStickToBottom(scrollHost, container)) {
          applyScrollSurface(scrollHost, container, null, true);
        }
      });
      containerResizeObserver.observe(container);
      return;
    }
    if (containerResizeObserver) {
      containerResizeObserver.disconnect();
      containerResizeObserver = null;
    }
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

  function readTurnBubbleText(wrap, role) {
    if (!wrap) return "";
    var turn =
      wrap.closest('[data-testid^="conversation-turn-"]') ||
      wrap.closest("[data-message-author-role]") ||
      wrap;
    var bubble =
      turn.querySelector('[data-message-author-role="' + role + '"]') ||
      turn.querySelector(".markdown") ||
      turn;
    return (bubble.innerText || bubble.textContent || "").trim();
  }

  function isEditSurfaceVisible(el) {
    if (!el || !el.getBoundingClientRect) return false;
    if (isComposerElement(el)) return false;
    var rect = el.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return false;
    var style = window.getComputedStyle(el);
    return style.display !== "none" && style.visibility !== "hidden";
  }

  function findEditSurface(root) {
    if (!root) return null;
    var surfaces = root.querySelectorAll(
      '[contenteditable="true"], textarea, [role="textbox"], form textarea'
    );
    var i;
    for (i = 0; i < surfaces.length; i++) {
      if (isEditSurfaceVisible(surfaces[i])) return surfaces[i];
    }
    return null;
  }

  function dispatchTurnHover(wrap) {
    if (!wrap || !wrap.dispatchEvent) return;
    ["pointerover", "mouseenter", "mouseover"].forEach(function (type) {
      try {
        wrap.dispatchEvent(
          new MouseEvent(type, { bubbles: true, cancelable: true, view: window })
        );
      } catch (_e) {
        /* ignore */
      }
    });
  }

  function revealNativeTurnForPeek(wrap) {
    document.documentElement.removeAttribute("data-cgw-continuous-inline-edit");
    document.documentElement.setAttribute("data-cgw-continuous-peek", "1");

    var container = document.getElementById(CONTAINER_ID);
    if (container) {
      container.style.visibility = "hidden";
      container.setAttribute("aria-hidden", "true");
    }

    wrap.classList.remove(SUPPRESS_CLASS);
    wrap.classList.add(PEEK_TARGET_CLASS);
    wrap.scrollIntoView({ block: "center", behavior: "auto" });
  }

  function revealNativeTurnForInlineEdit(wrap) {
    document.documentElement.removeAttribute("data-cgw-continuous-peek");
    document.documentElement.setAttribute("data-cgw-continuous-inline-edit", "1");

    wrap.classList.remove(SUPPRESS_CLASS);
    wrap.classList.add(PEEK_TARGET_CLASS);
  }

  function clearInlineEditMode() {
    document.documentElement.removeAttribute("data-cgw-continuous-inline-edit");
  }

  function tryNativeEdit(turnId, editedText, role, onComplete, opts) {
    opts = opts || {};
    var allowPeek = opts.allowPeek !== false;
    var registry = globalThis.__cgwTurnRegistry || {};
    var entry = registry[turnId];
    if (!entry || !entry.wrap) {
      onComplete(false);
      return;
    }

    var wrap = entry.wrap;
    hideContextMenu();
    if (peekState.turnId) exitPeekMode(false);

    peekState.turnId = turnId;
    peekState.wrap = wrap;
    peekState.actionKind = allowPeek ? "edit" : "edit-inline";

    if (allowPeek) {
      revealNativeTurnForPeek(wrap);
    } else {
      revealNativeTurnForInlineEdit(wrap);
    }

    var deadline = Date.now() + NATIVE_EDIT_TIMEOUT_MS;

    function failEdit() {
      if (allowPeek) {
        exitPeekMode(false);
      } else {
        wrap.classList.add(SUPPRESS_CLASS);
        wrap.classList.remove(PEEK_TARGET_CLASS);
        clearInlineEditMode();
        peekState.turnId = null;
        peekState.wrap = null;
        peekState.actionKind = null;
      }
      onComplete(false);
    }

    function attemptPopulate() {
      var surface = scanEditSurfaceInWrap(wrap);
      if (surface && populateEditSurface(surface, editedText)) {
        var sendBtn = findNativeSendButton(wrap);
        if (sendBtn) {
          peekState.pendingInvalidation = {
            turnId: turnId,
            reason: role === "user" ? "user_edit" : "native_edit",
            text: editedText,
            editRole: role,
            captureFromDom: role === "user",
          };
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
      failEdit();
    }

    requestAnimationFrame(function () {
      requestAnimationFrame(function () {
        dispatchTurnHover(wrap);
        var editBtn = findTurnActionButton(wrap, role, "edit");
        if (!editBtn) {
          failEdit();
          return;
        }
        editBtn.click();
        requestAnimationFrame(attemptPopulate);
      });
    });
  }

  function tryNativeAssistantEdit(turnId, editedText, onComplete) {
    tryNativeEdit(turnId, editedText, "assistant", onComplete);
  }

  function finalizePendingComposerRevision() {
    var pending = globalThis.__cgwPendingComposerRevision;
    if (!pending) return;
    if (isNativeStreaming()) return;
    if (Date.now() - pending.startedAt < 500) return;

    var captured = pending.revisedText || "";
    var turns = findTurnRoots();
    if (turns.length) {
      var last = turns[turns.length - 1];
      if (getTurnRole(last) === "assistant") {
        var wrap = turnWrapper(last);
        if (wrap) {
          var fromDom = readTurnBubbleText(wrap, "assistant");
          if (fromDom && fromDom.trim()) captured = fromDom.trim();
        }
      }
    }

    postTurnInvalidated(pending.assistantTurnId, "composer_revision", captured, {
      editRole: "assistant",
      revisionGroupId: pending.revisionGroupId,
      revisionPrompt: pending.revisionPrompt,
      assistantDomTurnId: pending.assistantTurnId,
    });
    globalThis.__cgwPendingComposerRevision = null;
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

    var prompt = buildRevisionPrompt(editedText, assistantTurnId);
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

  function submitSurrogateEdit(editedText, turnId, role) {
    closeSurrogateEditPanel(false, { skipRebuild: true });
    var editRole = role === "user" ? "user" : "assistant";

    if (editRole === "assistant") {
      var revisionGroupId =
        "rev-" +
        Date.now().toString(36) +
        "-" +
        Math.random().toString(36).slice(2, 9);
      var revisionPrompt = buildRevisionPrompt(editedText, turnId);
      submitComposerRevision(editedText, turnId, function (composerOk) {
        if (!composerOk) {
          openSurrogateEditPanel(turnId, null, editedText, "assistant");
          setSurrogateError(
            "Could not submit. Check that the composer is available and try again."
          );
          setSurrogateSubmitting(false);
          return;
        }
        globalThis.__cgwPendingComposerRevision = {
          assistantTurnId: String(turnId),
          revisedText: editedText,
          revisionGroupId: revisionGroupId,
          revisionPrompt: revisionPrompt,
          startedAt: Date.now(),
        };
      });
      return;
    }

    var allowPeek = !isOverlayTranscriptMode();
    tryNativeEdit(
      turnId,
      editedText,
      editRole,
      function (nativeOk) {
        if (nativeOk) {
          return;
        }

        scheduleContinuousViewRebuild();
        openSurrogateEditPanel(turnId, null, editedText, "user");
        setSurrogateError(
          allowPeek
            ? "Could not open native edit. Try again from the context menu."
            : "Could not submit edit while keeping overlay view. Try Native transcript mode or edit in ChatGPT directly."
        );
        setSurrogateSubmitting(false);
      },
      { allowPeek: allowPeek }
    );
  }

  function submitSurrogateAssistantEdit(editedText, turnId) {
    submitSurrogateEdit(editedText, turnId, "assistant");
  }

  function segmentHasPacketContext(segment) {
    return !!(
      segment &&
      segment.querySelector(".cgw-continuous-packet-context")
    );
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
    var togglePacketCtxBtn = menu.querySelector(
      '[data-action="toggle-packet-context"]'
    );

    if (role === "user") {
      editBtn.hidden = false;
      editBtn.textContent = "Edit message";
      editRespBtn.hidden = true;
      regenBtn.hidden = true;
      var editTarget = wrap ? findTurnActionButton(wrap, "user", "edit") : null;
      editBtn.disabled = !editTarget || isNativeStreaming();
      editBtn.title = editBtn.disabled && isNativeStreaming()
        ? "Wait for the current response to finish"
        : "";
    } else {
      editBtn.hidden = true;
      editRespBtn.hidden = false;
      editRespBtn.disabled = isNativeStreaming();
      editRespBtn.title = editRespBtn.disabled
        ? "Wait for the current response to finish"
        : "";
      regenBtn.hidden = false;
      var regenTarget = wrap ? findTurnActionButton(wrap, "assistant", "regenerate") : null;
      regenBtn.disabled = !regenTarget || isNativeStreaming();
      regenBtn.title = regenBtn.disabled && isNativeStreaming()
        ? "Wait for the current response to finish"
        : "";
    }

    if (togglePacketCtxBtn) {
      var showPacketToggle =
        role === "user" && segmentHasPacketContext(segment);
      togglePacketCtxBtn.hidden = !showPacketToggle;
      if (showPacketToggle) {
        var packetDisplay = globalThis.__cgwPacketDisplay;
        var visible =
          packetDisplay &&
          typeof packetDisplay.isPacketContextUiVisible === "function" &&
          packetDisplay.isPacketContextUiVisible();
        togglePacketCtxBtn.textContent = visible
          ? "Hide adventure context"
          : "Show adventure context";
      }
    }

    menu.style.left = x + "px";
    menu.style.top = y + "px";
    menu.hidden = false;
    menu.setAttribute("aria-hidden", "false");
    contextMenuState.open = true;
    var firstItem = menu.querySelector(
      ".cgw-continuous-context-menu__item:not([hidden]):not(:disabled)"
    );
    if (firstItem && typeof firstItem.focus === "function") firstItem.focus();

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
        var segment = e.target.closest(
          ".cgw-continuous-segment, .cgw-weave-embed, .cgw-weave-body"
        );
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
    clearInlineEditMode();
    peekState.turnId = null;
    peekState.wrap = null;
    peekState.pendingInvalidation = null;
    peekState.actionKind = null;

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
      if (peekState.pendingInvalidation) {
        var inv = peekState.pendingInvalidation;
        var text = inv.text || "";
        if (inv.captureFromDom && peekState.wrap) {
          text =
            readTurnBubbleText(peekState.wrap, inv.editRole || "user") || text;
        }
        postTurnInvalidated(inv.turnId, inv.reason, text, {
          editRole: inv.editRole,
          usedFallback: !!inv.usedFallback,
        });
        peekState.pendingInvalidation = null;
      }
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

  function scheduleApplyRetry(forcedDelay) {
    if (!globalThis.__cgwContinuousViewEnabled) return;
    var pending = document.documentElement.hasAttribute("data-cgw-cv-pending");
    if (applyRetryAttempts >= MAX_APPLY_RETRY_ATTEMPTS && !pending) return;

    var delay;
    if (typeof forcedDelay === "number") {
      delay = forcedDelay;
    } else {
      delay =
        APPLY_RETRY_DELAYS[
          Math.min(applyRetryAttempts, APPLY_RETRY_DELAYS.length - 1)
        ];
    }
    if (applyRetryAttempts < MAX_APPLY_RETRY_ATTEMPTS) {
      applyRetryAttempts++;
    }

    if (applyRetryTimer != null) clearTimeout(applyRetryTimer);
    applyRetryTimer = setTimeout(function () {
      applyRetryTimer = null;
      schedule({ fast: true });
    }, delay);
  }

  function onNavigationEvent() {
    var key = getConversationKey();
    if (shouldEnterTransitionForKey(key)) {
      enterConversationTransition(key, { force: true });
    }
    schedule({ immediate: true });
  }

  function ensureNavigationWatcher() {
    if (navWatcherBound) return;
    navWatcherBound = true;
    window.addEventListener("popstate", onNavigationEvent);
    var origPush = history.pushState;
    var origReplace = history.replaceState;
    history.pushState = function () {
      var result = origPush.apply(history, arguments);
      onNavigationEvent();
      return result;
    };
    history.replaceState = function () {
      var result = origReplace.apply(history, arguments);
      onNavigationEvent();
      return result;
    };
  }

  function hideContinuousOverlay() {
    document.documentElement.removeAttribute("data-cgw-continuous-view");
    document.documentElement.removeAttribute("data-cgw-cv-pending");
    clearSuppressed();
    var container = document.getElementById(CONTAINER_ID);
    if (container) {
      container.style.visibility = "hidden";
      container.setAttribute("aria-hidden", "true");
    }
  }

  var debounceTimer = null;
  var scheduled = null;

  function cancelPendingApply() {
    if (debounceTimer != null) {
      clearTimeout(debounceTimer);
      debounceTimer = null;
    }
    if (scheduled != null) {
      cancelAnimationFrame(scheduled);
      scheduled = null;
    }
    if (streamApplyFrameId != null) {
      cancelAnimationFrame(streamApplyFrameId);
      streamApplyFrameId = null;
    }
    streamApplyQueued = false;
  }

  function scheduleStreamingCoalesced() {
    if (streamApplyQueued) return;
    streamApplyQueued = true;
    if (debounceTimer != null) {
      clearTimeout(debounceTimer);
      debounceTimer = null;
    }
    if (scheduled != null) {
      cancelAnimationFrame(scheduled);
      scheduled = null;
    }
    streamApplyFrameId = requestAnimationFrame(function () {
      streamApplyFrameId = null;
      streamApplyQueued = false;
      applyActiveTranscriptView();
    });
  }

  function canResumeContinuousViewWithoutTransition(key) {
    if (!key || !isConversationUrl(location.href)) return false;
    if (document.getElementById(CONTAINER_ID)) return false;
    if (
      transitionPhase === TRANSITION_PHASE_TRANSITIONING &&
      targetConversationKey === key
    ) {
      return false;
    }
    return findTurnRoots().length > 0;
  }

  function resumeContinuousView(key) {
    targetConversationKey = key;
    activeConversationKey = key;
    transitionPhase = TRANSITION_PHASE_TRANSITIONING;

    invalidateTurnExtractCache();
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    globalThis.__cgwTurnRegistry = {};

    updateStreamingStickObserver(null, null, false);
    disconnectScrollHostResizeObserver();
    disconnectScrollHostScrollLock();
    disconnectContainerScrollClamp();
    markPreferBottomScroll();
    userScrollAnchor = null;
    userScrollAnchorAt = 0;
    lastResizeBottomInset = null;
    overlayGeometryPinnedHost = null;
    cachedScrollHostForKey = null;
    applyRetryAttempts = 0;
    cancelDomReadyWait();

    setTransitionAttributes();
    var turns = findTurnRoots();
    var scrollHost = findScrollHost(turns);
    if (scrollHost) {
      markScrollHost(scrollHost);
      cachedScrollHostForKey = { key: key, host: scrollHost };
      ensureTransitionShell(scrollHost);
    } else {
      markProvisionalScrollHost();
    }
  }

  function teardown() {
    cancelPendingApply();
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
    invalidateTurnExtractCache();
    updateStreamingStickObserver(null, null, false);
    wasNativeStreaming = false;
    streamApplyQueued = false;
    disconnectScrollHostResizeObserver();
    disconnectOverlayViewportWatcher();
    disconnectScrollHostWheelForward();
    if (overlayGeometrySyncTimer != null) {
      clearTimeout(overlayGeometrySyncTimer);
      overlayGeometrySyncTimer = null;
    }
    overlayGeometrySyncPending = null;
    userScrollAnchor = null;
    userScrollAnchorAt = 0;
    lastResizeBottomInset = null;
    overlayGeometryPinnedHost = null;
    disconnectScrollHostScrollLock();
    disconnectContainerScrollClamp();
    disconnectContainerScrollIntent();
    preferBottomOnNextApply = false;
    userDetachedFromBottom = false;
    activeConversationKey = null;
    targetConversationKey = null;
    transitionPhase = null;
    loadedConversationKey = null;
    cachedScrollHostForKey = null;
    applyRetryAttempts = 0;
    stopHrefPoll();
    cancelDomReadyWait();
    removeTransitionShell();
    if (composerClearanceUnsubscribe) {
      composerClearanceUnsubscribe();
      composerClearanceUnsubscribe = null;
    }
    composerClearanceBound = false;
    disconnectTranscriptObserver();
    document.documentElement.removeAttribute("data-cgw-transcript-mode");
    document.documentElement.removeAttribute("data-cgw-continuous-view");
    document.documentElement.removeAttribute("data-cgw-continuous-peek");
    document.documentElement.removeAttribute("data-cgw-surrogate-edit");
    document.documentElement.removeAttribute("data-cgw-cv-pending");
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

  function ensureScrollAnchor(container) {
    if (!container) return null;
    var anchor = document.getElementById(SCROLL_ANCHOR_ID);
    if (!anchor) {
      anchor = document.createElement("div");
      anchor.id = SCROLL_ANCHOR_ID;
      anchor.className = "cgw-cv-scroll-anchor";
      anchor.setAttribute("aria-hidden", "true");
    }
    if (anchor.parentElement !== container || container.firstChild !== anchor) {
      container.insertBefore(anchor, container.firstChild);
    }
    return anchor;
  }

  function renderSegments(scrollHost, container, segments, scrollTop, stickToBottom) {
    while (container.firstChild) container.removeChild(container.firstChild);
    ensureScrollAnchor(container);
    segments.forEach(function (segData) {
      var blocks = segData.blocks;
      if (!blocks.length) return;
      var seg = createSegmentElement(segData);
      blocks.forEach(function (block) {
        appendRichBlock(seg, block);
      });
      syncPacketContextExpandState(seg, segData.turnId);
      container.appendChild(seg);
    });
    applyContainerScroll(scrollHost, container, scrollTop, stickToBottom);
  }

  function applyActiveTranscriptView() {
    if (!isOverlayTranscriptMode()) {
      teardown();
      return;
    }
    var mode = getTranscriptViewMode();
    if (mode === "weave") {
      if (typeof globalThis.__cgwApplyWeaveView === "function") {
        globalThis.__cgwApplyWeaveView();
        return;
      }
      if (
        transcriptRenderers.weave &&
        typeof transcriptRenderers.weave.apply === "function"
      ) {
        transcriptRenderers.weave.apply();
        return;
      }
    }
    if (mode === "continuous") {
      if (
        transcriptRenderers.continuous &&
        typeof transcriptRenderers.continuous.apply === "function"
      ) {
        transcriptRenderers.continuous.apply();
        return;
      }
      applyContinuousViewCore();
    }
  }

  function activateOverlayTranscriptMode(mode) {
    syncTranscriptViewDom(mode);
    ensureNavigationWatcher();
    var key = getConversationKey();
    if (canResumeContinuousViewWithoutTransition(key)) {
      resumeContinuousView(key);
    } else if (shouldEnterTransitionForKey(key)) {
      enterConversationTransition(key, { force: true });
    }
  }

  function collectSegmentsFromTurns() {
    ensureNavigationWatcher();
    noteConversationKeyChange();
    loadRevisionHideQueue();
    loadUtilityHideQueue();

    if (!isConversationUrl(location.href)) {
      return { notReady: true, reason: "url" };
    }

    if (peekState.turnId || surrogateEditState.open) {
      return { notReady: true, reason: "interactive" };
    }

    if (!isConversationKeyStable()) {
      return { notReady: true, reason: "key" };
    }

    if (
      transitionPhase === TRANSITION_PHASE_TRANSITIONING &&
      !isTranscriptDomReady()
    ) {
      if (
        findTurnRoots().length > 0 &&
        Date.now() - domReadyLastMutation >= TRANSITION_MIN_HOLD_MS
      ) {
        /* turns already in DOM */
      } else {
        return { notReady: true, reason: "dom" };
      }
    }

    var turns = findTurnRoots();
    if (!turns.length) {
      return { notReady: true, reason: "turns" };
    }

    var registry = {};
    var segments = [];
    var hiddenWraps = [];
    var seenTurn = new Set();
    var hideNextUtilityAssistant = false;

    assignTurnIdsInDocumentOrder(turns);

    var streamingTurnId = getStreamingAssistantTurnId(turns);

    turns.forEach(function (turn) {
      if (seenTurn.has(turn)) return;
      seenTurn.add(turn);

      var wrap = turnWrapper(turn);
      if (!wrap) return;

      var turnId = wrap.getAttribute("data-cgw-turn-id") || getOrAssignTurnId(wrap);
      var role = getTurnRole(turn);

      var rawBlocks = getRawTurnBlocks(turn, turnId, role);

      if (shouldHideTurn(turnId, role, rawBlocks)) {
        hiddenWraps.push(wrap);
        return;
      }

      var utilityHide = shouldHideUtilityTurn(
        turnId,
        role,
        rawBlocks,
        hideNextUtilityAssistant
      );
      hideNextUtilityAssistant = utilityHide.hideNextAssistant;
      if (utilityHide.hide) {
        hiddenWraps.push(wrap);
        return;
      }

      var blocks = extractTurnBlocks(turn, turnId, role, {
        skipPhraseHighlights:
          streamingTurnId != null && String(turnId) === String(streamingTurnId),
      });
      if (!blocks.length) return;

      registry[turnId] = { turn: turn, wrap: wrap, role: role };
      segments.push({ turnId: turnId, role: role, blocks: blocks, turn: turn });
    });

    if (!segments.length) {
      return { notReady: true, reason: "segments" };
    }

    var ix = transcriptIx();
    if (ix && typeof ix.assignPlayPairIndices === "function") {
      ix.assignPlayPairIndices(registry, segments);
    }

    globalThis.__cgwTurnRegistry = registry;

    var scrollHost = findScrollHost(turns);
    if (!scrollHost) {
      return { notReady: true, reason: "scroll" };
    }

    return {
      segments: segments,
      registry: registry,
      hiddenWraps: hiddenWraps,
      scrollHost: scrollHost,
      streamingTurnId: streamingTurnId,
    };
  }

  function applyContinuousViewCore() {
    if (!isContinuousTranscriptMode()) {
      return;
    }

    var collected = collectSegmentsFromTurns();
    if (!collected) {
      return;
    }
    if (collected.notReady) {
      if (collected.reason === "url") {
        hideContinuousOverlay();
        bindTranscriptObserver(document.querySelector("main") || document.body);
        scheduleApplyRetry(400);
      } else if (collected.reason === "dom") {
        waitForTranscriptDomReady(function (ready) {
          if (!isOverlayTranscriptMode()) return;
          if (ready) schedule({ immediate: true });
          else handleApplyNotReady(document.querySelector("main") || document.body);
        });
      } else {
        handleApplyNotReady(document.querySelector("main") || document.body);
      }
      return;
    }

    ensureStyles();

    var segments = collected.segments;
    var registry = collected.registry;
    var hiddenWraps = collected.hiddenWraps;
    var scrollHost = collected.scrollHost;

    bindTranscriptObserver(scrollHost);

    var container = document.getElementById(CONTAINER_ID);
    var fingerprint = computeSegmentsFingerprint(segments);
    var prevFingerprint = globalThis.__cgwContinuousViewFingerprint;
    var unchanged =
      container &&
      container.querySelector(".cgw-continuous-segment") &&
      fingerprint === prevFingerprint &&
      container.childElementCount === segments.length;

    if (
      unchanged &&
      isContinuousViewStableActive() &&
      container.parentElement === scrollHost &&
      !isNativeStreaming() &&
      !segmentsNeedPhraseHighlightRefresh(container)
    ) {
      if (!shouldSkipOverlayGeometrySync(scrollHost, container, computeComposerBottomInset(scrollHost))) {
        scheduleOverlayGeometrySync(scrollHost, container, { preserveScroll: true });
      }
      updateStreamingStickObserver(scrollHost, container, isNativeStreaming());
      scheduleReadingGuides(container);
      return;
    }

    var needsAtomicSwap =
      !isContinuousViewStableActive() &&
      (transitionPhase === TRANSITION_PHASE_TRANSITIONING ||
        document.documentElement.hasAttribute("data-cgw-cv-pending"));

    if (needsAtomicSwap) {
      setTransitionAttributes();
    }

    markScrollHost(scrollHost);
    cachedScrollHostForKey = { key: activeConversationKey, host: scrollHost };
    bindScrollHostScrollLock(scrollHost);
    ensureTransitionShell(scrollHost);

    var reparented = false;
    if (!container) {
      container = document.createElement("div");
      container.id = CONTAINER_ID;
      container.className = "cgw-continuous-view";
      scrollHost.appendChild(container);
      reparented = true;
    } else if (ensureOverlayInScrollHost(scrollHost, container)) {
      reparented = true;
    }
    container.classList.remove("cgw-weave-view");

    if (needsAtomicSwap) {
      container.style.visibility = "hidden";
      container.setAttribute("aria-hidden", "true");
    } else {
      container.style.visibility = "";
      container.setAttribute("aria-hidden", "false");
    }

    ensureComposerClearanceWatcher();
    ensureScrollHostResizeObserver(scrollHost, container);
    ensureScrollHostWheelBinding(scrollHost, container);
    bindContainerScrollClamp(container);
    bindContainerScrollIntent(container);

    trimTurnExtractCache(
      segments.map(function (s) {
        return turnExtractCacheKey(s.turnId, s.role);
      })
    );

    var stickToBottom = shouldStickToBottom(scrollHost, container);
    unchanged =
      container.querySelector(".cgw-continuous-segment") &&
      fingerprint === prevFingerprint &&
      container.childElementCount === segments.length;

    var changedTurnIds = [];
    if (!unchanged) {
      var savedScrollTop = readScrollTop(scrollHost, container);
      changedTurnIds =
        syncSegments(
          scrollHost,
          container,
          segments,
          stickToBottom ? null : savedScrollTop,
          stickToBottom
        ) || [];
      globalThis.__cgwContinuousViewFingerprint = fingerprint;
    } else if (stickToBottom) {
      applyScrollSurface(scrollHost, container, null, true);
    }

    updateStreamingStickObserver(scrollHost, container, isNativeStreaming());

    var streamingNow = isNativeStreaming();
    noteStreamingLifecycle(container);

    if (needsAtomicSwap) {
      commitConversationOverlay(
        container,
        segments,
        registry,
        hiddenWraps,
        scrollHost
      );
      requestAnimationFrame(function () {
        if (changedTurnIds.length) {
          finalizeContinuousViewFormatting(container, changedTurnIds);
        } else if (!unchanged) {
          finalizeContinuousViewFormatting(container, null);
        } else {
          scheduleReadingGuides(container);
        }
      });
      stabilizeContinuousLayout(scrollHost, container, true);
    } else {
      if (changedTurnIds.length) {
        var finalizeIds = changedTurnIds;
        if (streamingNow) {
          finalizeIds = changedTurnIds.filter(function (id) {
            var seg = container.querySelector(
              '.cgw-continuous-segment[data-cgw-turn-id="' + id + '"]'
            );
            return !seg || seg.getAttribute("data-cgw-streaming") !== "1";
          });
        }
        if (finalizeIds.length) {
          finalizeContinuousViewFormatting(container, finalizeIds);
        }
      } else if (unchanged && !segmentsNeedPhraseHighlightRefresh(container)) {
        scheduleReadingGuides(container);
      } else {
        finalizeContinuousViewFormatting(container, null);
      }

      if (!unchanged || reparented) {
        applyTurnSuppressions(segments, registry, hiddenWraps);
      }

      document.documentElement.setAttribute("data-cgw-continuous-view", "1");
      document.documentElement.removeAttribute("data-cgw-cv-pending");
      disconnectScrollHostScrollLock();
      transitionPhase = TRANSITION_PHASE_ACTIVE;
      syncOverlayGeometry(scrollHost, container, { preserveScroll: true });
      if (reparented) {
        stabilizeContinuousLayout(scrollHost, container, true);
      } else if (!unchanged && !streamingNow) {
        stabilizeContinuousLayout(scrollHost, container, true);
      } else if (streamingNow && stickToBottom) {
        applyScrollSurface(scrollHost, container, null, true);
      }
    }

    bindContextMenuOnContainer(container);
    ensureContextMenu();
    ensureSurrogateEditPanel();
  }

  function schedule(opts) {
    opts = opts || {};
    if (!isOverlayTranscriptMode()) {
      teardown();
      return;
    }

    if (peekState.turnId || surrogateEditState.open) return;

    if (isNativeStreaming() && !opts.immediate && !opts.fast) {
      scheduleStreamingCoalesced();
      return;
    }

    if (debounceTimer != null) clearTimeout(debounceTimer);
    var delay;
    if (opts.immediate) {
      delay = 0;
    } else if (
      opts.fast ||
      document.documentElement.hasAttribute("data-cgw-cv-pending")
    ) {
      delay = NAV_FAST_DEBOUNCE_MS;
    } else if (isNativeStreaming()) {
      delay = STREAM_DEBOUNCE_MS;
    } else {
      delay = DEBOUNCE_MS;
    }
    debounceTimer = setTimeout(function () {
      debounceTimer = null;
      if (scheduled != null) cancelAnimationFrame(scheduled);
      scheduled = requestAnimationFrame(function () {
        scheduled = null;
        applyActiveTranscriptView();
      });
    }, delay);
  }

  function shouldEnterTransitionForKey(key) {
    if (!isConversationUrl(location.href)) return false;
    if (
      transitionPhase === TRANSITION_PHASE_TRANSITIONING &&
      targetConversationKey === key
    ) {
      return false;
    }
    if (
      transitionPhase === TRANSITION_PHASE_ACTIVE &&
      activeConversationKey === key &&
      document.getElementById(CONTAINER_ID)
    ) {
      return false;
    }
    return true;
  }

  globalThis.__cgwSetTranscriptViewMode = function (mode) {
    mode = normalizeTranscriptViewMode(mode);
    var prev = getTranscriptViewMode();
    globalThis.__cgwTranscriptViewMode = mode;
    globalThis.__cgwContinuousViewEnabled = mode !== "native";
    syncTranscriptViewDom(mode);

    diagScroll("transcript_view_mode", "Transcript view mode changed", {
      mode: mode,
      prev: prev,
      continuous: globalThis.__cgwContinuousViewEnabled,
    });

    if (mode === "native") {
      cancelPendingApply();
      teardown();
      if (typeof globalThis.__cgwApplyContextTagDisplay === "function") {
        globalThis.__cgwApplyContextTagDisplay();
      }
      return;
    }

    if (prev !== mode) {
      cancelPendingApply();
      delete globalThis.__cgwContinuousViewFingerprint;
      delete globalThis.__cgwSegmentFingerprints;
      delete globalThis.__cgwSegmentBlockFingerprints;
      delete globalThis.__cgwWeaveViewFingerprint;
      delete globalThis.__cgwWeaveFlowFingerprints;
      invalidateTurnExtractCache();
      if (mode !== "native") {
        markPreferBottomScroll();
      }
    }

    activateOverlayTranscriptMode(mode);
    if (
      typeof globalThis.__cgwSetContinuousViewFormat === "function" &&
      globalThis.__cgwContinuousViewFormat
    ) {
      globalThis.__cgwSetContinuousViewFormat(globalThis.__cgwContinuousViewFormat, false);
    }
    schedule({ immediate: true });
  };

  globalThis.__cgwSetContinuousView = function (enabled) {
    globalThis.__cgwSetTranscriptViewMode(enabled ? "continuous" : "native");
  };

  globalThis.__cgwContinuousViewNavigate = function () {
    if (!isOverlayTranscriptMode()) return;
    ensureNavigationWatcher();
    markPreferBottomScroll();
    var key = getConversationKey();
    if (shouldEnterTransitionForKey(key)) {
      enterConversationTransition(key, { force: true });
    }
    schedule({ immediate: true });
  };

  globalThis.__cgwSetHideAssistantEditArtifacts = function (enabled) {
    globalThis.__cgwHideAssistantEditArtifacts = !!enabled;
    if (enabled) {
      loadRevisionHideQueue();
    } else {
      globalThis.__cgwRevisionHideQueue = [];
    }
    delete globalThis.__cgwContinuousViewFingerprint;
    delete globalThis.__cgwSegmentFingerprints;
    delete globalThis.__cgwSegmentBlockFingerprints;
    invalidateTurnExtractCache();
    if (globalThis.__cgwContinuousViewEnabled) schedule({ immediate: true });
  };

  registerTranscriptRenderer("continuous", {
    apply: applyContinuousViewCore,
  });

  var transcriptInteractions = globalThis.__cgwTranscriptInteractions;
  if (transcriptInteractions && typeof transcriptInteractions.registerRenderer === "function") {
    transcriptInteractions.registerRenderer({
      id: "continuous",
      getTurnIdFromElement: function (el) {
        var segment = el.closest(
          ".cgw-continuous-segment, .cgw-weave-embed, .cgw-weave-body"
        );
        return segment ? segment.getAttribute("data-cgw-turn-id") : null;
      },
    });
  }

  globalThis.__cgwTranscriptKernel = {
    CONTAINER_ID: CONTAINER_ID,
    SCROLL_ANCHOR_ID: SCROLL_ANCHOR_ID,
    INTERACTIVE_SEGMENT_CLASS: INTERACTIVE_SEGMENT_CLASS,
    collectSegmentsFromTurns: collectSegmentsFromTurns,
    ensureStyles: ensureStyles,
    ensureScrollAnchor: ensureScrollAnchor,
    appendRichBlock: appendRichBlock,
    patchStreamingProseBlock: patchStreamingProseBlock,
    syncPacketContextExpandState: syncPacketContextExpandState,
    computeSegmentsFingerprint: computeSegmentsFingerprint,
    blocksFingerprint: blocksFingerprint,
    blockFingerprint: blockFingerprint,
    bindContextMenuOnContainer: bindContextMenuOnContainer,
    ensureContextMenu: ensureContextMenu,
    ensureSurrogateEditPanel: ensureSurrogateEditPanel,
    bindTranscriptObserver: bindTranscriptObserver,
    markScrollHost: markScrollHost,
    bindScrollHostScrollLock: bindScrollHostScrollLock,
    ensureTransitionShell: ensureTransitionShell,
    ensureOverlayInScrollHost: ensureOverlayInScrollHost,
    applyScrollSurface: applyScrollSurface,
    readScrollTop: readScrollTop,
    shouldStickToBottom: shouldStickToBottom,
    bindContainerScrollClamp: bindContainerScrollClamp,
    bindContainerScrollIntent: bindContainerScrollIntent,
    ensureComposerClearanceWatcher: ensureComposerClearanceWatcher,
    ensureScrollHostResizeObserver: ensureScrollHostResizeObserver,
    ensureScrollHostWheelBinding: ensureScrollHostWheelBinding,
    trimTurnExtractCache: trimTurnExtractCache,
    turnExtractCacheKey: turnExtractCacheKey,
    isNativeStreaming: isNativeStreaming,
    isContinuousViewStableActive: isContinuousViewStableActive,
    setTransitionAttributes: setTransitionAttributes,
    commitConversationOverlay: commitConversationOverlay,
    applyTurnSuppressions: applyTurnSuppressions,
    finalizeContinuousViewFormatting: finalizeContinuousViewFormatting,
    stabilizeContinuousLayout: stabilizeContinuousLayout,
    syncOverlayGeometry: syncOverlayGeometry,
    updateStreamingStickObserver: updateStreamingStickObserver,
    noteStreamingLifecycle: noteStreamingLifecycle,
    disconnectScrollHostScrollLock: disconnectScrollHostScrollLock,
    handleApplyNotReady: handleApplyNotReady,
    hideContinuousOverlay: hideContinuousOverlay,
    scheduleApplyRetry: scheduleApplyRetry,
    waitForTranscriptDomReady: waitForTranscriptDomReady,
    isOverlayTranscriptMode: isOverlayTranscriptMode,
    getTranscriptViewMode: getTranscriptViewMode,
    segmentHasPacketContextBlock: segmentHasPacketContextBlock,
    isPacketContextUiVisible: isPacketContextUiVisible,
  };

  globalThis.__cgwApplyActiveTranscriptView = applyActiveTranscriptView;

  globalThis.__cgwContinuousViewSchedule = schedule;

  globalThis.__cgwShouldStickToBottom = shouldStickToBottom;
  globalThis.__cgwResetContinuousScrollIntent = function () {
    markPreferBottomScroll();
  };
  globalThis.__cgwNoteUserDetachedFromBottom = function (detached) {
    userDetachedFromBottom = !!detached;
    if (detached) preferBottomOnNextApply = false;
  };

  globalThis.__cgwBenchmarkDecorateTurnBlocks = function (turnCount) {
    var blocks = [];
    var count = turnCount || 50;
    for (var i = 0; i < count; i++) {
      blocks.push({
        kind: "prose",
        html:
          "<p>Turn " +
          i +
          ' with <em>emphasis</em> and "dialogue."</p>',
      });
    }
    var t0 = performance.now();
    decorateTurnBlocks(blocks.slice());
    return performance.now() - t0;
  };
})();
