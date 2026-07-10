using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery.Automation;

/// <summary>
/// Minimal in-page helpers for project knowledge upload (mirrors <c>chatgpt-api-bridge.js</c>).
/// </summary>
internal static class ProjectKnowledgeDomScript
{
    public const string MarkAttribute = "data-cgw-project-file-input";

    public static string PrepareUiForGizmo(string gizmoId)
    {
        var escapedGizmo = JsonSerializer.Serialize(gizmoId);
        return $$"""
        (async function() {
          var gizmoId = {{escapedGizmo}};
          function sleep(ms) { return new Promise(function(r) { setTimeout(r, ms); }); }
          function isOnProjectPage() {
            var href = (location.href || "").toLowerCase();
            var gid = (gizmoId || "").toLowerCase();
            if (!gid) return false;
            return href.indexOf("/g/" + gid + "/project") >= 0
              || href.indexOf("project=" + encodeURIComponent(gizmoId).toLowerCase()) >= 0;
          }
          function isInsideComposer(el) {
            if (!el) return false;
            return !!(el.closest('[data-testid="composer"]') || el.closest("#cgw-play-composer-root"));
          }
          function isInSidebar(el) {
            if (!el) return false;
            return !!(el.closest("nav") || el.closest('[data-testid="sidebar"]') || el.closest("aside"));
          }
          function isSafePrepareClick(el) {
            if (!el || isInsideComposer(el) || isInSidebar(el)) return false;
            if (el.closest('[role="dialog"]') || el.closest("main") || el.closest("header")) return true;
            if (el.closest('[role="tablist"]') || el.getAttribute("role") === "tab") return true;
            return false;
          }
          function clickFirstVisible(selector, clicked, label) {
            var nodes = document.querySelectorAll(selector);
            for (var i = 0; i < nodes.length; i++) {
              var el = nodes[i];
              if (!isSafePrepareClick(el)) continue;
              var style = window.getComputedStyle(el);
              if (style.display === "none" || style.visibility === "hidden") continue;
              if (el.disabled) continue;
              try { el.click(); clicked.push(label || selector); return true; } catch (_e) {}
            }
            return false;
          }
          function clickButtonsByText(pattern, clicked, label) {
            var re = pattern instanceof RegExp ? pattern : new RegExp(pattern, "i");
            var nodes = document.querySelectorAll("button, [role='tab'], [role='button']");
            for (var i = 0; i < nodes.length; i++) {
              var el = nodes[i];
              if (!isSafePrepareClick(el)) continue;
              var text = (el.textContent && el.textContent.trim()) || el.getAttribute("aria-label") || "";
              if (!re.test(text)) continue;
              var style = window.getComputedStyle(el);
              if (style.display === "none" || style.visibility === "hidden") continue;
              try { el.click(); clicked.push(label || text.slice(0, 40)); return true; } catch (_e2) {}
            }
            return false;
          }
          function scoreProjectFileInput(el) {
            var score = 0;
            if (el.closest('[role="dialog"]')) score += 12;
            if (el.closest("aside") && !isInSidebar(el)) score += 8;
            if (el.closest('[class*="project"]')) score += 5;
            if (el.closest("main")) score += 2;
            var accept = el.getAttribute("accept") || "";
            if (/\.md|text\/|markdown/i.test(accept)) score += 3;
            if (el.multiple) score += 1;
            var testId = el.getAttribute("data-testid") || "";
            if (/file|upload|knowledge|project/i.test(testId)) score += 4;
            return score;
          }
          function findBestProjectFileInput(requireDialog) {
            var marked = document.querySelectorAll("input[{{MarkAttribute}}=\"1\"]");
            for (var m = 0; m < marked.length; m++) marked[m].removeAttribute("{{MarkAttribute}}");
            var nodes = document.querySelectorAll('input[type="file"]');
            var best = null, bestScore = -1;
            for (var i = 0; i < nodes.length; i++) {
              var el = nodes[i];
              if (isInsideComposer(el)) continue;
              if (requireDialog && !el.closest('[role="dialog"]')) continue;
              var score = scoreProjectFileInput(el);
              if (score > bestScore) { bestScore = score; best = el; }
            }
            var minScore = requireDialog ? 12 : 2;
            if (!best || bestScore < minScore) return { found: false, score: bestScore };
            best.setAttribute("{{MarkAttribute}}", "1");
            return {
              found: true,
              score: bestScore,
              accept: best.getAttribute("accept") || "",
              testId: best.getAttribute("data-testid") || "",
              multiple: !!best.multiple,
              inDialog: !!best.closest('[role="dialog"]'),
            };
          }
          async function waitForSourcesDialog(maxMs) {
            var deadline = Date.now() + (maxMs || 3000);
            while (Date.now() < deadline) {
              if (document.querySelector('[role="dialog"]')) return true;
              await sleep(200);
            }
            return !!document.querySelector('[role="dialog"]');
          }
          function clickSourcesTab(clicked) {
            var nodes = document.querySelectorAll('[role="tab"], button, [role="button"]');
            for (var i = 0; i < nodes.length; i++) {
              var el = nodes[i];
              if (!isSafePrepareClick(el)) continue;
              var text = (el.textContent && el.textContent.trim()) || el.getAttribute("aria-label") || "";
              if (!/^sources$/i.test(text)) continue;
              var style = window.getComputedStyle(el);
              if (style.display === "none" || style.visibility === "hidden") continue;
              if (el.disabled) continue;
              try { el.click(); clicked.push("tab:sources"); return true; } catch (_e) {}
            }
            if (clickFirstVisible('[role="tab"][aria-label*="Sources"]', clicked, "tab:sources-role")) return true;
            if (clickFirstVisible('button[aria-label*="Sources"]', clicked, "tab:sources-aria")) return true;
            return clickButtonsByText(/^sources$/i, clicked, "tab:sources-fallback");
          }
          function clickAddSources(clicked) {
            var nodes = document.querySelectorAll("button, [role='button']");
            for (var i = 0; i < nodes.length; i++) {
              var el = nodes[i];
              if (!isSafePrepareClick(el)) continue;
              var text = (el.textContent && el.textContent.trim()) || el.getAttribute("aria-label") || "";
              if (!/^add sources$/i.test(text)) continue;
              var style = window.getComputedStyle(el);
              if (style.display === "none" || style.visibility === "hidden") continue;
              if (el.disabled) continue;
              try { el.click(); clicked.push("add-sources"); return true; } catch (_e2) {}
            }
            var addSelectors = [
              'button[aria-label*="Add sources"]',
              'button[aria-label*="Add Sources"]',
              '[data-testid*="add-sources"]',
              '[data-testid*="add-source"]',
            ];
            for (var a = 0; a < addSelectors.length; a++) {
              if (clickFirstVisible(addSelectors[a], clicked, addSelectors[a])) return true;
            }
            return clickButtonsByText(/add sources/i, clicked, "add-sources-phrase");
          }
          function guardProject(context) {
            if (isOnProjectPage()) return null;
            return context + ":left_project:" + location.href;
          }

          var clicked = [];
          if (!isOnProjectPage()) {
            return { ok: false, error: "not_on_project_before_prepare", clicked: clicked, href: location.href };
          }

          if (!clickSourcesTab(clicked)) {
            return { ok: false, error: "sources_tab_not_found", clicked: clicked, href: location.href };
          }
          await sleep(500);
          var left = guardProject("after_sources_tab");
          if (left) return { ok: false, error: left, clicked: clicked, href: location.href };

          if (!clickAddSources(clicked)) {
            return { ok: false, error: "add_sources_not_found", clicked: clicked, href: location.href };
          }
          await waitForSourcesDialog(4000);
          await sleep(300);
          left = guardProject("after_add_sources");
          if (left) return { ok: false, error: left, clicked: clicked, href: location.href };

          var fileInput = findBestProjectFileInput(true);
          if (!fileInput || !fileInput.found) {
            fileInput = findBestProjectFileInput(false);
          }
          var onProject = isOnProjectPage();
          return {
            ok: !!(fileInput && fileInput.found && onProject),
            strategy: "sources_tab_add_sources",
            clicked: clicked,
            fileInput: fileInput,
            href: location.href,
            error: onProject ? null : "left_project:" + location.href,
          };
        })()
        """;
    }

    public static string MarkFileInputForUpload => $$"""
        (function() {
          function isInsideComposer(el) {
            if (!el) return false;
            return !!(el.closest('[data-testid="composer"]') || el.closest("#cgw-play-composer-root"));
          }
          function scoreProjectFileInput(el) {
            var score = 0;
            if (el.closest('[role="dialog"]')) score += 12;
            if (el.closest("main")) score += 4;
            if (el.closest('[class*="project"]')) score += 5;
            var testId = el.getAttribute("data-testid") || "";
            if (/file|upload|knowledge|source|project/i.test(testId)) score += 4;
            return score;
          }
          var marked = document.querySelectorAll("input[{{MarkAttribute}}=\"1\"]");
          for (var m = 0; m < marked.length; m++) marked[m].removeAttribute("{{MarkAttribute}}");
          var nodes = document.querySelectorAll('[role="dialog"] input[type="file"], main input[type="file"]');
          var best = null, bestScore = -1;
          for (var i = 0; i < nodes.length; i++) {
            var el = nodes[i];
            if (isInsideComposer(el)) continue;
            var score = scoreProjectFileInput(el);
            if (score > bestScore) { bestScore = score; best = el; }
          }
          if (!best || bestScore < 4) {
            return { ok: false, error: "automation_file_input_not_found", fileInput: { found: false, score: bestScore }, href: location.href };
          }
          best.setAttribute("{{MarkAttribute}}", "1");
          return {
            ok: true,
            fileInput: {
              found: true,
              score: bestScore,
              inDialog: !!best.closest('[role="dialog"]'),
            },
            href: location.href,
          };
        })()
        """;

    public static string PrepareDiagnostics => """
        (function() {
          function label(el) {
            return ((el.textContent && el.textContent.trim()) || el.getAttribute("aria-label") || "").slice(0, 60);
          }
          var tabs = [];
          var tabNodes = document.querySelectorAll('main [role="tablist"] [role="tab"], [role="tab"]');
          for (var i = 0; i < tabNodes.length && tabs.length < 12; i++) {
            var el = tabNodes[i];
            tabs.push({
              text: label(el),
              selected: el.getAttribute("aria-selected") === "true",
            });
          }
          var buttons = [];
          var btnNodes = document.querySelectorAll("main button, main [role='button']");
          for (var b = 0; b < btnNodes.length && buttons.length < 20; b++) {
            var btn = btnNodes[b];
            var text = label(btn);
            if (!text) continue;
            if (!/source|upload|add|file/i.test(text)) continue;
            buttons.push(text);
          }
          return { href: location.href, tabs: tabs, buttons: buttons };
        })()
        """;

    public static string PollUploadForFile(string remoteFileName)
    {
        var escapedName = JsonSerializer.Serialize(remoteFileName);
        return $$"""
        (function() {
          var fileName = {{escapedName}};
          var base = (fileName || "").split("/").pop().split("\\").pop();
          if (!base) return { ok: false, ready: false, pending: false, error: "missing_file_name", href: location.href };
          var lowerBase = base.toLowerCase();
          var nameSeen = false;
          var roots = document.querySelectorAll("main, [role='dialog']");
          for (var r = 0; r < roots.length && !nameSeen; r++) {
            var nodes = roots[r].querySelectorAll(
              '[data-testid*="file"], [class*="file-name"], li, tr, [role="listitem"], [role="row"]'
            );
            for (var i = 0; i < nodes.length; i++) {
              var text = (nodes[i].textContent || "").trim().toLowerCase();
              if (text.indexOf(lowerBase) >= 0) { nameSeen = true; break; }
            }
          }
          var busy = !!document.querySelector(
            '[aria-busy="true"], [data-testid*="upload"], [data-testid*="spinner"], [role="progressbar"]'
          ) && !nameSeen;
          return {
            ok: true,
            ready: nameSeen,
            pending: busy,
            fileName: base,
            href: location.href,
          };
        })()
        """;
    }

    public static string ConfirmUpload => """
        (async function() {
          function sleep(ms) { return new Promise(function(r) { setTimeout(r, ms); }); }
          function clickDialogButtonsByText(pattern, clicked, label) {
            var dialog = document.querySelector('[role="dialog"]');
            if (!dialog) return false;
            var re = pattern instanceof RegExp ? pattern : new RegExp(pattern, "i");
            var nodes = dialog.querySelectorAll("button, [role='button']");
            for (var i = 0; i < nodes.length; i++) {
              var el = nodes[i];
              var text = (el.textContent && el.textContent.trim()) || el.getAttribute("aria-label") || "";
              if (!re.test(text)) continue;
              var style = window.getComputedStyle(el);
              if (style.display === "none" || style.visibility === "hidden") continue;
              if (el.disabled) continue;
              try { el.click(); clicked.push(label || text.slice(0, 40)); return true; } catch (_e) {}
            }
            return false;
          }
          function clickDialogSelector(selector, clicked, label) {
            var dialog = document.querySelector('[role="dialog"]');
            if (!dialog) return false;
            var el = dialog.querySelector(selector);
            if (!el || el.disabled) return false;
            var style = window.getComputedStyle(el);
            if (style.display === "none" || style.visibility === "hidden") return false;
            try { el.click(); clicked.push(label || selector); return true; } catch (_e2) {}
            return false;
          }
          var clicked = [];
          var dialog = document.querySelector('[role="dialog"]');
          if (!dialog) {
            return { ok: true, skipped: true, reason: "no_dialog", clicked: clicked, href: location.href };
          }
          clickDialogButtonsByText(/^(save|done|upload|add|confirm)$/i, clicked, "confirm");
          clickDialogButtonsByText(/save changes|upload file|add to project|add files|add sources/i, clicked, "confirm-phrase");
          var confirmSelectors = [
            'button[type="submit"]',
            'button[data-testid*="save"]',
            'button[data-testid*="confirm"]',
            'button[data-testid*="upload"]',
            'button[aria-label*="Save"]',
            'button[aria-label*="Upload"]',
          ];
          for (var c = 0; c < confirmSelectors.length; c++) clickDialogSelector(confirmSelectors[c], clicked, confirmSelectors[c]);
          await sleep(400);
          return { ok: true, skipped: false, clicked: clicked, href: location.href };
        })()
        """;

    /// <summary>Lightweight auth/app shell probe after session warmup.</summary>
    public static string SessionProbe => """
        (function() {
          var href = location.href;
          var nodes = document.querySelectorAll('button, a, [role="button"]');
          var hasLoginCta = false;
          for (var i = 0; i < nodes.length; i++) {
            var el = nodes[i];
            var text = ((el.textContent && el.textContent.trim()) || el.getAttribute("aria-label") || "").toLowerCase();
            if (text.indexOf("log in") >= 0 || text.indexOf("sign in") >= 0 || text.indexOf("get started") >= 0) {
              hasLoginCta = true;
              break;
            }
          }
          var hasAppShell = !!(
            document.querySelector('[data-testid="sidebar"], nav[aria-label*="Chat"], #history, [data-testid="profile-button"], [data-testid="composer"]')
          );
          return { href: href, loggedIn: hasAppShell && !hasLoginCta, hasAppShell: hasAppShell, hasLoginCta: hasLoginCta };
        })()
        """;
}
