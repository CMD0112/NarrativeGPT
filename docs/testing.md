# Testing Guide

Test project: `tests/ChatGPTWrapper.ApiDiagnostics/` (xUnit, `net9.0-windows`).

Also see: [tests/ChatGPTWrapper.ApiDiagnostics/README.md](../tests/ChatGPTWrapper.ApiDiagnostics/README.md)

**CI status:** No GitHub Actions or other CI is configured. Recommended PR gate below.

---

## Test tiers

| Tier | Filter | Login | WebView2 |
|------|--------|-------|----------|
| **Unit** | `Category=Unit` | No | No (except asset tests read disk) |
| **Integration** | `Category=Integration` | No | Yes (composer harness) |
| **Live** | `Category=Live` + `CGW_RUN_LIVE_API_TESTS=1` | Yes | Yes |
| **Performance** | `Category=Performance` | Varies | Varies |

### Default commands

```powershell
# Unit only (fast, no login)
dotnet test tests\ChatGPTWrapper.ApiDiagnostics --filter "Category=Unit"

# Exclude performance (recommended CI)
dotnet test tests\ChatGPTWrapper.ApiDiagnostics --filter "Category!=Performance"

# Play composer suite
dotnet test tests\ChatGPTWrapper.ApiDiagnostics --filter "FullyQualifiedName~PlayCompose"
```

---

## Unit test catalog

| Test class | Feature area |
|------------|--------------|
| `AdventurePlayContextTests` | Project/conversation URLs, tab pin routing |
| `ApiProbeParsingTests` | API bridge probe parsing, Snorlax attach/upload (~97 tests) |
| `BridgeAssetTests` | `chatgpt-api-bridge.js` on disk and exports |
| `BridgeScriptJsonTests` | WebView2 JSON normalization for bridge RPC |
| `ChatGptApiSendSampleCaptureTests` | Send sample cache loading |
| `ChatGptConversationSendServiceTests` | Send body, parent cache, retries |
| `ChatGptProjectApiParsingTests` | Project API JSON parsing |
| `ConversationConduitCacheTests` | Conduit JWT cache expiry |
| `ConversationStreamParserTests` | SSE parsing, transcript extraction (incl. flat-mapping fixtures) |
| `AttachmentSendPolicyTests` | Attachment send mode classification |
| `AttachmentContextModeTests` | `AttachmentContextMode` minimal/full packet trimming |
| `ThreadMetadataReconcileTests` | Thread metadata backfill on load |
| `EditInvalidationTests` | Turn supersede + invalidation markers |
| `PlaySurfaceActionSendHelperTests` | InjectedOnly action packet injection |
| `DraftFrameworkServiceTests` | Draft framework writes to `sources/drafts/` |
| `EntityExtractionServiceTests` | Entity prompts, JSON normalization |
| `GenerationJobGuideTests` | Job instruction overrides, seed versions |
| `GenerationJobServiceTests` | Job prompts, continuity check, errors |
| `GenerationJobSchedulerTests` | Auto-schedule after turns |
| `InlineUtilityDomPipelineTests` | Inline utility DOM pipeline |
| `InlineUtilityWorkflowTests` | Utility detection, delivery modes |
| `InstructionSourcesPolicyTests` | Instructions vs sources policy |
| `JsonElementParsingTests` | `JsonElement` helpers (Core) |
| `PacketDisplayAssetTests` | JS display modules contract |
| `PacketDisplayParityTests` | C#/JS parity vectors |
| `PendingReviewServiceTests` | Review queue counts |
| `PlayComposeAssetTests` | Composer JS/CSS contract |
| `PlayComposeUiStateTests` | Compose state JSON shape |
| `PlaySendTraceTests` | Play-send trace logging |
| `PlayUxContextTagTests` | Context tags, packet builder, session cache |
| `ProcessTurnResponseTests` | Legacy process_turn parsing |
| `ProjectSourceInjectionTests` | Fat/thin packets, publish mode |
| `ProjectSourceProbeServiceTests` | Remote probe hash matching |
| `ProjectSourceSyncRobustnessTests` | Three-way sync, duplicates, 404 |
| `SourceFileHistoryServiceTests` | Source version archive/restore |
| `SyncTraceTests` | Sync trace JSONL format |
| `TextDiffServiceTests` | Line/unified diff |
| `TranscriptFilterServiceTests` | Lookback, utility filtering |
| `TranscriptTextSanitizerTests` | filecite/marker stripping (Core) |
| `TurnTimelineAcceptTests` | Turn accept/remove-pending |
| `UtilityConversationPageTests` | href-based page matching, strict navigation, URL matching |
| `UtilityConversationReadinessTests` | Registered/DomOnly/Unready gate, rate limits, DOM-capable errors |
| `JsonElementParsingTests` | Null-safe JSON helpers (utility job apply hardening) |
| `UtilityResponseParseTests` | Utility response unwrapping |
| `UtilityStoryContextBuilderTests` | Story context preview/trim |
| `UtilityStoryContextSettingsNormalizerTests` | Role toggle mapping |
| `UtilityTabPinServiceTests` | Utility tab pin resolution |

### Integration (WebView2 harness)

| Test class | Feature area |
|------------|--------------|
| `PlayComposeBehaviorTests` | Composer mount, send, focus, busy state (25 tests) |
| `ContinuousViewDecorationBenchmarkTests` | CV `decorateTurnBlocks` perf (50 turns, &lt;200ms; `Category=Performance`) |
| `EntityReviewRoutingTests` | Entity review queue persistence + STA selection routing |

**Hosts:** `PlayComposeTestHost`, `PlayComposeUiEnvironment`  
**Fixture:** `Fixtures/composer-fixture.html`

---

## Performance tests

| Test class | Tier | Measures |
|------------|------|----------|
| `ProjectSourceLocalPerfTests` | Unit | Export, hash, planner (no WebView) |
| `ProjectSourceMockBridgePerfTests` | Integration | Full sync via mock bridge |

**Script:**

```powershell
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-source-sync-perf.ps1 -Open
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-source-sync-perf.ps1 -Live -Open
```

Reports: `%LocalAppData%\ChatGPTWrapper\source-sync-perf-report.txt`

Tests always pass — report-only timings, no thresholds.

---

## Live diagnostics

Requires `CGW_RUN_LIVE_API_TESTS=1` and ChatGPT login in app profile.

```powershell
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-api-diagnostics.ps1 -Open
```

**Profile:** `%LocalAppData%\ChatGPTWrapper\WebView2UserData`

### Live API checklist (15 steps)

| Step | ID | Checks |
|------|-----|--------|
| 1 | `webview_init` | WebView2 environment |
| 2 | `page_injectable` | On chatgpt.com |
| 3 | `bridge_asset_on_disk` | `wrapper-assets/chatgpt-api-bridge.js` |
| 4 | `bridge_inject` | `__cgwApiInvoke` present |
| 5 | `bridge_ping` | Script RPC ping |
| 6 | `bridge_postmessage_fallback` | postMessage ping fallback |
| 7 | `api_context` | Session context |
| 8 | `session_endpoint` | `/api/auth/session` |
| 9 | `device_cookie` | `oai-did` |
| 10 | `probe_sidebar` | Projects sidebar API |
| 11 | `list_bootstrap` | Bootstrap gizmo list |
| 12 | `list_dom` | DOM project scrape |
| 13 | `discovery_merge` | `ProjectDiscoveryService` |
| 14 | `client_profile` | `api-client-profile.json` |
| 15 | `existing_logs` | Tail `link-project.log` |

**Output:** `api-diagnostic-report.json`, `api-diagnostic-report.txt`

| Live test class | Purpose |
|-----------------|---------|
| `LiveApiDiagnosticTests` | Full API diagnostic run |
| `LiveSourceSyncPerfTests` | Live source-sync perf against linked project |

### Live perf env vars

| Variable | Purpose |
|----------|---------|
| `CGW_RUN_LIVE_API_TESTS=1` | Enable live tier |
| `CGW_PERF_GIZMO_ID` | Linked project id |
| `CGW_PERF_SKIP_UPLOAD=1` | Download-only |
| `CGW_PERF_ENSURE_PROJECT_PAGE=1` | Include project page navigation |
| `CGW_PERF_TIMEOUT_MINUTES` | Timeout (default 20) |
| `CGW_PERF_CLEANUP_PROBE` / `CGW_PERF_CLEANUP_ALL_PROBES` | Probe cleanup |
| `CGW_PERF_DOWNLOAD_MAX` | Max download steps |
| `CGW_PERF_DOWNLOAD_FAIL_FAST=1` | Skip generic retries on 404 |
| `CGW_PERF_REFRESH_MATCHED_SOURCES=1` | Re-upload before download |
| `CGW_PERF_SKIP_ATTACH_VERIFY=1` | Skip attach verify |
| `CGW_PERF_SKIP_ATTACH_SIDEBAR=1` | Skip sidebar eval |

---

## Fixtures

| File | Used by |
|------|---------|
| `Fixtures/composer-fixture.html` | Play compose integration |
| `Fixtures/source-sync-fixture.html` | Mock bridge perf |
| `Fixtures/source-sync-bridge-mock.js` | Mock API bridge |
| `Fixtures/packet-display-parity.json` | C#/JS parity |
| `Fixtures/conversation-stream-sample.txt` | Stream parser |

---

## Recommended CI (not yet configured)

Example GitHub Actions job:

```yaml
- name: Test
  run: dotnet test tests/ChatGPTWrapper.ApiDiagnostics --filter "Category!=Performance" --no-restore
```

Optional nightly live job with secrets/env for `CGW_RUN_LIVE_API_TESTS` — not recommended on every PR.

**Build step:**

```yaml
- run: dotnet build chatgpt-wrapper.sln -c Release
```

---

## InternalsVisibleTo

`ChatGPTWrapper.csproj` exposes internals to `ChatGPTWrapper.ApiDiagnostics` for testing internal services.

---

## Teardown (remove test project)

1. Delete `tests/` folder
2. Remove project from `chatgpt-wrapper.sln`
3. Remove `InternalsVisibleTo` from `ChatGPTWrapper.csproj`

---

## Related documentation

- [Troubleshooting — Running API diagnostics](troubleshooting.md#running-api-diagnostics-advanced)
- [Build & Deploy](build-and-deploy.md)
- [ChatGPT API Integration](chatgpt-api-integration.md)
