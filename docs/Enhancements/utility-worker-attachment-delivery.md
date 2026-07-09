# Utility worker attachment delivery

Companion to [CMD-411](https://linear.app/cmd0112/issue/CMD-411) — reference file delivery for **manual** utility worker jobs (user-selected files, binary refs).

> **Programmatic utility file loops** (publish → pointer → scrape → delete) use [utility-source-file-io.md](utility-source-file-io.md) — not DOM attach. This doc covers manual QA / reference-panel flows only.

## Problem

Utility worker jobs run on an **off-screen parked** WebView. Chromium throttles DOM composer uploads when the surface is physically off-screen (`Margin=-20000`), causing `file upload pending` and `submit_not_observed`. Tab-switch visibility works but hijacks the play tab.

Linked project threads also block API multimodal attach (`SendUserMessageWithAttachmentsAsync` → `http_403`), same as play.

## Delivery lanes

| Lane | When | Composer chips | Implementation |
|------|------|----------------|----------------|
| **Packet embed** | Small text/JSON within policy limits | No — `=== FILE: … ===` in API packet | `UtilityReferenceAttachmentPolicy` |
| **DOM composer** | Binary, oversized, or mixed | Yes | Shadow compositor + CDP staging |
| **Attach worker** | In-process DOM fails | Yes | `UtilityAttachWorkerService` (separate WebView2 profile) |
| **API attach** (probe only) | Recorded on verify | N/A | `UtilityWorkerApiAttachProbe` — not routing yet |

Classifier: `UtilityAttachmentDeliveryClassifier`. Push orchestration: `UtilityMessagePushService`.

## Shadow compositor (CMD-413)

During DOM attach only:

1. **WPF:** `UtilityWorkerDomSendScope` — host `Margin=0`, `Opacity=0`, play tab stays selected.
2. **DOM:** `NativeComposerFileStaging.ExposeComposerForUploadAsync` around CDP staging.

Diagnostic (extended mode): `utility_worker_shadow_compositor_active`.

## Author entry points

- Play settings → **AI Tools** → **Run selected action…**
- Reference panel → **Suggest entities with reference files…**
- Play settings → Sources → **Edit sources with AI** (design thread; not worker QA primary)

Auto post-turn scheduler jobs remain **attachment-free**.

## Verification

| Surface | What to check |
|---------|----------------|
| Worker chat | Embed = packet text only; DOM = composer chips |
| History tab | `WorkerUtilitySend` rows with delivery lane + file names |
| Disk | `utility-results/{runId}.json` → `contextManifest.attachmentDeliveryLane` |
| Verify probe | Threads → Utility worker → detail shows `API attach probe: {result}` |

## Related

- [CMD-413](https://linear.app/cmd0112/issue/CMD-413) shadow compositor
- [CMD-414](https://linear.app/cmd0112/issue/CMD-414) attach-worker spike (in-process host shipped; OOP exe optional)
- [CMD-428](https://linear.app/cmd0112/issue/CMD-428) publication lab — shared browser-file delivery kernel ([plan](project-source-publication-redesign.md))
- `docs/Enhancements/chat-file-io-feasibility.md`
- [utility-source-file-io.md](utility-source-file-io.md) — preferred utility file loop (sources + scrape; [CMD-441](https://linear.app/cmd0112/issue/CMD-441))
