# UI Components

WPF views, dialogs, and shell structure. Play view deep-dive: [adventure-panel.md §3–5](adventure-panel.md).

---

## MainWindow

**Files:** `MainWindow.xaml` + partial classes

| Partial | Responsibility |
|---------|----------------|
| `MainWindow.xaml.cs` | Toolbar, continuous view checkbox, Format dialog, ui-chrome load/save |
| `MainWindow.ChatTabs.cs` | Tab control, WebView2 init, environment at `WebView2UserData` |
| `MainWindow.PageHost.cs` | Register `IPageFeature` per tab |
| `MainWindow.Adventures.cs` | Mode switch (`Browse`/`Adventures`/`Play`), dashboard host |
| `MainWindow.PlayTab.cs` | Ensure play session, pin tab |
| `MainWindow.PlayInjection.cs` | Send packet, continuation queue, review flow |
| `MainWindow.GenerationJobs.cs` | Run utility jobs from shell |
| `MainWindow.ProjectHost.cs` | Open project workspace, link wizard |
| `MainWindow.UtilityWebView.cs` | Utility-thread WebView for separate delivery |

### Toolbar controls (Browse)

- Mode buttons: Browse / Adventures / Play
- **Continuous view** checkbox + **Format…** button
- Tab strip: new tab, close tab

---

## Root-level dialogs (not in Views/)

| Component | Opens from | Purpose |
|-----------|------------|---------|
| `ContinuousViewFormatDialog` | Toolbar Format | Continuous view, prose, phrase highlights, paragraph formatting |
| `PhraseHighlightsDialog` | Format dialog / toolbar | Standalone phrase highlight editor |
| `PhraseHighlightsEditorControl` | Embedded in dialogs | Reusable rule list editor |
| `TextPromptDialog` | Various | Generic text input prompt |

---

## Adventure views (`Views/`)

### AdventureDashboardView

| Action | Effect |
|--------|--------|
| New adventure | Opens `ScenarioCreationDialog` |
| Open / Play | Loads adventure in Play mode |
| Link Project | Opens project workspace |
| Archive filter | Show/hide archived adventures |
| Backup / Import | `BackupService` |
| Libraries | `LibrariesDialog` |

### AdventurePlayView

Play cockpit — see [adventure-panel.md §4](adventure-panel.md#4-play-view).

| Area | Controls |
|------|----------|
| Header | Title, link status, settings |
| Session cockpit | Entity counts, review badges, session status |
| TabControl | Story log, entities, memory, cards, state, notes |
| Footer | More menu (export, search, recap, sync, jobs) |
| Composer | `PlayPromptComposer` or in-page `cgw-play-compose` |

**Events:** Start adventure, send turn, open review, run generation jobs.

### PlayPromptComposer

In-shell Do/Say/Story input when wrapper composer is disabled.

### PlayPromptInjectionDialog

**Play settings** — multi-tab dialog:

| Tab | Content |
|-----|---------|
| Scenario | Tone, perspective, boundaries |
| Memory / State | Pinned memory, state preview |
| Session | Utility sessions, tab pins, delivery mode |
| Sources | Publish mode, manifest status, sync shortcuts |
| AI Actions | Per-job guide overrides, run jobs |
| Automation | Bridge health, force fat packets, context tags |

### ScenarioCreationDialog

New adventure: title, genre, opening situation, optional library import.

### AdventureSettingsDialog

Adventure metadata: title, tags, archive, delete.

---

## Play and review dialogs

| Dialog | Opens from | Key actions |
|--------|------------|-------------|
| `ResponseReviewDialog` | Manual/debug fallback only (not automated send) | Accept, Retry, Regenerate, Edit |
| `ContextViewerDialog` | Context button | View full prompt packet sections |
| `EditTurnDialog` | Story log | Edit player/narrator text |
| `RecapDialog` | More menu | Local recap digest |

---

## Project and source dialogs

| Dialog | Opens from | Key actions |
|--------|------------|-------------|
| `ProjectLinkWizard` | Link Project | Step-through link flow |
| `ProjectWorkspaceDialog` | Dashboard / Play | Connection, Projects, Sources tabs |
| `SourceManagerDialog` | Play settings / Sources | Edit local files, confirm publish |
| `SourceSyncDialog` | Sources tab | Sync plan, apply safe/all, diagnostics |
| `SourceCompareDialog` | Source manager | Local vs remote diff |

**Helpers:** `SourceSyncUiHelper`, `SourceSyncGridHelper`, `SourceSyncRowViewModel`, `SourcePublishRowViewModel`, `SourceSyncActionLabels`

---

## Entity and content dialogs

| Dialog | Purpose |
|--------|---------|
| `EntityEditDialog` | CRUD characters, locations, quests, etc. |
| `LibrariesDialog` | Browse/import scenario, world, character libraries |
| `SearchDialog` | Full-text search hits across adventure |
| `RandomTableDialog` | Roll on random tables |

---

## Themes (`Themes/`)

| File | Role |
|------|------|
| `WrapperTokens.xaml` | Color, spacing, font tokens |
| `WrapperChrome.xaml` | Window chrome, toolbar |
| `WrapperControls.xaml` | Shared control styles |

`UiBrushes.cs` — programmatic brush helpers.

---

## Play composer modes

| Mode | UI | Control |
|------|-----|---------|
| Native (default Play) | ChatGPT stock composer | `cgw-play-compose.js` intercepts Send → `cgwComposeSend` → host `PrepareSend` → bridge |
| Legacy wrapper | `cgw-play-compose.js` overlay | Same postMessage path; custom attach UI + CDP pre-upload |
| In-shell adapter | `PlayPromptComposer` (hidden) | Merged preview + cached text sync only |

Toggle legacy wrapper via Play settings → *Use custom wrapper composer*.

---

## ViewModels (source sync)

| Class | Role |
|-------|------|
| `SourceSyncRowViewModel` | Row in sync grid |
| `SourceManagerRowViewModel` | Source manager list row |
| `SourcePublishRowViewModel` | Publish status per file |

---

## Related documentation

- [Adventure Panel Reference](adventure-panel.md)
- [User Guide](user-guide.md)
- [Injected Assets](injected-assets.md) — in-page UI
- [Architecture — MainWindow map](architecture.md#mainwindow-partial-class-map)
