# Appearance & Theme Settings

How **Appearance & theme** customization works: scope layers, dialog tabs, token catalog, persistence, and relationship to **Format…** transcript styling.

**Related:** [User Guide — Appearance & theme](user-guide.md#appearance--theme) · [UI Components — Themes](ui-components.md#themes-themes) · [CMD-111](https://linear.app/cmd0112/issue/CMD-111) epic · [CMD-179](https://linear.app/cmd0112/issue/CMD-179) wave 2 phase 1

---

## Overview

Appearance settings control the **wrapper shell** (WPF) and **wrapper-owned WebView chrome** (injected UI such as scrollbars and play compose). They do **not** restyle stock ChatGPT.com UI.

| Layer | Settings home | What it controls |
|-------|---------------|------------------|
| **Shell** | Appearance & theme | Windows, dialogs, toolbars, lists, menus, adventure panels |
| **WebView chrome** | Appearance & theme (partial) | `--cgw-*` variables in `wrapper-overrides.css`, compose/tags (expanding) |
| **Transcript** | Format… | Continuous / Weave / Native per-mode typography and role colors |
| **Decoration** | Highlights editor | Phrase highlight rules (independent of theme palette) |

---

## Where to find controls

**Path:** Main window → **Preferences…** (toolbar overflow) → **Appearance & theme…**

**Implementation:** `ChatGPTWrapper/Views/ThemeCustomizationDialog.xaml`

| Tab | Controls |
|-----|----------|
| **Presets** | Search, category filter, 37+ built-in palettes, saved presets with **category tags** (create, duplicate, rename, delete, save, re-categorize) |
| **Colors** | Semantic tokens grouped by surface/text/accent/status; search; per-token reset; contrast warnings; auto-fix |
| **Typography** | Shell font family and body/title/hint sizes |
| **Spacing & shape** | Five-step spacing scale (Xs–Xl) and control/card corner radii |
| **Advanced** | Import/export theme JSON (multi-select files, preset packs, or preset arrays); open `user-overrides.css` and styles folder |

Changes preview live while the dialog is open. **Apply** saves other edits to `ui-chrome.json`. **Cancel** restores the theme from when the dialog opened — except **imports**, which are saved immediately when confirmed.

Transcript typography and role colors remain in **Format…** (see [Continuous transcript view](user-guide.md#continuous-transcript-view)).

### Theme JSON import formats

Advanced → **Import theme JSON…** accepts **one or more** `.json` files (Ctrl+click or Shift+click in the file dialog). **Confirmed imports are written to `ui-chrome.json` immediately** — you do not need to click Apply for imported presets to persist.

| Format | Example root | Behavior |
|--------|--------------|----------|
| **Full theme** | `{ "activePresetId": "...", "userPresets": [...] }` | Single-file import uses save-as-preset or apply working copy; multi-file materializes each export as a named preset |
| **Preset pack** | `{ "presets": [ {...}, {...} ] }` | Merges all presets; optional apply first |
| **Preset array** | `[ {...}, {...} ]` | Same as preset pack |
| **Theme array** | `[ { "activePresetId": "..." }, ... ]` | Each entry becomes a saved preset |

**Multi-file:** presets from every selected file merge into your library (by `id`, replacing on conflict). Full-theme exports without embedded presets are saved under the **file name** (without `.json`).

Preset objects use `name`, `category`, `tokens`, and optional typography/spacing fields.

---

## Persistence

Stored in `%LocalAppData%\ChatGPTWrapper\ui-chrome.json`:

| Field | Purpose |
|-------|---------|
| `theme.activePresetId` | Selected preset or `custom` |
| `theme.customOverrides` | Per-token hex overrides |
| `theme.fontFamily`, `fontSize*` | Shell typography overrides |
| `theme.space*`, `radius*` | Spacing and shape overrides |
| `theme.userPresets[].category` | Preset list grouping (Essentials, Dark accents, My presets, etc.) |
| `themeRevision` | Bumped on apply; triggers WebView CSS re-injection |

Code: `ChatGPTWrapper/Theme/ThemeSettings.cs`, `UiChromeStore.cs`

---

## Token catalog

**Source of truth:** `ChatGPTWrapper/Theme/ThemeTokenCatalog.cs`

| Group | Examples | WPF brush | WebView CSS |
|-------|----------|-----------|-------------|
| Surfaces | Base, Surface, Elevated, Chrome | `BgBaseBrush`, … | `--cgw-bg-base`, … (subset) |
| Text | Primary, Muted, On accent | `TextPrimaryBrush`, … | `--cgw-text-primary`, … |
| Accent | Primary (+ derived hover/pressed/subtle/link) | `AccentPrimaryBrush`, … | `--cgw-accent`, … |
| Status | Success, Warning, Error | `SuccessBrush`, … | `--cgw-success`, … |
| Borders | Subtle, Strong | `BorderSubtleBrush`, … | `--cgw-border-subtle`, … |
| Lists | Hover, Selected, Alternate | `RowHoverBrush`, … | WPF only today |
| Chrome | Menus, ghost buttons, popups | `PopupBrush`, … | WPF only today |

Human-readable labels for the dialog: `ThemeTokenDisplay.cs`.

Derived tokens (hover/pressed/subtle) are calculated by `ThemeDerivation.cs`. Readability is enforced by `ThemeContrast.cs` on apply; the dialog shows **pre-enforcement warnings** via `ThemeApplicationService.ValidateUserTokens`.

---

## CSS layering (WebView)

On trusted `chatgpt.com` pages (`ChatGptStyleInjection`):

1. Bundled CSS from `wrapper-assets/`
2. Runtime `:root { --cgw-* }` from saved theme
3. `user-overrides.css` in `%LocalAppData%\ChatGPTWrapper\styles\`
4. Format dialog variables when continuous/weave overlay is active

---

## Wave 2 expansion plan (CMD-111)

| Phase | Issue | Focus |
|-------|-------|-------|
| **1** | [CMD-179](https://linear.app/cmd0112/issue/CMD-179) | Dialog polish + user preset library |
| **2** | [CMD-180](https://linear.app/cmd0112/issue/CMD-180) | Multi-surface preview panel |
| **3** | [CMD-181](https://linear.app/cmd0112/issue/CMD-181) | WebView chrome token mapping |
| **4** | [CMD-182](https://linear.app/cmd0112/issue/CMD-182) | Density presets (compact / comfortable) |
| **5** | [CMD-183](https://linear.app/cmd0112/issue/CMD-183) | Preset format companions |
| **6** | [CMD-184](https://linear.app/cmd0112/issue/CMD-184) | Follow system theme + quick switcher (optional) |

---

## Related documentation

- [Injected Assets](injected-assets.md) — JS/CSS injection details
- [Narrator Settings](narrator-settings.md) — play-side panel (not appearance)
- [linear-issue-reference.md](linear-issue-reference.md) — issue taxonomy
