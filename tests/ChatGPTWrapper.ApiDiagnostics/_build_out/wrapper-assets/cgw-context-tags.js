(function () {
    'use strict';

    var MARKER = '[[cgw:';

    function hideEnabled() {
        return globalThis.__cgwHideContextTags === true;
    }

    function setRootFlag(enabled) {
        var root = document.documentElement;
        if (!root) return;
        if (enabled) root.setAttribute('data-cgw-hide-context-tags', '1');
        else root.removeAttribute('data-cgw-hide-context-tags');
    }

    function collapseTaggedUserMessages() {
        setRootFlag(hideEnabled());
        if (!hideEnabled()) {
            document.querySelectorAll('[data-cgw-context-collapsed]').forEach(function (node) {
                node.removeAttribute('data-cgw-context-collapsed');
                node.style.removeProperty('display');
            });
            document.querySelectorAll('[data-cgw-context-tag-summary]').forEach(function (node) {
                node.remove();
            });
            return;
        }

        document.querySelectorAll('[data-message-author-role="user"]').forEach(function (node) {
            var text = node.textContent || '';
            if (text.indexOf(MARKER) < 0) {
                node.removeAttribute('data-cgw-context-collapsed');
                return;
            }

            node.setAttribute('data-cgw-context-collapsed', '1');
            if (!node.querySelector('[data-cgw-context-tag-summary]')) {
                var hint = document.createElement('div');
                hint.setAttribute('data-cgw-context-tag-summary', '1');
                hint.textContent = 'Adventure context packet (hidden)';
                node.insertBefore(hint, node.firstChild);
            }
        });
    }

    globalThis.__cgwApplyContextTagCollapse = collapseTaggedUserMessages;

    if (!globalThis.__cgwContextTagsObserver) {
        globalThis.__cgwContextTagsObserver = new MutationObserver(function () {
            collapseTaggedUserMessages();
        });
        globalThis.__cgwContextTagsObserver.observe(document.body, {
            childList: true,
            subtree: true,
        });
    }

    collapseTaggedUserMessages();
})();
