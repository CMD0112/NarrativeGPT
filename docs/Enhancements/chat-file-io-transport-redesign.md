# Chat file I/O transport redesign

Companion to Linear epic [CMD-437](https://linear.app/cmd0112/issue/CMD-437).

**Status:** Transport layer implemented (2026-07-02). **API attach automation retired** — [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md). **ChatFileIo diagnostic orchestrator removed 2026-07-03** — see [utility-source-file-io-retired-methodologies.md](utility-source-file-io-retired-methodologies.md).
**Product stance:** DOM/native composer attach remains canonical for Play; API storage round-trip is shippable.

**Related:**

| Topic | Doc / issue |
|-------|-------------|
| Feasibility + gate | [chat-file-io-feasibility.md](chat-file-io-feasibility.md) · [CMD-436](https://linear.app/cmd0112/issue/CMD-436) |
| Diagnostic lanes | [CMD-435](https://linear.app/cmd0112/issue/CMD-435) |
| Utility worker DOM attach | [utility-worker-attachment-delivery.md](utility-worker-attachment-delivery.md) |
| Publication lane pattern | [project-source-publication-redesign.md](project-source-publication-redesign.md) |

---

## Problem statement

Chat file I/O logic was duplicated across:

| Surface | Before |
|---------|--------|
| Play send | Inline API vs DOM branch in `AdventureTurnService` |
| Utility worker | Separate DOM staging in `SubmitUtilityJobWithAttachmentsAsync` |
| Diagnostics | ~~`ChatFileIoOrchestrator`~~ removed 2026-07-03; utility gate: `LiveUtilitySourceFileIoRunner` |
| Warmup | `PlaySendWarmupService` vs diagnostic `warmup_send_context` |

Parent/conduit caches were process-global statics with no per-WebView scope. Sentinel observability was opaque when attach returned `http_403`.

---

## Architecture

```mermaid
flowchart TB
    subgraph callers [Callers]
        ATS[AdventureTurnService]
        UTIL[UtilityWorkerTransport]
        DIAG[ChatFileIoOrchestrator]
    end

    subgraph facade [ChatGptChatFileService]
        CFO[Stage / Send / List / Download / Warmup]
    end

    subgraph transport [ChatFileTransport]
        POL[ChatFileTransportPolicy]
        API[ApiChatSendTransport]
        DOM[DomChatSendTransport]
        HYB[HybridChatSendTransport]
        REG[ChatFileTransportRegistry]
    end

    subgraph ctx [ConversationSendContext per core+conversationId]
        WARM[SendWarmupPipeline]
        CACHE[Scoped parent/conduit/sentinel state]
        TRACE[TransportDiagnosticSession]
    end

    subgraph primitives [Extracted primitives]
        UP[ChatUploadService]
        DL[ChatDownloadService]
        TMPL[SendBodyTemplateProvider]
    end

    callers --> facade
    facade --> REG
    REG --> transport
    transport --> ctx
    facade --> primitives
    API --> ChatGptConversationSendService
    DOM --> AdventureTurnService DOM helpers
```

Code: `ChatGPTWrapper/ChatGptApi/ChatFileTransport/`

---

## Transport policy

`ChatFileTransportPolicy.Resolve` chooses:

| Plan | When |
|------|------|
| `DomOnly` | Pre-staged DOM bytes, DOM delivery channel, or no API refs |
| `ApiOnly` | Play API attach, diagnostic `api-text` / `storage` |
| `ApiWithDomFallback` | Registered utility worker conversations |

Diagnostics: `ResolveForDiagnostics(lane)` maps lane id → plan.

---

## Scoped send context

`ConversationSendContextStore` keys `(CoreWebView2, conversationId)`:

- `ParentMessageId`, `ConduitToken`, `LastSentinelPrefetch`
- `SendWarmupPipeline` writes context + static caches during migration
- `ChatGptConversationSendService.BindContextStore` — reads scoped first, falls back to `ConversationParentCache` / `ConversationConduitCache`

---

## Sentinel spike (Track A)

Bridge (`chatgpt-api-bridge.js`):

- `lastSentinelDiagnostic` + `acquireConversationSentinelHeaders` → `json.diagnostic`
- Page `SentinelSDK` probe before script inject; `chat-requirements/prepare` + `finalize` fetch tap
- Fresh token per send (no replay of tap-cached single-use tokens)

C#:

- `SentinelPrefetchResult` from `PrefetchSentinelAsync`
- `ChatGptApiSendSampleCapture` — do not overwrite golden 200 attach with 403; capture sentinel endpoints

### API attach automation gate

Run `CGW_CHAT_LANE=api-text` × **3 sessions** (no manual composer seed):

| Pass | Fail |
|------|------|
| `sentinel_source` SDK/page-derived | Document **API attach automation no-go** |
| Attach HTTP 200 | Utility uses DOM fallback only for attach |
| No manual composer seed | Product keeps DOM canonical |

**Last evaluation (2026-07-02):** **FAIL** — retired. See [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md).

---

## Shared DOM staging

`NativeComposerDomStaging.StageAttachmentsAsync` — bridge stash + CDP `setFileInputFiles` used by:

- `AdventureTurnService.SubmitDomAttachmentPromptAsync`
- `AdventureTurnService.SubmitUtilityJobWithAttachmentsAsync`

---

## Diagnostics conformance

`ChatFileIoOrchestrator` lanes call `ChatGptChatFileService` entry points:

| Lane | Transport path |
|------|----------------|
| `storage` | Upload + list/download (no send transport) |
| `api-text` | `WarmupSendContextAsync` + `ApiOnly` send |
| `api-attach-probe` | `ApiOnly` on unprovisioned id; expect `http_403` |
| `dom` | `SubmitUtilityJobWithAttachmentsAsync` + API verify |

`TransportDiagnosticSession` records warmup/gap steps; Play logs compact gap on API attach fallback.

---

## Delivery phases

| Phase | Scope | Status |
|-------|-------|--------|
| 1 | Sentinel observability + golden capture fix | Done |
| 2 | Upload/download/template extraction | Done |
| 3 | Scoped context + unified warmup | Done |
| 4 | Transports + policy + Play wiring | Done |
| 5 | Orchestrator + production traces | Done |
| 6 | CDP dedup + docs + Linear | Done |

---

## Out of scope

- Merging `chatgpt-api-bridge.js` and `adventure-bridge.js`
- Product UI attach button
- OpenAI official API
- Replacing `ProjectSourceSyncService`
