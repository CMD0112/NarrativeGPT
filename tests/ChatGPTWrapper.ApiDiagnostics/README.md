# ChatGPT Wrapper API diagnostics

Isolated test project for diagnosing why the ChatGPT **web** API bridge fails. Safe to delete entirely when no longer needed.

**Full testing guide:** [docs/testing.md](../../docs/testing.md)

## Tiers

| Tier | When it runs | Needs login |
|------|----------------|-------------|
| **Unit** (`Category=Unit`) | Default `dotnet test` | No |
| **Integration** (`Category=Integration`) | Play composer WebView2 harness | No (WebView2 runtime) |
| **Live** (`Category=Live`) | Only when `CGW_RUN_LIVE_API_TESTS=1` | Yes (shared WebView profile) |
| **Performance** (`Category=Performance`) | Opt-in via script or filter | Unit/Integration: no; Live tier: yes |

## Run unit tests

```powershell
dotnet test tests\ChatGPTWrapper.ApiDiagnostics --filter "Category=Unit"
```

### Play composer suite

Behavioral tests for the in-page wrapper composer (`cgw-play-compose.js`):

```powershell
dotnet test tests\ChatGPTWrapper.ApiDiagnostics --filter "FullyQualifiedName~PlayCompose"
```

- **Asset/contract** (`PlayComposeAssetTests`, `PlayComposeUiStateTests`): script exports, CSS rules, C# state JSON shape.
- **Integration** (`PlayCompose*Tests` in `PlayComposeBehaviorTests.cs`): WebView2 fixture with mocked `postMessage`, covering mount, send, focus, busy/idle state, and DOM stability.

## Run live diagnostics

Uses the same WebView2 profile as the app: `%LocalAppData%\ChatGPTWrapper\WebView2UserData`. Sign in to ChatGPT in the main app first (or in the diagnostic window when it opens).

```powershell
$env:CGW_RUN_LIVE_API_TESTS = "1"
dotnet test tests\ChatGPTWrapper.ApiDiagnostics --filter "Category=Live"
```

Or:

```powershell
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-api-diagnostics.ps1
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-api-diagnostics.ps1 -Open
```

Reports are written to:

- `%LocalAppData%\ChatGPTWrapper\api-diagnostic-report.json`
- `%LocalAppData%\ChatGPTWrapper\api-diagnostic-report.txt`

The text report highlights the **first failing step** and suggested next actions.

## Live checklist steps

1. `webview_init` — WebView2 environment
2. `page_injectable` — on chatgpt.com
3. `bridge_asset_on_disk` — `wrapper-assets/chatgpt-api-bridge.js`
4. `bridge_inject` — `__cgwApiInvoke` present
5. `bridge_ping` — script RPC ping
6. `bridge_postmessage_fallback` — postMessage ping (if script ping failed)
7. `api_context` — session context from bridge
8. `session_endpoint` — `/api/auth/session`
9. `device_cookie` — `oai-did` / hasDeviceId
10. `probe_sidebar` — Projects sidebar API
11. `list_bootstrap` — bootstrap gizmo list
12. `list_dom` — DOM project scrape
13. `discovery_merge` — full `ProjectDiscoveryService`
14. `client_profile` — `api-client-profile.json`
15. `existing_logs` — tail of `link-project.log` / discovery trace

## Teardown (remove entire suite)

1. Delete the `tests/` folder
2. Remove `ChatGPTWrapper.ApiDiagnostics` from `chatgpt-wrapper.sln`
3. Remove the `InternalsVisibleTo` block from `ChatGPTWrapper/ChatGPTWrapper.csproj`
4. Optionally delete `%LocalAppData%\ChatGPTWrapper\api-diagnostic-report.*`

The optional `BridgeScriptJson.cs` extraction in the main app can stay; it is independent of this project.

## Source sync performance

Report-only timing suite for project source **find**, **download**, **read**, **modify**, and **upload** operations. Tests always pass; results are written for analysis (no speed thresholds).

```powershell
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-source-sync-perf.ps1
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-source-sync-perf.ps1 -Open

# Live tier (requires ChatGPT login)
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-source-sync-perf.ps1 -Live -Open
```

Reports:

- `%LocalAppData%\ChatGPTWrapper\source-sync-perf-report.json` (latest tier to finish)
- `%LocalAppData%\ChatGPTWrapper\source-sync-perf-report.txt`
- Per-tier copies: `source-sync-perf-report-unit.txt`, `source-sync-perf-report-integration.txt`, `source-sync-perf-report-live.txt`

The text report groups steps by phase (`find`, `download`, `read`, `modify`, `upload`) with subtotals.

The script builds first and exits non-zero on build or test failure (stale reports from a prior run may still be printed).

| Tier | Test class | What it measures |
|------|------------|------------------|
| **Unit** | `ProjectSourceLocalPerfTests` | Export, hash, read bytes, planner reconcile, remote-list cache (no WebView) |
| **Integration** | `ProjectSourceMockBridgePerfTests` | Full pipeline via mock bridge (`delayMs=0` and `50`) |
| **Live** | `LiveSourceSyncPerfTests` | Real ChatGPT list/download/upload against a linked project |

**Live script switches:**

- `-SkipUpload` / `-DownloadOnly` — sets `CGW_PERF_SKIP_UPLOAD=1` (download-only run)
- `-CleanupProbe` — delete the probe file created in the current run (default **on** for `-Live`; pass `-CleanupProbe:$false` to keep it)

**Live env vars:**

- `CGW_RUN_LIVE_API_TESTS=1` — required for Live tier
- `CGW_PERF_GIZMO_ID` — optional linked project id (default: first Snorlax sidebar project); use a dedicated clean project for stable download comparisons
- `CGW_PERF_SKIP_UPLOAD=1` — skip mutating upload/attach/apply steps
- `CGW_PERF_ENSURE_PROJECT_PAGE=1` — opt in to project-page navigation timing (default: API-only from chatgpt.com)
- `CGW_PERF_TIMEOUT_MINUTES` — overall live run timeout (default: 20)
- `CGW_PERF_CLEANUP_PROBE=1` — delete current-run probe in `finally` (default on for live script; set `0` to disable)
- `CGW_PERF_CLEANUP_ALL_PROBES=1` — at run start, delete all `cgw-perf-probe-*` files on the linked project
- `CGW_PERF_DOWNLOAD_MAX` — max matched download steps (default: 6; `0` skips download phase; `1` for single-file debug)
- `CGW_PERF_DOWNLOAD_FAIL_FAST=1` — skip generic `/backend-api/files/` retries after project paths 404 for `fs` files
- `CGW_PERF_REFRESH_MATCHED_SOURCES=1` — re-upload and attach local perf bundle files before download phase (fixes stale phantom file refs)
- `CGW_PERF_SKIP_ATTACH_VERIFY=1` — skip post-attach ownership verify (perf measurement only)
- `CGW_PERF_SKIP_ATTACH_SIDEBAR=1` — skip post-attach sidebar evaluation (perf measurement only)

Example — download-only on a refreshed clean project:

```powershell
$env:CGW_PERF_REFRESH_MATCHED_SOURCES = "1"
$env:CGW_PERF_CLEANUP_ALL_PROBES = "1"
$env:CGW_PERF_GIZMO_ID = "g-p-your-project-id"
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-source-sync-perf.ps1 -Live -DownloadOnly -Open
```

Example — measure raw attach API time (no verify/sidebar):

```powershell
$env:CGW_PERF_SKIP_ATTACH_VERIFY = "1"
$env:CGW_PERF_SKIP_ATTACH_SIDEBAR = "1"
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-source-sync-perf.ps1 -Live -Open
```

Live perf tests stay on `chatgpt.com` and use bridge APIs with `ensureProjectPage: false` so they do not wait on SPA project-page navigation unless you set `CGW_PERF_ENSURE_PROJECT_PAGE=1`.

Exclude performance tests from default CI:

```powershell
dotnet test tests\ChatGPTWrapper.ApiDiagnostics --filter "Category!=Performance"
```

Results depend on machine, network, ChatGPT load, and project size — use for comparison over time, not pass/fail gates.
