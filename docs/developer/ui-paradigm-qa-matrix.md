# UI paradigm alignment — QA matrix

**Status:** Living checklist (Wave 0 — [CMD-586](https://linear.app/cmd0112/issue/CMD-586))  
**Last updated:** 2026-07-09  
**Canon:** [wrapper-ui-paradigm.md](../reference/wrapper-ui-paradigm.md) § Definition of done · [ui-surface-catalog.md](../reference/ui-surface-catalog.md)

Use this matrix before marking a T3/T4 surface **paradigm aligned**. Final program sign-off: [CMD-621](https://linear.app/cmd0112/issue/CMD-621).

**Legend:** ✅ pass · ⚠️ partial · ❌ fail · — not applicable · ⬜ not yet audited

## Column definitions

| Column | Criterion | Principle |
|--------|-----------|-----------|
| **Scope** | Scope badge on every editable section | P2 |
| **Cards** | `ShellSectionCard` / `PlaySettingsSectionCard` (or successor) | Workbench |
| **Save** | Explicit Save/Cancel footer; dirty indicator | P6 |
| **Deep link** | Hub/cockpit passes tab/section id | P1 |
| **Scroll** | Body scroll contract; no wheel traps | P8 |
| **Enrich** | ≥1 WinUI enrichment (TeachingTip, filter, Ctrl+S, …) | P9 |
| **Entry** | Single primary entry for surface intent | P1 |
| **WinUI** | Native WinUI host (not WPF dialog body) | P10 |

## T4 session workbenches

| Catalog ID | Surface | Scope | Cards | Save | Deep link | Scroll | Enrich | Entry | WinUI | Notes |
|------------|---------|-------|-------|------|-----------|--------|--------|-------|-------|-------|
| MODAL-010 | Play settings | ⚠️ | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | ✅ | ✅ | Scope badges semantic ([CMD-585](https://linear.app/cmd0112/issue/CMD-585)); Save footer status-only ([CMD-570](https://linear.app/cmd0112/issue/CMD-570)) |
| MODAL-030+ | Project workspace | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⚠️ | [CMD-605](https://linear.app/cmd0112/issue/CMD-605) |
| MODAL-016 | Instruction designer | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ❌ | [CMD-609](https://linear.app/cmd0112/issue/CMD-609) |
| MODAL-017 | Design wizard | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ❌ | [CMD-611](https://linear.app/cmd0112/issue/CMD-611) |

## T3 global / review workbenches

| Catalog ID | Surface | Scope | Cards | Save | Deep link | Scroll | Enrich | Entry | WinUI | Notes |
|------------|---------|-------|-------|------|-----------|--------|--------|-------|-------|-------|
| MODAL-001 | Preferences hub | ⬜ | ⬜ | — | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | [CMD-595](https://linear.app/cmd0112/issue/CMD-595) |
| MODAL-003 | Format | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | [CMD-597](https://linear.app/cmd0112/issue/CMD-597) · [CMD-554](https://linear.app/cmd0112/issue/CMD-554) |
| MODAL-004 | Appearance & theme | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | [CMD-598](https://linear.app/cmd0112/issue/CMD-598) |
| MODAL-030 | Proposal review hub | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | [CMD-599](https://linear.app/cmd0112/issue/CMD-599) · [CMD-557](https://linear.app/cmd0112/issue/CMD-557) |
| MODAL-012 | Thread manager | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | Master-detail pattern |
| MODAL-013 | Play handoff | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | T3 workbench |
| MODAL-017 | Search | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | Master-detail |
| MODAL-008 | Canon inbox | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ❌ | [CMD-600](https://linear.app/cmd0112/issue/CMD-600) |
| MODAL-009 | Canon reconcile | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ❌ | [CMD-601](https://linear.app/cmd0112/issue/CMD-601) |
| MODAL-011 | Source compare | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ❌ | [CMD-603](https://linear.app/cmd0112/issue/CMD-603) |
| MODAL-014 | Entity edit | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | ⚠️ | [CMD-607](https://linear.app/cmd0112/issue/CMD-607) |

## T3/T4 pages (in-frame)

| Catalog ID | Surface | Scope | Cards | Save | Deep link | Scroll | Enrich | Entry | WinUI | Notes |
|------------|---------|-------|-------|------|-----------|--------|--------|-------|-------|-------|
| PAGE-005 | Preferences hub (in-frame) | ⬜ | ⬜ | — | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | Hub paradigm |
| PAGE-002 | Adventures dashboard | — | ⬜ | — | ⬜ | ⬜ | ⬜ | ⬜ | ✅ | Discovery hub · [CMD-110](https://linear.app/cmd0112/issue/CMD-110) |

## Play Settings baseline (template row)

Dry-run audit **2026-07-09** against `PlaySettingsWorkbenchPage` ([CMD-572](https://linear.app/cmd0112/issue/CMD-572) template):

| Check | Result | Follow-up |
|-------|--------|-----------|
| Scope badges on all section cards | ⚠️ | Semantic tokens shipped Wave 0; nav + section header use `ScopeBadgeView` |
| Section cards on all tabs | ✅ | `PlaySettingsSectionCard` |
| Footer Save/Cancel | ⚠️ | Status line + dirty badge; explicit Save/Cancel buttons tracked in CMD-570 |
| Deep links (`PlaySettingsTab`) | ✅ | Preferences, cockpit, command bar |
| Scroll contract | ✅ | `SettingsScrollViewer`; Preview tab exception |
| WinUI enrichment | ⚠️ | Nav filter; TeachingTips → CMD-619 |
| Single primary entry | ✅ | Gear / command bar / Preferences shortcuts |
| WinUI native | ✅ | `PlaySettingsWorkbenchPage` |

## Maintenance

- Add a row when a new T3/T4 surface ships
- Update **Notes** with Linear issue when work is scheduled
- On surface completion: fill all columns ✅ and link evidence in issue or PR
- Program exit: all T3/T4 rows ✅ or — with documented N/A

**Related:** [ui-paradigm-linear-tracker.md](../plans/ui-paradigm-linear-tracker.md) · [CMD-584](https://linear.app/cmd0112/issue/CMD-584)
