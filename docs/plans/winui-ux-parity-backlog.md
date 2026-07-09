# WinUI UX Parity Backlog — Manual QA Capture

Structured capture of **post-migration UX and functional gaps** observed on the WinUI host after the CMD-521 parity pass. Use this as the working backlog for the next WinUI UX wave (native dialogs, play/design surfaces, shell polish).

**Parent epic:** [CMD-521](https://linear.app/cmd0112/issue/CMD-521) · **Wave 2 epic:** [CMD-552](https://linear.app/cmd0112/issue/CMD-552) · **QA gate:** [CMD-532](https://linear.app/cmd0112/issue/CMD-532) · **Dialog native port:** [CMD-515](https://linear.app/cmd0112/issue/CMD-515)

**Related:** [winui-post-migration-parity-plan.md](winui-post-migration-parity-plan.md) · [winui-shell-migration-adr.md](../adr/winui-shell-migration-adr.md) · [play-surface-ux-modernization-implementation-plan.md](play-surface-ux-modernization-implementation-plan.md) · [play-settings-ui-roadmap.md](play-settings-ui-roadmap.md)

*Captured: 2026-07-05 (manual QA on WinUI host via `run.ps1`)*  
*Updated: 2026-07-06 — follow-up QA session (see [2026-07-06 session](#2026-07-06-qa-session))*

---

## Summary

| Tier | Count | Theme |
|------|-------|--------|
| **P0 — broken / unusable** | 0 | Fixed 2026-07-06 — Play Settings cluster + Storage & paths |
| **P1 — rebuild required** | 2 | Play Settings native UI; Format live WebView preview |
| **P2 — native UX port + polish** | 4 | Format, Appearance, Local inference lab, Review — **working or deferred; old WPF UX** |

---

## 2026-07-06 QA session

Manual QA on WinUI host (`cmd-521-winui-post-migration-parity` branch). Items marked **defer** are functional on the WPF STA path but still use legacy dialog UX — revisit in a later design wave, not blocking this pass.

### Active — broken or unusable (fixed 2026-07-06)

| # | Entry point | Report | Fix |
|---|-------------|--------|-----|
| 1 | Adventure panel → **Full narrator settings** | `Dialog failed` | `WinUiPlaySettingsBridge` — no WinUI XAML access on WPF STA during `Wire()`; preview/pin delegates marshaled via `WinUiShellHost.RunOnUiThreadSync` |
| 2 | Companion **State** tab → **Edit world in settings** | `Dialog failed` | Same Play Settings open-path fix (`PlaySettingsTab.World`) |
| 6 | Preferences → **Storage & paths** | Does not work | `WinUiDialogHelper.WaitForCloseAsync` after Preferences `Hide` before nested `ShowWrapperSettingsAsync` |
| 8 | **Sources** | `Dialog failed` | Same Play Settings open-path fix (`PlaySettingsTab.Sources`) |
| 9 | **Play Settings** | `Dialog failed` | Same cluster |

### Deferred — working, old WPF design

Discuss in a later UX wave.

| # | Entry point | Report | Related |
|---|-------------|--------|---------|
| 3 | **Review proposals** — update (?) | Deferred with UX wave | [CMD-557](https://linear.app/cmd0112/issue/CMD-557) |
| 4 | **Format** | Working; old WPF UX | [CMD-554](https://linear.app/cmd0112/issue/CMD-554) |
| 5 | **Appearance** | Working; old WPF UX | [CMD-559](https://linear.app/cmd0112/issue/CMD-559) |
| 7 | **Local inference lab** (Preferences) | Working; old WPF UX | SVA-12 / preferences hub |

---

## Backlog items

### 1. View modes — not working (P0)

**Linear:** [CMD-553](https://linear.app/cmd0112/issue/CMD-553)

**Report:** None of the view modes (Native / Continuous / Weave) work at all.

**Surfaces:** `ViewCommandBar` transcript segment · `SessionTopBar` · `ChatTabHost` theme/CSS injection · `UiChromeStore.TranscriptViewMode` persistence · per-tab WebView refresh after mode change.

**Acceptance criteria:**

- [ ] Switching Native / Continuous / Weave updates active chat tabs visually within one session.
- [ ] Mode persists across restart (`ui-chrome.json`).
- [ ] Mode switch does not crash or leave WebView in a broken CSS state.
- [ ] Status line / View bar reflects active mode.

---

### 2. Format — old UX, needs rework (P1) — **deferred 2026-07-06**

**Linear:** [CMD-554](https://linear.app/cmd0112/issue/CMD-554)

**Report:** Format options still use the old WPF UX and need to be reworked for WinUI wrapper tokens and settings taxonomy. **2026-07-06:** Opens and works on WPF path; visual/IA port deferred.

**Surfaces:** `WinUiDialogHostService.ShowFormatDialogAsync` (native) · View → Format… · Preferences → Reading & format.

**Acceptance criteria:**

- [x] Native WinUI format surface using `WrapperTokens` / `WrapperControls` (Essentials + Refine tabs; not full 9-tab WPF dialog).
- [x] Essentials + refinement IA preserved from WPF ([settings-ux-taxonomy.md](../settings/settings-ux-taxonomy.md)).
- [ ] Live preview applies to active adventure WebView on WinUI thread.
- [x] No WPF STA modal for primary Format entry path.

---

### 3. Design mode sidebar — non-functional, full rebuild (P1)

**Linear:** [CMD-555](https://linear.app/cmd0112/issue/CMD-555)

**Report:** Design mode sidebar is essentially non-functional and needs a complete rebuild.

**Surfaces:** `AdventureDesignPage` · design step nav · cast/scenario/sources panels · link to `AdventureDesignWizard` (still WPF).

**Acceptance criteria:**

- [ ] All design sidebar sections load data from active adventure bundle.
- [ ] Cast/scenario edit, sources shortcuts, and wizard entry match WPF `AdventureDesignView` workflows.
- [ ] Layout uses WinUI responsive patterns (scroll hosts, min widths, collapse rails).
- [ ] No dead buttons or WPF-only paths without WinUI equivalent.

---

### 4. Play cockpit top panel — rebuild (Session / Narrator / AI Tools) (P1)

**Linear:** [CMD-556](https://linear.app/cmd0112/issue/CMD-556)

**Report:** Top panel of the adventure sidebar (Session, Narrator, AI Tools) barely functions and needs a complete rebuild.

**Surfaces:** `PlaySessionCockpit` · segmented section switcher · narrator controls · AI tools list / job UI.

**Acceptance criteria:**

- [ ] Session segment shows live thread/session context and actionable controls.
- [ ] Narrator segment: profiles, overrides, scope (send/session/adventure) wired to `NarratorOverrideResolver`.
- [ ] AI Tools segment: job list, status, run/cancel aligned with utility worker orchestration.
- [ ] Section switcher uses WinUI `SegmentedControl` + theme tokens; no raw type names or empty panels.

---

### 5. Manage Threads — old UX, rework/rebuild (P1)

**Linear:** [CMD-558](https://linear.app/cmd0112/issue/CMD-558)

**Report:** Manage Threads still uses the old WPF UX.

**Surfaces:** `WinUiDialogHostService.ShowThreadManagerAsync` (native) · `WinUiThreadManagerBridge` · Session bar · Preferences hub shortcut.

**Acceptance criteria:**

- [x] Native WinUI threads hub with registry rows, pin/pick tab, create slot, handoff entry points.
- [x] All `AdventureThreadManagerActions` delegate to WinUI WebView hosts (project workspace + handoff still WPF secondary modals).
- [x] Matches thread-manager copy and behavior from WPF reference (`initialKind`, Design tab rename).

---

### 6. Review — old UX, rework/rebuild (P1)

**Linear:** [CMD-557](https://linear.app/cmd0112/issue/CMD-557)

**Report:** Review still uses the old WPF UX. **2026-07-06:** Play cockpit Review chip routes to WPF `ProposalReviewHubDialog`; native `ProposalReviewHubPage` not wired. QA note "proposals update (?)" — clarify refresh-after-job vs open failure.

**Surfaces:** `WinUiDialogHostService.ShowProposalReviewAsync` (native) · dashboard/play review entry points.

**Acceptance criteria:**

- [x] Native WinUI proposal review hub with proposal list, diff preview, accept/reject/defer.
- [ ] Live refresh after utility jobs complete (manual QA).
- [x] Themed with wrapper chrome; resizable scroll-safe layout per dialog contract.

---

### 7. Appearance — functional but old UX (P2) — **deferred 2026-07-06**

**Linear:** [CMD-559](https://linear.app/cmd0112/issue/CMD-559)

**Report:** Appearance works but still uses the old WPF dialog UX. **2026-07-06:** Confirmed working on WPF path; native port deferred.

**Surfaces:** `WinUiDialogHostService.ShowThemeCustomizationAsync` (native) · View → Appearance… · Preferences → Appearance & theme.

**Acceptance criteria:**

- [x] Native WinUI appearance editor with preset grid and live shell preview (Colors/Typography tabs not ported).
- [x] Theme apply path uses in-place brush updates + `RefreshShellChromeFromThemeChange` (no stale chrome).
- [x] Persist via `UiChromeStore` unchanged schema.

---

### 8. Play Settings — cannot open; full end-to-end rework (P0 / P1)

**Linear:** [CMD-560](https://linear.app/cmd0112/issue/CMD-560)

**Report:** Play Settings cannot be opened at all; even when fixed, it likely needs comprehensive top-to-bottom rework with new UX and design choices. **2026-07-06 regression:** `Dialog failed` from narrator settings, State → edit world, Sources link, Preferences play shortcuts, and play footer — all `ShowPlaySettingsAsync` / `PlayPromptInjectionDialog` on WPF STA.

**Surfaces:** `WpfDialogHostService.ShowPlaySettingsAsync` → `PlayPromptInjectionDialog` (WPF UI) · `WinUiPlaySettingsBridge` · Play footer / companion / Preferences shortcuts · `PlaySessionCockpit` narrator + sources links · `PlayCompanionHost` State → edit world.

**Acceptance criteria:**

- [ ] Play Settings opens reliably from all entry points (View bar, companion, Preferences, play footer, narrator settings, sources, State → edit world).
- [ ] No STA/COM crash on open; errors logged to `wrapper-diagnostics.jsonl`.
- [ ] Native WinUI tabbed settings surface (World, Injection, Sources, Play surface, Session, Automation) per [settings-ux-taxonomy.md](../settings/settings-ux-taxonomy.md).
- [ ] All `WinUiPlaySettingsDialogHost` callbacks round-trip (preview compose, sources probe, threads, utility jobs, handoff, review).
- [ ] Progressive disclosure tiers (Essential / Common / Advanced / Developer) preserved (WPF UI retained).

---

### 9. Preferences — margin/spacing polish (P2)

**Linear:** [CMD-561](https://linear.app/cmd0112/issue/CMD-561)

**Report:** Preferences hub needs margins and spacing polish. **2026-07-06:** **Storage & paths** row does not work (`ShowWrapperSettingsAsync` from hub sub-dialog opener).

**Surfaces:** `PreferencesHubPage.xaml` · `ContentDialog` host in `MainWindow.ShowPreferencesHubAsync` · `ActionListRow` cards · `WrapperSettingsPage`.

**Acceptance criteria:**

- [x] Consistent padding with shell chrome (`ShellChromePadding`, `SpaceMd`/`SpaceLg`).
- [x] Section headers, hint text, and card gaps match WPF `PreferencesHubDialog` v2 rhythm.
- [x] ScrollViewer does not clip focus rings or card shadows.
- [ ] **Storage & paths** opens wrapper settings, validates path, saves via `WrapperSettingsStore`.

---

### 10. Adventure side panel — spacing + responsive design (P2)

**Linear:** [CMD-562](https://linear.app/cmd0112/issue/CMD-562)

**Report:** Adventure side panel needs margin/spacing polish and much better responsive design for elements.

**Surfaces:** `AdventurePlayPage` column layout · `PlayCompanionHost` · companion width / collapse rails · `PlayFooterBar`.

**Acceptance criteria:**

- [ ] Min/max companion width respects viewport; collapse/expand rails usable at narrow widths.
- [ ] Consistent outer margin with content frame; no clipped controls at 1280×720 and 1920×1080.
- [ ] Footer bar wraps or truncates gracefully; status chips do not overlap session controls.

---

### 11. Companion tabs — Reference / Warnings / State rework (P2)

**Linear:** [CMD-563](https://linear.app/cmd0112/issue/CMD-563)

**Report:** Reference, Warnings, and State tabs need rework — both contents and tab styling.

**Surfaces:** `PlayCompanionHost` tab strip · `PlayCompanionReferencePanel` · Warnings/State panels · tab `ItemTemplate` / segmented styling.

**Acceptance criteria:**

- [ ] Tab chrome uses WinUI wrapper tokens (not default `TabView` chrome only).
- [ ] Reference: entity list filters, search, row actions, edit/delete/merge wired natively.
- [ ] Warnings: live canon/conflict chips with actionable drill-down.
- [ ] State: session/adventure state summary with refresh on turn complete.
- [ ] No `ToString()` rows or placeholder copy in production paths.

---

## Suggested implementation waves

| Wave | Items | Focus |
|------|-------|--------|
| **A — Unblock daily play** | CMD-553, CMD-560, CMD-561 (storage) | View modes + Play Settings / Sources / narrator open path + Preferences storage |
| **B — Native settings/dialogs** | CMD-554, CMD-558, CMD-557, CMD-559 | Format, Threads, Review, Appearance on WinUI |
| **B-defer — WPF UX still OK** | CMD-554, CMD-559, SVA-12 | Format, Appearance, Local inference lab — working; old design only |
| **C — Play surface rebuild** | CMD-556, CMD-563, CMD-562 | Cockpit header, companion tabs, responsive layout |
| **D — Design parity** | CMD-555 | Design sidebar + in-session workspace |
| **E — Polish** | CMD-561 | Preferences spacing pass |

---

## Tracking

- Attach QA screenshots/recordings to [CMD-532](https://linear.app/cmd0112/issue/CMD-532) when addressing each tier.
- Track execution under epic [CMD-552](https://linear.app/cmd0112/issue/CMD-552); link PRs with `Ref CMD-XX`.
- Update [winui-shell-migration-adr.md](../adr/winui-shell-migration-adr.md) appendix B when a dialog moves from WPF STA to native WinUI.

---

*This document is the canonical capture for manual QA findings on the WinUI host. Update checkboxes and add issue keys as work lands.*
