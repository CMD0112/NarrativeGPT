(function () {
  "use strict";

  var kernel = globalThis.__cgwPageKernel;
  if (!kernel) return;

  var COMPOSER_DOM_VERSION = 4;
  var OFFSCREEN_ID = "cgw-native-composer-offscreen";
  var WRAPPER_ROOT_ID = "cgw-play-composer-root";

  function isInsideWrapper(node) {
    return !!(node && node.closest && node.closest("#" + WRAPPER_ROOT_ID));
  }

  function isInsideOffscreen(node) {
    return !!(node && node.closest && node.closest("#" + OFFSCREEN_ID));
  }

  function getOffscreenBucket() {
    return document.getElementById(OFFSCREEN_ID);
  }

  function ensureOffscreenBucket() {
    var bucket = getOffscreenBucket();
    if (bucket) return bucket;
    bucket = document.createElement("div");
    bucket.id = OFFSCREEN_ID;
    bucket.setAttribute("aria-hidden", "true");
    bucket.style.cssText = OFFSCREEN_HIDDEN_STYLE;
    document.body.appendChild(bucket);
    return bucket;
  }

  function findAllComposers() {
    return kernel.query.all(kernel.selectors.composer);
  }

  function findActiveComposer(wrapperRoot) {
    var composers = findAllComposers();
    if (!composers.length) return null;
    if (wrapperRoot && wrapperRoot.isConnected) {
      var host = wrapperRoot.closest('[data-testid="composer"]');
      if (host) return host;
      var formHost = wrapperRoot.closest("form");
      if (formHost) return formHost;
    }
    return composers[composers.length - 1];
  }

  function isComposerChromeNode(el) {
    if (!el || !el.closest) return false;
    if (isInsideWrapper(el)) return false;
    if (isInsideOffscreen(el)) return false;
    return !!(
      el.closest('[data-testid="composer"]') ||
      el.closest("form:has(#prompt-textarea)") ||
      el.closest("#" + OFFSCREEN_ID)
    );
  }

  function findComposerAnchor(wrapperRoot) {
    if (wrapperRoot && wrapperRoot.isConnected) {
      var host = wrapperRoot.closest('[data-testid="composer"]');
      if (host) return { node: host, mode: "inline" };
      var formHost = wrapperRoot.closest("form");
      if (formHost) return { node: formHost, mode: "inline" };
    }

    var composers = findAllComposers();
    if (composers.length) {
      return { node: composers[composers.length - 1], mode: "inline" };
    }

    var form = document.querySelector("form:has(#prompt-textarea)");
    if (form) return { node: form, mode: "inline" };

    var textareaForm = document.querySelector("#prompt-textarea")?.closest("form");
    if (textareaForm) return { node: textareaForm, mode: "inline" };

    return { node: document.body, mode: "fixed" };
  }

  function findComposerInput(options) {
    options = options || {};
    var preferOffscreen = options.preferOffscreen !== false;
    var skipWrapper = options.skipWrapper !== false;

    if (preferOffscreen) {
      var bucket = getOffscreenBucket();
      if (bucket && bucket.childElementCount > 0) {
        var inBucket = bucket.querySelector("#prompt-textarea");
        if (inBucket && (!skipWrapper || !isInsideWrapper(inBucket))) {
          return kernel.query.editable(inBucket);
        }
        var bucketCandidates = bucket.querySelectorAll(
          '[data-testid="composer-text-input"], div.ProseMirror[contenteditable="true"], [contenteditable="true"].ProseMirror'
        );
        var b;
        for (b = 0; b < bucketCandidates.length; b++) {
          if (bucketCandidates[b] && (!skipWrapper || !isInsideWrapper(bucketCandidates[b]))) {
            return kernel.query.editable(bucketCandidates[b]);
          }
        }
      }
    }

    var candidates = kernel.query.all(kernel.selectors.composerInput);
    var i;
    for (i = 0; i < candidates.length; i++) {
      var el = candidates[i];
      if (skipWrapper && isInsideWrapper(el)) continue;
      if (isInsideOffscreen(el)) continue;
      var resolved = kernel.query.editable(el);
      if (resolved) return resolved;
    }
    return null;
  }

  function findComposerRoot(options) {
    options = options || {};
    var bucket = getOffscreenBucket();
    if (options.preferOffscreen !== false && bucket && bucket.childElementCount > 0) {
      return bucket;
    }

    var el = findComposerInput(options);
    if (el && el.closest) {
      var root =
        el.closest('[data-testid="composer"]') ||
        el.closest("form:has(#prompt-textarea)");
      if (root) return root;
    }

    var anchor = document.querySelector("#prompt-textarea");
    if (anchor) {
      var node = anchor.parentElement;
      while (node && node !== document.body) {
        if (
          node.querySelector &&
          node.querySelector(
            'button[data-testid*="submit"], button[data-testid*="publish"]'
          )
        ) {
          return node;
        }
        node = node.parentElement;
      }
    }

    return findActiveComposer(options.wrapperRoot) || document;
  }

  function collectSubmitSearchRoots() {
    var roots = [];
    var seen = new Set();
    function add(node) {
      if (!node || seen.has(node)) return;
      seen.add(node);
      roots.push(node);
    }
    var bucket = getOffscreenBucket();
    if (bucket) add(bucket);
    findAllComposers().forEach(add);
    var form = document.querySelector("form:has(#prompt-textarea)");
    if (form) add(form);
    return roots.length ? roots : [document];
  }

  function findComposerSubmitButton(allowDisabled, root) {
    var roots = root ? [root] : collectSubmitSearchRoots();
    var selectors = kernel.selectors.composerSubmit.concat([
      'button[data-testid*="send-button"]',
      'button[data-testid*="send"]',
    ]);
    var r;
    var i;
    var btn;
    for (r = 0; r < roots.length; r++) {
      root = roots[r];
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
    }
    return null;
  }

  function restoreNativeFromOffscreen() {
    var bucket = getOffscreenBucket();
    if (!bucket) return;
    var composers = findAllComposers();
    var target = composers.length ? composers[composers.length - 1] : null;
    while (bucket.firstChild) {
      if (target) target.appendChild(bucket.firstChild);
      else bucket.removeChild(bucket.firstChild);
    }
    bucket.remove();
    clearRelocateStyles();
  }

  var OFFSCREEN_HIDDEN_STYLE =
    "position:fixed!important;left:0!important;top:0!important;width:1px!important;height:1px!important;overflow:hidden!important;opacity:0!important;pointer-events:none!important;clip:rect(0,0,0,0)!important;white-space:nowrap!important;z-index:-1!important;";

  var OFFSCREEN_AUTOMATION_STYLE =
    "position:fixed!important;left:0!important;bottom:0!important;width:100%!important;max-width:100%!important;height:auto!important;min-height:48px!important;overflow:visible!important;opacity:0.01!important;pointer-events:auto!important;clip:auto!important;white-space:normal!important;z-index:2147483646!important;";

  function temporarilyExposeOffscreenComposer() {
    var bucket = getOffscreenBucket();
    if (!bucket || bucket.childElementCount === 0) {
      return { exposed: false, restore: function () {} };
    }
    var prevCss = bucket.style.cssText;
    var prevAria = bucket.getAttribute("aria-hidden");
    bucket.style.cssText = OFFSCREEN_AUTOMATION_STYLE;
    bucket.setAttribute("aria-hidden", "false");
    return {
      exposed: true,
      restore: function () {
        bucket.style.cssText = prevCss || OFFSCREEN_HIDDEN_STYLE;
        if (prevAria === null) bucket.removeAttribute("aria-hidden");
        else bucket.setAttribute("aria-hidden", prevAria);
      },
    };
  }

  function temporarilyRestoreNativeToAnchor(wrapperRoot) {
    if (!wrapperRoot || !wrapperRoot.isConnected) {
      return { restored: false, restore: function () {} };
    }

    var anchorInfo = findComposerAnchor(wrapperRoot);
    var anchor = anchorInfo && anchorInfo.node;
    if (!anchor || anchor === document.body) {
      return { restored: false, restore: function () {} };
    }

    var bucket = getOffscreenBucket();
    if (!bucket || bucket.childElementCount === 0) {
      return { restored: false, restore: function () {} };
    }

    var prevWrapperDisplay = wrapperRoot.style.display || "";
    wrapperRoot.style.setProperty("display", "none", "important");

    while (bucket.firstChild) {
      anchor.insertBefore(bucket.firstChild, wrapperRoot);
    }

    clearRelocateStyles();

    return {
      restored: true,
      restore: function () {
        if (prevWrapperDisplay) wrapperRoot.style.display = prevWrapperDisplay;
        else wrapperRoot.style.removeProperty("display");
        relocateNativeComposerChrome(anchor, wrapperRoot);
      },
    };
  }

  function relocateNativeComposerChrome(anchor, wrapperRoot) {
    if (!anchor || !wrapperRoot || anchor === document.body) return;

    var bucket = ensureOffscreenBucket();
    Array.from(anchor.children).forEach(function (child) {
      if (child === wrapperRoot) return;
      bucket.appendChild(child);
    });

    var clearanceMin =
      typeof globalThis.__cgwComposerClearanceMinPx === "number"
        ? globalThis.__cgwComposerClearanceMinPx
        : 0;
    var userMargin = anchor.getAttribute("data-cgw-user-composer-margin");
    var computedPad = window.getComputedStyle(anchor).paddingTop;
    var hasUserPad =
      userMargin === "1" ||
      (computedPad && parseFloat(computedPad) > 0);
    if (!(clearanceMin > 0 && hasUserPad)) {
      anchor.style.setProperty("padding", "0", "important");
    }
    anchor.style.setProperty("background", "transparent", "important");
    anchor.style.setProperty("box-shadow", "none", "important");
    anchor.style.setProperty("border", "none", "important");
    anchor.style.setProperty("min-height", "0", "important");

    findAllComposers().forEach(function (node) {
      if (node === anchor) return;
      if (wrapperRoot && node.contains(wrapperRoot)) return;
      node.style.setProperty("display", "none", "important");
    });

    kernel.query.all(kernel.selectors.composerInput).forEach(function (el) {
      if (!isComposerChromeNode(el)) return;
      if (anchor.contains(el)) return;
      var row =
        el.closest('[data-testid="composer-text-input"]') ||
        el.closest('[data-testid="composer"]') ||
        el.parentElement;
      if (row && row !== anchor) {
        row.style.setProperty("display", "none", "important");
      }
    });
  }

  function clearRelocateStyles() {
    findAllComposers().forEach(function (node) {
      node.style.removeProperty("display");
      node.style.removeProperty("padding");
      node.style.removeProperty("background");
      node.style.removeProperty("box-shadow");
      node.style.removeProperty("border");
      node.style.removeProperty("min-height");
    });
    kernel.query.all(kernel.selectors.composerInput).forEach(function (el) {
      if (!isComposerChromeNode(el)) return;
      var row =
        el.closest('[data-testid="composer-text-input"]') ||
        el.closest('[data-testid="composer"]') ||
        el.parentElement;
      if (row) row.style.removeProperty("display");
    });
  }

  function probeComposer() {
    return {
      composerFound: !!findComposerInput({ preferOffscreen: true, skipWrapper: true }),
      submitFound: !!findComposerSubmitButton(true),
    };
  }

  function listNativeComposerAttachments() {
    var items = [];
    var root = findComposerRoot();
    if (!root || root === document) return items;

    var chips = root.querySelectorAll(
      '[data-testid*="attachment"], [class*="attachment"], img[src^="blob:"], img[src^="data:"]'
    );
    for (var i = 0; i < chips.length; i++) {
      var el = chips[i];
      if (el.tagName === "IMG") {
        items.push({
          name: el.getAttribute("alt") || el.getAttribute("title") || "image",
          mimeType: "image/*",
        });
        continue;
      }
      var label = (
        el.getAttribute("aria-label") ||
        el.getAttribute("title") ||
        el.textContent ||
        ""
      ).trim();
      if (!label) continue;
      items.push({
        name: label.slice(0, 120),
        mimeType: null,
      });
    }
    return items;
  }

  function findNativeComposerSurface(probeRoot) {
    if (!probeRoot) return null;
    var surface = probeRoot.querySelector('[data-composer-surface="true"]');
    if (surface) return surface;
    var unified = probeRoot.querySelector('form[data-type="unified-composer"]');
    if (unified) return unified;
    var child = probeRoot.firstElementChild;
    return child && child.id !== WRAPPER_ROOT_ID ? child : null;
  }

  function findNativeStyleProbeRoot() {
    var bucket = getOffscreenBucket();
    if (bucket && bucket.childElementCount > 0) return bucket;
    return findActiveComposer();
  }

  function readStyleValue(el, prop) {
    if (!el) return "";
    try {
      return window.getComputedStyle(el).getPropertyValue(prop) || "";
    } catch (_e) {
      return "";
    }
  }

  function syncComposeThemeFromNative(wrapperRoot) {
    if (!wrapperRoot) return false;

    var probe = findNativeStyleProbeRoot();
    var surface = findNativeComposerSurface(probe);
    var input = findComposerInput({ preferOffscreen: true, skipWrapper: true });
    var sendBtn = findComposerSubmitButton(true, probe);
    var attachBtn = probe
      ? probe.querySelector(
          'button[data-testid*="attach"], button[aria-label*="Attach"], [data-testid="composer-action-attach"]'
        )
      : null;

    var synced = false;
    if (surface) {
      var shell = wrapperRoot.querySelector(".cgw-compose-shell");
      var ss = window.getComputedStyle(surface);
      wrapperRoot.style.setProperty("--cgw-compose-bg", ss.backgroundColor);
      wrapperRoot.style.setProperty("--cgw-compose-border", ss.borderTopColor || ss.borderColor);
      wrapperRoot.style.setProperty("--cgw-compose-radius", ss.borderRadius);
      if (ss.boxShadow && ss.boxShadow !== "none") {
        wrapperRoot.style.setProperty("--cgw-compose-shadow", ss.boxShadow);
      } else {
        wrapperRoot.style.removeProperty("--cgw-compose-shadow");
      }
      if (shell) {
        shell.style.borderRadius = ss.borderRadius;
        if (ss.boxShadow && ss.boxShadow !== "none") {
          shell.style.boxShadow = ss.boxShadow;
        } else {
          shell.style.removeProperty("box-shadow");
        }
      }
      synced = true;
    }

    if (input) {
      var is = window.getComputedStyle(input);
      wrapperRoot.style.setProperty("--cgw-compose-text", is.color);
      wrapperRoot.style.setProperty("--cgw-compose-font-size", is.fontSize);
      wrapperRoot.style.setProperty("--cgw-compose-line-height", is.lineHeight);
      wrapperRoot.style.setProperty("--cgw-compose-font-family", is.fontFamily);
      synced = true;
    }

    if (sendBtn) {
      var bs = window.getComputedStyle(sendBtn);
      wrapperRoot.style.setProperty("--cgw-compose-send-bg", bs.backgroundColor);
      wrapperRoot.style.setProperty("--cgw-compose-send-fg", bs.color);
      wrapperRoot.style.setProperty("--cgw-compose-send-size", bs.width);
      wrapperRoot.style.setProperty("--cgw-compose-send-radius", bs.borderRadius);
      synced = true;
    }

    if (attachBtn) {
      wrapperRoot.style.setProperty("--cgw-compose-muted", readStyleValue(attachBtn, "color"));
      synced = true;
    }

    return synced;
  }

  function listComposerFileUi() {
    var fileInputs = [];
    var nodes = document.querySelectorAll('input[type="file"]');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      fileInputs.push({
        accept: el.getAttribute("accept") || "",
        multiple: !!el.multiple,
        hidden: !el.offsetParent && el.type === "file",
        id: el.id || "",
        name: el.name || "",
        testId: el.getAttribute("data-testid") || "",
      });
    }

    var attachSelectors = [
      'button[data-testid*="attach"]',
      'button[aria-label*="Attach"]',
      'button[aria-label*="Upload"]',
      '[data-testid="composer-action-attach"]',
    ];
    var attachButtons = [];
    for (var s = 0; s < attachSelectors.length; s++) {
      var selector = attachSelectors[s];
      var matches = document.querySelectorAll(selector);
      for (var m = 0; m < matches.length; m++) {
        var btn = matches[m];
        attachButtons.push({
          selector: selector,
          testId: btn.getAttribute("data-testid") || "",
          ariaLabel: btn.getAttribute("aria-label") || "",
          text: (btn.textContent || "").trim().slice(0, 80),
        });
      }
    }

    return {
      href: location.href,
      fileInputs: fileInputs,
      attachButtons: attachButtons,
    };
  }

  var ComposerDom = {
    version: COMPOSER_DOM_VERSION,
    offscreenId: OFFSCREEN_ID,
    wrapperRootId: WRAPPER_ROOT_ID,
    isInsideWrapper: isInsideWrapper,
    isInsideOffscreen: isInsideOffscreen,
    findAllComposers: findAllComposers,
    findActiveComposer: findActiveComposer,
    findComposerAnchor: findComposerAnchor,
    findComposerInput: findComposerInput,
    findComposerRoot: findComposerRoot,
    findComposerSubmitButton: findComposerSubmitButton,
    ensureOffscreenBucket: ensureOffscreenBucket,
    temporarilyExposeOffscreenComposer: temporarilyExposeOffscreenComposer,
    temporarilyRestoreNativeToAnchor: temporarilyRestoreNativeToAnchor,
    restoreNativeFromOffscreen: restoreNativeFromOffscreen,
    relocateNativeComposerChrome: relocateNativeComposerChrome,
    syncComposeThemeFromNative: syncComposeThemeFromNative,
    probeComposer: probeComposer,
    listComposerFileUi: listComposerFileUi,
    listNativeComposerAttachments: listNativeComposerAttachments,
  };

  kernel.features.register("composer-dom", {
    onDeactivate: restoreNativeFromOffscreen,
  });
  kernel.features.activate("composer-dom");

  globalThis.__cgwComposerDom = ComposerDom;
  globalThis.__cgwComposerDomVersion = COMPOSER_DOM_VERSION;
})();
