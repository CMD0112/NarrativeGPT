/**
 * ChatGPT Wrapper — rich block extraction, sanitization, and overlay rendering.
 */
(function () {
  if (globalThis.__cgwContinuousRichFormatBooted) return;
  globalThis.__cgwContinuousRichFormatBooted = true;

  try {
    if (typeof marked !== "undefined" && typeof marked.setOptions === "function") {
      marked.setOptions({ breaks: true, gfm: true });
    }
  } catch (_markedOpts) {
    /* ignore */
  }

  var LANG_RE = /^[\w#+\-.]+$/;

  var ALLOWED_TAGS = {
    p: 1,
    h1: 1,
    h2: 1,
    h3: 1,
    h4: 1,
    h5: 1,
    h6: 1,
    ul: 1,
    ol: 1,
    li: 1,
    blockquote: 1,
    hr: 1,
    strong: 1,
    em: 1,
    b: 1,
    i: 1,
    u: 1,
    s: 1,
    del: 1,
    code: 1,
    pre: 1,
    a: 1,
    span: 1,
    br: 1,
    img: 1,
    table: 1,
    thead: 1,
    tbody: 1,
    tr: 1,
    th: 1,
    td: 1,
    sup: 1,
    sub: 1,
    input: 1,
    div: 1,
    annotation: 1,
    semantics: 1,
    mrow: 1,
    mi: 1,
    mo: 1,
    mn: 1,
    mtext: 1,
    ms: 1,
    mfrac: 1,
    msup: 1,
    msub: 1,
    msubsup: 1,
    munder: 1,
    mover: 1,
    munderover: 1,
    mpadded: 1,
    mspace: 1,
  };

  var STRIP_TAGS = {
    button: 1,
    svg: 1,
    script: 1,
    iframe: 1,
    form: 1,
    nav: 1,
    aside: 1,
  };

  function isAllowedAttr(name) {
    if (!name) return false;
    var lower = name.toLowerCase();
    if (lower.indexOf("on") === 0) return false;
    if (lower === "style") return false;
    if (
      lower === "href" ||
      lower === "target" ||
      lower === "rel" ||
      lower === "class" ||
      lower === "alt" ||
      lower === "src" ||
      lower === "type" ||
      lower === "checked" ||
      lower === "disabled" ||
      lower === "colspan" ||
      lower === "rowspan"
    ) {
      return true;
    }
    if (lower.indexOf("aria-") === 0) return true;
    if (lower.indexOf("data-") === 0) return true;
    return false;
  }

  function isSafeImageSrc(src) {
    if (!src) return false;
    var s = src.trim().toLowerCase();
    if (s.indexOf("https://") === 0) return true;
    if (s.indexOf("data:image/") === 0) return true;
    return false;
  }

  function sanitizeNode(node, outParent) {
    if (!node) return;
    if (node.nodeType === 3) {
      outParent.appendChild(document.createTextNode(node.textContent || ""));
      return;
    }
    if (node.nodeType !== 1) return;

    var tag = node.tagName ? node.tagName.toLowerCase() : "";
    if (STRIP_TAGS[tag]) return;

    if (tag === "img") {
      var src = node.getAttribute("src") || "";
      var alt = node.getAttribute("alt") || "image";
      if (isSafeImageSrc(src)) {
        var img = document.createElement("img");
        img.setAttribute("src", src);
        img.setAttribute("alt", alt);
        img.setAttribute("loading", "lazy");
        outParent.appendChild(img);
      } else {
        outParent.appendChild(document.createTextNode("[Image: " + alt + "]"));
      }
      return;
    }

    if (tag === "a") {
      var a = document.createElement("a");
      var href = node.getAttribute("href") || "";
      if (href.indexOf("javascript:") === 0) href = "#";
      a.setAttribute("href", href);
      a.setAttribute("target", "_blank");
      a.setAttribute("rel", "noopener noreferrer");
      var attrs = node.attributes;
      var i;
      for (i = 0; i < attrs.length; i++) {
        var an = attrs[i].name;
        if (isAllowedAttr(an) && an !== "href" && an !== "target" && an !== "rel") {
          a.setAttribute(an, attrs[i].value);
        }
      }
      for (i = 0; i < node.childNodes.length; i++) {
        sanitizeNode(node.childNodes[i], a);
      }
      outParent.appendChild(a);
      return;
    }

    if (tag === "input") {
      var type = (node.getAttribute("type") || "").toLowerCase();
      if (type === "checkbox") {
        var cb = document.createElement("input");
        cb.setAttribute("type", "checkbox");
        cb.setAttribute("disabled", "disabled");
        if (node.hasAttribute("checked")) cb.setAttribute("checked", "checked");
        outParent.appendChild(cb);
      }
      return;
    }

    if (!ALLOWED_TAGS[tag]) {
      for (var j = 0; j < node.childNodes.length; j++) {
        sanitizeNode(node.childNodes[j], outParent);
      }
      return;
    }

    var el = document.createElement(tag);
    var attrs2 = node.attributes;
    for (var k = 0; k < attrs2.length; k++) {
      var attrName = attrs2[k].name;
      if (isAllowedAttr(attrName)) {
        el.setAttribute(attrName, attrs2[k].value);
      }
    }
    for (var c = 0; c < node.childNodes.length; c++) {
      sanitizeNode(node.childNodes[c], el);
    }
    outParent.appendChild(el);
  }

  function sanitizeToHtml(el) {
    if (!el) return "";
    var wrap = document.createElement("div");
    if (el.nodeType === 1) {
      sanitizeNode(el, wrap);
    } else {
      wrap.textContent = el.textContent || "";
    }
    return wrap.innerHTML.trim();
  }

  function sanitizeCloneHtml(el) {
    return sanitizeToHtml(el);
  }

  function blockPlainText(block) {
    if (block.text) return block.text;
    if (block.html) {
      var d = document.createElement("div");
      d.innerHTML = block.html;
      return (d.textContent || "").trim();
    }
    return "";
  }

  function blockSignature(block) {
    if (block.kind === "table" && block.rows) {
      return block.rows
        .map(function (row) {
          return row.join("\t");
        })
        .join("\n");
    }
    if (block.html) {
      return block.html.length + "\x00" + blockPlainText(block);
    }
    return block.text || "";
  }

  function extractCodeFromPre(preEl) {
    var codeEl = preEl.querySelector("code");
    var raw = codeEl ? codeEl.textContent || "" : preEl.textContent || "";
    raw = raw.replace(/\r\n/g, "\n");
    if (!raw.trim()) return null;

    var lang = "";
    if (codeEl && codeEl.className) {
      var cls = typeof codeEl.className === "string" ? codeEl.className : "";
      var langMatch = cls.match(/language-([\w#+\-.]+)/);
      if (langMatch) lang = langMatch[1];
    }

    var lines = raw.split("\n");
    var text = raw.trim();
    if (!lang && lines.length >= 2) {
      var first = lines[0].trim();
      if (LANG_RE.test(first)) {
        lang = first;
        text = lines.slice(1).join("\n");
        if (text.charAt(0) === "\n") text = text.slice(1);
        text = text.trim();
      }
    }
    if (!text) return null;
    return { kind: "code", lang: lang, text: text };
  }

  function findPreInSubtree(node) {
    if (!node || node.nodeType !== 1) return null;
    var tag = node.tagName ? node.tagName.toLowerCase() : "";
    if (tag === "pre") return node;
    var pres = node.querySelectorAll("pre");
    if (pres.length === 1) return pres[0];
    return null;
  }

  function isCodeBlockWrapper(node) {
    if (!node || node.nodeType !== 1) return false;
    var cls = node.className && typeof node.className === "string" ? node.className : "";
    if (cls.indexOf("code-block") >= 0) return true;
    var pre = findPreInSubtree(node);
    if (!pre || node.querySelectorAll("pre").length !== 1) return false;
    var tag = node.tagName ? node.tagName.toLowerCase() : "";
    return tag === "div" || tag === "section";
  }

  function isAttachmentNode(node) {
    if (!node || node.nodeType !== 1) return false;
    var cls = node.className && typeof node.className === "string" ? node.className : "";
    var tid = node.getAttribute("data-testid") || "";
    if (cls.indexOf("attachment") >= 0 || cls.indexOf("file") >= 0) return true;
    if (tid.indexOf("file") >= 0 || tid.indexOf("attachment") >= 0) return true;
    if (node.querySelector('[class*="attachment"], [class*="file-name"], [data-testid*="file"]')) {
      return true;
    }
    return false;
  }

  function isMathNode(node) {
    if (!node || node.nodeType !== 1) return false;
    if (node.matches && node.matches(".katex, .katex-display, .katex-html, [data-testid*='math']")) {
      return true;
    }
    if (node.querySelector && node.querySelector(".katex, .katex-display")) return true;
    return false;
  }

  function cellText(cell) {
    return (cell.innerText || cell.textContent || "").trim().replace(/\s+/g, " ");
  }

  function extractTable(tableEl) {
    var rows = [];
    var headerRow = false;
    tableEl.querySelectorAll("tr").forEach(function (tr, rowIndex) {
      var cells = [];
      var rowHasTh = false;
      tr.querySelectorAll("th, td").forEach(function (cell) {
        if (cell.tagName && cell.tagName.toLowerCase() === "th") rowHasTh = true;
        cells.push(cellText(cell));
      });
      if (cells.length) {
        rows.push(cells);
        if (rowIndex === 0 && rowHasTh) headerRow = true;
      }
    });
    if (!rows.length) return null;
    return { kind: "table", rows: rows, headerRow: headerRow };
  }

  function extractAriaTable(root) {
    var tableRole = root.matches('[role="table"]')
      ? root
      : root.querySelector('[role="table"]');
    if (!tableRole) return null;

    var rows = [];
    var headerRow = false;
    tableRole.querySelectorAll('[role="row"]').forEach(function (row, rowIndex) {
      var cells = [];
      var rowHasHeader = false;
      row
        .querySelectorAll(
          '[role="cell"], [role="columnheader"], [role="rowheader"]'
        )
        .forEach(function (cell) {
          var role = (cell.getAttribute("role") || "").toLowerCase();
          if (role === "columnheader" || role === "rowheader") rowHasHeader = true;
          cells.push(cellText(cell));
        });
      if (cells.length) {
        rows.push(cells);
        if (rowIndex === 0 && rowHasHeader) headerRow = true;
      }
    });
    if (!rows.length) return null;
    return { kind: "table", rows: rows, headerRow: headerRow };
  }

  function pushHtmlBlock(blocks, kind, el, extra) {
    var html = sanitizeCloneHtml(el);
    if (!html && kind !== "hr") return;
    var block = { kind: kind, html: html };
    if (extra) {
      Object.keys(extra).forEach(function (k) {
        block[k] = extra[k];
      });
    }
    blocks.push(block);
  }

  function pushImageBlock(blocks, imgEl) {
    var src = imgEl.getAttribute("src") || "";
    var alt = imgEl.getAttribute("alt") || "image";
    if (!isSafeImageSrc(src)) {
      blocks.push({ kind: "fallback", text: "[Image: " + alt + "]" });
      return;
    }
    blocks.push({ kind: "image", src: src, alt: alt });
  }

  function hasNestedBlocks(node) {
    return !!node.querySelector(
      'pre, table, ul, ol, blockquote, hr, .katex, [role="table"]'
    );
  }

  function walkBlockNode(node, blocks) {
    if (!node || node.nodeType !== 1) return;
    var tag = node.tagName ? node.tagName.toLowerCase() : "";

    if (isMathNode(node)) {
      pushHtmlBlock(blocks, "math", node);
      return;
    }
    if (isAttachmentNode(node)) {
      var label = (node.innerText || "").trim().replace(/\s+/g, " ");
      if (label) {
        blocks.push({ kind: "attachment", label: label, html: sanitizeCloneHtml(node) });
      }
      return;
    }
    if (tag === "img") {
      pushImageBlock(blocks, node);
      return;
    }
    if (tag === "pre") {
      var codeBlock = extractCodeFromPre(node);
      if (codeBlock) blocks.push(codeBlock);
      return;
    }
    if (isCodeBlockWrapper(node)) {
      var pre = findPreInSubtree(node);
      if (pre) {
        var cb = extractCodeFromPre(pre);
        if (cb) blocks.push(cb);
      }
      return;
    }
    if (tag === "ul" || tag === "ol") {
      pushHtmlBlock(blocks, "list", node, { ordered: tag === "ol" });
      return;
    }
    if (tag === "table") {
      var tbl = extractTable(node);
      if (tbl) blocks.push(tbl);
      return;
    }
    if (node.getAttribute && node.getAttribute("role") === "table") {
      var ariaTable = extractAriaTable(node);
      if (ariaTable) blocks.push(ariaTable);
      return;
    }
    if (tag === "hr") {
      blocks.push({ kind: "hr" });
      return;
    }
    if (tag === "blockquote") {
      pushHtmlBlock(blocks, "quote", node);
      return;
    }
    if (/^h[1-6]$/.test(tag)) {
      var level = parseInt(tag.charAt(1), 10);
      pushHtmlBlock(blocks, "heading", node, { level: level });
      return;
    }
    if (tag === "p") {
      if (hasNestedBlocks(node)) {
        Array.prototype.forEach.call(node.children, function (ch) {
          walkBlockNode(ch, blocks);
        });
        return;
      }
      pushHtmlBlock(blocks, "prose", node);
      return;
    }

    if (node.children.length === 0) {
      var t = (node.innerText || "").trim();
      if (t) blocks.push({ kind: "fallback", text: t });
      return;
    }

    Array.prototype.forEach.call(node.children, function (ch) {
      walkBlockNode(ch, blocks);
    });
  }

  function plainTextToProseHtml(text) {
    text = (text || "").trim();
    if (!text) return "";
    if (typeof marked !== "undefined" && typeof marked.parse === "function") {
      try {
        var html = marked.parse(text, { breaks: true, gfm: true });
        if (typeof DOMPurify !== "undefined" && typeof DOMPurify.sanitize === "function") {
          return DOMPurify.sanitize(html, {
            USE_PROFILES: { html: true },
            ADD_ATTR: ["class", "target", "rel", "type", "checked", "disabled"],
          });
        }
        var tmp = document.createElement("div");
        tmp.innerHTML = html;
        return sanitizeCloneHtml(tmp);
      } catch (_e) {
        /* fall through */
      }
    }
    return sanitizeCloneHtml(document.createTextNode(text));
  }

  function plainTextToProseBlocks(text) {
    var blocks = [];
    text = (text || "").trim();
    if (!text) return blocks;
    var html = plainTextToProseHtml(text);
    if (!html) return blocks;
    var wrap = document.createElement("div");
    wrap.innerHTML = html;
    if (wrap.children.length) {
      Array.prototype.forEach.call(wrap.children, function (ch) {
        walkBlockNode(ch, blocks);
      });
    } else {
      blocks.push({ kind: "prose", html: html });
    }
    if (!blocks.length) {
      blocks.push({ kind: "prose", html: html });
    }
    return blocks;
  }

  function splitParagraphFallback(text) {
    return plainTextToProseBlocks(text);
  }

  function blocksFromRoot(root) {
    var blocks = [];
    var hasStructure = root.querySelector(
      'pre, p, ul, ol, table, hr, blockquote, h1, h2, h3, h4, h5, h6, img, .katex, [role="table"], [class*="attachment"]'
    );
    if (!hasStructure) {
      var plain = (root.innerText || "").trim();
      return plain ? plainTextToProseBlocks(plain) : [];
    }

    if (root.children.length) {
      Array.prototype.forEach.call(root.children, function (ch) {
        walkBlockNode(ch, blocks);
      });
    } else {
      walkBlockNode(root, blocks);
    }

    if (!blocks.length) {
      var fallback = (root.innerText || "").trim();
      return fallback ? plainTextToProseBlocks(fallback) : [];
    }

    return blocks;
  }

  function appendTableBlock(parent, block) {
    var scroll = document.createElement("div");
    scroll.className = "cgw-continuous-block cgw-continuous-block--table-scroll";

    var table = document.createElement("table");
    table.className = "cgw-continuous-table";

    var bodyRows = block.rows;
    if (block.headerRow && bodyRows.length) {
      var thead = document.createElement("thead");
      var headTr = document.createElement("tr");
      bodyRows[0].forEach(function (cell) {
        var th = document.createElement("th");
        th.textContent = cell;
        headTr.appendChild(th);
      });
      thead.appendChild(headTr);
      table.appendChild(thead);
      bodyRows = bodyRows.slice(1);
    }

    var tbody = document.createElement("tbody");
    bodyRows.forEach(function (row) {
      var tr = document.createElement("tr");
      row.forEach(function (cell) {
        var td = document.createElement("td");
        td.textContent = cell;
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    scroll.appendChild(table);
    parent.appendChild(scroll);
  }

  function appendCodeBlock(parent, block) {
    var wrap = document.createElement("div");
    wrap.className = "cgw-continuous-block--code-wrap";

    if (block.lang) {
      var langEl = document.createElement("div");
      langEl.className = "cgw-continuous-code-lang";
      langEl.textContent = block.lang;
      wrap.appendChild(langEl);
    }

    var pre = document.createElement("pre");
    pre.className = "cgw-continuous-block cgw-continuous-block--code";
    pre.textContent = block.text;
    wrap.appendChild(pre);
    parent.appendChild(wrap);
  }

  function setInnerHtml(el, html) {
    el.innerHTML = html || "";
  }

  function appendRichBlock(parent, block) {
    if (block.kind === "code") {
      appendCodeBlock(parent, block);
      return;
    }
    if (block.kind === "table") {
      appendTableBlock(parent, block);
      return;
    }
    if (block.kind === "heading") {
      var level = block.level || 2;
      if (level < 1) level = 1;
      if (level > 6) level = 6;
      var h = document.createElement("h" + level);
      h.className = "cgw-continuous-heading cgw-continuous-block";
      setInnerHtml(h, block.html);
      parent.appendChild(h);
      return;
    }
    if (block.kind === "prose") {
      var prose = document.createElement("div");
      prose.className = "cgw-continuous-prose cgw-continuous-block";
      setInnerHtml(prose, block.html);
      parent.appendChild(prose);
      return;
    }
    if (block.kind === "list") {
      var listWrap = document.createElement("div");
      listWrap.className =
        "cgw-continuous-list cgw-continuous-block" +
        (block.ordered ? " cgw-continuous-list--ordered" : " cgw-continuous-list--unordered");
      setInnerHtml(listWrap, block.html);
      parent.appendChild(listWrap);
      return;
    }
    if (block.kind === "quote") {
      var q = document.createElement("blockquote");
      q.className = "cgw-continuous-quote cgw-continuous-block";
      setInnerHtml(q, block.html);
      parent.appendChild(q);
      return;
    }
    if (block.kind === "hr") {
      var hr = document.createElement("hr");
      hr.className = "cgw-continuous-hr cgw-continuous-block";
      parent.appendChild(hr);
      return;
    }
    if (block.kind === "math") {
      var math = document.createElement("div");
      math.className = "cgw-continuous-math cgw-continuous-block";
      setInnerHtml(math, block.html);
      parent.appendChild(math);
      return;
    }
    if (block.kind === "image") {
      var fig = document.createElement("figure");
      fig.className = "cgw-continuous-figure cgw-continuous-block";
      var img = document.createElement("img");
      img.src = block.src;
      img.alt = block.alt || "";
      img.loading = "lazy";
      fig.appendChild(img);
      if (block.alt) {
        var cap = document.createElement("figcaption");
        cap.className = "cgw-continuous-figure__caption";
        cap.textContent = block.alt;
        fig.appendChild(cap);
      }
      parent.appendChild(fig);
      return;
    }
    if (block.kind === "attachment") {
      var att = document.createElement("div");
      att.className = "cgw-continuous-attachment cgw-continuous-block";
      if (block.html) {
        setInnerHtml(att, block.html);
      } else {
        att.textContent = block.label || "Attachment";
      }
      parent.appendChild(att);
      return;
    }
    var fallback = document.createElement("div");
    fallback.className = "cgw-continuous-block cgw-continuous-fallback";
    if (block.html) setInnerHtml(fallback, block.html);
    else fallback.textContent = block.text || blockPlainText(block);
    parent.appendChild(fallback);
  }

  globalThis.__cgwContinuousRichFormat = {
    blockSignature: blockSignature,
    blockPlainText: blockPlainText,
    blocksFromRoot: blocksFromRoot,
    splitParagraphFallback: splitParagraphFallback,
    plainTextToProseBlocks: plainTextToProseBlocks,
    appendRichBlock: appendRichBlock,
    appendCodeBlock: appendCodeBlock,
    appendTableBlock: appendTableBlock,
  };
})();
