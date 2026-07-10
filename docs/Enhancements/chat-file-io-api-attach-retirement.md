# Chat file I/O — API attach automation: current state and retirement

**Decision date:** 2026-07-02  
**Status:** **Retired** — stop pursuing programmatic API attach on linked project threads. **Diagnostic runner code removed 2026-07-03** — evidence preserved here; utility file I/O canon is [utility-source-file-io.md](utility-source-file-io.md).

**Companion docs:**

| Doc | Role |
|-----|------|
| [utility-source-file-io.md](utility-source-file-io.md) | **Canon** — Project sources + pointer + delimited scrape + ephemeral thread |
| [utility-source-file-io-retired-methodologies.md](utility-source-file-io-retired-methodologies.md) | What was removed and why |
| [chat-file-io-feasibility.md](chat-file-io-feasibility.md) | Original spike + lane matrix (archived) |
| [chat-file-io-transport-redesign.md](chat-file-io-transport-redesign.md) | Transport architecture (production layer retained) |
| [CMD-437](https://linear.app/cmd0112/issue/CMD-437) | Transport redesign epic |
| [CMD-436](https://linear.app/cmd0112/issue/CMD-436) | API attach + sentinel gate |
| [CMD-435](https://linear.app/cmd0112/issue/CMD-435) | Diagnostic lanes (code removed 2026-07-03) |
| [CMD-441](https://linear.app/cmd0112/issue/CMD-441) | Utility file loop canon |

---

## Diagnostic code removed (2026-07-03)

The multi-lane live runner (`ChatFileIoOrchestrator`, `run-chat-file-io-diagnostics.ps1`, Playwright chat attach helpers) was **deleted** after canonizing utility source file I/O. Gate results below remain as historical evidence. Re-run is not available unless the spike is explicitly re-opened.

**Active utility gate:** `run-utility-source-file-io-diagnostics.ps1 -E2E`

---

## Executive summary

We invested in a **dual-track** effort:

| Track | Goal | Outcome |
|-------|------|---------|
| **A — Sentinel spike** | Observable fresh sentinel per send; go/no-go gate for API attach | **Gate FAIL** — sentinel unavailable in diagnostic WebView; attach `http_403` |
| **B — Transport redesign** | Unified upload/send/warmup for Play, utility worker, diagnostics | **Shipped** — `ChatFileTransport/` layer; diagnostics use same entry points as production |

**Product decision:** Keep **DOM / native composer attach** as the canonical Play and utility-worker path. Keep **API storage** (upload, list, download). **Do not** enable or further chase API `f/conversation` attach sends on project threads until explicitly re-opened.

---

## What works today (retained)

| Capability | Path | Status |
|------------|------|--------|
| Upload bytes to ChatGPT storage | `ChatGptProjectApiService` / bridge `uploadFile` | **Proven** |
| List + download conversation files | `ChatGptChatFileService.ListConversationFilesAsync` / `DownloadConversationFileAsync` | **Proven** |
| Play attach (production) | Native composer + `attachmentsPreStaged` → `AdventureTurnService` DOM submit | **Canonical** |
| Utility worker attach | `SubmitUtilityJobWithAttachmentsAsync` + `NativeComposerDomStaging` | **Canonical** |
| Diagnostic lanes | `CGW_CHAT_LANE=dom` / `storage` / `api-attach-probe` | **Usable** |
| Golden sample capture | `api-send-samples/` + no 403 overwrite of 200 attach fixture | **Shipped** |

---

## What does not work (retired pursuit)

| Capability | Blocker | Evidence |
|------------|---------|----------|
| API attach on provisioned server thread (`/c/{uuid}`) | Request rejected without browser sentinel + conduit headers on wire | Repeated `http_403` |
| Fresh sentinel via `SentinelSDK` in wrapper WebView | `sentinel_stage=exhausted`, `sentinel_error=sentinel_unavailable` | Warmup + send traces |
| Tap-cached sentinel replay | Single-use / anti-replay — correctly disabled | Earlier 403 “Unusual activity” when replaying |

Manual attach in a normal ChatGPT browser tab on the same thread **can** return HTTP 200. The gap is **wrapper-side header acquisition and attachment to bridge `apiRequest`**, not attach body shape.

---

## Latest live evaluation (2026-07-02)

**Command:**

```powershell
$env:CGW_CHAT_LANE = "api-text"
$env:CGW_CHAT_GIZMO_ID = "g-p-6a2c6cd152e08191b018455b5712bd5e"
$env:CGW_CHAT_CONVERSATION_ID = "6a45badd-b010-83ea-98f5-c5f1d4b3e383"
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-chat-file-io-diagnostics.ps1
```

**Report:** `%LocalAppData%\ChatGPTWrapper\chat-file-io-report.{txt,json}`

| Step | Result | Notes |
|------|--------|-------|
| `ensure_server_thread_page` | Pass | Navigated to project `/c/{uuid}` |
| `warmup_send_context` | Pass (step) / sentinel fail | `parent_ready=True`, `conduit_ready=True`, `sentinel_ok=False` |
| `upload_attachment` | Pass | `file_id=file_000000006ec0720c9a4fc3eda928d7ca` |
| `send_with_attachment_on_server_thread` | **Fail** | `http_403` |

**Warmup detail:**

```
sentinel_stage=exhausted
sentinel_error=sentinel_unavailable
sentinel_source=none
wire_sentinel=0
wire_conduit=0
bridge_conduit=0
missing_vs_golden=[oai-echo-logs, openai-sentinel-chat-requirements-token,
  openai-sentinel-proof-token, openai-sentinel-turnstile-token, x-conduit-token]
```

**Gate criteria (×3 sessions, no manual seed):** **Not met** — only one post-implementation session recorded; sentinel never SDK-derived.

**`diagnose_attach_gap` showing `parent_cached=False`:** Expected after failed send — `SendUserMessageWithAttachmentsAsync` invalidates caches on retry; gap snapshot is post-failure.

---

## Code delivered (remains in repo)

### Bridge — `ChatGPT_files/chatgpt-api-bridge.js`

- `recordSentinelDiagnostic` / `__CGW_LAST_SENTINEL_DIAGNOSTIC__`
- `resolvePageSentinelSdk`, `loadSentinelSdkAsync`, `ensureSentinelSdkInitialized`
- `refreshConversationSentinelHeaders` → SDK `token()` → `POST .../sentinel/chat-requirements/finalize`
- Fetch tap for `chat-requirements/prepare` and `finalize` (sample keys under `api-send-samples/`)
- `acquireConversationSentinelHeaders` returns `json.diagnostic`
- No replay of tap-cached tokens on conversation POST (anti-replay)

### C# — core

| Area | Key types / paths |
|------|-------------------|
| Send + sentinel | `ChatGptConversationSendService.PrefetchSentinelAsync` → `SentinelPrefetchResult` |
| Capture policy | `ChatGptApiSendSampleCapture.ShouldPersistSample` — preserve golden 200 attach |
| Facade | `ChatGptChatFileService` → `ChatFileTransportRegistry` |
| Transport layer | `ChatGPTWrapper/ChatGptApi/ChatFileTransport/` (18 files) |
| Warmup | `SendWarmupPipeline`, `PlaySendWarmupService` delegation |
| Scoped cache | `ConversationSendContextStore`, `BindContextStore` on send service |
| DOM staging dedup | `NativeComposerDomStaging` |
| Play wiring | `AdventureTurnService` — attachments DOM-only via `RequiresDomComposerForAttachments`; no API attach send |
| Diagnostics | `ChatFileIoOrchestrator` → `WarmupSendContextAsync` + `ChatFileTransportPlan` |
| Gap traces | `ChatFileIoApiGapDiagnostics`, `TransportDiagnosticSession` |

### Tests

- `BridgeAssetTests` — sentinel symbols + fresh flow
- `ChatGptApiSendSampleCaptureTests` — golden overwrite + sentinel sample keys
- `LiveChatFileIoTests` — multi-lane live runner

### Diagnostics script

```
tests/ChatGPTWrapper.ApiDiagnostics/scripts/run-chat-file-io-diagnostics.ps1
```

Not `scripts/` at repo root.

---

## Root-cause analysis (as of retirement)

1. **Sentinel acquisition fails in the diagnostic WebView** — all paths exhaust: SDK load/token, webpack page module, fetch-tap cache.
2. **Outbound attach has zero wire headers** (`wire_keys=0`) despite warmup reporting conduit/parent ready — bridge `apiRequest` path does not mirror what the browser sends on manual attach.
3. **Body shape is aligned** — prior work matched golden `metadata.attachments` + `file_token_size`; 403 persists without headers.
4. **ChatGPT anti-abuse** — attach on project threads appears to require in-page sentinel flow tied to the live session; wrapper `apiRequest` is treated as a different trust surface.

---

## Immediate planned next steps (not pursued — recorded for reopen)

If this track were continued, the next engineering steps would have been:

### 1. Sentinel SDK presence probe (1–2 sessions)

- On `/c/{uuid}` after navigation, log whether `globalThis.SentinelSDK` exists before bridge inject.
- Compare diagnostic WebView vs standalone Chrome on same account/thread.
- **Hypothesis:** SDK loads only after specific page lifecycle / chunk hydration not reached in diagnostic host.

### 2. In-page send experiment (spike)

- Instead of bridge `apiRequest` for attach POST, inject a one-shot script that calls the same fetch the UI uses (with page-origin credentials).
- **Pass criterion:** 200 on attach without manual composer seed.
- **Fail criterion:** Still 403 → confirms wrapper cannot impersonate UI send; retire permanently.

### 3. Header merge audit

- Trace `apiRequest` for attach: verify `x-conduit-token` and sentinel headers from warmup are merged into the actual `fetch` options.
- Fix if warmup caches exist but `wire_conduit=0` on failed sample.

### 4. Gate re-run

- Only after (1) or (2) shows SDK-derived `sentinel_source` — run `api-text` × 3 sessions.
- Update [CMD-436](https://linear.app/cmd0112/issue/CMD-436) and feasibility doc gate table.

### 5. Product policy fork (only if gate passes)

- Enable `HybridChatSendTransport` for Play API attach with DOM fallback.
- Otherwise: remove dead API attach branches from Play hot path (optional cleanup).

---

## Retirement scope

### Paused / out of scope

- API attach automation on linked project threads as a product feature
- Further sentinel SDK / webpack reverse-engineering in `chatgpt-api-bridge.js`
- Gate re-runs and CMD-436 acceptance until explicit re-open
- Enabling `ApiOnly` attach in Play by default

### Retained (no rollback required)

- `ChatFileTransport` architecture — useful for diagnostics and future lanes
- DOM attach paths (unchanged canonical behavior)
- API storage round-trip
- Diagnostic lanes (`dom`, `storage`, `api-attach-probe` as expected-block regression)
- Sentinel observability (harmless; aids future triage)
- Golden sample capture improvements

### Optional future cleanup (low priority)

- Mark `ApiChatSendTransport` attach path as diagnostic-only in code comments
- Reduce Play `ApiOnly` attach attempt when `_chatFileService` is wired (skip straight to DOM for attachments)
- Do **not** delete transport layer or diagnostics — they document the boundary.

---

## How to re-run diagnostics after retirement

**Removed (2026-07-03).** Former lanes (`playwright`, `dom`, `storage`, `api-text`, `api-attach-probe`) and `run-chat-file-io-diagnostics.ps1` are no longer in the repo. See [utility-source-file-io-retired-methodologies.md](utility-source-file-io-retired-methodologies.md).

**Utility file I/O gate (canon):**

```powershell
$env:CGW_RUN_LIVE_API_TESTS = "1"
$env:CGW_UTILITY_SOURCE_IO_GIZMO_ID = "g-p-…"
$env:CGW_UTILITY_SOURCE_IO_E2E = "1"
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-utility-source-file-io-diagnostics.ps1 -E2E
```

### Historical lane matrix (archived)

## Latest live gate (2026-07-03)

| Lane | Result | Notes |
|------|--------|-------|
| **`api-attach-probe`** | **PASS** (8/8) | Unprovisioned attach → `http_403` as expected; sentinel exhausted / unavailable |
| **`dom`** | **PARTIAL** (7/8) | Composer + provision OK; send fails with **`submit_disabled`** (submit never enabled after CDP attachment staging in live diagnostic WebView). List + download pass. Prior runs showed **`submit_timeout`** when host gave up at 120s before bridge replied — fixed via attach submit budget + stash invoke flag. |
| **`playwright`** | **PASS** (7/7) | With `CGW_CHAT_CONVERSATION_ID` = server `/c/{uuid}`: send `wire_attach=true` (~109s), list + download OK. Auto-provision without env still fails (client-bootstrap / UI create). |

Reports: `%LOCALAPPDATA%\ChatGPTWrapper\chat-file-io-report.txt` (and `.json`).

**First Playwright live run (2026-07-03):** 5/7 pass — provision OK; **`send_with_attachment` → `attachment_not_visible`** (file chooser / SetInputFiles did not surface attachment chip within 90s). List/download skipped (0 files). Follow-up: file-chooser-first ordering + change-event dispatch on staged input.

**Playwright gate PASS (2026-07-03):** With `CGW_CHAT_CONVERSATION_ID` set to a real server thread UUID — **7/7 pass** (`send_with_attachment` ~109s, `wire_attach=true`, list + download OK). Fresh uploads may return **`download_stub`** until CDN propagates; playwright lane retries download up to 8×5s and verifies `cgw-chat-io-diag.md` SHA256 when present in the list.

**Playwright gate procedure (recommended until auto-provision is stable):**

```powershell
$env:CGW_RUN_LIVE_API_TESTS = "1"
$env:CGW_CHAT_GIZMO_ID = "g-p-…"
$env:CGW_CHAT_CONVERSATION_ID = "6a45badd-b010-83ea-98f5-c5f1d4b3e383"   # bare UUID from …/c/{uuid} in browser URL
$env:CGW_CHAT_LANE = "playwright"
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-chat-file-io-diagnostics.ps1
```

Pass criteria: `send_with_attachment` (`wire_attach=true`) + `list_conversation_files` (`sent_in_list=True`). `download_user_attachment` is **best-effort**: fresh composer uploads often return `download_stub` until CDN propagates — playwright lane records **`propagation-deferred`** (pass) unless `CGW_CHAT_REQUIRE_DOWNLOAD=1`.

**Confirm download (strict bytes on a propagated file):**

```powershell
# After a gate run, or pick any file_id from list_conversation_files in the report:
$env:CGW_CHAT_CONFIRM_DOWNLOAD = "1"
$env:CGW_CHAT_VERIFY_DOWNLOAD_FILE_ID = "file_00000000cd3871f5a64123606e9da798"  # optional; else oldest cgw-chat-io-diag*
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-chat-file-io-diagnostics.ps1
```

Download-only (no send, ~2 min):

```powershell
$env:CGW_CHAT_SKIP_SEND = "1"
$env:CGW_CHAT_CONFIRM_DOWNLOAD = "1"
$env:CGW_CHAT_VERIFY_DOWNLOAD_FILE_ID = "file_…"   # from report list preview
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-chat-file-io-diagnostics.ps1
```

Expect `download_propagated_attachment` with `classification: pass` and `bytes=` + `sha256=`.

**Product policy (updated):** Playwright is **validated for automated attach send** on server threads. In-app utility/play routing remains WebView2 DOM until Playwright is wired behind a feature flag / worker lane. DOM diagnostic lane remains **informational** (flaky in WebView host).

**DOM send failure (2026-07-03 investigation — closed):**

1. **`submit_timeout`** — Host `WaitAsync` capped at 120s while bridge attachment path needs up to ~90s (`waitForSubmitButtonEnabled`) + verify retries. Bridge `promptSubmitted` arrived after `EndPendingTurn()`. **Fixed:** attach path uses ≥240s budget; `InvokeSubmitPromptAsync` now passes `useWrapperAttachmentStash`.
2. **`submit_disabled`** (post-fix re-run) — Bridge responds; ChatGPT composer submit stays disabled after CDP-staged attachment in the **live diagnostic WebView** (not attach-worker tab). Environment/UI flake, not API policy. Provision, list, and download succeed on the same thread.

**Decision:** Retire API attach; ship DOM-only policy for **in-app WebView2** utility/play routing. **WebView2 DOM send could not be validated** in the live diagnostic host (`submit_disabled` / `submit_timeout`) — treat as **not viable for automated gates**.

**Pivot (2026-07-03):** Attempt **Playwright (headless Chrome)** for chat attach send validation — reuses `HeadlessBrowserSessionPool` + WebView cookie import (same stack as project-knowledge upload). New diagnostic lane: **`playwright`** (default gate).

| Component | Path |
|-----------|------|
| Playwright send | `ChatGptApi/BrowserFileDelivery/Automation/HeadlessBrowserChatAttachmentSend.cs` |
| Composer probes | `ChatGptApi/BrowserFileDelivery/Automation/ChatComposerDomScript.cs` |
| Diagnostic lane | `CGW_CHAT_LANE=playwright` |

```powershell
$env:CGW_RUN_LIVE_API_TESTS = "1"
$env:CGW_CHAT_LANE = "playwright"
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-chat-file-io-diagnostics.ps1
```

**Product routing unchanged for now:** Play is still DOM via attach-worker WebView. **Playwright attach send is gate-validated** (see procedure above); wire into utility worker is the next integration step.

---

## Re-open criteria

Revisit API attach automation only if one of:

- ChatGPT removes or relaxes sentinel requirements for `f/conversation` attach on project threads
- In-page send spike (above) demonstrates stable 200 × 3 sessions without manual seed
- Official documented API for chat attachments (unlikely; out of original scope)

---

## Linear status

| Issue | Disposition |
|-------|-------------|
| [CMD-437](https://linear.app/cmd0112/issue/CMD-437) | Transport redesign — **Done — Review Later** (code landed; gate not met) |
| [CMD-436](https://linear.app/cmd0112/issue/CMD-436) | API attach gate — **Out of Scope** (retired 2026-07-02) |
| [CMD-435](https://linear.app/cmd0112/issue/CMD-435) | Diagnostic lanes — **Done — Review Later** |

---

*Last updated: 2026-07-03 (diagnostic code removed; utility source I/O canonized)*
