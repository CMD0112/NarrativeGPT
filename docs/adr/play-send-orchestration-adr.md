# ADR: Play Send Orchestration — Host-Owned Delivery

**Status:** Proposed (Phase 1 capability resolver in progress)  
**Companion plan:** [play-send-orchestration-implementation-plan.md](../plans/play-send-orchestration-implementation-plan.md)

Normative architecture for **reliable play packet delivery**. When this ADR conflicts with ad-hoc behavior in `MainWindow.PlayInjection.cs`, native compose intercept, or DOM-default send policy, **this ADR wins**.

**Related:** [architecture.md](../developer/architecture.md) · [webview-bridges.md](../developer/webview-bridges.md) · [chatgpt-api-integration.md](../developer/chatgpt-api-integration.md) · [injection-policy-adr.md](injection-policy-adr.md) · [play-thread-canonical-adr.md](play-thread-canonical-adr.md)

---

## Context

Play sends today traverse many handoffs:

1. User types in **native** ChatGPT composer  
2. Injected JS **intercepts** (when not in passthrough)  
3. Host **merges** draft into `MergedText`  
4. Bridge **re-fills** native composer and **submits**  
5. Optional **API** path when `PreferDomPlaySend` is false  

Failures at any layer produce **draft-only delivery** — the user sees a correct preview but ChatGPT receives short text. Root causes include: passthrough/suppress policy bugs, pin/registry drift, busy-state native bypass, fill/readback truncation, and preview/send re-prepare drift.

Reliability requires **eliminating bypass paths**, not adding more guards.

---

## Decision

Adopt **host-owned send** with six invariants:

| # | Invariant | Rule |
|---|-----------|------|
| **I1** | Host-owned draft | Play mode on the pinned tab: user input is **wrapper composer only**. Native ChatGPT composer cannot submit play content. |
| **I2** | Frozen artifact | What is sent is a **`PreparedSendArtifact`** the user previewed (same bytes + hash). Send does not silently re-`PrepareSend` with different inputs. |
| **I3** | API-canonical text | On a bound play conversation, **text** delivers via `ConversationSendService` (backend-api). DOM fill/submit is **fallback only**. |
| **I4** | Verified delivery | Success requires **post-send verification** (API ack + turn increment, or DOM hash match). No success on fill alone. |
| **I5** | Session aggregate | **`PlayTabSession`** is the sole authority for pin, WebView, conversation id, and automation profile. |
| **I6** | Derived capabilities | **`PlayTabCapabilityResolver`** returns an exhaustive capability struct. No stored `passthrough` / `suppress` flags. |

---

## Capability matrix (normative)

`PlayTabCapabilities` is computed from `(PlayTabSession, currentUrl)`:

| URL kind | Pin | Conversation match | Profile | `AcceptPlayDraft` | `AllowSend` | `DeliveryChannel` |
|----------|-----|--------------------|---------|-------------------|-------------|-------------------|
| Project landing (no `/c/`) | any | stored play thread | Full | false | false | `None` |
| Play thread `/g/.../c/{id}` | pinned | yes | Full | true | true | `Api` |
| Play thread | pinned | no | Full | true | false | `None` until synced |
| Project landing | pinned | — | DraftProjectOnly | true | true (bootstrap only) | `DomBootstrap` |
| Design/utility draft tab | draft tab | — | Disabled | false | false | `None` |
| Unlinked adventure | pinned | n/a | Full | true | true | `Api` or `DomFallback` per readiness |

`AllowNativeComposerInput` is **always false** when `Profile == Full`.

---

## Delivery tiers

| Tier | When | Mechanism | Verification |
|------|------|-----------|--------------|
| **0 — API canonical** | `DeliveryChannel == Api`, conversation id set | `SendUserMessageAsync(artifact.MergedText)` | Parent id + user turn count +1 |
| **1 — DOM bootstrap** | Start/handoff, no conversation id yet | `DomDeliveryAdapter` one-shot | Readback hash == artifact hash before submit; bind conversation id |
| **2 — DOM fallback** | API retryable failure | Same artifact via bridge fill/submit | Readback + turn count; fail closed on `fill_incomplete` |
| **3 — Attachments** | Attachments present | `AttachmentDeliveryAdapter` (API or staged) | Text still tier 0/2; attachments verified separately |

**Default:** `PreferDomPlaySend` becomes **false** for new adventures; DOM is not the primary text path.

---

## Orchestrator state machine

`PlaySendOrchestrator` owns all play sends:

```text
Idle → ValidateSession → ResolveCapabilities → LoadArtifact → Preflight
  → Deliver → Verify → RecordTurn → CaptureAssistant → Idle
```

Terminal: `Blocked` | `Failed` (never silent native send).

Preflight **must** block when: capabilities disallow send, artifact missing/stale, conduit cold (Api), sources gate unresolved, wrong conversation URL.

---

## UI contract: Injection Armed

Pinned play tab displays **Armed** / **Disarmed** with reason code. **Send is disabled when disarmed.** Reasons include: `no_pin`, `stale_preview`, `conduit_cold`, `wrong_url`, `session_degraded`.

---

## Retire (after migration)

| Retired | Replacement |
|---------|-------------|
| Native compose intercept as primary path | Wrapper composer + orchestrator |
| `__cgwNativeComposePassthrough` on play pin | Capability resolver |
| `ShouldSuppressPlayAutomation` boolean soup | Capability matrix |
| `PreferDomPlaySend` default true | API tier 0 |
| Second `PrepareSend` at send time | `PreparedSendArtifact` |
| `MainWindow.PlayInjection` orchestration | `PlaySendOrchestrator` |
| `metadata.PinnedPlayTabKey` reads | `PlayTabSession` + registry |

---

## Consequences

### Positive

- Draft-only sends become **structurally impossible** when armed (I1, I3).  
- Preview/send parity enforced by artifact (I2).  
- Policy bugs become table-driven tests (I6).  
- Trace log maps 1:1 to orchestrator states.

### Negative

- Users no longer type in native ChatGPT composer during play (wrapper composer mandatory).  
- Large refactor; `MainWindow` play partials shrink but new core services grow.  
- API/schema changes require DOM fallback maintenance.

### Neutral

- `ChatGPTWrapper.SessionHost` becomes the long-term host for orchestrator (optional phase).

---

## Acceptance (epic sign-off)

- [ ] Golden capability matrix tests (all URL × pin × conversation rows).  
- [ ] Orchestrator integration tests: block reasons + happy path Api mock.  
- [ ] WebView2 harness: wrapper send only; native submit cannot deliver text.  
- [ ] Manual QA: pinned play thread → armed → send → trace shows `artifact_hash` + `delivery_api` + `verify_ok`.  
- [ ] No `compose_send_start` on native path for play pin (intercept retired).

---

## Appendix A — Callsite inventory (Phase 0)

Migration owner for each legacy entry point:

| Symbol | Location | Future owner | Phase |
|--------|----------|--------------|-------|
| `ShouldSuppressPlayAutomation` | `ProjectChatDraftService.cs` | **`PlayTabCapabilityResolver`** (delegates today) | 1 ✓ / delete 9 |
| `SetNativePassthroughAsync` | `ChatGptPlayComposeInjection.cs` | **Deleted** — `AllowNativeComposerInput` from capabilities | 3 / 9 |
| `SetNativePassthroughAsync` | `MainWindow.PlayTab.cs` (6 calls) | `RefreshCapabilities` → wrapper mode only | 3 |
| `RegisterPlayComposeInjection` | `MainWindow.PlayTab.cs` | `PlayTabSession` + capabilities | 1 / 3 |
| `SendPlayPromptAsync` | `MainWindow.PlayInjection.cs` | **`PlaySendOrchestrator.RequestSendAsync`** | 4 |
| `SendPlayPromptWithContextAsync` | `MainWindow.PlayInjection.cs` | **`ApiDeliveryAdapter`** | 5 |
| `SubmitPromptViaDomAsync` | `AdventureTurnService.cs` | **`DomDeliveryAdapter`** (bootstrap/fallback only) | 5 |
| `PlayComposeInjectionPolicy` | `PlayComposeInjectionPolicy.cs` | Merged into capability + session factory | 1 / 9 |
| `GetActivePlayComposeInjection` | `MainWindow.PlayTab.cs` | `PlayTabSession` WebView resolution | 1 / 4 |
| `PlaySendWarmupService` | uses `ShouldSuppressPlayAutomation` | `PlaySendPreflight` + armed gate | 5 / 7 |
| Native intercept | `cgw-play-compose.js` `triggerNativeSend` | Wrapper composer send only | 3 / 9 |
| DOM fill/submit | `adventure-bridge.js` | `DomDeliveryAdapter` + verification | 5 / 6 |

### Trace → orchestrator state (target)

| Legacy event | Orchestrator state |
|--------------|-------------------|
| `compose_send_start` | Retired (wrapper `draft_captured`) |
| `send_run_start` | `RequestSend` |
| `packet_prepared` | `artifact_loaded` |
| `bridge_fill_*` | `Deliver` (Dom tiers only) |
| `delivery_api` | `Deliver` (Api tier) |
| `verify_ok` / `verify_failed` | `Verify` |
| `send_run_end` | terminal |

