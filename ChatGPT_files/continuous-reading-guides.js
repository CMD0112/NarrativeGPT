/**
 * ChatGPT Wrapper — per-line reading guides via layout rects (wrap-accurate).
 */
(function () {
  var CONTAINER_ID = "cgw-continuous-view";
  var LAYER_CLASS = "cgw-reading-guide-layer";
  var MARK_CLASS = "cgw-reading-guide-mark";
  var JS_STYLES = { line: 1, band: 1, underline: 1, "margin-rail": 1 };
  var BLOCK_SELECTOR =
    ".cgw-continuous-prose > p," +
    ".cgw-continuous-prose > blockquote," +
    ".cgw-continuous-prose > li," +
    ".cgw-continuous-prose > .cgw-continuous-list > li," +
    ".cgw-continuous-fallback," +
    ".cgw-continuous-quote > p," +
    ".cgw-continuous-quote > li," +
    ".cgw-weave-body > p," +
    ".cgw-weave-body > blockquote," +
    ".cgw-weave-body > li," +
    ".cgw-weave-body > .cgw-continuous-list > li," +
    ".cgw-weave-embed > p," +
    ".cgw-weave-embed > blockquote," +
    ".cgw-weave-embed > li";

  var paintScheduled = null;
  var resizeObserver = null;
  var observedContainer = null;
  var layoutRetryTimer = null;
  var layoutRetryCount = 0;
  var LAYOUT_RETRY_MAX = 10;

  function clearLayoutRetry() {
    if (layoutRetryTimer != null) {
      clearTimeout(layoutRetryTimer);
      layoutRetryTimer = null;
    }
    layoutRetryCount = 0;
  }

  function scheduleLayoutRetry(container) {
    if (layoutRetryCount >= LAYOUT_RETRY_MAX) return;
    layoutRetryCount += 1;
    if (layoutRetryTimer != null) clearTimeout(layoutRetryTimer);
    var delay = layoutRetryCount <= 2 ? 0 : Math.min(40 * layoutRetryCount, 400);
    layoutRetryTimer = setTimeout(function () {
      layoutRetryTimer = null;
      if (!isActive()) return;
      scheduleApply(container);
    }, delay);
  }

  function containerPaintDeferred(container) {
    if (!container) return false;
    if (container.style.visibility === "hidden") return true;
    if (container.getAttribute("aria-hidden") === "true") return true;
    if (container.clientHeight === 0 && container.childElementCount > 0) return true;
    return false;
  }

  function rootEl() {
    return document.documentElement;
  }

  function isActive() {
    var root = rootEl();
    return (
      root &&
      root.getAttribute("data-cgw-ruled-lines") === "1" &&
      root.getAttribute("data-cgw-ruled-js") === "1"
    );
  }

  function currentStyle() {
    return rootEl().getAttribute("data-cgw-ruled-style") || "line";
  }

  function clipToText() {
    return rootEl().getAttribute("data-cgw-ruled-clip") === "1";
  }

  function skipTextNode(node) {
    var parent = node.parentElement;
    if (!parent) return true;
    if (parent.closest("pre, code, .cgw-continuous-code, .cgw-continuous-pre")) {
      return true;
    }
    return !String(node.textContent || "").trim();
  }

  function mergeLineRects(rects) {
    if (!rects.length) return [];
    var sorted = rects.slice().sort(function (a, b) {
      if (Math.abs(a.top - b.top) > 1) return a.top - b.top;
      return a.left - b.left;
    });
    var merged = [];
    var group = null;

    function flush() {
      if (!group) return;
      merged.push({
        left: group.left,
        top: group.top,
        right: group.right,
        bottom: group.bottom,
        width: group.right - group.left,
        height: group.bottom - group.top,
      });
      group = null;
    }

    sorted.forEach(function (rect) {
      if (!rect.width && !rect.height) return;
      if (
        !group ||
        Math.abs(rect.top - group.top) > 1.5 ||
        Math.abs(rect.height - group.height) > 1.5
      ) {
        flush();
        group = {
          left: rect.left,
          top: rect.top,
          right: rect.right,
          bottom: rect.bottom,
        };
        return;
      }
      group.left = Math.min(group.left, rect.left);
      group.right = Math.max(group.right, rect.right);
      group.top = Math.min(group.top, rect.top);
      group.bottom = Math.max(group.bottom, rect.bottom);
    });
    flush();
    return merged;
  }

  function collectLineRects(block) {
    var range = document.createRange();
    var rects = [];
    var walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT, {
      acceptNode: function (node) {
        return skipTextNode(node)
          ? NodeFilter.FILTER_REJECT
          : NodeFilter.FILTER_ACCEPT;
      },
    });
    var node = walker.nextNode();
    while (node) {
      try {
        range.selectNodeContents(node);
        var nodeRects = range.getClientRects();
        for (var i = 0; i < nodeRects.length; i++) {
          var r = nodeRects[i];
          if (r.width > 0 && r.height > 0) rects.push(r);
        }
      } catch (_) {
        /* ignore detached ranges */
      }
      node = walker.nextNode();
    }
    return mergeLineRects(rects);
  }

  function readThicknessPx(block) {
    var raw = getComputedStyle(block).getPropertyValue("--cgw-cv-ruled-thickness");
    var n = parseFloat(raw);
    return Number.isFinite(n) && n > 0 ? n : 1;
  }

  function readMarginTickRatio(block) {
    var raw = getComputedStyle(block).getPropertyValue("--cgw-cv-margin-rail-tick");
    var match = String(raw || "").match(/([\d.]+)/);
    if (!match) return 0.42;
    var n = parseFloat(match[1]);
    return Number.isFinite(n) ? n : 0.42;
  }

  function ensureLayer(host) {
    var layer = host.querySelector("." + LAYER_CLASS);
    if (layer) {
      layer.replaceChildren();
      return layer;
    }
    host.setAttribute("data-cgw-reading-guide-host", "1");
    if (getComputedStyle(host).position === "static") {
      host.setAttribute("data-cgw-reading-guide-was-static", "1");
      host.style.position = "relative";
    }
    layer = document.createElement("div");
    layer.className = LAYER_CLASS;
    layer.setAttribute("aria-hidden", "true");
    host.insertBefore(layer, host.firstChild);
    return layer;
  }

  function addMark(layer, className, styleObj) {
    var mark = document.createElement("div");
    mark.className = MARK_CLASS + " " + className;
    Object.keys(styleObj).forEach(function (key) {
      mark.style[key] = styleObj[key];
    });
    layer.appendChild(mark);
  }

  function bandShadeOddLines() {
    return rootEl().getAttribute("data-cgw-ruled-band-invert") !== "1";
  }

  function paintBlock(block, style, clip, globalLineIndex) {
    var lines = collectLineRects(block);
    if (!lines.length) return globalLineIndex;

    var blockRect = block.getBoundingClientRect();
    var layer = ensureLayer(block);
    var thickness = readThicknessPx(block);
    var tickRatio = readMarginTickRatio(block);
    var shadeOdd = bandShadeOddLines();

    lines.forEach(function (line, index) {
      var lineIndex = globalLineIndex + index;
      var x = clip ? line.left - blockRect.left : 0;
      var width = clip ? line.width : blockRect.width;
      var y = line.top - blockRect.top;
      var lineHeight = line.height;

      if (style === "line") {
        addMark(layer, "cgw-reading-guide-mark--line", {
          left: x + "px",
          top: y + lineHeight - thickness + "px",
          width: width + "px",
          height: thickness + "px",
        });
        return;
      }

      if (style === "band") {
        var shaded = shadeOdd ? lineIndex % 2 === 0 : lineIndex % 2 === 1;
        if (!shaded) return;
        addMark(layer, "cgw-reading-guide-mark--band", {
          left: x + "px",
          top: y + "px",
          width: width + "px",
          height: lineHeight + "px",
        });
        return;
      }

      if (style === "underline") {
        addMark(layer, "cgw-reading-guide-mark--underline", {
          left: x + "px",
          top: y + lineHeight - thickness + "px",
          width: width + "px",
          height: thickness + "px",
        });
        return;
      }

      if (style === "margin-rail") {
        var tickHeight = lineHeight * tickRatio;
        var tickLeft = clip ? line.left - blockRect.left - thickness : 0;
        addMark(layer, "cgw-reading-guide-mark--margin-rail", {
          left: tickLeft + "px",
          top: y + "px",
          width: thickness + "px",
          height: tickHeight + "px",
        });
      }
    });
    return globalLineIndex + lines.length;
  }

  function clearGuides(root) {
    if (!root) return;
    root.querySelectorAll("[data-cgw-reading-guide-host]").forEach(function (host) {
      var layer = host.querySelector("." + LAYER_CLASS);
      if (layer) layer.remove();
      if (host.getAttribute("data-cgw-reading-guide-was-static") === "1") {
        host.style.position = "";
        host.removeAttribute("data-cgw-reading-guide-was-static");
      }
      host.removeAttribute("data-cgw-reading-guide-host");
    });
  }

  function paintGuides(container) {
    if (!container) return;
    clearGuides(container);
    if (!isActive()) return;

    var style = currentStyle();
    if (!JS_STYLES[style]) return;

    var clip = clipToText();
    var blockCount = 0;
    var globalLineIndex = 0;
    container.querySelectorAll(BLOCK_SELECTOR).forEach(function (block) {
      if (block.closest('[data-cgw-streaming="1"]')) return;
      blockCount += 1;
      globalLineIndex = paintBlock(block, style, clip, globalLineIndex);
    });

    var markCount = container.querySelectorAll("." + MARK_CLASS).length;
    if (blockCount > 0 && markCount === 0) {
      scheduleLayoutRetry(container);
    } else if (markCount > 0) {
      clearLayoutRetry();
    }
  }

  function resolveContainer(container) {
    if (container && container.nodeType === 1) return container;
    return document.getElementById(CONTAINER_ID);
  }

  function applyReadingGuides(container) {
    container = resolveContainer(container);
    if (!container) return;
    if (containerPaintDeferred(container)) {
      if (paintScheduled != null) cancelAnimationFrame(paintScheduled);
      paintScheduled = requestAnimationFrame(function () {
        paintScheduled = requestAnimationFrame(function () {
          paintScheduled = null;
          applyReadingGuides(container);
        });
      });
      return;
    }
    paintGuides(container);
    ensureResizeObserver(container);
  }

  function scheduleApply(container) {
    if (paintScheduled != null) cancelAnimationFrame(paintScheduled);
    paintScheduled = requestAnimationFrame(function () {
      paintScheduled = null;
      applyReadingGuides(container);
    });
  }

  function ensureResizeObserver(container) {
    if (typeof ResizeObserver === "undefined") return;
    if (observedContainer === container && resizeObserver) return;
    if (resizeObserver) resizeObserver.disconnect();
    observedContainer = container;
    resizeObserver = new ResizeObserver(function () {
      if (!isActive()) return;
      scheduleApply(container);
    });
    resizeObserver.observe(container);
  }

  function teardownResizeObserver() {
    if (resizeObserver) resizeObserver.disconnect();
    resizeObserver = null;
    observedContainer = null;
  }

  globalThis.__cgwApplyReadingGuides = applyReadingGuides;
  globalThis.__cgwScheduleReadingGuides = scheduleApply;
  globalThis.__cgwTeardownReadingGuides = function () {
    clearLayoutRetry();
    teardownResizeObserver();
    clearGuides(resolveContainer());
  };

  if (typeof MutationObserver !== "undefined") {
    var attrObserver = new MutationObserver(function (mutations) {
      var relevant = false;
      for (var i = 0; i < mutations.length; i++) {
        var name = mutations[i].attributeName;
        if (
          name === "data-cgw-ruled-lines" ||
          name === "data-cgw-ruled-style" ||
          name === "data-cgw-ruled-js" ||
          name === "data-cgw-ruled-clip" ||
          name === "data-cgw-ruled-band-invert"
        ) {
          relevant = true;
          break;
        }
      }
      if (relevant) scheduleApply();
    });
    attrObserver.observe(document.documentElement, {
      attributes: true,
      attributeFilter: [
        "data-cgw-ruled-lines",
        "data-cgw-ruled-style",
        "data-cgw-ruled-js",
        "data-cgw-ruled-clip",
        "data-cgw-ruled-band-invert",
      ],
    });
  }

  if (isActive()) {
    scheduleApply();
  }

  if (
    typeof document !== "undefined" &&
    document.fonts &&
    typeof document.fonts.ready === "object" &&
    typeof document.fonts.ready.then === "function"
  ) {
    document.fonts.ready.then(function () {
      if (isActive()) scheduleApply();
    });
  }
})();
