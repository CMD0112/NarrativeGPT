# Settings UX Taxonomy (ADR)

Architecture decision record for settings scope, categories, discovery rules, and deprecation policy. [CMD-262](https://linear.app/cmd0112/issue/CMD-262) under epic [CMD-254](https://linear.app/cmd0112/issue/CMD-254).

**Related:** [Inventory](settings-interactables-inventory.md) · [Audit findings](settings-interactables-audit.md) · [Appearance & theme](appearance-theme-settings.md) · [UI Components](ui-components.md) · [WPF scroll & overflow layout](ui-components.md#wpf-scroll--overflow-layout-contract-cmd-278--cmd-285)

---

## Status

**Accepted** (W0–W2). Hub v2 implemented in [CMD-264](https://linear.app/cmd0112/issue/CMD-264) (W5).

---

## 1. Scope layers

Settings apply at nested scopes; inner scopes override outer defaults for a single send or session where noted.

| Layer | Store | Examples |
|-------|-------|----------|
| **Global** | `ui-chrome.json` | Transcript mode, theme, format profiles |
| **Wrapper** | `wrapper-settings.json` | Adventures root path |
| **Adventure** | `adventure.json` → `settings` | Tone, automation, layout, sources mode |
| **Session** | `adventure.json` sessions + pins | Utility sessions, tab pins, inline utility peek |
| **Send** | `playTurnOverrides`, narrator cockpit | One-shot length/tone for next packet |
| **Transcript mode** | Per-mode buckets in `ui-chrome.json` | Continuous vs Weave format tokens |

**Rule:** Global appearance/format does not mix with adventure play behavior. Theme ([appearance-theme-settings.md](appearance-theme-settings.md)) and Format (ContinuousViewFormatDialog) stay separate surfaces cross-linked from Preferences hub.

---

## 2. Categories

| Category | Scope | Primary discovery |
|----------|-------|-------------------|
| **Appearance** | global | Preferences → Appearance & theme |
| **Reading / format** | global, per-mode | View → Format…; Preferences → Continuous view & format |
| **Play behavior** | adventure | Play header → Play settings (World, Settings tabs) |
| **Automation (advanced)** | adventure | Play settings → Settings tab; developer tier |
| **Project / sources** | adventure | SourceManagerDialog; Play settings Sources tab |
| **Design authoring** | adventure | Design mode wizard + sources panel |
| **Utility dialogs** | session | More actions, AI tools, entity CRUD |

---

## 3. Discovery rules

| User intent | Go here first | Hub shortcut |
|-------------|---------------|--------------|
| Change colors/fonts of chat reading | View → Format… | Preferences → Continuous view & format |
| Change shell chrome / app theme | Preferences → Appearance & theme | — |
| Change adventures folder | Preferences → Wrapper settings | — (dashboard routes to hub) |
| Tune narrator for this adventure | Play → Play settings | Preferences → Play session settings |
| Link Project / sync files | Play → Sources… | Dashboard → Link Project |
| Layout side panels in play | Play settings → Play surface | — |

Tabbed settings dialogs (`PlayPromptInjectionDialog`, `ContinuousViewFormatDialog`, Preferences hub) must follow the [WPF scroll & overflow layout contract](ui-components.md#wpf-scroll--overflow-layout-contract-cmd-278--cmd-285): per-tab `ShellTabScrollViewerStyle`, no nested `TextBox` wheel traps, pixel scroll on form hosts.

**Duplicates policy (W2):**

- Dashboard **Wrapper settings** / **Storage settings** → open **Preferences hub** (not direct WrapperSettingsDialog).
- Play **Play settings** remains contextual primary; hub entry is a shortcut when an adventure is loaded.
- **Sources** tab duplicates SourceManager shortcuts — keep both until CMD-264 regroups.

---

## 4. Progressive disclosure

| Tier | Audience | Placement |
|------|----------|-----------|
| **Essential** | All users | View menu transcript + Format **Essentials** tab; format refinement panel; Preferences hub top cards |
| **Common** | Regular players | Play settings World/Settings; theme presets |
| **Advanced** | Power users | Format Advanced tab; automation toggles; import/export |
| **Developer** | Maintainers | Force fat packets, Prefer DOM send, bridge diagnostics, packet preview |

CMD-264 will add explicit **Advanced automation** expander in Play settings for developer-tier toggles (**done** in CMD-263).

---

## 5. Deprecation register

UI removed or dead; **JSON fields retained** for backward compatibility unless a migration explicitly strips them.

| Field / surface | Status | Notes |
|-----------------|--------|-------|
| `UseWrapperComposer` | UI removed | Runtime treats as false; field kept in schema |
| `PhraseHighlightsDialog` | Files deleted | Editor embedded in Format dialog |
| `AdventureSettingsDialog` | Files deleted | Was shim to Play settings |
| `ProjectLinkWizard` | Files deleted | `OpenProjectLinkWizardAsync` → ProjectWorkspaceDialog |
| `LegacyContinuousViewEnabled` | No UI | Migrated to `transcriptViewMode` |
| `SourcePublishMode.ApiSync` | No UI | Forced Manual |
| `UtilityDeliveryMode.SeparateThread` | No UI | Migrated InlinePlayThread |
| `ResponseReviewDialog`, `EditTurnDialog` | Files deleted | Superseded by continuous-view surrogate edit + automated review |

---

## 6. Roadmap (CMD-254 streams)

| Stream | Issues | Wave | Status (this session) |
|--------|--------|------|------------------------|
| **A** Inventory & audits | CMD-255, CMD-256–261 | W0–W1 | **Done** (docs) |
| **B** Taxonomy ADR | CMD-262 | W2 | **Done** (this doc) |
| **C** Deprecation cleanup | CMD-263 | W2–W3 | **In progress** (advanced automation expander; dead dialogs removed) |
| **D** Format/fonts/colors | CMD-178, CMD-146, CMD-176, **CMD-306** | W3–W4 | CMD-306 phase 3: Essentials tab, refinement panel, diagnostics, rich preview |
| **E** Preferences hub v2 | CMD-264, CMD-20 | W5 | **In progress** (hub v2 + play tab regroup) |
| **F** Dashboard revamp | CMD-110, CMD-214–218 | W6 | Deferred |
| **G** Theme / Weave wave 2 | CMD-111, CMD-156, CMD-158 | W3–W4 | CMD-158 In Progress |

---

## 7. Disposition of related issues

| Issue | Disposition after W2 |
|-------|----------------------|
| **CMD-20** Play settings overhaul | Absorbed by CMD-257 audit + CMD-264 hub v2 |
| **CMD-178** Format font families | Close when FormatSettings tests green |
| **CMD-146** Format colors | In Review + Needs Manual QA when wired |
| **CMD-176** Accent center adjust | Close when ChromePreferences test green |
| **CMD-263** Deprecation | In Review after UI removal subset |

---

## 8. Decision log

| Date | Decision |
|------|----------|
| 2026-06 | Split theme vs format per existing appearance-theme ADR |
| 2026-06 | Preferences hub is global discovery; play settings stay contextual |
| 2026-06 | Deprecation = remove UI + dead dialogs; keep schema fields |
| 2026-06 | CMD-264 owns full hub IA; this ADR defines target state |

---

## Maintenance

When adding settings UI or changing discovery paths, update this ADR, [settings-interactables-inventory.md](settings-interactables-inventory.md), and [ui-components.md](ui-components.md) in the same change.
