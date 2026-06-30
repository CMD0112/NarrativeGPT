# Projects & Source Sync — User Guide

Link adventures to **ChatGPT Projects** so lore lives in Project source files (retrieved by ChatGPT) instead of being repeated in every play prompt. For the theory behind instructions vs sources vs packets, see [Instruction vs Sources Paradigm](instruction-sources-paradigm.md). For defining boundaries and portrayal rules, see [Instruction Contract Guide](instruction-contract-guide.md).

---

## Prerequisites

1. **Sign in** to ChatGPT in any wrapper browser tab (Browse mode).
2. Have or create a **ChatGPT Project** (ChatGPT's "Projects" feature with custom instructions and file uploads).
3. Optional: ChatGPT Plus or tier that supports Projects (depends on OpenAI account).

---

## Quick start: link a Project

### From the dashboard

1. **Adventures** → select an adventure → **Link Project…** (toolbar or right-click menu)

### From Play mode

1. **Play** → **Link Project…** in the header, the yellow banner, or click the thread status line when no project is linked

All paths open **Link ChatGPT Project** (`ProjectWorkspaceDialog`), starting on the **Projects** tab with the list refreshed automatically.

If project discovery fails, use **Paste URL** on the Projects tab and paste a URL like `https://chatgpt.com/g/g-p-…` or the raw gizmo id.

---

## Link ChatGPT Project dialog

`ProjectWorkspaceDialog` has three tabs when linked (Projects, Connection, Sources). On first link, only **Projects** and **Connection** appear — **Sources** unlocks after linking.

### Connection tab

| Check | Meaning |
|-------|---------|
| Signed in | `/api/auth/session` returns a valid user |
| Device cookie | `oai-did` present (required for API calls) |
| Bridge warm | `chatgpt-api-bridge.js` injected and responding to ping |

**Test connection** runs the checklist. If Projects list is empty, browse Projects in ChatGPT in another tab — headers are captured automatically for API discovery.

**Copy diagnostics** writes paths and recent log tails to the clipboard (see [Troubleshooting](troubleshooting.md)).

### Projects tab (default)

Opens first when you link from the dashboard or Play mode. Projects load automatically; use **Refresh** if the list is stale. When not fully signed in, an inline warning on this tab links to the **Connection** tab.

Mode selector (radio chips, not nested tabs):

- **From list** — select a project, then **Link Project** (or double-click a row). Selection context appears above the list.
- **Create new** — name is pre-filled from the adventure title
- **Paste URL** — link by pasted URL or gizmo id

**Link options** expander (collapsed by default): sync via API, push narrator instructions, create play thread.

**Link Project** / **Switch Project** — fixed footer label; runs binding flow (enabled on Projects tab only when inputs are valid).

### Sources tab

Shows the sync plan for canonical markdown files. Opens automatically after linking with sync enabled.

---

## Canonical source files

The wrapper exports these files to `{adventure}/sources/` and syncs them with the ChatGPT Project:

| File | Content |
|------|---------|
| `scenario.md` | Opening situation, premise |
| `world.md` | World rules, setting |
| `author-note.md` | Style guidance (no new facts) |
| `boundaries.md` | Content boundaries |
| `memories.md` | Pinned / reviewed memories |
| `story-cards.md` | Story card definitions |
| `instructions-snippet.md` | RAG mirror of Project custom instructions |
| `characters.md` | Character bible (when entities exported) |
| `plot.md` | Plot essentials (when applicable) |

Managed by `ProjectSourceExportService`. The Sources tab and **Source Manager** show publish status per file.

---

## Publish workflow

**Manual publish** is the only active workflow. Export locally, upload to ChatGPT Project, mark **Published** in Source Manager.

**Remote sync diagnostics** (Source Manager) can repair remote bindings when troubleshooting — not the primary publish path.

See [instruction-sources-paradigm.md § Publish workflow](instruction-sources-paradigm.md#publish-workflow-manual-only).

### Manual publish walkthrough

1. Export or edit files locally in **Source Manager**
2. **Design instructions…** → define contract → **Generate instructions file** → **Copy instructions** (narrator contract: perspective, tone, boundaries, portrayal rules — see [instruction-contract-guide.md § Tutorial](instruction-contract-guide.md#tutorial-drafting-narrator-instructions))
3. Paste into ChatGPT Project custom instructions
4. Drag updated `.md` files into the Project's file area
5. In Source Manager, **Confirm published** on each file (or bulk confirm)

The manifest records `manuallyPublishedAt` and `manuallyPublishedSha256` per file.

---

## Source sync states

Each file in `source-manifest.json` has a **sync state**:

| State | Meaning | Typical action |
|-------|---------|----------------|
| **InSync** | Local and remote hashes match | None |
| **LocalNewer** | You edited locally | Push to Project |
| **RemoteNewer** | Edited in ChatGPT UI | Pull to `sources/` |
| **Conflict** | Both sides changed since baseline | Resolve manually |
| **LocalOnly** | Not on Project yet | Push |
| **MissingRemote** | Remote file deleted (404) | Re-push or clear binding |
| **RemoteOnly** | On Project but not tracked locally | Pull or ignore |

### Apply actions

In **Source Sync** dialog:

- **Apply safe** — only non-conflicting Pull/PushReplace actions
- **Apply all** — requires all conflicts resolved first
- Per-row resolution: Keep local / Keep remote / Skip

---

## Packet profiles (what you see)

| Condition | Profile | What you notice in **Context** viewer |
|-----------|---------|--------------------------------------|
| No linked Project | **Minimal local** | Opening + session deltas; pointers note to link a Project |
| Project linked, sources **not** all published | **Source-delegated** (preview) or **Inline fallback** (after send warning → No) | Preview: pointers + `Sources not ready`; inline: full lore |
| Project linked, all lore files **Published** | **Source-delegated** | Pointers only; lore delegated to Project RAG |
| **Force inline lore (debug)** enabled | **Inline fallback** | Always full inline |

Source-delegated packets are smaller and rely on ChatGPT retrieving from Project files. If narration "forgets" lore, publish sources in Source Manager — unpublished files block delegation.

Play link status line (play header) shows conversation URL, project link health, and sync summary. Format documented in [adventure-developer-reference.md §6](../developer/adventure-developer-reference.md#play-status-line-format).

---

## Mid-play sync workflow

1. Edit `world.md` (or other source) locally via **Source Manager**
2. **Refresh export** → upload to ChatGPT Project → mark **Published**
3. Next play turn uses **source-delegated** packets once all core lore files are published

---

## Phase 1 manual test checklist (play loop)

1. **Adventures** → **New adventure** → fill scenario → **Create** → Play opens.
2. Click **Start adventure** → accept narrator response → text appears in story log.
3. Send a second turn with **Do** or **Say** → **Context** shows `=== SCENARIO ===`, mode prefix, automation/bridge health.
4. **Response review** → **Retry** → prior narrator text kept in archive view; no duplicate prompt-history rows for the same turn.
5. **Export** → Markdown → file contains accepted turns only.
6. Restart app → adventure loads; link status shows `chatgpt.com/c/{id}` after first accepted turn.

If automation fails, use manual fallback: copy the packet from Context, paste into ChatGPT, paste the reply into review.

---

## Phase 2 Projects checklist

1. Log in to ChatGPT in any browser tab.
2. **Adventures** → **Link Project…** or Play → **Link Project**.
3. **Connection** → **Test connection** (signed in, device cookie).
4. **Projects** → **Refresh** or **Advanced: URL**.
5. Link project → **Sources** tab shows publish status.
6. **Manage sources…** → export, upload, mark Published.
7. **Copy diagnostics** if listing fails.
8. Edit `world.md` locally → re-export → re-publish.
9. **Context** shows source-delegated packets when all lore files are published.

---

## Utility jobs and Projects

Generation jobs (entity extraction, memory proposals, etc.) run in **utility ChatGPT threads** inside the same linked Project. They use inline instruction guides — not separate `*-guide.md` source files. Configure per-job overrides in **Play settings → AI Actions**.

See [instruction-sources-paradigm.md § Generation jobs](instruction-sources-paradigm.md#generation-jobs-delegation-matrix-target).

---

## Related documentation

- [Adventure Developer Reference §6–7](../developer/adventure-developer-reference.md) — technical linking and sync details
- [Instruction vs Sources Paradigm](instruction-sources-paradigm.md)
- [Troubleshooting](troubleshooting.md) — API bridge failures, 404 recovery
- [Data Model — Source manifest](../reference/data-model-reference.md#source-manifest)
