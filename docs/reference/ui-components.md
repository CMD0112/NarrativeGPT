# UI Components

WPF views, dialogs, and shell structure. Play view deep-dive: [adventure-panel.md §3–5](../user/adventure-panel.md).

**Cross-surface alignment:** [wrapper-ui-paradigm.md](wrapper-ui-paradigm.md) — layout paradigms (shell, companion, hub, workbench), principles, component kit, surface matrix. **Full inventory:** [ui-surface-catalog.md](ui-surface-catalog.md). **Paradigm QA matrix:** [ui-paradigm-qa-matrix.md](../developer/ui-paradigm-qa-matrix.md). Workbench layout detail: [§ Workbench paradigm](#workbench-paradigm-t3t4) below and [play-settings-ui-roadmap.md §3–5](../plans/play-settings-ui-roadmap.md).

WinUI shell (CMD-478 migration): `ChatGPTWrapper.WinUI/` — `NavigationView` (Browse / Adventures), `SessionTopBar`, `ViewCommandBar`, `ChatTabHost`, native `AdventureDashboardPage`. Shared controls mirror WPF: `Controls/SegmentedControl`, `StatusChip`, `ActionListRow`, `ScopeBadgeView` (theme tokens via `Themes/WrapperTokens.xaml`).

---

## Workbench paradigm (T3/T4)

Normative layout for multi-section editors (Play settings, Format, Review, Preferences children, Entity edit). Reference implementation: `ChatGPTWrapper.WinUI/Views/Dialogs/PlaySettings/PlaySettingsWorkbenchPage.xaml`. Canon: [wrapper-ui-paradigm.md § Workbench](wrapper-ui-paradigm.md#workbench-layout-paradigm).

### Layout regions

```
┌ Header ─────────────────────────────────────────────┐
│ Title · context chips · dirty badge               │
├ Nav rail ─┬─ Content scroll host ─────────────────┤
│ [filter]  │  Section title · ScopeBadgeView       │
│ Group     │  ┌ ShellSectionCard ────────────────┐ │
│  • item   │  │ Title · scope · hint              │ │
│  • item   │  │ body                              │ │
│ Advanced  │  └───────────────────────────────────┘ │
├ Footer ───┴───────────────────────────────────────┤
│ Save status · edit count · Save / Cancel          │
└───────────────────────────────────────────────────┘
```

| Region | Component (today → target) | Contract |
|--------|---------------------------|----------|
| Header | `PlaySettingsWorkbenchPage` header strip → `ShellWorkbenchPage` | Adventure/context line; unsaved badge when dirty |
| Nav | `ListView` + `PlaySettingsNavItem` → `ShellNavItem` | Group headers; scope badge per item; filter at 5+ sections |
| Body | `ScrollViewer` + tab `UserControl` hosts | One section visible; `ShellSectionCard` per settings group |
| Footer | Inline status bar → `WorkbenchStatusBar` | Save status icon, edit summary, explicit Save/Cancel ([P6](wrapper-ui-paradigm.md#p6--explicit-save-model-workbenches--editors)) |

### Footer save model

| State | UX |
|-------|-----|
| Clean | Muted save line; Save disabled or hidden per surface policy |
| Dirty | Header **Unsaved** badge + footer edit count |
| Save | Persists staged model; clears dirty |
| Cancel / close | Confirm when dirty ([CMD-570](https://linear.app/cmd0112/issue/CMD-570)) |
| Drill-down | Footer hyperlinks jump to owning nav section |

Keyboard: Ctrl+S → Save, Esc → Cancel when shell kit wired ([CMD-620](https://linear.app/cmd0112/issue/CMD-620)).

### Responsive breakpoints

From `PlaySettingsWorkbenchLayout` ([CMD-623](https://linear.app/cmd0112/issue/CMD-623)):

| Width | Nav | Content |
|-------|-----|---------|
| ≥ 1200px | Fixed rail (~232px) | Full section cards; card-grid tabs use two columns |
| 960–1199px | Fixed rail (min 200px) | Single-column cards |
| &lt; 960px | Collapsible / overlay rail | Stacked cards; reduced horizontal padding |

Test at **1280×720** and **1920×1080** before marking a workbench aligned ([ui-paradigm-qa-matrix.md](../developer/ui-paradigm-qa-matrix.md)).

### Scope badges

Use `ScopeBadgeView` with semantic tokens (`ScopeBadge*BackgroundBrush` / `ScopeBadge*ForegroundBrush` in WinUI `WrapperTokens.xaml`). Do not use plain `ShellBadgeStyle` for scope labels on workbench surfaces.

### Deep links

Entry points pass a **tab/section enum** — e.g. `ShowPlaySettingsAsync(..., PlaySettingsTab.World)`. Hubs open workbench sections; they do not duplicate full forms ([P1](wrapper-ui-paradigm.md#p1--one-intent-one-primary-home)).

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
| `MainWindow.ShellShortcuts.cs` | Centralized keyboard shortcuts, focus-chat toggle |
| `MainWindow.ShellSegments.cs` | `SegmentedControl` wiring for app mode and Play/Design session toggle (CMD-417) |
| `MainWindow.SessionChrome.cs` | Unified session top bar — status chips, session overflow, `UpdateSessionStatusChips` (CMD-421) |
| `MainWindow.ThreadLogSync.cs` | Thread log sync |
| `MainWindow.TurnInvalidation.cs` | Turn invalidation |

### Toolbar controls (Browse)

- Mode segments: **Browse** / **Adventures** via `SegmentedControl` (`AppModeSegment`); Play/Design session toggle in shell context (`ShellSessionModeSegment`)
- **View** menu: Native / Continuous / Weave transcript modes via `TranscriptModeSegment`; **Format…** (transcript typography — no toolbar Format button)
- **⋯** overflow menu: **Preferences…** → `PreferencesHubDialog`
- Tab strip: new tab, close tab

---

## Root-level dialogs (not in Views/)

| Component | Opens from | Purpose |
|-----------|------------|---------|
| `ContinuousViewFormatDialog` | View → Format… / Preferences hub | **Essentials** tab (common reading controls), per-category format refinement panel (suggested + common tweaks), settings search, profile presets, reading layout, colors, highlights, thread display; rich live sample preview (CMD-80, CMD-146, CMD-306) |
| `PreferencesHubDialog` | ⋯ → Preferences… | Taxonomy-grouped hub v2: global cards (appearance, reading/format, storage), reading mode summary, active-adventure shortcuts |
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

Play cockpit — see [adventure-panel.md §4](../user/adventure-panel.md#4-play-view).

| Area | Controls |
|------|----------|
| Session chrome | Shell owns back, title, Play \| Design segment, status chips, session overflow — no in-view header |
| Session cockpit | `SegmentedControl` sections (Session, Narrator, Tools); pill-style companion tabs (`ShellCompanionTabControlStyle`) |
| Reference tab | Entity list in `ShellCardStyle` host; rows show portrait thumbnails, type badges, tag labels, and capability-driven detail; double-click opens `EntityEditDialog` |
| Warnings / State tabs | Warnings (`ShellCardStyle` + empty state); State (preview cards + all-fields expander). Tabs reparent between left and right hosts per placement. |
| Notes | `AdventureNotesPanel` in right column — autosave, find (Ctrl+F), insert snippets, section jump (`##` headings), copy/export; `NavigateToPlayTab("Notes")` |
| Footer | `ShellCommandBarStyle`: Search, Export, **More** menu |

### AdventureDesignView

| Area | Controls |
|------|----------|
| Header | Collapsed when `UseUnifiedSessionChrome` (shell owns back, title, threads via session **⋯**) |
| Step tabs | `ShellCompanionTabControlStyle` — Concept, World, Plot, Cast, Lexicon, Sources, Instructions, Review |
| Brief card | Send step brief, Extract proposals |

**Events:** Start adventure, send turn, open review, run generation jobs, `ExpandPlaySidePanelRequested`, `ExpandPlayNotesPanelRequested`, `NavigateToPlayTab`.

### PlayRightCompanionHost

Right-column host: `PlayRightTabControl` (relocated companion tabs) stacked above `NotesSlot`.

### PlayPromptComposer

In-shell Do/Say/Story input when wrapper composer is disabled.

### PlayPromptInjectionDialog

**Play settings** — multi-tab dialog:

| Tab (XAML name) | Content |
|-----------------|---------|
| Next send | Send scope — continuation queue, turn overrides, packet preview |
| World | Adventure — summary, location, objectives, author's note |
| Play surface | Adventure — layout presets, side panel tab placement, cockpit on-enter prefs, narrator density, AI tools layout |
| Behavior | Adventure — narrator contract, automation toggles, advanced automation expander |
| Session | Session — utility sessions, tab pins, inline utility peek |
| Sources | Adventure — publish checkboxes; primary path is SourceManagerDialog |
| Memory & cards | Adventure — pinned memory, cards review |
| AI Actions | Session — per-job overrides and run buttons |
| History | Flight recorder — per-turn send audit (timeline, manifest, pointers, diff) — [adventure-panel.md](../user/adventure-panel.md#flight-recorder-play-settings--history) |

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
| `EntityEditDialog` | Resizable modal entity editor — header card (portrait, badges, sync), tabs **Profile · Sources · Mentions · History**, grouped profile sections, extended-fields editor, sticky footer. All entity CRUD from Play → Reference and Design → Cast. |
| `EntityEditFormHost` | Shared schema-driven form body (portrait, shell fields, extras, **phrase highlight** card for Characters/Player/Party/Locations) used by `EntityEditDialog` and embedded panel edit modes. |
| `LibrariesDialog` | Browse/import scenario, world, character libraries |
| `SearchDialog` | Full-text search hits across adventure |
| `KeyboardShortcutsDialog` | View → Keyboard shortcuts… | Grouped list of default shell chords (`ShellShortcutCatalog`) |
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

Shared entity UI: `EntityReferencePanel` + `EntityReferenceEditService` (also hosts Play → Reference tab). Companion list only — **dialog-only** editing via `EntityEditDialog` (`EditMode` defaults to `Modal`; `Auto` also resolves to modal).

### Adventures dashboard (`AdventureDashboardView`)

| Region | Rule |
|--------|------|
| Toolbar | Primary actions left; overflow in **More…** menu |
| Grid | Title column `*`; status columns fixed width |
| Empty state | Handled in code-behind refresh |

### Dialog minimum widths

Tier defaults use theme keys `DialogMinWidthSmall/Medium/Large` and `DialogMinHeightSmall/Medium/Large` in `WrapperControls.xaml`. All modal shell dialogs inherit `ShellDialogWindow` (see [Dialog viewport sizing](#dialog-viewport-sizing-cmd-279)) unless listed as SizeToContent exceptions.

| Tier | Min (W×H) | Default target | Examples |
|------|-----------|----------------|----------|
| **Small** | 440×400 | ~520×420 | Recap, Search, RandomTable, Libraries, ConversationFiles, EntityRetire/Merge, CanonInbox, NarratorAdvanced, CastPhraseImport, FormatSystemFontPicker |
| **Medium** | 480×400 | ~640–720×520 | PreferencesHub, PlayHandoff, AdventureThreadManager, CanonReconcile, ContextViewer, HighlightColorAssignment, ScenarioCreation, EntityRenameWizard, AdventureDesignWizard, ProjectWorkspace |
| **Large** | 640×520 | ~760–1080×680–1040 | PlayPromptInjection (920×760), ThemeCustomization (860×720), InstructionDesigner, JsonImportReview, SourceManager/Compare/Sync, EntityEdit, EntityChangePlanDiffPreview, ContinuousViewFormat |

| Dialog | Default (W×H) | Notes |
|--------|---------------|-------|
| `PlayPromptInjectionDialog` | 920×760 | Split tabs + live preview column; body `MinHeight="360"` |
| `ThemeCustomizationDialog` | 860×720 | Tab body `MinHeight="320"` |
| `InstructionDesignerDialog` | 960×720 | Editor `*` row `MinHeight="320"` |
| `JsonImportReviewDialog` | 920×640 | Review pane `MinHeight="280"` |
| `ContinuousViewFormatDialog` | 1080×1040 | Essentials-first IA; refinement expander (CMD-80, CMD-146, CMD-306) |
| `PreferencesHubDialog` | 520×640 | Resizable; no `SizeToContent` cap |
| `AdventureRenameDialog` | 360×auto | `SizeToContent=Height` exception |

See also [prompt-construction-guide.md — Preview/send parity](../user/prompt-construction-guide.md#previewsend-parity-cmd-56--cmd-60) (CMD-56) and [adventure-panel.md](../user/adventure-panel.md).

---

## WPF scroll & overflow layout contract (CMD-278 / CMD-285)

Authoritative rules for shell **dialogs** and settings surfaces. Theme keys live in `Themes/WrapperControls.xaml`; play companion responsive layout is a separate contract above ([Layout contract](#layout-contract-cmd-61--cmd-91), [adventure-panel.md](../user/adventure-panel.md)).

**Related:** [settings-ux-taxonomy.md](../settings/settings-ux-taxonomy.md) (discovery for tabbed settings) · [CMD-279](https://linear.app/cmd0112/issue/CMD-279) (dialog migration) · [CMD-286](https://linear.app/cmd0112/issue/CMD-286) (nested-scroll enforcement)

### Dialog layout tiers

Apply on each `Window` explicitly (see `WrapperControls.xaml` `DialogMinWidth*` keys):

| Tier | Min size | Default | Resize | Scroll pattern |
|------|----------|---------|--------|----------------|
| **Small form** | 440×400 | ~520×420 | Yes | `ShellFormScrollViewerStyle` in `Grid` `*` row |
| **Medium editor** | 480×400 | ~640–720×520 | Yes | Form or tab scroll per decision tree |
| **Large workspace** | 640×520 | ~760–920×680 | Grip | Form scroll and/or split `*` + side panel |

**Height budget:** The scroll host must sit in a `Grid` row with `Height="*"` (or a fixed `MaxHeight` cap). Never place `ShellFormScrollViewerStyle` / `ShellTabScrollViewerStyle` as the direct child of a `Window` without a star row — content grows unbounded and scrollbars stay inert.

Large editors with heavy header chrome (profile bars, hint blocks) should also set `MinHeight` on the body `*` row (e.g. `PlayPromptInjectionDialog` 360px, `ThemeCustomizationDialog` 320px) and collapse optional chrome into `Expander` controls so the scroll host keeps vertical budget.

### Dialog viewport sizing (CMD-279)

All wrapper modal dialogs use **`ShellDialogWindow`** (`ChatGPTWrapper/Shell/ShellDialogWindow.cs`) instead of raw `Window`, except `MainWindow`.

**On open (`ContentRendered`):**

1. Set `MaxWidth` / `MaxHeight` from `SystemParameters.WorkArea` (24px margin) via `DialogViewportLayout`.
2. If `%LocalAppData%\ChatGPTWrapper\dialog-layouts.json` has valid saved bounds for the dialog's layout key → restore them.
3. Else apply XAML design `Width` / `Height`.
4. Clamp position to the work area (respects `WindowStartupLocation="CenterOwner"` when no saved position).

**On close:** when `ActualWidth` / `ActualHeight` differ from design defaults (~4px tolerance), persist bounds to `dialog-layouts.json` (camelCase keys matching layout key, e.g. `playPromptInjectionDialog`).

**Layout key:** `protected virtual string LayoutKey => GetType().Name` unless overridden.

### WinUI dialog viewport sizing (CMD-565)

WinUI Tier 2–4 workbenches use **`WinUiShellDialogWindow`** / **`WinUiShellDialogHostWindow`** (`ChatGPTWrapper.WinUI/Shell/`) instead of `ContentDialog`.

| Concern | WPF | WinUI |
|---------|-----|-------|
| Shell base | `ShellDialogWindow` | `WinUiShellDialogWindow` |
| Scroll host | `ShellFormScrollViewerStyle` in `Grid` `*` row | `WinUiShellDialogHostWindow` body `ScrollViewer` (`VerticalScrollMode=Enabled`) |
| Open layout | `DialogViewportLayout.ApplyOpenLayout` | `WinUiDialogViewportLayout.ApplyOpenLayout` via `AppWindow.MoveAndResize` |
| Persist | `DialogLayoutStore` (shared) | Same store; keys unchanged (e.g. `ContinuousViewFormatDialog`) |
| **Open design size** | XAML `Width`/`Height` defaults | `WorkbenchViewportDesign` — work-area ratio + tier clamps; see [wrapper-ui-paradigm.md § Workbench open viewport](reference/wrapper-ui-paradigm.md) |
| Modal owner | WPF `Owner` + `ShowDialog` | `EnableWindow(owner, false)` + `GWL_HWNDPARENT` |
| Min size | `Window.MinWidth` / `MinHeight` | `Reclamp` on `AppWindow.Changed` (SDK 1.6 has no `PreferredMinimum*`) |

**Routing:** `WinUiDialogService.ShowWorkbenchAsync` for T2–T4; T1 confirm/prompt/rename stay on `ContentDialog` via `WinUiDialogHelper`.

**Footer contract:** Save/Cancel or Close buttons live in the host window footer row — not duplicated in page XAML.

**Opt-out flags:** same semantics as WPF (`PersistLayout`, `LayoutKey` override, design size capture).

**Related:** [winui-dialog-redesign-strategy.md](../plans/winui-dialog-redesign-strategy.md) · [CMD-565](https://linear.app/cmd0112/issue/CMD-565)

**Opt-out flags:**

| Flag | Default | Use when |
|------|---------|----------|
| `PersistLayout` | `true` | `WrapperSettingsDialog` (`SizeToContent`, `NoResize`) |
| `ApplyDesignSizeOnOpen` | `true` | `ThemeColorPickerDialog` (expander-driven sizing); `TextPromptDialog` when multiline |
| `RestorePersistedSizeOnOpen` | `true` | `AdventureRenameDialog`, `SyncFromThreadDialog` (`SizeToContent=Height`) |

**SizeToContent exceptions** (clamp max only; no forced design size / no persist unless user resized):

- `WrapperSettingsDialog` — full opt-out
- `AdventureRenameDialog`, `SyncFromThreadDialog` — `MaxHeight` clamp only
- `TextPromptDialog` — height `SizeToContent` for single-line prompts
- Ad-hoc `SizeToContent` prompts in `SourceManagerDialog` — call `DialogViewportLayout` helpers directly

`ThemeColorPickerDialog` calls `ReapplyViewportLayout()` after expander changes; initial open still flows through the base class.

### Scroll style map

| Style key | Based on | Use when |
|-----------|----------|----------|
| `WrapperInteriorScrollViewerStyle` | implicit `ScrollViewer` | Base pixel scroll; `CanContentScroll=False`, `PanningMode=VerticalOnly` |
| `ShellFormScrollViewerStyle` | interior | Single-column forms, wizards, reconcile/rename dialogs — body in `*` row |
| `ShellTabScrollViewerStyle` | interior + `Margin="8"` | **Each** `TabItem` body in tabbed settings (`PlayPromptInjectionDialog`, `PlayHandoffDialog`, `ProjectWorkspaceDialog` tabs) |
| `WrapperInteriorScrollBarStyle` | `ScrollBar` | Wider thumb + inset track for nested panels (format tabs, highlight lists) |

**Pixel vs logical scroll**

| Host | `CanContentScroll` | Why |
|------|-------------------|-----|
| `StackPanel` / `WrapPanel` inside `Shell*ScrollViewerStyle` | **False** (default) | Panels do not implement `IScrollInfo`; logical scroll breaks thumb drag |
| `ListBox`, `ListView`, `DataGrid` | **True** (theme default) | Virtualized / item-scrolling hosts in a `*` row with `VerticalScrollBarVisibility=Auto` |

Implicit `ScrollViewer` style sets `CanContentScroll=False` globally. Only items controls opt into logical scroll via `ScrollViewer.CanContentScroll`.

### Decision tree

```
Tabbed settings dialog?
├─ Yes → TabControl with Stretch content alignment (theme default)
│         └─ Each tab page: ShellTabScrollViewerStyle wrapping tab body
└─ No → Single form or wizard
          └─ Grid: Auto header/footer rows + * body
                └─ ShellFormScrollViewerStyle on *

Split list + detail?
├─ List in * column → ListBox/ListView (theme scroll) or capped preview host
└─ Detail in * column → ShellFormScrollViewerStyle or ShellTabScrollViewerStyle

Read-only monospace dump (JSON, diff, log)?
└─ TextBox in * row, AcceptsReturn, VerticalScrollBarVisibility=Auto
   (no outer ScrollViewer — single scroll tier)

Multiline field inside tab/form scroll?
└─ Default: VerticalScrollBarVisibility=Disabled + MinHeight (outer panel scrolls)
   Exception: capped preview zone (see below)
```

### Trap patterns (CMD-278)

| Trap | Symptom | Fix |
|------|---------|-----|
| **Unbounded scroll root** | Scrollbar visible but wheel/thumb do nothing | `Grid` + `*` row + shell scroll style |
| **Tab content not stretched** | Tab page grows with content; outer scroll inert | `TabControl` `HorizontalContentAlignment` / `VerticalContentAlignment` = `Stretch` (theme); tab body uses `ShellTabScrollViewerStyle` |
| **Logical scroll on panels** | Thumb jumps or does not track content | `CanContentScroll=False` on form/tab `ScrollViewer` |
| **Broken control templates** | ComboBox popup or scrollbar thumb stuck | Bind `ScrollViewer.*` on `ComboBox`; transparent `RepeatButton` on `ScrollBar` track (theme) |
| **Nested scroll traps** | Wheel stuck on inner `TextBox` | Disable inner `VerticalScrollBarVisibility` unless capped preview exception |
| **List without height budget** | List expands dialog; no list scroll | List host in `*` row; theme `ListBox`/`ListView` vertical scroll |

### Nested scroll policy (summary)

Full enforcement: [CMD-286](https://linear.app/cmd0112/issue/CMD-286).

| Zone | Inner `TextBox` scroll | Rule |
|------|------------------------|------|
| **Panel scroll** (default) | `Disabled` + `MinHeight` | Outer `ShellTabScrollViewerStyle` / `ShellFormScrollViewerStyle` receives wheel |
| **Capped preview** | `Auto` when `MaxHeight` ≤ ~160px | Read-only diff/snippet previews (`PlayPromptInjectionDialog`, `JsonImportReviewDialog`) |
| **Monospace dump** | `Auto` in `*` row | No outer `ScrollViewer` wrapper |

### ComboBox & ScrollBar requirements

- **ComboBox:** `ScrollViewer.HorizontalScrollBarVisibility` / `VerticalScrollBarVisibility` = `Auto` on the style; `MaxDropDownHeight` = 240. Popup list scrolls independently of dialog body scroll.
- **ScrollBar:** Default 10px thumb; `WrapperInteriorScrollBarStyle` 12px with inset track for dense nested panels. Page up/down `RepeatButton` templates must stay **transparent** so only the thumb captures drag.
- **Editable ComboBox:** Use `ComboBoxEditableTextBoxStyle` — not the standalone `TextBox` chrome (nested `PART_ContentHost` with hidden scrollbars).

### Documented exceptions

| Surface | Pattern | Rationale |
|---------|---------|-----------|
| `ContinuousViewFormatDialog` | Custom tab interior scroll + `WrapperInteriorScrollBarStyle` | Wide format editor; Essentials + category tabs predate full migration ([CMD-279](https://linear.app/cmd0112/issue/CMD-279)) |
| `EntityReferencePanel` inline preview | `ShellFormScrollViewerStyle` + `MaxHeight="420"` | Capped side preview below entity list |
| `EntityRenameWizardDialog` | Nested `ShellFormScrollViewerStyle` + `MaxHeight="200"` on alias list | Wizard step sub-panel cap |
| `ShellAppBarTabControlStyle` | Horizontal `ScrollViewer` around `TabPanel` | Chat tab strip overflow — not a form body |
| Play / Design companion panels | `PlayLayoutCoordinator` breakpoints | In-session cockpit layout — not shell dialog contract ([CMD-126](https://linear.app/cmd0112/issue/CMD-126)) |
| `InjectionPacketPreviewControl` | Standalone `ScrollViewer` on preview body | Embedded control; parent dialog supplies `*` budget |

**Exception process:** New raw `ScrollViewer` (no `Shell*` style) requires a PR note citing which exception row applies or an update to this table. Prefer shell styles for all new dialogs.

### PR review checklist (scroll)

- [ ] Dialog body scroll uses `ShellFormScrollViewerStyle` or per-tab `ShellTabScrollViewerStyle`
- [ ] Scroll host is in a `Grid` `*` row (or documented exception)
- [ ] No `CanContentScroll=True` on `StackPanel` hosts
- [ ] Multiline fields inside tab scroll use `VerticalScrollBarVisibility=Disabled` unless capped preview
- [ ] `ListBox`/`DataGrid` in split layouts sit in `*` column with theme scroll enabled

---

## Themes (`Themes/`)

Runtime theme customization (CMD-111) updates WPF resource keys and WebView `--cgw-*` CSS variables from **Preferences → Appearance & theme…**. See [appearance-theme-settings.md](../settings/appearance-theme-settings.md) for scope layers and wave 2 plan. Static XAML tokens below are the compile-time defaults; at startup and on apply, `ThemeApplicationService` replaces brushes and typography in `Application.Current.Resources`.

| File | Role |
|------|------|
| `WrapperTokens.xaml` | Color, spacing, font tokens (defaults; overridden at runtime) |
| `WrapperChrome.xaml` | Window chrome, toolbar |
| `WrapperControls.xaml` | Shared control styles; scroll contract header + `ShellFormScrollViewerStyle` / `ShellTabScrollViewerStyle` |

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

### Shell user controls (`ChatGPTWrapper/Controls/` — CMD-417)

| Control | Purpose | Key API |
|---------|---------|---------|
| `SegmentedControl` | Single-selection mode toggle (replaces `ModeButtonStyle` clusters) | `ItemsSource`, `SelectedIndex`, `SelectedTag`, `SelectionChanged` |
| `StatusChip` | Clickable status badge (review count, link attention, running job) | `Label`, `Count`, `Kind`, `Click` |
| `ScopeBadgeView` | Semantic scope label badge (P2) | `ScopeLabel` — maps to `ScopeBadge*Brush` tokens |
| `ActionListRow` | Scannable list action with Run affordance | `Title`, `Hint`, `RunCommand`, `RowEnabled`, `DisabledReason`, optional `LeadingIcon` |

Supporting styles: `StatusChipButtonStyle`, `ActionListRowBorderStyle`, `ShellIconButtonStyle`, `ShellIconLabelButtonStyle` in `WrapperControls.xaml`. Icon glyphs in `WrapperIcons.xaml` (Segoe MDL2 Assets).

**Icon tier rules (CMD-422):** Shell View/⋯/Focus = icon-only + tooltip; play footer = `ShellIconLabelButtonStyle` (labels hidden at Compact density); companion tabs = icon+label; AI action rows = text-primary (optional `LeadingIcon`).

**Companion expander persistence (CMD-418):** `PlayCompanionRestoreService.StateAllFieldsExpanderKey` (`StateAllFields`) restores State tab “All fields” expander when **Remember cockpit section** is enabled on Play surface tab.

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

Native ChatGPT composer is always used; legacy wrapper composer UI was removed (CMD-263).

---

## ViewModels (source sync)

| Class | Role |
|-------|------|
| `SourceSyncRowViewModel` | Row in sync grid |
| `SourceManagerRowViewModel` | Source manager list row |
| `SourcePublishRowViewModel` | Publish status per file |

---

## Related documentation

- [Adventure Panel Reference](../user/adventure-panel.md)
- [User Guide](../user/user-guide.md)
- [Injected Assets](../developer/injected-assets.md) — in-page UI
- [Architecture — MainWindow map](../developer/architecture.md#mainwindow-partial-class-map)
