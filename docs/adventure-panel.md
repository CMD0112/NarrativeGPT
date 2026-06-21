# Adventure Panel — User Reference

User-facing reference for the **Adventure panel** in ChatGPT Wrapper: UI surfaces, dialogs, workflows, and smoke checklists. For data model, turn automation, and service internals, see [Adventure Developer Reference](adventure-developer-reference.md).

**Documentation hub:** [INDEX.md](INDEX.md)

**Related docs:** [Adventure Developer Reference](adventure-developer-reference.md) · [Projects & Source Sync (user guide)](user-projects-and-sync.md) · [Instruction Contract Guide](instruction-contract-guide.md) · [Data Model Reference](data-model-reference.md) · [Services Reference](services-reference.md) · [Instruction vs Sources Paradigm](instruction-sources-paradigm.md) · [WebView Bridges](webview-bridges.md) · [Troubleshooting](troubleshooting.md)

---

## Table of contents

1. [Overview](#1-overview)
2. [Application shell and navigation](#2-application-shell-and-navigation)
3. [Adventure dashboard](#3-adventure-dashboard)
4. [Play view](#4-play-view)
5. [Dialogs and modals](#5-dialogs-and-modals)
6. [Prompt packets, projects, and sync (overview)](#6-prompt-packets-projects-and-sync-overview)
7. [End-to-end workflows](#7-end-to-end-workflows) — includes [**canonical begin-play**](#g-canonical-begin-play-workflow-design--first-turn)
8. [Diagnostics and logging](#8-diagnostics-and-logging)
9. [Known limitations and edge cases](#9-known-limitations-and-edge-cases)

---

## 1. Overview

### Purpose

The Adventure panel is a **local-first interactive fiction engine** inside ChatGPT Wrapper. It lets you:

- Create and manage multiple adventures stored only on disk.
- Build structured **prompt packets** from scenario, state, memory, entities, and transcript.
- Send those packets to ChatGPT via **WebView2 automation** (or manual clipboard fallback).
- Optionally link an adventure to a **ChatGPT Project** and sync markdown **source files** so the model can retrieve lore from the Project instead of repeating it in every packet.

**Privacy principle (shown in the dashboard):** all adventure documents stay under `%LocalAppData%\ChatGPTWrapper`. Only the text you explicitly send as a prompt packet goes to ChatGPT during play.

---

## 2. Application shell and navigation

### Mode buttons (MainWindow toolbar)

| Button | `AppMode` | Left column | Chat tabs | Chat chrome |
|--------|-----------|-------------|-----------|-------------|
| **Browse** | `Browse` | Hidden (width 0) | Visible, full width | Visible |
| **Adventures** | `Adventures` | Dashboard (`AdventureDashboardView`) | Hidden | Visible |
| **Play** | `Play` | Tabbed play companion (~300px, collapsible per adventure) + notes panel (~240px, right, collapsible) | Visible, primary width | Visible |
| **Design** | `Design` | Fixed-width design companion (~420px), step tabs, no notes rail | Visible, primary width | Visible |

Play and Design are entered from the dashboard (not toolbar mode buttons). **Play ↔ Design** in-session: shell **Play / Design** toggle (same adventure session), Play **Continue design…**, and Design **Launch adventure** — see [Play/Design surface convergence ADR](play-design-surface-convergence-adr.md).

Implementation: `SetAppMode()` in `MainWindow.Adventures.cs`.

### Play mode layout

```
┌────────────────────┬──────────────────────────────┬─────────────────────────┐
│  AdventurePlayView │  Normal ChatGPT browser tabs │  PlayRightCompanionHost │
│  (cockpit + left   │  (pinned tab receives Send   │  (right tabs + notes,   │
│   companion tabs)  │   automation; live chat)     │   notes + right tabs)   │
└────────────────────┴──────────────────────────────┴─────────────────────────┘
```

Each side panel has its own collapse rail and resizable splitter (left: 200–640px as `PlaySidePanelWidth`; right: 180–480px as `PlayNotesPanelWidth`). Collapse state persists per adventure as `PlaySidePanelCollapsed` and `PlayNotesPanelCollapsed`. The right column hides automatically when no tabs or notes are assigned to the right host.

**Layout customization:** Play settings → **Play surface** offers named presets (Writer, GM, Minimal) and per-tab placement (`Reference`, `Warnings`, `State`, `Notes` → Left / Right / Hidden). `PlayPanelLayoutService` and `PlayLayoutPresetLibrary` apply placement; `NavigateToPlayTab` expands the correct column and selects the tab in either host.

- Entering play: `StartPlayModeAsync(adventureId)` loads the bundle, wires events, and calls `EnsurePlaySessionAsync` to validate or prompt for a **pinned play tab**.
- **Link to active browser tab** in Play settings → **Session** assigns the current ChatGPT tab for Send automation; **Open pinned tab** switches back to it.
- Leaving play: **← Dashboard** sets mode back to Adventures and refreshes the dashboard list.

### UI chrome

WPF shell, adventure views, and dialogs share a dark token palette in `ChatGPTWrapper/Themes/WrapperTokens.xaml` and `WrapperControls.xaml` (muted slate-blue accent `#5B9FD4`, layered surfaces, semantic success/warning/error brushes). Wrapper-owned in-page UI (play composer, continuous-view overlay chrome, context tags, scrollbars) mirrors the same tokens via `--cgw-*` CSS variables in `ChatGPT_files/wrapper-overrides.css`. No blur, shadow effects, or animations were added to preserve WebView2 and story-log performance.

### Events wired in Play mode

| Play view event | MainWindow handler |
|-----------------|-------------------|
| `BackRequested` | `OnPlayBack` |
| `PinPlayTabRequested` | `OnPinPlayTabRequested` |
| `OpenPinnedPlayTabRequested` | `OnOpenPinnedPlayTabRequested` |
| `ClearPlayTabPinRequested` | `OnClearPlayTabPinRequested` |
| `RollIntoPlayerLineRequested` | `AppendPlayPlayerLineText` |

---

## 3. Adventure dashboard

**File:** `AdventureDashboardView.xaml`

The dashboard is the home screen for all adventures. It lists local adventures and provides creation, import, library, and project-link entry points.

### Toolbar controls

| Control | Name | Behavior |
|---------|------|----------|
| **New adventure** | `NewAdventureButton` | Opens `ScenarioCreationDialog`. On success, creates adventure via `AdventureStore.CreateNew`, saves, refreshes grid, auto-starts Play mode. |
| **Import…** | `ImportButton` | OpenFileDialog (`*.zip`) → `BackupService.RestoreBackup`. |
| **Search** | `SearchBox` | Filters grid by title, genre, or tags (case-insensitive substring). |
| **More…** | Menu: Link Project, Libraries, Save scenario to library, **Draft adventure framework…** (design-thread job; import draft in Design → Sources). |

### Privacy hint and archive filter

- `LocalOnlyHint` — static text pointing to `AppDirectories.Root`.
- **Show archived** (`ShowArchivedCheck`) — when unchecked (default), archived adventures are hidden from the grid.

### Adventure grid

`AdventureGrid` — read-only DataGrid columns: Title, Genre, Status, Last played.

**Interactions:** single-click select; double-click → **Play**.

### Bottom actions

| Control | Behavior |
|---------|----------|
| **Play** | `PlayRequested` for selected adventure. |
| **More…** | Menu: Toggle archive, Backup, Delete. |

---

## 4. Play view

**File:** `AdventurePlayView.xaml`

The play sidebar is a **tabbed play companion**: session cockpit, reference entities, director notes, and read-only state. Narrative reading happens in the pinned ChatGPT tab (continuous view); turns are stored in the adventure bundle and reachable via Search, Export, and Edit turn.

### Header

| Control | Behavior |
|---------|----------|
| **← Dashboard** | Raises `BackRequested`. Hidden when shell breadcrumb is active. |
| Title | `Metadata.Title`. |
| **Play settings…** / **Sources…** | Primary `ShellCommandBarStyle` actions. Sources shown when linked or attention needed. |
| **More…** | Rename, Link/Change Project (when banner hidden), Continue design. |
| **◀ / ▶ left collapse toggle** | Grip capsule + chevron between left panel and chat — click chevron to hide/show; drag grip to resize (200–640px, `PlaySidePanelWidth`). Double-click grip for recommended width. |
| **◀ / ▶ right collapse toggle** | Same grip family between chat and right companion host. Tooltip shows first line of notes when notes are on the right. Drag to resize (180–480px, `PlayNotesPanelWidth`). |

### Session cockpit

Expanders: **Session** (pin / thread / sources), **Narrator** (scene profiles, scoped overrides with inherit, advanced directive), **AI tools** (wrap panel or **AI tools…** flyout below 280px), **Reviews** (pending-review banner with Memories / Summary / Entities / Cards shortcuts; `ShellBadgeStyle` count on expander header).

A slim **pending review** row above expanders stays visible when review items exist. Review queue in the Reference tab appears only when navigated via Entities / `FocusEntityReviewQueue` (deduped from cockpit).

### TabControl

Companion tabs can host on the **left** (`PlaySideTabControl`) or **right** (`PlayRightTabControl` inside `PlayRightCompanionHost`) per `PlayTabPlacement`.

| Tab | Purpose |
|-----|---------|
| **Reference** | Filter pills (Characters, Locations, …); list-based entity rows with pin/role chips; sticky Add/Edit/Delete + overflow (Pin, Suggest, Expand); inline review queue when navigated from cockpit. |
| **Warnings** | Continuity list with severity chips, last-checked line, dismiss/open-in-Reference actions; empty state when none. Also reachable from AI tools **Continuity**. |
| **State** | World skim: summary/location/objectives preview cards, empty-state card, last-updated hint, optional **All fields** grid; edit via Play settings → World. Location card links to Reference when a matching entity exists. |

### Notes panel

**File:** `AdventureNotesPanel.xaml`

`bundle.Notes` (`notes.txt`) in a `ShellCardStyle` card — **never injected into packets**. Hosted in the right column `NotesSlot`; placement Right or Hidden via Play settings → Play surface.

| Feature | Behavior |
|---------|----------|
| **Autosave** | Debounced save (~400ms) to `notes.txt`; footer shows Saving… / Saved time; blur and play exit flush immediately |
| **Find** | Toolbar **Find** or Ctrl+F — inline query, match-case toggle, Prev/Next with wrap (Enter / Shift+Enter), match count updates as you type without jumping until you navigate; × or Esc closes |
| **Insert** | Timestamp, turn reference (`[Turn N]`), selected Reference entity name, section heading (`## `) at caret |
| **More…** | Copy all, select all, export to `.txt`; in compact mode, jump-to-section submenu |
| **Sections** | Lines starting with `## ` populate a jump list; combo tracks caret position (plain-text convention; no schema change) |
| **Navigation** | `NavigateToPlayTab("Notes")` expands the right column and focuses the editor |
| **Collapsed rail** | Tooltip shows word count and first-line preview |

Responsive: compact toolbar icons below `NotesFullChrome` (280px content width).

### Footer action bar

`ShellCommandBarStyle` row: **Search** and **Export** visible; **More actions…** menu for Branch, Roll, Move to new play thread, Continue design. Use dashboard **Backup** or **Export…** (ZIP) for snapshots — not in-play Save state. Icon-only mode below 280px width.

### In-page composer (primary play controls)

**File:** `cgw-play-compose.js` on the pinned ChatGPT tab.

| Control | Behavior |
|---------|----------|
| **Send** | `SendPlayPromptAsync` — sends packet, captures narrator response, auto-logs turn via `TurnTimelineService.AcceptTurn`. |

**Player input for Send** (first match wins):

1. Wrapper composer text box
2. Fallback player line from **Play settings → Next send** (when composer is empty)
3. Continuation queue (first line, consumed on Send; edited in Play settings)

After Send, status shows *"Sent — turn logged."* (or a capture warning if narrator text was unavailable).

Linked Project play context is prepared when entering Play mode. Subsequent Sends reuse `PlayContextSessionCache` when the pinned tab is already on the play thread.

### Manual play-mode smoke checklist

1. Enter Play → collapse/expand left and right panels independently; layout persists per adventure.
2. Edit notes in the right panel → autosave indicator shows Saved; verify `notes.txt` updated; Ctrl+F find; insert timestamp; `## Section` jump list; confirm notes absent from merged packet preview.
3. **Reference** → add/edit/delete entity; accept/dismiss review queue item if present.
4. **Play settings → World** → edit summary/location/objectives → Send still builds correct packet.
5. Pin play tab from session cockpit or Play settings → **Session** tab status updates.

### Play surface reliability (CMD-28)

| Check | Expected |
|-------|----------|
| **Adventures** mode switch | Dashboard shows loading overlay briefly, then adventure list (never blank host) |
| **Continuous view off** send | Player line visible in thread; no indefinite hidden state after navigation |
| **Continuous view on** long thread | Player/narrator segments alternate even when ChatGPT DOM groups roles |
| **Generation job** completes | Composer restored and interactive (native or wrapper per settings) |
| **Play settings** accept/dismiss memory | Pending-review banner and **Memories** button update without closing settings |
| **Review / Entities** with collapsed panel | Correct column expands (left or right per placement); tab selected; hidden tab shows Play settings → **Play surface** guidance |
| **Wrapper composer** | Last messages not covered; scroll host reserves bottom inset |
| **Attachment send** | Merged packet includes `=== ATTACHMENTS (staged with this turn) ===` when files staged |

**Canonical play record:** The linked ChatGPT play thread is the narrative source of truth. Accepted turns in `log.json` are a derived cache synced on user prompt via `ThreadLogSyncService` + `SyncFromThreadDialog`. `thread-metadata.json` maps DOM ordinals. Turns are logged automatically on Send. Correct text via continuous-view surrogate edit — not local-only Undo/Edit turn.

### Panel layout customization (shipped)

Play settings → **Play surface**:

| Feature | Description |
|---------|-------------|
| **Layout presets** | Writer, GM, Minimal — set tab placement and optional collapse defaults |
| **Tab placement** | Per tab (`Reference`, `Warnings`, `State`, `Notes`) → Left / Right / Hidden |
| **Right host** | `PlayRightCompanionHost` stacks relocated tabs above notes |
| **Navigation** | `NavigateToPlayTab` expands the correct column and selects the tab |

Custom edits to the placement grid clear the active preset (`PlayLayoutPresetId` → null).

### Narrator overrides (session cockpit + Next send)

> **Full reference:** [Narrator Settings](narrator-settings.md) — scopes, scene profiles, advanced dialog, packet injection, persistence, and code map.

Narrator behavior can be modulated at three scopes:

| Scope | Storage | Lifetime |
|-------|---------|----------|
| **Adventure default** | `AdventureSettings` (`detailLevel`, `tone`, `difficulty`, …) | Until changed in Play settings or cockpit |
| **Session** | `SessionNarratorOverrides[sessionId]` | Active play session (`CurrentSessionId`) |
| **This send** | `PlayTurnOverrides` | Next play packet only; cleared after a successful send |

Each parameter supports **— inherit —** (null): the effective value falls through session → adventure baseline. Only values that differ from the adventure contract baseline are injected into the merged packet.

**Scene profiles** (Action, Exploration, Introspection, Social, Lore) apply coordinated presets to the active scope.

**Advanced** (dialog): turn directive (one-shot `=== TURN DIRECTIVE ===`), session addendum, emphasis toggles for boundaries/portrayal rules.

Packet injection (`NarratorOverrideResolver`):

```
=== TURN OVERRIDES ===
Response length: brief
Tone: grim
Session note: …

=== TURN DIRECTIVE ===
Keep this exchange terse and tactical.
```

Configure from Play settings → **Next send** or the session cockpit **Narrator** expander. Below 280px panel width, use the **Narrator…** flyout to expand controls or open Advanced.

---

## 5. Dialogs and modals

### PlayPromptInjectionDialog (Play settings)

Tabbed editor for play configuration and injected prompt content:

- **Next send** — continuation queue, fallback player line, live merged preview, copy/view/start packet actions
- **World** — rolling summary, location, objectives, author's note (saved on OK)
- **AI Actions** — per-job utility instruction editor (built-in defaults, customize, reset); per-job response length/detail overrides; story context feed
- **Session** — dual-pin setup: **play tab** (Send automation) and **utility tab** (AI jobs); thread and sources status; **Start new play thread…** (releases stale conversation/pin, copies start packet); **Draft new project chat…** (pause redirect while drafting on Project page); per-job utility thread status; open / rotate utility thread; utility parse archive; link to Sources tab
- **Play surface** — attachment context mode (`Auto` / `Full` / `Minimal`), attachment-only placeholder, inject guidance toggle; play quick-action visibility (`Visible` / `Hidden` / `InjectedOnly`); **layout presets** and side-panel tab placement (`Left` / `Right` / `Hidden`, including Notes)
- **Settings** — max packet size, automation, force fat packets, perspective, global content boundaries, character portrayal rules, instruction addendum; **Auto-extract entities** (requires linked Project). See [instruction-contract-guide.md](instruction-contract-guide.md).
- **Memory & cards** — pinned memory and keyword-triggered story cards included in packets
- **Sources** — project source readiness, manifest sync state, refresh/sync actions
- **History** — recently sent merged packets (view/copy)

OK persists queue, world fields, adventure settings, memory, cards, and fallback line to the adventure bundle.

### Generation jobs (Step 2)

> **Delegation paradigm:** Utility jobs use a separate channel from the play narrator. Instructions are built-in defaults with optional per-adventure overrides (Play settings → **AI Actions**), always inlined in utility-thread seed and job packets — not Project source files. See [instruction-sources-paradigm.md](instruction-sources-paradigm.md).

Project-linked adventures run **background utility conversations** inside the same ChatGPT Project (not the play thread).

### Background utility chat (default)

Utility jobs run in a **hidden auto-managed WebView** — no second browser tab required. Link a ChatGPT Project, pin the **play tab** on your story `/c/…` thread, then run Memories, Summary, Cards, etc. The app creates or **reuses** per-job utility conversations (by session metadata and title reconcile — not discarded just because a thread is missing from the project sidebar list).

**Send orchestration (current):** Before each send, a **readiness probe** classifies the utility page:

| Level | Send path |
|-------|-----------|
| **Registered** (API sees the conversation) | Internal API `SendUserMessageAsync` |
| **DomOnly** (404/429 on API — typical for client-bootstrapped threads) | Atomic DOM `sendPrompt` → `turnComplete` (same pattern as play turns) |
| **Unready** | Fail fast — no phantom submit |

There is no separate multi-minute capture loop. DomOnly jobs use one atomic bridge turn with a timeout scaled to packet size (default 120s, up to 180s).

Full pipeline reference: **[utility-job-orchestration.md](utility-job-orchestration.md)**

Responses accept JSON arrays in markdown fences or object envelopes (`{ "memories": [...] }`). Null-safe parsing handles `null` entries in API JSON arrays.

### Optional utility tab override

To inspect or debug utility threads in a visible tab:

1. Open a **second** ChatGPT tab (not the pinned play tab). In that tab, open the linked Project and click **New chat**.
2. In **Play settings → Session**, pin it as the **utility tab** (must be a `/c/…` page, not the play thread).
3. Jobs use the pinned tab instead of the hidden WebView until you clear the pin.

**Rotate selected thread** re-seeds job instructions on the active utility conversation (resets per-job `JobCount`).

Each job type tracks its own `UtilitySessions` entry (`JobCount`, seed version, errors). Auto-created threads use naming `[CGW:{kind}] {Title} · {AdventureId} · #{n}`.

| Job ID | Utility prefix | Review target |
|--------|----------------|---------------|
| `extract_entities` | `[CGW:entity]` | Reference → entity review queue |
| `propose_memories` | `[CGW:memory]` | Play settings → Memory review list |
| `update_summary` | `[CGW:summary]` | Play settings → World summary banner |
| `bootstrap_lore` / `expand_story_card` | `[CGW:lore]` | Play settings → Card review list |
| `continuity_check` | `[CGW:check]` | Warnings tab |
| `generate_recap` | `[CGW:recap]` | Recap dialog (display only) |

| Component | Role |
|-----------|------|
| `GenerationJobService` | Ensure/reconcile utility threads, readiness probe, tiered send, parse, enqueue |
| `UtilityConversationReadinessService` | Registered / DomOnly / Unready gate before send |
| `AdventureTurnService.SubmitUtilityJobAsync` | Atomic DOM utility turn (`sendPrompt` → `turnComplete`) |
| `GenerationJobScheduler` | Post-turn auto-queue from adventure settings |
| `UtilitySessions` metadata | Per-job active thread state + archive on rotation (not on missing sidebar alone) |
| Utility thread seed | Full instruction body in thread (`GenerationJobHandlers.BuildSeedPrompt`) |
| Job packet inline guide | Same instruction body appended per run (`=== JOB GUIDE (inline) ===`) |
| **AI Actions** tab (Play settings) | Edit / reset per-job instruction overrides (`UtilityJobGuideOverrides`) + **story context feed** settings |
| `PendingReviewService` | Aggregates review-queue counts; play cockpit banner routes to accept/dismiss UI |

**Review after jobs:** When a job queues proposals, the compose status line names where to review (e.g. Play settings → Memory & cards). The play cockpit also shows a **pending review** banner with shortcuts to Memories, Summary, Entities (Reference tab), or Cards.

**Story context feed (AI Actions → Story context):** Each utility job packet can include live play-thread transcript (primary), with fallback to accepted `log.json` turns. Configure in grouped panels:

- **Source & lookback:** source mode, max/skip/min turn pairs, max transcript chars; advanced anchors (`FromEnd`, `SinceLastAcceptedTurn`, `SinceTurnIndex`, `AcceptedOnly`)
- **Transcript roles:** include player and/or narrator messages; layout (verbatim or compact arrow)
- **Live/local behavior:** pending local turns, exclude incomplete trailing live pair, strip empty pairs, per-pair char cap
- **Ancillary sections:** summary, state, pinned memory, entities, scenario, direction preamble
- **Treatment:** max total chars, trim strategy, omit redundant turn slices in job body when transcript is present

Use **Preview (local)** or **Preview (live)** (requires pinned play tab). After jobs, compose status shows capture source and pair count (e.g. `story context: live API · 8 pair(s)`). Per-action overrides: `UtilityJobGuideOverrides[job].Context`; defaults: `metadata.json` → `settings.utilityStoryContext`.

**Inline utility pattern:** Built-in defaults in code + optional overrides in adventure metadata. Publish mode does not affect utility jobs. Job schemas are **not** in narrator project instructions or `sources/`.

**Session cockpit buttons:** Memories (AI), Summary (AI), Cards (AI), **Publish sources…** (manual) or **Sync via API…** (advanced), Recap (AI). Reference tab: **Suggest from last turn (AI)**.

**Play settings → Settings auto toggles:** extract entities, propose memories, update summary (interval), continuity check, auto-sync project instructions on OK.

**`AutoSyncProjectInstructions`:** Pushes `BuildProjectInstructions` on Play settings OK only when instruction-domain fields changed (`InstructionSourcesPolicy` hash). World/plot/entity edits do not trigger a push. Sources tab shows instruction drift hints.

**Requires linked Project** — without a Project, all generation jobs are disabled; manual CRUD still works.

### ScenarioCreationDialog

**Purpose:** Create a new adventure from structured scenario fields.

| Field | Maps to |
|-------|---------|
| Title | `AdventureMetadata.Title` |
| Genre | `ScenarioDocument.Genre` |
| Setting | `Scenario.Setting` |
| Player role | `Scenario.PlayerRole` |
| Opening situation | `Scenario.OpeningSituation` |
| Plot essentials | `Scenario.PlotEssentials` |
| Author's note | `Scenario.AuthorsNote` |
| Offer Start adventure on first play | `Settings.OfferStartOnPlay` |

**Buttons:** Cancel, **Create** (default).

---

### AdventureSettingsDialog

Legacy entry point — opens **Play settings → Settings** tab (`PlayPromptInjectionDialog`). Prefer **Play settings…** from the play sidebar.

---

### ProjectWorkspaceDialog

**Title:** "Projects & Sources" — primary UI for linking and syncing.

#### Connection tab

| Element | Purpose |
|---------|---------|
| Checklist | Sign-in, device cookie, account id status |
| **Test connection** | Session probe via `ChatGptProjectApiService` |
| **Capture headers hint** | Guidance for API header capture |
| `ProbeLine` | Last probe result text |

#### Projects tab

| Element | Purpose |
|---------|---------|
| **Refresh** | Reload project list from ChatGPT |
| `LinkedProjectBanner` | Warning when already linked; disables create-new tab |
| **From list** | Select project from `ProjectList`; double-click or Link |
| **Create new** | `NewProjectNameBox` — creates gizmo via API (blocked if already linked) |
| **Advanced: URL** | Paste project URL or `g-p-…` id |
| **Export and sync sources** | `SyncSourcesCheck` (default on) |
| **Push narrator instructions** | `UpdateInstructionsCheck` (default off) |
| **Create or pick play thread** | `CreateThreadCheck` (default on) |
| **Link project** | Runs binding flow |

#### Sources tab

Same sync grid as `SourceSyncDialog` (file, state, action dropdown): **Refresh plan**, **Apply safe**, **Apply all**.

**Footer:** Copy diagnostics, **Link project**, Close, **Done (linked)**.

---

### SourceSyncDialog

**Title:** "Sync project sources"

| Column | Meaning |
|--------|---------|
| File | Manifest relative path (e.g. `world.md`) |
| State | `SourceSyncState` |
| Local | Short SHA256 of local file |
| Remote | Short SHA256 of remote (when known) |
| Action | Editable dropdown: Skip, Pull remote, Push local |

| Button | Behavior |
|--------|----------|
| **Open logs folder** | Opens `%LocalAppData%\ChatGPTWrapper` |
| **Keep local** | Sets selected row action to Push local (conflict resolution) |
| **Keep remote** | Sets selected row action to Pull remote |
| **Open sources folder** | Explorer → `sources/` |
| **Preview file** | `ContextViewerDialog` with local file contents |
| **Refresh plan** | Rebuilds sync plan (API file list + hash compare) |
| **Apply safe** | Applies only auto-safe items (Pull/Push without unresolved conflicts) |
| **Apply all** | Applies all rows including user-resolved conflicts |

Status line shows auto-safe count, conflict count, file count. After apply, plan rebuilds using cached remote file list (skips redundant API probes).

---

### Play record contract

Play sends **auto-log** narrator responses to `log.json` via `TurnTimelineService.AcceptTurn` after capture — there is no per-turn narrator review gate.

| Layer | Owner | Contents |
|-------|-------|----------|
| **Canonical** | ChatGPT play thread | Narrative transcript (utility/injected pairs excluded when syncing) |
| **Derived** | `log.json` | Accepted turn pairs rebuilt on confirmed sync; sessions, export source |
| **Thread index** | `thread-metadata.json` | Per-message ordinals, utility flags, link to turns |
| **Derived** | `summary.json`, `entities.json`, `memory.json`, review queues | AI job proposals awaiting local accept |
| **Ephemeral** | DOM stamps, `sessionStorage` hide queues | Display optimization; migrate toward metadata |

**Sync policy:** On play load (linked thread + WebView available), compare filtered thread pairs to accepted log turns using the **tail** of each side (handles handoff threads with shorter history). Drift shows as a footer hint only — use **Sync from thread…** in More actions to open the sync dialog. **Sync** rebuilds log + play-linked metadata; never silent auto-sync.

**Review gates** apply to **AI job proposals** (memories, entities, cards, summary, source edits), not narrator text. Use continuous-view surrogate edit to correct logged text.

`ResponseReviewDialog` is retained for manual/debug fallback only; it is not shown on the automated send path.

---

### ContextViewerDialog

Read-only packet preview + **Copy packet** button. Meta line shows diagnostics passed from caller.

---

### EditTurnDialog

Edits turn player/narrator text (internal/legacy). Prefer continuous-view surrogate edit for play corrections.

---

### SyncFromThreadDialog

Shown when `ThreadLogSyncService` detects drift between filtered play-thread pairs and `log.json`. Summarizes counts; **Sync log from thread** rebuilds accepted turns; **Skip** preserves local log and records `ThreadLogDriftHint`.

---

### SearchDialog

Live search via `SearchService` across log, summary, memory, cards, characters. Results show snippet; selection navigates context.

---

### RandomTableDialog

Per-adventure tables in `random-tables.json` (`AdventureRandomTablesStore`), seeded from global `RandomTablesStore` defaults when empty. **Roll** picks a random entry; **Use in composer** returns text to the play composer (append or replace via checkbox). **Manage tables…** edits table names and line-delimited entries.

---

### LibrariesDialog

`KindBox`: Scenarios, Worlds, Characters, Presets, Templates. Read-only browse of `LibraryStore` items.

---

### ProjectLinkWizard

Legacy simpler link dialog. **Entry points now use `ProjectWorkspaceDialog`** via `OpenProjectLinkWizardAsync` alias.

---

## 6. Prompt packets, projects, and sync (overview)

Adventures build **prompt packets** from scenario, state, memory, entities, and transcript, then send them to ChatGPT via WebView automation.

| Concept | User doc | Developer doc |
|---------|----------|---------------|
| Thin vs fat packets | [Instruction vs Sources](instruction-sources-paradigm.md) | [§5 Prompt packets](adventure-developer-reference.md#5-prompt-packets-source-delegated-vs-fat-fallback) |
| Project linking | [Projects & Source Sync](user-projects-and-sync.md) | [§6 Project linking](adventure-developer-reference.md#6-chatgpt-project-linking) |
| Source publish/sync | [Projects & Source Sync](user-projects-and-sync.md) | [§7 Source export and sync](adventure-developer-reference.md#7-source-export-and-sync) |
| Packet construction | [Prompt Construction Guide](prompt-construction-guide.md) | [adventure-developer-reference.md](adventure-developer-reference.md) |

**Thin (source-delegated) packets** require a linked ChatGPT Project with all lore files **Published** (manual) or **InSync** (API). Otherwise the app uses **fat fallback** — lore inlined in every packet. Check readiness in Play settings → **Sources** or the play footer status line.

**Data model and persistence:** JSON documents under `%LocalAppData%\ChatGPTWrapper\adventures\{guid}\`. See [Data Model Reference](data-model-reference.md) and [adventure-developer-reference.md §2–3](adventure-developer-reference.md#2-data-model).

---

---

## 7. End-to-end workflows

### A. Create and play (no Project)

1. **Adventures** → **New adventure** → fill scenario → **Create**.
2. Play mode opens; optional **Start adventure** sends opening packet.
3. Enter mode + text → **Send** → turn auto-logged (read narrative in ChatGPT tab; use Search/Export for transcript).
4. Edit State/Memory/Cards tabs; saved on field blur.

### B. Link ChatGPT Project

1. Dashboard or Play → **Link Project** → `ProjectWorkspaceDialog`.
2. **Connection** → sign in in ChatGPT tab → **Test connection**.
3. **Projects** → pick/create/URL → enable sync/thread options → **Link project**.
4. **Sources** tab → **Refresh plan** → **Apply safe**.
5. Play status shows project, thread, sync, thin/fat mode.

### C. Mid-play sync

1. **Manual (default):** Play → **Publish sources…** → Refresh export → copy/drag files → check **Published**. See [instruction-sources-paradigm.md § Manual publish walkthrough](instruction-sources-paradigm.md#manual-publish-walkthrough).
2. **API sync (diagnostics only):** Source Manager → **API sync diagnostics…** if needed for legacy remote bindings.
3. Dialog reloads adventure; link status refreshes.

### D. Branch timeline

1. Play until desired turn → **Branch**.
2. New adventure created with copies of documents + turns through current index.
3. Original adventure unchanged.
4. Optional prompt: **Open the new branch now?** navigates play to the branch adventure.

#### Branch enhancement options (spike — CMD-165)

| Option | Description | Trade-off |
|--------|-------------|-----------|
| **Turn picker** | Dialog to branch from any accepted turn index, not only the latest | More UI; clearer fork points |
| **Project/thread fork** | Copy linked Project binding or start fresh thread on branch | Thread continuity vs clean slate |
| **State reset policy** | Choose whether `state.json`, memory, entities copy verbatim or reset | Safer forks vs full continuity |
| **Named branches** | Dashboard tree or tags for branch lineage | Navigation complexity |

**Recommendation:** Keep current “branch at latest turn” as default; backlog turn picker + named lineage unless authors request fork-at-milestone workflows frequently.

### E. Import backup

1. Dashboard → **Import…** → select zip.
2. Adventure appears in grid (new id if imported as copy).

### F. Design with AI (chat-assisted onboarding)

1. **Adventures** → **Design with AI…** (or **New** → **Design with AI instead…**).
2. Wizard steps: Setup → Concept → World → Plot → Cast → Sources → Instructions → Review.
3. Edit drafts locally on each step; **Link Project…** to enable **Discuss with AI** chat.
4. **Send** / **Extract proposals** → **Accept proposals** → **Continue**.
5. **Review** → optional bootstrap story cards → **Launch adventure** (writes `scenario.json`, exports `sources/`, sets status Active).
6. Resume later via dashboard context menu **Continue design…** (adventures marked **In design**).

**Design thread rotation:** If the design chat was deleted in ChatGPT or bindings are stale, use Design panel → **Start new design thread…** (releases session + pin, copies start packet to clipboard, navigates to Project) → **New chat** → paste (Ctrl+V) → Send → **Use this tab as design thread**. Local source editing does not require a design thread (CMD-84).

### G. Canonical begin-play workflow (design → first turn)

Use this checklist when moving from **design complete** (or **Launch adventure**) to a reliable **turn 1** on a linked ChatGPT Project. It consolidates [CMD-63](https://linear.app/cmd0112/issue/CMD-63) (Start new play thread), fresh-thread scoping, and source-pointer readiness ([CMD-66](https://linear.app/cmd0112/issue/CMD-66) / [CMD-71](https://linear.app/cmd0112/issue/CMD-71)).

**Related:** [Projects & Source Sync](user-projects-and-sync.md) · [Instruction vs Sources](instruction-sources-paradigm.md) · [Prompt Construction Guide § Start packets](prompt-construction-guide.md#start-packets-bootstrap)

#### Prerequisites

| Step | What to verify |
|------|------------------|
| Adventure status | **Active** (or play anyway from **In design** after finalize) |
| Scenario content | Setting, opening, rules, and cast exist in `scenario.json` / design exports |
| ChatGPT session | Signed in on the ChatGPT browser tab (Connection test in Project workspace) |

#### Step 1 — Link Project

1. **Adventures** or **Play** → **Link Project…** → `ProjectWorkspaceDialog`.
2. **Connection** → sign in → **Test connection**.
3. **Projects** → pick, create, or paste Project URL → **Link project**.
4. Confirm Play footer / **Play settings → Session** shows the linked Project id. The **Link now…** banner should hide as soon as the wizard closes.

**Post-link Play behavior (project-only link):**

| Expectation | Detail |
|-------------|--------|
| Banner | **Link now…** hides immediately — no Play mode exit/re-enter |
| Create thread checkbox | Leave **unchecked** unless you want the wizard to provision a play thread during link |
| Play thread | Not auto-created on link when unchecked — footer may show `Thread: missing — will create on Send` |
| Composer / navigation | Deferred until you pin a play tab (Step 3) or use **Start new play thread…** — no composer-not-found dialog on link alone |
| Stale bootstrap ids | Prior client-bootstrap thread ids are cleared on next Play session open when no pin and no turns |

If Play still shows **Link now…**, linking did not persist — reopen **Link Project…** and confirm **Done (linked)**.

#### Step 2 — Export and publish sources

Default mode is **manual publish** (`SourcePublishMode.Manual`).

1. **Play** → **Publish sources…** (or **Play settings → Sources**).
2. **Refresh export** — writes canonical files under the adventure `sources/` folder and updates `SourceManifest`.
3. For each lore file (`scenario.md`, `world.md`, `plot.md`, `cast.md`, …):
   - Copy or drag the file into the linked ChatGPT Project (Project **Sources** in ChatGPT UI).
   - Check **Published** in the wrapper when the Project copy matches local canonical content.
4. Optional: **Play settings → Settings** → push/sync **Project instructions** (narrator contract) if you edited boundaries or portrayal rules.

**Readiness gate:** Source-delegated (thin) packets require every lore file **Published** (manual) or **InSync** (API sync diagnostics). Until ready, sends use **fat fallback** — lore is inlined in the packet instead of delegated to Project files.

Check status in:

- Play sidebar footer: `Sources: published (N files) | source-delegated packets`
- **Play settings → Sources** tab and **Next send** meta line
- Packet preview: `[[cgw:sources]]` → **ALWAYS RETRIEVE** (see [[adventure-developer-reference.md §5](adventure-developer-reference.md#5-prompt-packets-source-delegated-vs-fat-fallback))

#### Step 3 — Pin play tab and open play thread

Play-thread navigation and composer readiness run when you pin a tab or already have accepted play turns — not immediately after project-only link (see Step 1 table).

1. Enter **Play** mode for the adventure (`StartPlayModeAsync` prepares session state).
2. In ChatGPT, open your linked **Project** page, then **New chat** to start a play thread (or use an existing `/c/{id}?project=…` thread).
3. **Play settings → Session** → **Link to active browser tab** (or pin from session cockpit) so **Send** automation targets that tab.
4. Send your first message (see Step 4). After the first successful send, `LinkedConversationId` binds to that thread.

**Fresh thread expectations (turn 1):**

| Packet field | Expected on new thread |
|--------------|-------------------------|
| `[[cgw:meta … turn="1"]]` | Turn index **1** — no prior accepted turns on the active session/thread |
| `[[cgw:transcript]]` | **Absent** or empty — prior design/utility turns must not leak in |
| `[[cgw:sources]]` ALWAYS RETRIEVE | All indexed sections in core lore files (`scenario.md`, `world.md`, `plot.md`, `cast.md`, `lexicon.md`) on turn 1 |

#### Step 4 — Start packet and first send

The **start packet** uses the same `PromptPacketBuilder.Build` path as normal sends (`AdventureBootstrapService.BuildStartPacket`). It includes full adventure context plus a **source-directed opening directive** — the model's first reply is the opening scene (not pre-written hook prose in the player line).

**Ways to begin:**

| Method | When to use |
|--------|-------------|
| **Create adventure** with **Offer Start adventure on first play** | Auto-prompt on first Play entry |
| **Play settings → Next send** → view/copy **start packet** | Manual control; inspect ALWAYS RETRIEVE before sending |
| **Send** with player line `Begin` (or opening hook text) | Default ongoing play after thread is bound |
| **Start new play thread…** | New Project chat after deleting/old thread; see Step 5 |

**First send checklist:**

1. **Sync canon** and publish sources to the Project (thin packets).
2. **Play settings → Next send** → **Preview narrative start packet** — confirm ALWAYS RETRIEVE lists all lore files and the player directive lists sources to retrieve.
3. **Session** → **Start narrative from sources…** → New chat → paste → Send → pin play tab.
4. The model's first reply is the opening scene; turn 2 uses normal play sends.

#### Step 5 — Start new play thread (rotation)

Use when you need a **new** play chat inside the same linked Project without re-linking — e.g. you deleted the old chat in ChatGPT, or `LinkedConversationId` points at a stale conversation.

**Play settings → Session** → **Start new play thread…**

What it does (`PlayThreadRotationService.ReleasePlayThread`):

- Clears **pinned play tab** metadata and **`LinkedConversationId`** (keeps **`LinkedProjectId`**)
- Ends the current play session and opens a fresh session scope (turn log history is **retained** but scoped turns reset for the new thread)
- Copies the **start packet** to the clipboard and navigates the Play tab to the linked Project page
- Re-pins the active browser tab for Send automation

**Your steps after the dialog:**

1. In the pinned Play tab, click **New chat** in the Project.
2. Focus the ChatGPT composer → **Ctrl+V** (start packet from clipboard).
3. **Send** in ChatGPT (or use wrapper **Send** on the next player line once the thread binds).

The conversation id binds after the first message on the new thread. Turn 1 on the new thread should again show `turn="1"` with no stale transcript.

For **in-progress** adventures (not a fresh start), **Start new play thread…** automatically copies a **handoff packet** instead — see Step 5b.

#### Step 5b — Move play to new thread (handoff)

Use when the adventure is **already in progress** and you need a **fresh ChatGPT context** without losing narrative continuity — e.g. long chat, utility noise in the thread, or model attention drift.

**When to use handoff vs fresh rotation (Step 5):**

| Situation | Action |
|-----------|--------|
| Deleted/stale chat, adventure never really started | Step 5 — **Start new play thread…** (start packet) |
| Mid-adventure, need clean context window | Step 5b — **Move to new play thread (handoff)…** |
| Major summarization + checkpoint | Play Advanced → **Move to new play thread…** wizard (same handoff flow) |

**Entry points:**

- Play Advanced → **Move to new play thread…** (wizard: review → rotate → verify)
- Play settings → Session → **Move to new play thread (handoff)…** (when adventure has accepted turns)
- Play settings → Next send → **Preview handoff packet**

**What the wrapper preserves (always on disk):**

- Full `log.json` turn history (all threads)
- `entities.json`, memories, cards, summaries, `source-manifest.json`, published Project sources
- `thread-metadata.json` message mapping

**What travels to the new chat (handoff packet):**

- Carry-forward summary (editable in wizard; defaults to rolling summary)
- Optional recent transcript excerpt (6 or 12 turns) or summary-only mode
- Current state, sources, memory, cards — via normal `PromptPacketBuilder` paths
- `[[cgw:meta continuation="true" turn="1" adventureTurn="N"]]` — new thread turn 1, not adventure restart

**Workflow (`PlayHandoffService`):**

1. **Review** — choose handoff mode; edit carry-forward summary; preview structured packet
2. **Checkpoint** — saves `play-handoff-checkpoint.json` with hash, fingerprints, prior conversation id
3. **Rotate** — `ReleasePlayThread` (same as Step 5); clipboard gets **handoff** packet (not start packet)
4. **Seed** — New chat → paste → Send; `PreparePrebuiltPacket` accepts pasted `[[cgw:` packet
5. **Verify** — hash check in wizard; optional **Rollback** if new thread has no accepted turns yet
6. **Reconcile** — after first successful send, archives prior conversation id in `PlayThreadArchive`

**Draft new project chat…** (Play settings → Session) enters **drafting mode** without releasing your stored play thread.

Design thread rotation uses the same draft guard: **Start new design thread…** enables drafting on the Project page until you pin the new tab with **Use this tab as design thread**.

#### Step 6 — Ongoing play

| Turn | Expect |
|------|--------|
| 2+ | `turn="N"` increments; `[[cgw:transcript]]` includes prior accepted turns on the **active** thread/session |
| Source edits | Re-export → re-publish → next send picks up delegated pointers or fat fallback per readiness |
| Branch | **Branch** copies timeline to a new adventure id; original unchanged |

#### Quick decision tree

```
Design done?
  └─ Link Project (Step 1)
       └─ Publish sources (Step 2) — required for source-delegated packets
            └─ Pin play tab + New chat (Step 3)
                 └─ Start packet / first Send (Step 4)
                      ├─ Continuing same thread → normal Send
                      └─ Need fresh Project chat → Start new play thread… (Step 5)
```

---

## 8. Diagnostics and logging

| Artifact | Location | Purpose |
|----------|----------|---------|
| `link-project.log` | App root | Project link events |
| `sync-trace.jsonl` | App root | Structured sync trace events |
| `sync-runs/{runId}/` | App root | Per-run summaries |
| `source-sync-perf-report.json` / `.txt` | App root | Source sync performance suite output ([test README](../tests/ChatGPTWrapper.ApiDiagnostics/README.md#source-sync-performance)) |
| API discovery logs | `ChatGptApiDiscovery` paths | Endpoint probe history |
| **Open logs folder** | SourceSyncDialog | Opens App root in Explorer |

Sync dialog **CapabilitiesHint** points to API capabilities diagnostics path.

**Copy diagnostics** in Project workspace copies host diagnostics text to clipboard.

---

## 9. Known limitations and edge cases

| Topic | Behavior |
|-------|----------|
| **Continuation queue persistence** | Saved on `QueueBox` blur and when dequeuing on Send, but **`AdventureStore.Load` does not restore `ContinuationQueue` into bundle** — queue is empty after reload until code path repopulates from UI. |
| **Archived adventures** | Flag toggled but grid shows all adventures regardless of archive state. |
| **Recap button** | Implemented in code-behind but hidden in XAML. |
| **ProjectLinkWizard** | Superseded by `ProjectWorkspaceDialog`; wizard file retained. |
| **Entity editing** | Play → **Reference** tab and Design → **Cast** → **Canon entities** use `EntityReferencePanel` (entity list in the side panel; **Edit** opens a modal `EntityEditDialog` so the panel stays uncluttered). Wide layouts can opt into `EntityWorkspaceHost` side panel via explicit `EditMode.SidePanel`. Schema-driven CRUD on `entities.json`. On save, **EntityEditSourceSyncService** auto-exports to `sources/*.md` and applies cross-canon renames; residual drift opens **Reconcile canon** or click session status **Sources out of sync — click to repair**. Row badges show in sync / sources stale / needs publish. Post-sync green banner: **View diff · Open Source Manager**. Review-queue accept/edit remains Play-only. See [entity-canon-change-paradigm.md](entity-canon-change-paradigm.md). |
| **Automation off** | Every turn copies packet to clipboard; user pastes response manually. |
| **Thin / source-delegated packets** | Require linked Project **and** published/in-sync sources; otherwise fat fallback. Blocked linked adventures surface `Sources not ready: …` in `[[cgw:sources]]` ALWAYS RETRIEVE (not silent `(none)`). |
| **Duplicate remote files** | Same basename with multiple ChatGPT file ids → LocalOnly + RemoteOnly pairs; use **Reconcile duplicates…** to remove orphans (confirm before delete). Planner also auto-collapses pairs when possible. |
| **Ghost / 404 project files** | Listed on project but not downloadable; browser shows `{"detail":"Not found."}`. Delete in ChatGPT UI, Refresh plan, Apply all. Banner may show listed-not-downloadable or stale-binding counts. |
| **Unmanaged remote files** | Files on Project not matched to manifest paths are listed as unmatched; sync dialog leaves them unchanged unless reconciled. |
| **Apply safe vs conflicts** | Unresolved conflicts skipped on Apply safe; user must set Action or use Keep local/remote first. |
| **Regenerate** | Uses last stored packet from `PromptHistory`; archives previous narrator response as alternate attempt. |

---

## Quick reference: turn input

```
Adventure panel → session status + world-state editing + injection tools
Wrapper composer → Send (below ChatGPT tabs in Play mode)
Pinned ChatGPT tab → native composer hidden; prompts arrive via bridge sendPrompt
```

---

## Quick reference: SourceSyncState → default planned action

| State | Typical planned action |
|-------|------------------------|
| InSync | Skip |
| LocalOnly | PushReplace |
| LocalNewer | PushReplace |
| RemoteNewer | Pull |
| Conflict | NeedsResolution |
| RemoteOnly | Skip (user may choose Pull) |
| MissingRemote | Skip |

---

*Generated from the ChatGPT Wrapper source tree. For implementation changes, prefer reading the cited `.cs` / `.xaml` files directly.*