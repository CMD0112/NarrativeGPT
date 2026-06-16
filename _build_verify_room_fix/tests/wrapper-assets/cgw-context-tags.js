(function () {
  "use strict";

  function setRootFlag(enabled) {
    var root = document.documentElement;
    if (!root) return;
    if (enabled) root.setAttribute("data-cgw-hide-context-tags", "1");
    else root.removeAttribute("data-cgw-hide-context-tags");
  }

  function applyContextTagDisplay() {
    setRootFlag(globalThis.__cgwHideContextTags === true);
    if (typeof globalThis.__cgwApplyContextTagDisplay === "function") {
      globalThis.__cgwApplyContextTagDisplay();
    }
  }

  globalThis.__cgwApplyContextTagCollapse = applyContextTagDisplay;
  applyContextTagDisplay();
})();
