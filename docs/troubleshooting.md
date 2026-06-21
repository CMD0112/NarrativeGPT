# Troubleshooting & Diagnostics

Common issues, log file locations, and recovery steps for ChatGPT Wrapper.

---

## Log and diagnostic files

All paths are under `%LocalAppData%\ChatGPTWrapper\` unless noted.

| File | Purpose |
|------|---------|
| `link-project.log` | Project linking, API errors, attach/upload attempts |
| `project-discovery-trace.jsonl` | Project list discovery (sidebar, bootstrap, DOM) |
| `sync-trace.jsonl` | Source sync operations with phase timing |
| `play-send-trace.jsonl` | Play prompt send pipeline steps |
| `api-diagnostic-report.json` / `.txt` | Live API diagnostic test output |
| `api-client-profile.json` | Captured API client headers/profile |
| `source-sync-perf-report.json` / `.txt` | Source sync performance benchmarks |
| `api-send-samples/` | Sanitized API send request/response captures |
| `last-sidebar-probe.json` | Most recent Projects sidebar probe |
| `ui-chrome.json` | UI settings (safe to edit if corrupted — delete to reset) |

### Adventure-specific

| Path | Purpose |
|------|---------|
| `adventures/{id}/` | All adventure JSON — backup before manual edits |
| `backups/` | Adventure backup zip files from **Backup** menu |

---

## Blank or broken WebView

**Symptoms:** ChatGPT tab stays white, never loads, or shows WebView2 error.

**Fixes:**

1. Install [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually included with Edge).
2. Delete `%LocalAppData%\ChatGPTWrapper\WebView2UserData` **only if** you accept losing login cookies — then sign in again.
3. Check antivirus is not blocking `ChatGPT Wrapper.exe` or WebView2 subprocesses.
4. Portable build: ensure you extracted the full `dist` folder, not just the exe.

---

## Not signed in / session expired

**Symptoms:** Connection tab shows not signed in; API calls return 401; "Sign in to ChatGPT" errors.

**Fixes:**

1. Switch to **Browse**, open a ChatGPT tab, sign in normally.
2. Verify `WebView2UserData` is writable (not redirected by enterprise policy).
3. **Test connection** in Projects workspace — requires both session **and** `oai-did` device cookie.
4. If device cookie missing: fully load `chatgpt.com` in a tab and wait for SPA init before testing API.

---

## Bridge / automation failures

**Symptoms:** Play send hangs; "bridge not ready"; automation timeout; composer not filling.

**Diagnosis:**

1. Play header or Context viewer shows bridge health / automation status.
2. Check `play-send-trace.jsonl` for the failed step.
3. Ensure you are on a trusted `chatgpt.com` URL (not login redirect).

**Manual fallback (always works):**

1. Open **Context** — copy the full prompt packet.
2. Paste into ChatGPT composer manually.
3. Copy the assistant reply.
4. **Response review** → paste and **Accept**.

**Fixes:**

1. Refresh the play-pinned ChatGPT tab (navigate to conversation URL).
2. Toggle out of Play and back to re-initialize injections.
3. Restart the app if `__cgwAdventureHandleCommand` or bridge scripts failed to inject.

---

## Project discovery empty

**Symptoms:** Projects tab shows no projects after Refresh.

**Fixes:**

1. Confirm Projects exist in ChatGPT web UI for your account tier.
2. Open Projects in ChatGPT in another tab — captures auth headers for API.
3. Use **Advanced: URL** with direct Project link.
4. **Copy diagnostics** — inspect `link-project.log` and `project-discovery-trace.jsonl`.
5. Run live API diagnostics (developers): see [Testing — Live diagnostics](testing.md#live-diagnostics).

---

## Source sync failures

### Conflicts blocking apply

Resolve each **Conflict** row: Keep local, Keep remote, or Skip — then **Apply all**.

### Remote file 404 / deleted in browser

When ChatGPT UI deletes a project file but the manifest still references it:

1. Sync plan shows **MissingRemote**
2. **Apply safe** may re-upload local copy (ApiSync mode)
3. Or clear stale `remoteFileId` via sync reconcile and re-push
4. See [adventure-developer-reference.md — Recovering from browser-deleted files](adventure-developer-reference.md#recovering-from-browser-deleted--404-project-files)

### ApiSync attach/upload errors

1. Open **API sync diagnostics** from Source Manager
2. Check `link-project.log` for attach attempt sequence
3. Try **Manual** publish mode as workaround
4. Copy `api-client-profile.json` when reporting issues

---

## Utility job failures

**Symptoms:** Entity extraction or memory jobs fail; compose status shows `{jobId} failed: {error}`; errors in Play settings → Session tab.

**Diagnosis:**

- `AdventureMetadata.UtilityJobLastErrors` persists last error per job id
- `play-send-trace.jsonl` — search for `utility_job_phase` (readiness level, send_api vs send_dom)
- Full error reference: [utility-job-orchestration.md § Error codes](utility-job-orchestration.md#error-codes)

### Common errors

| Error | Meaning | Fix |
|-------|---------|-----|
| `utility_page_not_ready` | Utility WebView not on target conversation or composer missing | Let ChatGPT finish loading; **pin a utility tab** (Play settings → Session); retry |
| `bridge_not_ready` | Adventure bridge not injected | Refresh ChatGPT tab; restart app |
| `conversation_unregistered` | API cannot see utility thread | Open linked Project in ChatGPT; retry after SPA init |
| `rate_limited` | Too many API probes (`http_429`) | Wait ~15 seconds; retry |
| `capture_timeout` | DOM atomic turn timed out waiting for `turnComplete` | Pin utility tab; shorten story context; check model still generating |
| `empty_response` | Turn completed with no assistant text | Rotate utility thread; retry |
| `submit_not_observed` | Submit not verified in DOM | Pin utility tab; ensure not on homepage |
| `utility_seed_send_failed: http_403` | Seed could not register conversation with API | Open Project tab in ChatGPT; retry |

**Fixes:**

1. Ensure Project is linked (most utility jobs require it).
2. **Pin a utility tab** — open Project → New chat → pin in Play settings → Session (recommended for DomOnly threads).
3. Pin the **play tab** on your story thread for live story-context capture.
4. Rotate utility session from play settings if conversation is corrupted.
5. Review `utility-exchanges.json` in adventure folder for raw job history.

See [Utility Job Orchestration](utility-job-orchestration.md) for the full pipeline.

---

## Thin packets but model forgets lore

**Cause:** Manifest may show InSync but Project files not actually attached/retrieved by ChatGPT.

**Fixes:**

1. Open ChatGPT Project UI — verify files are present.
2. Re-confirm manual publish or re-apply ApiSync push.
3. Temporarily enable **Force fat packets** in adventure settings.
4. Run **Probe** from Source Manager to verify remote hashes.

---

## Corrupted adventure data

**Symptoms:** Adventure won't load; JSON parse errors.

**Fixes:**

1. Restore from `backups/` via **Import backup** (Adventures menu).
2. Manually inspect `{adventure}/adventure.json` — must be valid JSON.
3. Delete individual corrupt document files; `AdventureStore` recreates defaults on missing files.

---

## Running API diagnostics (advanced)

Requires .NET SDK and the test project:

```powershell
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-api-diagnostics.ps1 -Open
```

Sign in to ChatGPT in the main app first. Reports written to `api-diagnostic-report.txt` with the **first failing step** highlighted.

See [Testing](testing.md) for full checklist steps and env vars.

---

## Backup and restore

**Backup:** Adventures dashboard → select adventure → **Backup** (or Play → More → Backup).

Creates a timestamped archive in `%LocalAppData%\ChatGPTWrapper\backups\`.

**Restore:** **Import backup** — merges or replaces adventure folder by GUID.

Implemented by `BackupService` in `Adventure/Stores/BackupService.cs`.

---

## Reporting issues

Include when possible:

1. **Copy diagnostics** from Projects workspace
2. `link-project.log` (last 50 lines)
3. `api-diagnostic-report.txt` if API-related
4. App version / portable vs dev build
5. Whether Manual or ApiSync publish mode

---

## Related documentation

- [User Guide](user-guide.md)
- [Projects & Source Sync](user-projects-and-sync.md)
- [WebView Bridges](webview-bridges.md) — bridge command failures
- [ChatGPT API Integration](chatgpt-api-integration.md) — endpoint errors
- [Testing](testing.md) — automated diagnostics
