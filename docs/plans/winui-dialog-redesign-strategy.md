# WinUI Dialog Redesign Strategy — Full Wrapper Inventory

Strategy for replacing **all** WPF modal surfaces with **WinUI-only** dialogs that are **resizable, movable, and visually coherent** with the shell — and that **elevate** UX using WinUI capabilities WPF did not offer.

**Epic:** [CMD-552](https://linear.app/cmd0112/issue/CMD-552) (Wave 2 parity) · **Dialog port:** [CMD-515](https://linear.app/cmd0112/issue/CMD-515) · **Settings IA:** [settings-ux-taxonomy.md](../settings/settings-ux-taxonomy.md) · **Prior capture:** [winui-ux-parity-backlog.md](winui-ux-parity-backlog.md) · **Play Settings detail:** [play-settings-ui-roadmap.md](play-settings-ui-roadmap.md)

*Drafted: 2026-07-06*

---

## 1. Problem statement

### What users feel today

| Surface | Experience |
|---------|------------|
| **WPF dialogs** (`ShellDialogWindow`) | Dated chrome, but **movable, resizable**, persisted layout (`dialog-layouts.json`), scroll contracts that work |
| **Early WinUI ports** (`ContentDialog`) | Modern tokens in places, but **fixed size, centered modal**, cramped content, nested-dialog fragility, no layout memory |

**Conclusion:** Moving to WinUI-only is correct long-term, but **ContentDialog is the wrong default** for most wrapper dialogs. A straight port that shrinks `PlayPromptInjectionDialog` into a `ContentDialog` will repeat the mishapen-content problem.

### Architectural mistake to stop repeating

```
WPF ShellDialogWindow  →  resizable Window + DialogLayoutStore  ✓
WinUI ContentDialog    →  fixed overlay, one per XamlRoot         ✗ for hubs/editors
```

**New default:** WinUI **secondary `Window`** (child of main shell) with shared `ShellDialogWindow` behavior ported to WinUI — use `ContentDialog` only for **alerts and micro-prompts**.

---

## 2. Vision

> Every modal in ChatGPT Wrapper is a **first-class WinUI surface**: same Mica/tokens as the shell, **user-resizable** with remembered layout, scroll-safe at 1280×720 and 1920×1080, and **enriched** with WinUI-only affordances (InfoBar, TeachingTips, adaptive panes, native pickers).

**Non-goals for this program:**

- Rewriting domain logic in `ChatGPTWrapper.Core` / `ChatGPTWrapper.Adventure`
- 1:1 visual clone of every WPF tab — **simplify IA** where taxonomy allows
- Keeping WPF STA bridge after Phase 6 gate ([CMD-517](https://linear.app/cmd0112/issue/CMD-517))

---

## 3. Design principles

| # | Principle | Implication |
|---|-----------|-------------|
| **P1** | **Right chrome for the job** | 4-tier taxonomy (§4) — never put a 9-tab editor in `ContentDialog` |
| **P2** | **Resizable is mandatory** for Tier 2+ | Port `DialogLayoutStore` + `DialogViewportLayout` to WinUI windows |
| **P3** | **One primary entry per intent** | Align with [play-surface ADR](../adr/play-surface-ux-modernization-adr.md) — no duplicate Review/Settings paths |
| **P4** | **Progressive disclosure** | Essential / Common / Advanced / Developer tiers from [settings-ux-taxonomy.md](../settings/settings-ux-taxonomy.md) |
| **P5** | **Live preview where it matters** | Format, Appearance, Play injection preview — same UI thread as `ChatTabHost` (no STA marshaling) |
| **P6** | **Scroll contract** | Port WPF scroll/overflow rules to WinUI `ScrollViewer` + `Expander` patterns; document in `ui-components.md` |
| **P7** | **Enrich, don't just port** | Each Tier 2+ dialog gets ≥1 WinUI enrichment from §7 |
| **P8** | **Delete WPF body when routed** | Dual implementations are temporary; track in appendix inventory |

---

## 4. Surface taxonomy (choose before designing)

| Tier | Name | WinUI host | Movable / resizable | Examples |
|------|------|------------|---------------------|----------|
| **T0** | **Toast / inline** | `InfoBar`, panel inline | N/A | Save confirmation, probe status in Sources |
| **T1** | **Alert / micro-prompt** | `ContentDialog` | No (by design) | Confirm delete, rename box, single text prompt |
| **T2** | **Form sheet** | `ShellDialogWindow` (WinUI) | **Yes** | Wrapper settings, recap, scenario creation, export picker |
| **T3** | **Hub / workbench** | `ShellDialogWindow` + `NavigationView` or master-detail | **Yes**, min 900×600 | Preferences, Format, Appearance, Review, Threads |
| **T4** | **Session workbench** | `ShellDialogWindow` + tabs or pivot | **Yes**, min 1000×700 | Play settings, Project workspace, Source sync, Design wizard |

**Rule:** If it has **tabs, a grid, or a preview pane**, it is **T3 or T4** — never T1.

---

## 5. Platform foundation (build once)

### 5.1 `WinUiShellDialogWindow`

Port `ChatGPTWrapper.Shell.ShellDialogWindow` + `DialogViewportLayout` to WinUI:

| Capability | WPF today | WinUI target |
|------------|-----------|--------------|
| Persist size | `DialogLayoutStore` | **Reuse same JSON schema** (`dialog-layouts.json`) |
| Open clamp | `DialogViewportLayout.ApplyOpenLayout` | `AppWindow` size + monitor work area |
| Drag move | `Window` title bar | Custom title bar + `ExtendsContentIntoTitleBar` |
| Modal semantics | `ShowDialog()` | `Window` with owner HWND + disabled owner interaction |
| Theme | WPF `ThemeApplicationService` | `WrapperTokens` + `ThemeApplicationService` WinUI path |

**Deliverable:** `ChatGPTWrapper.WinUI/Shell/WinUiShellDialogWindow.cs` + `WinUiDialogService.ShowAsync<TWindow>(...)`.

### 5.2 `WinUiDialogService` (replaces split host)

Consolidate `WinUiDialogHostService` + `WpfDialogHostService` into one registry:

```text
WinUiDialogService
├── ShowAlert / Confirm / Prompt          → ContentDialog (T1)
├── ShowSheet<TPage>                      → ShellDialogWindow (T2)
└── ShowWorkbench<TPage>                  → ShellDialogWindow (T3/T4)
```

- **No WPF STA thread** after migration complete
- All WebView callbacks run on shell UI thread
- Sub-dialog sequencing: `WaitForCloseAsync` pattern generalized for window stack

### 5.3 Layout contract (WinUI)

Extend [ui-components.md](../reference/ui-components.md) with **WinUI scroll & overflow contract**:

- `ShellTabScrollViewerStyle` equivalent for WinUI
- `MinHeight` / `*` row discipline for master-detail
- `ContentDialog` max width only for T1
- Visual states: `Narrow` (&lt; 800), `Wide` (≥ 1200)

---

## 6. WinUI enrichment catalog (apply per dialog)

Use at least one per Tier 2+ surface:

| Feature | Use when | Example |
|---------|----------|---------|
| **Mica / Acrylic backdrop** | All T2–T4 windows | Match main shell |
| **InfoBar** | Validation, async job status | Sources probe, review accept errors |
| **TeachingTip** | First visit / new feature | Format Essentials, thread handoff |
| **BreadcrumbBar** | Drill-down editors | Entity edit → merge → reconcile |
| **NavigationView (compact)** | Hubs with 5+ sections | Play settings, Format |
| **SegmentedControl** | 2–5 peer modes | Review categories, transcript mode preview |
| **Animated transitions** | Wizard steps | Design wizard, handoff |
| **Adaptive master-detail** | List + detail | Review proposals, thread manager |
| **Keyboard accelerators** | Power users | Ctrl+S save, Ctrl+Enter accept proposal |
| **FolderPicker / FileOpenPicker** | Storage, export, import | Wrapper settings, backup import |
| **Live WebView preview** | Settings affecting chat CSS | Format, injection preview — **native win** |
| **Connected animation** | Optional polish | Open Format from Preferences card |
| **Accessibility** | Always | Narrator names on all hub nav items |

---

## 7. Full dialog inventory & redesign briefs

**Status key:** `WPF` = production today · `WinUI-partial` = native page exists, wrong host or routing · `WinUI-target` = planned

### 7.1 Shell & global settings

| Dialog | Tier | Today | Target UX | Enrichment | Wave |
|--------|------|-------|-----------|------------|------|
| **Preferences hub** | T3 | WinUI `ContentDialog` | **T3 window** — hub cards, adventure section | TeachingTips on first open; card connected animation | W1 |
| **Wrapper settings** (storage & paths) | T2 | WinUI `ContentDialog` | **T2 window** — path + browse + default | FolderPicker; InfoBar on invalid path | W1 |
| **Format** (`ContinuousViewFormatDialog`) | T3 | WPF | **T3 window** — Essentials + Refine + Advanced (collapsed) | **Live WebView preview** pane; Segmented mode switch | W1 |
| **Appearance / theme** (`ThemeCustomizationDialog`) | T3 | WPF | **T3 window** — Presets + Colors + Typography tabs | Live shell preview strip; preset grid with thumbnails | W1 |
| **Keyboard shortcuts** | T2 | WPF only | **T2 window** — searchable list | `AutoSuggestBox` filter | W3 |
| **Libraries** | T3 | WPF only | **T3 window** or fold into Format Advanced | — | W4 |

### 7.2 Play session — settings & threads

| Dialog | Tier | Today | Target UX | Enrichment | Wave |
|--------|------|-------|-----------|------------|------|
| **Play settings** (`PlayPromptInjectionDialog`) | T4 | WPF | **T4 window** — `NavigationView` sections per [settings-ux-taxonomy](../settings/settings-ux-taxonomy.md): World, Injection, Next send, Sources, Play surface, Session, Automation, Memory | Live injection preview; InfoBar for pin state; **no 9 equal tabs** — group rare tabs under Advanced | W2 |
| **Thread manager** (`AdventureThreadManagerDialog`) | T3 | WPF + partial WinUI page | **T3 window** — registry list + detail + handoff actions | Adaptive master-detail; status chips | W1 |
| **Play handoff** | T3 | WPF | **T3 window** — wizard steps | Step indicator; clipboard packet preview | W2 |
| **Browser tab picker** | T1 | WPF | **T1 ContentDialog** or inline `TabView` flyout | — | W1 |

### 7.3 Play session — review & AI tools

| Dialog | Tier | Today | Target UX | Enrichment | Wave |
|--------|------|-------|-----------|------------|------|
| **Proposal review hub** | T3 | WPF + partial WinUI page | **T3 window** — category nav + item list + diff detail | Segmented source filter; keyboard accept/dismiss; **live refresh** on job complete | W1 |
| **JSON import review** | T3 | WPF | **T3 window** — queue + diff | Same shell as proposal review (shared `ReviewWorkbench` control) | W2 |
| **Utility job attachment launch** | T1 | WPF code-only | **T1 ContentDialog** | — | W3 |
| **Flight packet compare** | T3 | WPF | **T3 window** — side-by-side monospace | Adjustable split ratio (persisted) | W4 |
| **Context viewer** | T2 | WPF | **T2 window** — read-only packet | Copy button; word wrap toggle | W3 |

### 7.4 Project & sources

| Dialog | Tier | Today | Target UX | Enrichment | Wave |
|--------|------|-------|-----------|------------|------|
| **Source manager** (Sources tab) | T4 | Part of Play settings | **Dedicated T3 entry** optional; primary remains Play settings Sources section | Probe InfoBar; sync status | W2 |
| **Source sync** (`SourceSyncDialog`) | T4 | WPF | **T4 window** — plan + apply + diagnostics | Progress ring; per-file status list | W2 |
| **Source compare** | T3 | WPF | **T3 window** — diff view | — | W3 |
| **Project workspace** | T4 | WPF | **T4 window** — link/unlink + files + instructions | WebView pane optional; InfoBar for link state | W2 |
| **Conversation files** | T2 | WPF | **T2 window** — file list + download | — | W3 |

### 7.5 Design mode

| Dialog | Tier | Today | Target UX | Enrichment | Wave |
|--------|------|-------|-----------|------------|------|
| **Design wizard** (`AdventureDesignWizard`) | T4 | WPF | **T4 window** — step nav (Cast → Lexicon → Sources → …) | Animated step transitions; validation InfoBar | W3 |
| **Instruction designer** | T3 | WPF | **T3 window** — template + preview | — | W4 |
| **Cast phrase import** | T2 | WPF | **T2 window** — paste + validate | — | W4 |
| **Scenario creation** | T2 | WinUI partial | **T2 window** | Secondary “Design with AI” path | W1 |

### 7.6 Entities & canon

| Dialog | Tier | Today | Target UX | Enrichment | Wave |
|--------|------|-------|-----------|------------|------|
| **Entity edit** | T3 | WPF + partial `EntityEditPage` | **T3 window** — category nav + form | Breadcrumb; merge entry | W2 |
| **Entity merge** | T3 | WPF | **T3 window** — pick target + preview | Diff preview pane | W2 |
| **Entity retire** | T2 | WPF | **T2 sheet** | Confirm + consequence summary | W2 |
| **Entity rename wizard** | T3 | WPF | **T3 window** — plan steps | Diff preview | W3 |
| **Entity change plan diff** | T2 | WPF | **T2 window** | — | W3 |
| **Canon inbox** | T3 | WPF | **T3 window** | — | W4 |
| **Canon reconcile** | T2 | WPF | **T2 sheet** | — | W4 |

### 7.7 Adventure utilities

| Dialog | Tier | Today | Target UX | Enrichment | Wave |
|--------|------|-------|-----------|------------|------|
| **Search** | T3 | WPF | **T3 window** — query + results | `AutoSuggestBox`; result highlight | W2 |
| **Recap** | T2 | WinUI partial | **T2 window** — scrollable narrative | Copy / export actions | W1 |
| **Export** | T1–T2 | WinUI partial | **T1 picker** → **T2** if options needed | `FileSavePicker` | W1 |
| **Import backup** | T2 | WinUI partial | **T2 window** | `FileOpenPicker`; validation InfoBar | W1 |
| **Rename adventure** | T1 | Both | **T1 ContentDialog** only | — | W1 |
| **Random table** | T2 | WPF | **T2 window** | — | W4 |
| **Text prompt** (generic) | T1 | WPF | **T1 ContentDialog** via `PromptAsync` | — | W1 |
| **Local inference lab** | T4 | WPF | **T4 window** — Ollama test bench | Job status InfoBar; deferred until SVA-12 product scope | W4 |

### 7.8 Theme helpers (sub-dialogs)

| Dialog | Tier | Today | Target UX | Wave |
|--------|------|-------|-----------|------|
| **Theme color picker** | T1 | WPF | **T1 flyout** (`ColorPicker` control) | W1 |
| **Highlight color assignment** | T2 | WPF | **T2 sheet** | W2 |
| **Highlight color grouping** | T2 | WPF | **T2 sheet** | W2 |

**Total:** ~45 distinct surfaces → **~12 T1**, **~15 T2**, **~18 T3/T4**.

---

## 8. Information architecture changes (elevation, not parity)

### Play settings (largest redesign)

Replace 9 peer tabs with **NavigationView** groups:

```text
Essential          World · Injection · Next send
Session            Threads shortcut · Pin · Handoff
Content            Sources · Memory & cards
Presentation       Play surface layout
Advanced ▾         Automation · Developer · History · AI actions
```

- **Injection preview** docked right (collapsible) — always visible on wide layout
- **Narrator** quick controls stay in cockpit; full settings open Injection section
- Sources opens same window focused on Content → Sources (not a separate mishapen dialog)

### Format & Appearance

- **Format:** Essentials (mode, font scale, spacing) + Refine (colors/highlights) + Advanced collapsed
- **Appearance:** Preset gallery first; custom colors typographic second — match shell, not WPF matrix on day one
- Cross-link between Format and Appearance in header (`HyperlinkButton`)

### Review hub

- Single **`ReviewWorkbench`** user control shared by proposal review + JSON import review
- Category list never narrower than 220px; detail pane gets remaining width
- **Auto-refresh** when utility jobs complete (`INotifyPropertyChanged` on bundle store)

### Preferences

- Remains discovery hub — **does not host heavy UI**
- Every card opens a **T2/T3 window**, never embeds editors in `ContentDialog`

---

## 9. Implementation waves

| Wave | Focus | Surfaces | Exit gate |
|------|-------|----------|-----------|
| **W0 — Foundation** | `WinUiShellDialogWindow`, `WinUiDialogService`, layout port, scroll contract doc | 0 user-visible | Unit tests for layout persist; modal owner HWND |
| **W1 — Global & light** | Preferences, Wrapper settings, Format, Appearance, Review, Threads, Rename, Export/Import, Recap, Scenario | 12 | All View menu + Preferences paths native; no `ContentDialog` &gt; 500px tall |
| **W2 — Play core** | Play settings, Handoff, Search, Entity edit/merge/retire, Source sync, Project workspace | 10 | Play session fully usable; zero `WpfDialogHostService` on play path |
| **W3 — Design & depth** | Design wizard, Instruction designer, Canon, Context viewer, Conversation files | 10 | Design mode native |
| **W4 — Long tail** | Libraries, Random table, Local inference lab, highlight pickers, flight compare | 8 | `WpfDialogHostService` deleted |
| **W5 — Cleanup** | Remove WPF XAML dialogs, STA host, dual routes | — | [CMD-517](https://linear.app/cmd0112/issue/CMD-517) gate |

**Parallelism:** W0 blocks all. W1 surfaces are independent per dialog after W0. Play settings (W2) should not start until Format/Appearance preview patterns proven in W1.

---

## 10. Quality gates (per dialog)

Before retiring WPF body:

- [ ] **Resize:** User can resize; size persists in `dialog-layouts.json`
- [ ] **Move:** Drag title bar; stays on-screen after monitor change
- [ ] **Scroll:** No clipped controls at 1280×720 and 1920×1080
- [ ] **Keyboard:** Esc closes; Enter commits where safe; Ctrl+S saves on editors
- [ ] **Theme:** Respects light/dark + custom theme tokens
- [ ] **Diagnostics:** Open/close logged; failures → `wrapper-diagnostics.jsonl`
- [ ] **Enrichment:** At least one §6 feature documented in dialog header comment
- [ ] **Routing:** All entry points in inventory table use `WinUiDialogService`
- [ ] **Tests:** ApiDiagnostics test for persistence or critical command where applicable

---

## 11. Migration mechanics

### Per-dialog checklist

1. Classify tier (§4)
2. Write 3-line UX brief (what improves vs WPF)
3. Implement as `*Page.xaml` inside `WinUiShellDialogWindow`
4. Wire all entry points (grep `WpfDialogHostService` + menu handlers)
5. Manual QA on entry points listed in [winui-ux-parity-backlog.md](winui-ux-parity-backlog.md)
6. Mark WPF file `@deprecated` → delete in W5
7. Update appendix B in [winui-shell-migration-adr.md](../adr/winui-shell-migration-adr.md)

### Anti-patterns (explicitly banned)

- ❌ Large `ContentDialog` with `ScrollViewer` as the only layout strategy
- ❌ Nested `ContentDialog` without close handshake
- ❌ WPF STA for anything that touches `ChatTabHost`
- ❌ Dual routing without tracking issue
- ❌ Margin-only “redesign” without IA review

---

## 12. Success metrics

| Metric | Target |
|--------|--------|
| WPF dialog invocations from WinUI host | **0** by W5 |
| Dialog layout complaints (manual QA) | No “can’t resize” / “cut off” on Tier 2+ |
| ContentDialog used for Tier 3+ | **0** |
| Dual implementations | **0** at W5 |
| Play settings open reliability | 100% entry points (regression suite) |

---

## 13. Linear mapping

| Wave | Epic / issues |
|------|----------------|
| Program | [CMD-564](https://linear.app/cmd0112/issue/CMD-564) Epic |
| W0 | [CMD-565](https://linear.app/cmd0112/issue/CMD-565) Dialog shell foundation |
| W1 | [CMD-554](https://linear.app/cmd0112/issue/CMD-554) Format · [CMD-559](https://linear.app/cmd0112/issue/CMD-559) Appearance · [CMD-557](https://linear.app/cmd0112/issue/CMD-557) Review · [CMD-558](https://linear.app/cmd0112/issue/CMD-558) Threads · [CMD-561](https://linear.app/cmd0112/issue/CMD-561) Preferences |
| W2 | [CMD-560](https://linear.app/cmd0112/issue/CMD-560) Play settings · [CMD-567](https://linear.app/cmd0112/issue/CMD-567) Play workbenches |
| W3 | [CMD-566](https://linear.app/cmd0112/issue/CMD-566) Design & depth · [CMD-555](https://linear.app/cmd0112/issue/CMD-555) Design sidebar |
| W4 | [CMD-568](https://linear.app/cmd0112/issue/CMD-568) Long tail |
| W5 | [CMD-569](https://linear.app/cmd0112/issue/CMD-569) Delete WPF bridge → [CMD-517](https://linear.app/cmd0112/issue/CMD-517) |

---

## 14. Immediate next steps

1. **Approve taxonomy** — T1 `ContentDialog` vs T2+ `WinUiShellDialogWindow`
2. **Spike W0** — one resizable WinUI window with `DialogLayoutStore` persist (prove movable + resize)
3. **Pilot W1** — migrate **Format** or **Review** as reference T3 (most visible enrichment: live preview / master-detail)
4. **Freeze** new `ContentDialog` hosts for Tier 2+ until W0 lands

---

## Related

- [winui-ux-parity-backlog.md](winui-ux-parity-backlog.md) — QA findings
- [winui-post-migration-parity-plan.md](winui-post-migration-parity-plan.md) — functional parity pass
- [settings-ux-taxonomy.md](../settings/settings-ux-taxonomy.md) — settings IA
- [ui-components.md](../reference/ui-components.md) — control catalog

*Update this document when a wave completes or a dialog is retired.*
