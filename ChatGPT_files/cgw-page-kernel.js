(function () {
  "use strict";

  if (globalThis.__cgwPageKernel && globalThis.__cgwPageKernel.version >= 3) {
    return;
  }

  var KERNEL_VERSION = 3;

  var Selectors = {
    composer: ['[data-testid="composer"]', 'form:has(#prompt-textarea)'],
    composerInput: [
      "#prompt-textarea",
      '[data-testid="composer-text-input"]',
      '[data-testid="composer"] div.ProseMirror[contenteditable="true"]',
      'form:has(#prompt-textarea) div.ProseMirror[contenteditable="true"]',
    ],
    composerSubmit: [
      'button[data-testid="composer-submit-button"]',
      'button[data-testid="composer-publish-button"]',
      'button[data-testid*="submit"]',
      'button[data-testid*="publish"]',
    ],
    assistantTurn: ['[data-message-author-role="assistant"]'],
    turnGroup: ['[class*="group/turn-messages"]'],
    wrapperComposerRoot: ["#cgw-play-composer-root"],
    nativeOffscreen: ["#cgw-native-composer-offscreen"],
  };

  function normalizeRoot(root) {
    return root && root.querySelector ? root : document;
  }

  function queryFirst(selectorList, root) {
    root = normalizeRoot(root);
    var list = selectorList || [];
    var i;
    for (i = 0; i < list.length; i++) {
      try {
        var hit = root.querySelector(list[i]);
        if (hit) return hit;
      } catch (_e) {
        /* invalid selector on older engines */
      }
    }
    return null;
  }

  function queryAll(selectorList, root) {
    root = normalizeRoot(root);
    var out = [];
    var seen = new Set();
    var list = selectorList || [];
    var i;
    var j;
    for (i = 0; i < list.length; i++) {
      try {
        var nodes = root.querySelectorAll(list[i]);
        for (j = 0; j < nodes.length; j++) {
          if (!seen.has(nodes[j])) {
            seen.add(nodes[j]);
            out.push(nodes[j]);
          }
        }
      } catch (_e2) {
        /* ignore */
      }
    }
    return out;
  }

  function resolveEditableInput(el) {
    if (!el) return null;
    if (el.getAttribute && el.getAttribute("contenteditable") === "true") return el;
    if (el.tagName === "TEXTAREA" || el.tagName === "INPUT") return el;
    var inner = el.querySelector
      ? el.querySelector('textarea, [contenteditable="true"], [role="textbox"]')
      : null;
    return inner || el;
  }

  var domSubscribers = Object.create(null);
  var domObserver = null;
  var domDebounceTimers = Object.create(null);
  var domPending = false;

  function flushDomSubscribers() {
    domPending = false;
    var id;
    for (id in domSubscribers) {
      if (!Object.prototype.hasOwnProperty.call(domSubscribers, id)) continue;
      var sub = domSubscribers[id];
      if (!sub || sub.paused) continue;
      try {
        sub.callback(sub.lastMutations || []);
      } catch (_e) {
        /* feature error must not break hub */
      }
    }
  }

  function scheduleDomFlush() {
    if (domPending) return;
    domPending = true;
    requestAnimationFrame(flushDomSubscribers);
  }

  function ensureDomObserver() {
    if (domObserver) return;
    domObserver = new MutationObserver(function (records) {
      var id;
      for (id in domSubscribers) {
        if (!Object.prototype.hasOwnProperty.call(domSubscribers, id)) continue;
        var sub = domSubscribers[id];
        if (!sub || sub.paused) continue;
        sub.lastMutations = records;
        var delay = typeof sub.debounceMs === "number" ? sub.debounceMs : 0;
        if (delay <= 0) {
          scheduleDomFlush();
          continue;
        }
        if (domDebounceTimers[id]) clearTimeout(domDebounceTimers[id]);
        domDebounceTimers[id] = setTimeout(function () {
          domDebounceTimers[id] = null;
          scheduleDomFlush();
        }, delay);
      }
    });
    domObserver.observe(document.documentElement || document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      characterData: false,
    });
  }

  function subscribeDom(id, options, callback) {
    if (!id || typeof callback !== "function") return function noop() {};
    ensureDomObserver();
    domSubscribers[id] = {
      debounceMs: options && typeof options.debounceMs === "number" ? options.debounceMs : 0,
      callback: callback,
      paused: false,
      lastMutations: null,
    };
    return function unsubscribe() {
      unsubscribeDom(id);
    };
  }

  function unsubscribeDom(id) {
    if (domDebounceTimers[id]) {
      clearTimeout(domDebounceTimers[id]);
      delete domDebounceTimers[id];
    }
    delete domSubscribers[id];
    var hasAny = false;
    var key;
    for (key in domSubscribers) {
      if (Object.prototype.hasOwnProperty.call(domSubscribers, key)) {
        hasAny = true;
        break;
      }
    }
    if (!hasAny && domObserver) {
      domObserver.disconnect();
      domObserver = null;
    }
  }

  function pauseDom(id, paused) {
    if (!domSubscribers[id]) return;
    domSubscribers[id].paused = !!paused;
  }

  function postToHost(msg) {
    try {
      var envelope = msg;
      if (!msg || typeof msg !== "object") return;
      if (!msg.feature && msg.type) {
        envelope = Object.assign({ feature: inferFeatureFromLegacyType(msg.type) }, msg);
      }
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(JSON.stringify(envelope));
      }
    } catch (_e) {
      /* ignore */
    }
  }

  function inferFeatureFromLegacyType(type) {
    if (!type || typeof type !== "string") return "legacy";
    if (type.indexOf("cgwCompose") === 0) return "play-compose";
    if (type === "cgwPlaySendLog") return "play-compose";
    if (type === "cgwDiagnosticsLog") return "play-compose";
    if (type === "bridgeReady" || type === "turnComplete" || type === "probeResult") {
      return "adventure-bridge";
    }
    return "legacy";
  }

  function inferDiagnosticsChannel(source) {
    var s = (source || "").toLowerCase();
    if (s.indexOf("play-compose") >= 0) return "compose";
    if (s.indexOf("adventure-bridge") >= 0) return "bridge";
    if (s.indexOf("continuous") >= 0) return "navigation";
    return "page";
  }

  function diagnosticsLog(level, eventName, message, data, source, channel) {
    try {
      var lvl = (level || "info").toLowerCase();
      if (lvl === "debug" && !globalThis.__cgwExtendedDiagnostics) return;
      var payload = {
        type: "cgwDiagnosticsLog",
        level: lvl,
        event: eventName || "js",
        message: message || "",
        source: source || "page",
        channel: channel || inferDiagnosticsChannel(source),
        url: location.href,
        ts: Date.now(),
      };
      if (data !== undefined && data !== null) payload.data = data;
      postToHost(payload);
    } catch (_e) {
      /* ignore */
    }
  }

  function playSendLog(level, eventName, message, data, source) {
    diagnosticsLog(level, eventName, message, data, source, "play_send");
  }

  var injectedStyles = Object.create(null);

  function injectStyle(styleId, cssText) {
    if (!styleId) return;
    try {
      var el = document.getElementById(styleId);
      if (!el) {
        el = document.createElement("style");
        el.id = styleId;
        document.head.appendChild(el);
      }
      el.textContent = cssText || "";
      injectedStyles[styleId] = true;
    } catch (_e) {
      /* ignore */
    }
  }

  function injectStyleFromGlobal(styleId, globalKey) {
    var css = globalKey && globalThis[globalKey];
    injectStyle(styleId, typeof css === "string" ? css : "");
  }

  var featureRegistry = Object.create(null);

  function registerFeature(id, spec) {
    if (!id) return;
    featureRegistry[id] = Object.assign({ id: id, active: false }, spec || {});
  }

  function getFeature(id) {
    return featureRegistry[id] || null;
  }

  function activateFeature(id) {
    var feature = featureRegistry[id];
    if (!feature) return;
    feature.active = true;
    if (typeof feature.onActivate === "function") feature.onActivate();
  }

  function deactivateFeature(id) {
    var feature = featureRegistry[id];
    if (!feature) return;
    feature.active = false;
    if (typeof feature.onDeactivate === "function") feature.onDeactivate();
  }

  function isFeatureActive(id) {
    return !!(featureRegistry[id] && featureRegistry[id].active);
  }

  globalThis.__cgwPageKernel = {
    version: KERNEL_VERSION,
    selectors: Selectors,
    query: {
      first: queryFirst,
      all: queryAll,
      editable: resolveEditableInput,
    },
    dom: {
      subscribe: subscribeDom,
      unsubscribe: unsubscribeDom,
      pause: pauseDom,
    },
    bus: {
      post: postToHost,
      inferFeature: inferFeatureFromLegacyType,
      playSendLog: playSendLog,
      diagnosticsLog: diagnosticsLog,
    },
    style: {
      inject: injectStyle,
      injectFromGlobal: injectStyleFromGlobal,
    },
    features: {
      register: registerFeature,
      activate: activateFeature,
      deactivate: deactivateFeature,
      get: getFeature,
      isActive: isFeatureActive,
    },
  };
})();
