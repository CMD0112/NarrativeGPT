(function () {
  "use strict";

  function localSetWrapperComposer(enabled) {
    globalThis.__cgwWrapperComposer = !!enabled;
    var root = document.documentElement;
    if (!root) return;
    if (enabled) root.setAttribute("data-cgw-wrapper-composer", "1");
    else root.removeAttribute("data-cgw-wrapper-composer");
  }

  if (typeof globalThis.__cgwSetWrapperComposer !== "function") {
    globalThis.__cgwSetWrapperComposer = localSetWrapperComposer;
  }
})();
