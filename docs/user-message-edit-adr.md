# ADR: Overlay-first user message edit

**Status:** Accepted (2026-06-25)  
**Issue:** [CMD-350](https://linear.app/cmd0112/issue/CMD-350)  
**Epic:** [CMD-348](https://linear.app/cmd0112/issue/CMD-348)  
**Plan:** [play-message-edit-refinement-plan.md](play-message-edit-refinement-plan.md)

## Context

Continuous and Weave transcript overlays expose **Edit message** via a surrogate panel. The first thread-canonical batch always called `tryNativeEdit`, which enters **peek mode** (`data-cgw-continuous-peek`, overlay teardown). Authors experience a view-mode warp; edit often fails outside Native view.

Player-line seed text incorrectly included adventure packet chrome (`.cgw-continuous-packet-context`) because `segmentPlainText` used naive `innerText` ([CMD-284](https://linear.app/cmd0112/issue/CMD-284)).

## Decision

### Transport per transcript view mode

| Mode | Primary transport | Peek allowed |
|------|-------------------|--------------|
| **Native** | Surrogate panel → `tryNativeEdit` with peek | Yes |
| **Continuous** | Surrogate panel → `tryNativeEdit` **in-place** (`allowPeek: false`) | **No** |
| **Weave** | Same as Continuous | **No** |

In-place edit sets `data-cgw-continuous-inline-edit` on `<html>` so the target native turn is reachable for DOM automation while the overlay stays visible (native turn is positioned off-screen; no `data-cgw-continuous-peek` warp).

**Option A (overlay surrogate-first)** is adopted for Continuous/Weave: the author stays in the overlay; native DOM automation runs on the suppressed turn wrapper without hiding the overlay container or setting `data-cgw-continuous-peek`.

Native inline edit remains available in **Native** view where peek is acceptable.

There is **no** ChatGPT API for in-place user message edit today. Overlay failure surfaces an error in the surrogate panel; authors may switch to Native view or use **Sync from thread** after manual ChatGPT edit.

### Player-line extraction

Canonical function: `playerLineFromSegment(segment)` in `continuous-transcript-view.js`:

1. Prefer registry `playerSnippet` from `__cgwLogTurnLinkMap` / turn registry.
2. Clone segment DOM, remove `.cgw-continuous-packet-context`, strip chrome.
3. Run `sanitizeExtractedMessageText` (shared with `cgw-packet-display.js`).

Used for surrogate edit seed, Copy, and any user-role plain-text extraction. Assistant segments continue to use full `segmentPlainText`.

### Invalidation

Unchanged from [play-thread-canonical-adr.md](play-thread-canonical-adr.md): user edit at turn *N* posts `turnInvalidated` with `reason: user_edit`, `editRole: user`; `TurnInvalidationService` updates player text and trims tail.

### Failure UX

| Scenario | Behavior |
|----------|----------|
| In-place native edit succeeds | Overlay rebuild; invalidation on send |
| In-place native edit fails (overlay) | Re-show surrogate panel with error; overlay never peeked |
| Native view native edit fails | Re-show surrogate panel with error |
| No API fallback in v1 | `PlaySendRepairService` clipboard repair deferred — not auto-invoked from edit panel |

## Implementation map

| File | Change |
|------|--------|
| `continuous-transcript-view.js` | `playerLineFromSegment`, `tryNativeEdit(..., { allowPeek })`, gated `submitSurrogateEdit` user path |
| `weave-transcript-view.js` | Shared kernel — no separate edit path |
| `cgw-packet-display.js` | Reference implementation for `sanitizeExtractedMessageText` |

## Consequences

- Continuous/Weave authors no longer warp into peek during user edit.
- Fragile DOM coupling remains for in-place native automation; failures are localized to the surrogate panel.
- Future API/bridge user-message edit can replace in-place native path without changing invalidation contract.
