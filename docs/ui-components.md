# UI Components

WPF views, dialogs, and shell structure. Play view deep-dive: [adventure-panel.md §3–5](adventure-panel.md).

---

## MainWindow

**Files:** `MainWindow.xaml` + partial classes

| Partial | Responsibility |
|---------|----------------|
| `MainWindow.xaml.cs` | Toolbar, View menu transcript modes, Format dialog, ui-chrome load/save |
| `MainWindow.ChatTabs.cs` | Tab control, WebView2 init, environment at `WebView2UserData` |
| `MainWindow.PageHost.cs` | Register `IPageFeature` per tab |
| `MainWindow.Adventures.cs` | Mode switch (`Browse`/`Adventures`/`Design`/`Play`), dashboard host |
| `MainWindow.AdventureDesign.cs` | Design wizard panel |
| `MainWindow.DesignTab.cs` | Design tab pin |
| `MainWindow.AdventureNavigationGuard.cs` | Navigation guards |
| `MainWindow.PlayTab.cs` | Ensure play session, pin tab |
| `MainWindow.PlayInjection.cs` | Send packet, continuation queue, review flow |
| `MainWindow.GenerationJobs.cs` | Run utility jobs from shell |
| `MainWindow.ProjectHost.cs` | Open project workspace, link wizard |
| `MainWindow.UtilityWebView.cs` | Utility-thread WebView for separate delivery |
| `MainWindow.Theme.cs` | Theme customization dialog |
| `MainWindow.ShellStatus.cs` | Status line, breadcrumbs |
| `MainWindow.ThreadLogSync.cs` | Thread log sync |
| `MainWindow.TurnInvalidation.cs` | Turn invalidation |

### Toolbar controls (Browse)

- Mode buttons: **Browse** / **Adventures** only (Play and Design entered from dashboard)
- **View** menu: Native / Continuous / Weave transcript modes; **Format…** (transcript typography — no toolbar Format button)
- **⋯** overflow menu: **Preferences…** → `PreferencesHubDialog`
- Tab strip: new tab, close tab

---

## Root-level dialogs (not in Views/)

| Component | Opens from | Purpose |
|-----------|------------|---------|
| `ContinuousViewFormatDialog` | View → Format… / Preferences hub | Presets, reading layout (per-role typography), colors, highlights, thread display; live sample preview (CMD-80, CMD-146) |
| `PreferencesHubDialog` | ⋯ → Preferences… | Routes to theme, format, wrapper settings, play settings shortcut |
| `PhraseHighlightsEditorControl` | Embedded in Format dialog | Reusable rule list editor (standalone `PhraseHighlightsDialog` removed) |
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
| Wrapper / Storage settings | More menu / footer | Opens **Preferences hub** (not direct `WrapperSettingsDialog`) |

### AdventurePlayView

Play cockpit — see [adventure-panel.md §4](adventure-panel.md#4-play-view).

| Area | Controls |
|------|----------|
| Header | `ShellCommandBarStyle`: Play settings, Sources (when linked), **More…** overflow (rename, link project, continue design). Back hidden when shell breadcrumb is active. |
| Session cockpit | Pin/session status, Reviews expander with `ShellBadgeStyle` count, pending-review pinned row, Narrator controls (scene profiles, scoped inherit/preset combos, advanced directive), AI tools flyout at narrow widths |
| Reference tab | Entity list in `ShellCardStyle` host; rows show portrait thumbnails, type badges, tag labels, and capability-driven detail; double-click opens `EntityEditDialog` |
| Warnings / State tabs | Warnings (`ShellCardStyle` + empty state); State (preview cards + all-fields expander). Tabs reparent between left and right hosts per placement. |
| Notes | `AdventureNotesPanel` in right column — autosave, find (Ctrl+F), insert snippets, section jump (`##` headings), copy/export; `NavigateToPlayTab("Notes")` |
| Footer | `ShellCommandBarStyle`: Search, Export (text labels — **Export…** / **Export**, not icons), **More…** for remaining actions |

**Events:** Start adventure, send turn, open review, run generation jobs, `ExpandPlaySidePanelRequested`, `ExpandPlayNotesPanelRequested`, `NavigateToPlayTab`.

### PlayRightCompanionHost

Right-column host: `PlayRightTabControl` (relocated companion tabs) stacked above `NotesSlot`.

### PlayPromptComposer

In-shell Do/Say/Story input when wrapper composer is disabled.

### PlayPromptInjectionDialog

**Play settings** — multi-tab dialog:

| Tab (XAML name) | Content |
|-----------------|---------|
| Next send | Continuation queue, fallback player line, **turn overrides** (response length, detail), live merged preview |
| World | Summary, location, objectives, author's note |
| AI Actions | Per-job guide overrides, run jobs |
| Session | Utility sessions, tab pins, inline utility peek |
| Play surface | Attachment context, quick-action visibility, **layout presets** (Writer / GM / Minimal), per-tab placement (Left / Right / Hidden) including Notes |
| Settings | Perspective, tone, boundaries, automation toggles, max packet size, force fat packets, context tags |
| Memory & cards | Pinned memory, cards review |
| Sources | Published checkboxes, manifest status, sync shortcuts |
| History | Send history viewer |

### ScenarioCreationDialog

New adventure: title, genre, opening situation, optional library import.

---

## Play and review dialogs

| Dialog | Opens from | Key actions |
|--------|------------|-------------|
| `ContextViewerDialog` | Context button | View full prompt packet sections |
| `RecapDialog` | More menu | Local recap digest |

---

## Project and source dialogs

| Dialog | Opens from | Key actions |
|--------|------------|-------------|
| `ProjectWorkspaceDialog` | Dashboard / Play Link Project | Connection, Projects, Sources tabs (`ProjectLinkWizard` removed) |
| `SourceManagerDialog` | Play settings / Sources | Edit local files, confirm publish |
| `SourceSyncDialog` | Sources tab | Sync plan, apply safe/all, diagnostics |
| `SourceCompareDialog` | Source manager | Local vs remote diff |

**Helpers:** `SourceSyncUiHelper`, `SourceSyncGridHelper`, `SourceSyncRowViewModel`, `SourcePublishRowViewModel`, `SourceSyncActionLabels`

---

## Entity and content dialogs

| Dialog | Purpose |
|--------|---------|
| `EntityEditDialog` | Modal entity editor — wraps shared `EntityEditFormHost`. Open from Play → Reference (double-click), Design → Cast → **Canon entities**, or inline/side-panel when `EntityReferencePanelOptions.EditMode` is not `Modal`. |
| `EntityEditFormHost` | Shared schema-driven form body (portrait, shell fields, extras) used by `EntityEditDialog` and embedded panel edit modes. |
| `LibrariesDialog` | Browse/import scenario, world, character libraries |
| `SearchDialog` | Full-text search hits across adventure |
| `RandomTableDialog` | Roll on random tables |

---

## Layout contract (CMD-61 / CMD-91)

Shared responsive rules for adventure surfaces. Tokens live in `Themes/WrapperTokens.xaml` — prefer `BgSurfaceBrush`, `TextMutedBrush`, `RadiusCard`, `FontSizeHint` over ad-hoc values.

### Play side panel (`AdventurePlayView`)

`PlayLayoutCoordinator` builds a **side-aware snapshot** (`PlayLayoutSnapshot`) with shell (left column) and companion (right column) contexts. `AdventurePlayView.ApplyLayout(snapshot)` applies shell chrome from the shell context and tab bodies from whichever side hosts each tab. Breakpoints are centralized in `PlayLayoutCapabilities` (derived from content width via `PlayLayoutContext`).

| Tier | Content width | Typical behavior |
|------|---------------|------------------|
| Compact | &lt; 220px | Back `←`; Play settings `⚙`; entity list compact template |
| Cozy | 220–239px | Full back label; State all-fields hidden |
| Standard | 240–279px | Narrator/AI flyouts; icon footer; warning source hidden |
| Comfortable | 280–319px | Inline narrator/AI; full footer labels |
| Wide | 320–399px | Wide entity rows; comfortable chrome floor |
| ExtraWide | ≥ 400px | Two-column state preview |

`UpdateResponsiveLayout(panelWidth)` remains as a legacy shim; `MainWindow.Adventures.cs` calls `ApplyLayout` with both column widths on load, splitter drag, and window resize.

**Splitter chrome (CMD-154):** `SidePanelGridSplitterStyle` renders a full-height track plus a centered grip capsule (three dots) at rest; hover/drag accent the track and border. Play collapse/expand rails use matching `SidePanelCollapseRailButtonStyle` / `SidePanelExpandRailButtonStyle` (chevron over grip dots). Shell splitter column is 12px; dialog splitters use 8px.

**Splitter double-click:** Play side and notes splitters snap to widths from `PlayPanelOptimalWidthCalculator`, which derives targets from visible tabs and `PlayPanelWidthRequirements` (responsive breakpoints). Validate with `PlayPanelOptimalWidthCalculator.ValidateLeft` / `ValidateRight`.

**Link UX (CMD-79):** When the **Link now** banner is visible, the header **Link Project…** button is hidden — one primary CTA per surface.

**In-session narrator (CMD-127):** `NarratorControlsPanel` exposes scene profiles, per-parameter inherit/preset combos with scope selector (**This send** / **Session** / **Adventure default**), active-override chips, and **Advanced…** dialog. Overrides resolve through `NarratorOverrideResolver` and inject via `=== TURN OVERRIDES ===` / `=== TURN DIRECTIVE ===`.

### Design companion panel (`AdventureDesignView`)

| Region | Rule |
|--------|------|
| Header | Title truncates; thread actions wrap |
| Step tabs | Brainstorm order (Concept → … → Review) — **not** canonical source pipeline order |
| Pipeline checklist | Always visible (row 3); canonical draft order; click row to jump |
| Draft panel | Scrollable; source prompt panel shows inline out-of-order warnings |
| **Canon entities** (Cast step) | Inline card in draft scroll — after cast fields, before Additional notes; full schema categories via `EntityReferencePanel` |

Shared entity UI: `EntityReferencePanel` + `EntityReferenceEditService` (also hosts Play → Reference tab). Panel supports `EditMode`: `Modal` (default), `Inline`, `SidePanel`, or `Auto` (compact → modal, medium → inline, wide → side panel).

### Adventures dashboard (`AdventureDashboardView`)

| Region | Rule |
|--------|------|
| Toolbar | Primary actions left; overflow in **More…** menu |
| Grid | Title column `*`; status columns fixed width |
| Empty state | Handled in code-behind refresh |

### Dialog minimum widths

| Dialog | Min width | Notes |
|--------|-----------|-------|
| `PlayPromptInjectionDialog` | 720px | Wider fields (CMD-20) |
| `InstructionDesignerDialog` | 760px | Split preview |
| `ContinuousViewFormatDialog` | 800px | Presets-first IA; role-grouped typography; color catalog; sample preview (CMD-80, CMD-146) |
| `AdventureRenameDialog` | 360px | `SizeToContent=Height` (CMD-15) |

See also [prompt-construction-guide.md — Preview/send parity](prompt-construction-guide.md#previewsend-parity-cmd-56--cmd-60) (CMD-56) and [adventure-panel.md](adventure-panel.md).

---

## Themes (`Themes/`)

Runtime theme customization (CMD-111) updates WPF resource keys and WebView `--cgw-*` CSS variables from **Preferences → Appearance & theme…**. See [appearance-theme-settings.md](appearance-theme-settings.md) for scope layers and wave 2 plan. Static XAML tokens below are the compile-time defaults; at startup and on apply, `ThemeApplicationService` replaces brushes and typography in `Application.Current.Resources`.

| File | Role |
|------|------|
| `WrapperTokens.xaml` | Color, spacing, font tokens (defaults; overridden at runtime) |
| `WrapperChrome.xaml` | Window chrome, toolbar |
| `WrapperControls.xaml` | Shared control styles |

Theme code lives in `ChatGPTWrapper/Theme/` (`ThemeSettings`, `ThemeTokenCatalog`, `ThemePresetLibrary`, `ThemeApplicationService`). Format transcript colors use `ChatGPTWrapper/Format/FormatTokenCatalog` and `FormatCssBuilder`, injected via `continuous-format-settings.js`. WebView injection prepends `BuildCssVariableBlock` in `ChatGptStyleInjection`.

### Semantic color tokens (`WrapperTokens.xaml`)

| Token | Use |
|-------|-----|
| `BgBaseBrush` | Window/dialog base |
| `BgSurfaceBrush` | Panels, cards |
| `BgElevatedBrush` | Toolbars, raised surfaces |
| `BgChromeBrush` / `BgWorkspaceBrush` / `BgInsetBrush` | Shell chrome, body workspace, recessed areas |
| `BorderSubtleBrush` / `BorderStrongBrush` | Dividers, focus rings |
| `TextPrimaryBrush` / `TextMutedBrush` | Body and hint text |
| `AccentPrimaryBrush` / `AccentPrimaryHoverBrush` / `AccentPrimaryPressedBrush` | Primary actions |
| `AccentSubtleBrush` | Selected mode pills, tinted backgrounds |
| `RowHoverBrush` / `RowSelectedBrush` | List rows |
| `ButtonGhostBrush` / `ButtonGhostHoverBrush` / `ButtonGhostPressedBrush` | Secondary buttons |
| `PopupBrush` | Menus, flyouts |
| `SuccessBrush` / `WarningBrush` / `ErrorBrush` (+ subtle variants) | Status semantics |

### Shell layout primitives (`WrapperControls.xaml`)

| Style | Target | Use |
|-------|--------|-----|
| `ShellCardStyle` | `Border` | Grouped chrome regions (mode switcher, panels) |
| `ShellSectionHeaderStyle` | `TextBlock` | Section titles |
| `ShellSectionHintStyle` | `TextBlock` | Muted helper text |
| `ShellCommandBarStyle` | `StackPanel` | Horizontal command row |
| `ShellCommandBarPrimarySlot` | `Button` | Primary action in a command bar |
| `ShellCommandBarSecondarySlot` | `Button` | Ghost/secondary action in a command bar |
| `ShellMenuSectionHeaderStyle` | `MenuItem` | Non-interactive section label in context menus |
| Implicit `ContextMenu` / `MenuItem` / `Separator` | — | Dark popup chrome; no empty icon gutter; full-width separators; checkable rows show ✓ only when checked |

Context menus: `AdventurePlayView` **More actions…**, `AdventureDashboardView` grid right-click. Top-level shell menus (`MainWindow` View / overflow, dashboard **New ▾** / **More…**) share the same `MenuItem` template.

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
