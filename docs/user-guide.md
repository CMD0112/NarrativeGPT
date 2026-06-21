# User Guide — Browse Mode & Shell Features

This guide covers the **ChatGPT Wrapper shell** outside the Adventures subsystem. For interactive fiction, see [Adventure Panel Reference](adventure-panel.md). For ChatGPT Projects, see [Projects & Source Sync](user-projects-and-sync.md).

---

## Application modes

The toolbar has two mode buttons: **Browse** and **Adventures**. **Play** and **Design** are entered from the adventure dashboard (double-click an adventure to Play, or use **Design with AI…** / **Continue design…**).

| Mode | Layout | Use when |
|------|--------|----------|
| **Browse** | Full-width ChatGPT browser tabs | General ChatGPT use with wrapper enhancements |
| **Adventures** | Left: adventure dashboard · Right: ChatGPT tabs | Managing adventures, linking projects, starting design |
| **Design** | Left: design wizard panel · Right: ChatGPT tab | AI-assisted adventure authoring (wizard steps, design-thread chat) |
| **Play** | Left: play companion panel · Right: pinned ChatGPT tab | Active adventure play with automation |

Switching to **Play** collapses the adventure list to a single active adventure and pins (or creates) a dedicated ChatGPT tab for that adventure's conversation.

---

## Chat tabs

- **New tab** (+): Opens another ChatGPT tab in the same WebView2 profile.
- **Close tab**: Closes the selected tab (play-pinned tabs may be protected during active play).
- **Session persistence**: Cookies and login state are stored in `%LocalAppData%\ChatGPTWrapper\WebView2UserData`. Sign in once; sessions survive app restarts.
- **Compatibility**: The app data folder name matches the reference `cursor-wrapper` build, so cookies may carry over if you used that project before.

---

## Transcript view modes

ChatGPT's default UI shows each message in separate bubbles. The wrapper offers three **transcript view** modes via **View → Native / Continuous / Weave**:

| Mode | Effect |
|------|--------|
| **Native** | Stock ChatGPT bubbles (default) |
| **Continuous** | Collapses the thread into readable prose blocks (`continuous-transcript-view.js`) |
| **Weave** | Alternate prose layout with weave-specific typography (`weave-transcript-view.js`) |

Settings persist in `%LocalAppData%\ChatGPTWrapper\ui-chrome.json`.

### Enable

- **View** menu → **Native**, **Continuous**, or **Weave**
- **View** menu → **Format…** (or **Preferences…** → **Continuous view & format…**) to configure transcript typography

The legacy toolbar **Continuous view** checkbox is hidden; use the View menu instead.

### Format settings dialog

**Format…** opens `ContinuousViewFormatDialog` with tabs: **Presets**, **Reading layout**, **Colors**, **Highlights**, and **Thread display**.

A **Format profile** picker at the top of the dialog lets you save, switch, and manage named configurations (similar to Appearance & theme presets):

| Action | What it does |
|--------|----------------|
| **Profile combo** | Select **Compact**, **Default**, **Relaxed**, or a custom profile |
| **New profile…** | Save the current working settings under a new name |
| **Duplicate** | Copy the selected profile (or current custom layout) |
| **Rename** / **Delete** | Custom profiles only — built-ins cannot be removed |
| **Save to profile** | Overwrite the selected custom profile with unsaved edits |
| **Custom** | Shown when layout diverges from every saved profile, or when you have unsaved edits |

The active profile is stored in `ui-chrome.json` and restored on relaunch. Status text under the picker explains whether you are on a saved profile, have unsaved changes, or are in a fully custom layout.

| Tab | Contents |
|-----|----------|
| **Presets** | Quick apply **Compact** / **Default** / **Relaxed**; section reset (layout, colors, role distinction); import/export JSON under Advanced |
| **Reading layout** | Layout sliders; **Your messages** and **Assistant messages** typography and distinction (font size, line height, letter spacing, weight, accent border, background tint, indent); optional role labels; enhanced prose and code/headings in collapsed sections |
| **Colors** | Per-token color overrides (layout, user/assistant roles, prose links, code blocks, tables) with swatch + hex + **Inherit**; opens the same color picker as Appearance & theme |
| **Highlights** | Phrase highlight rules (preset swatches + **Pick…** for full color picker) |
| **Thread display** | Prose enhancements, hide edit prompts, packet context, images, composer clearance |

The right panel shows a **live sample transcript** (user + assistant) plus an optional **Developer: injected CSS** expander. Use **Preview in chat** to live-apply in the WebView (requires Continuous view). **Apply** commits without closing; **OK** saves to `ui-chrome.json`. Cancel warns when there are unsaved changes.

| Setting | Applies when | Effect |
|---------|----------------|--------|
| **Layout / role typography / colors** | Continuous or Weave **on** | CSS variables on transcript overlay (spacing, per-role fonts, distinction, colors) |
| **Phrase highlights** | Continuous or Weave **on** | Color-code matching phrases in the overlay |
| **Show images** | Continuous or Weave **on** | Inline images vs filename placeholders |
| **Composer clearance** | Continuous or Weave **on** | Min/max padding above the composer |
| **Enhanced prose** | Native thread + CV/Weave | `prose-enhancements.css` on stock bubbles; enhanced CV sliders when on |
| **Hide assistant edit artifacts** | Continuous or Weave **on** | Hides edit/regenerate clutter in the overlay |
| **Hide packet context in thread** | Play tab thread | Shows player line only in ChatGPT (default: on) |
| **Expand hidden context** | Play tab thread | Collapsed adventure context when tags are hidden |

Presets can be exported/imported as JSON from **Presets → Advanced**. On import, choose **Import as new profile** (creates a named profile from the file) or replace the working copy only.

### In-page behavior

When **Continuous** or **Weave** is active:

- Rebuilds the thread DOM into prose blocks (continuous or weave layout)
- Supports peek/edit/regenerate actions on individual turns (where ChatGPT exposes turn IDs)
- Integrates phrase highlights and prose enhancements
- Reschedules on navigation and DOM mutations

---

## Phrase highlights

Highlight specific words or phrases in the continuous transcript (e.g. character names, locations).

### Configure

1. Open **Format…** → **Highlights** tab
2. Add rules in `PhraseHighlightsEditorControl`:
   - **Phrase** — text to match (case-sensitive option available)
   - **Color** — highlight color (preset swatches or **Pick…** for full color picker)
   - **Match whole word** — optional boundary matching

Rules are saved in `ui-chrome.json` under `phraseHighlightRules`.

### How it appears

`continuous-phrase-highlights.js` decorates matching text inside continuous-view blocks. Highlights update when the transcript rebuilds.

---

## Style overrides

### Bundled CSS

At build time, files from `ChatGPT_files/` copy to `wrapper-assets/` beside the executable:

- `wrapper-overrides.css` — general ChatGPT UI tweaks
- `prose-enhancements.css` — typography when prose enhancements are enabled
- `continuous-transcript-view.css`, `cgw-context-tags.css`, `cgw-play-compose.css` — feature-specific styles

`ChatGptStyleInjection` injects bundled + theme CSS variables + user CSS on trusted `chatgpt.com` pages.

### Appearance & theme

Open **Preferences…** (toolbar overflow) → **Appearance & theme…** to customize the WPF shell and ChatGPT WebView chrome from one place.

See **[Appearance & Theme Settings](appearance-theme-settings.md)** for the full scope model, token catalog, CSS layering, and expansion roadmap.

| Tab | What it controls |
|-----|------------------|
| **Presets** | Built-in palettes: Default dark, High contrast, Warm reading, Midnight, Forest, Ocean, Rose, Amethyst |
| **Colors** | Semantic tokens (surfaces, text, accent, borders, lists) with search, human labels, hex fields, per-token reset, contrast warnings, and **Pick…** color dialog |
| **Typography** | Shell font family and sizes (toolbar, dialogs) — not transcript text |
| **Spacing & shape** | Five-step spacing scale (extra small through extra large) and control/card corner radii |
| **Advanced** | Import/export theme JSON (multi-select files, preset packs, or preset arrays); open styles folder or `user-overrides.css` |

Changes apply live while the dialog is open; **Apply** saves other edits to `ui-chrome.json`. **Cancel** restores the theme from when the dialog opened — except **imports**, which persist immediately when confirmed.

Transcript typography (paragraph spacing, speaker fonts, phrase highlights) remains in **Format…** — see [Continuous transcript view](#continuous-transcript-view).

### CSS layering (WebView)

On trusted ChatGPT pages, styles load in this order:

1. Bundled CSS from `wrapper-assets/` (`wrapper-overrides.css`, feature sheets)
2. **Runtime theme variables** — `:root { --cgw-* }` injected from your theme settings
3. **`user-overrides.css`** — personal tweaks in `%LocalAppData%\ChatGPTWrapper\styles\`
4. **Format dialog** variables — continuous-view transcript typography when enabled

### User overrides (survive updates)

Create or edit:

```
%LocalAppData%\ChatGPTWrapper\styles\user-overrides.css
```

User CSS loads after bundled styles. Use this for personal theme tweaks without modifying the app install.

---

## Context tags (play and browse)

When playing Adventures with **Use context tags** enabled, prompt packets wrap sections in markers like `[[cgw:scenario]]`. `cgw-context-tags.js` can display or hide these in the ChatGPT thread.

Shell setting **Hide context tags in thread** (default on) keeps the reading experience clean while still sending full packets to the model.

---

## UI chrome persistence

All browse-mode display settings live in:

```
%LocalAppData%\ChatGPTWrapper\ui-chrome.json
```

| Field | Default | Description |
|-------|---------|-------------|
| `continuousViewEnabled` | `false` | Continuous transcript mode (legacy field; View menu is primary) |
| `transcriptViewMode` | `Native` | `Native`, `Continuous`, or `Weave` |
| `proseEnhancementsEnabled` | `false` | Prose typography enhancements |
| `hideAssistantEditArtifacts` | `false` | Strip edit UI from transcript |
| `hideContextTagsInThread` | `true` | Hide `[[cgw:…]]` markers |
| `expandHiddenContextInThread` | `true` | Expandable context when tags hidden |
| `phraseHighlightsEnabled` | `false` | Phrase highlight rules active |
| `phraseHighlightRules` | `[]` | Array of `{ phrase, color, … }` |
| `continuousViewFormat` | defaults | Paragraph/speaker/code formatting options |
| `theme` | `default-dark` preset | Active preset, color overrides, shell typography, **user presets** |
| `themeRevision` | `0` | Bumped on theme apply; triggers WebView style re-injection |

---

## Keyboard and window chrome

The main window uses custom theme resources (`Themes/WrapperChrome.xaml`, `WrapperTokens.xaml`, `WrapperControls.xaml`) for a consistent dark wrapper chrome around the embedded browser.

**Preferences hub** (app bar **⋯ → Preferences…**) links to:

| Entry | Opens |
|-------|--------|
| Continuous view & format… | `ContinuousViewFormatDialog` |
| Wrapper settings… | Adventures folder path (`WrapperSettingsDialog`) |
| Play session settings… | `PlayPromptInjectionDialog` (Session tab; requires active adventure) |

Adventure-specific controls (Do/Say, composer, review) are documented in [adventure-panel.md §4](adventure-panel.md#4-play-view).

---

## What stays local vs what goes to ChatGPT

| Stays on your machine | Sent to ChatGPT |
|----------------------|-----------------|
| `ui-chrome.json`, user CSS | — |
| WebView2 cookies (session only) | Your typed messages and automated prompt packets |
| Adventure JSON under `adventures/` | Only packet text you send during play |
| Library templates | — |

Signing in to ChatGPT stores session cookies locally; the wrapper does not transmit your password outside the normal ChatGPT login flow in WebView2.

---

## Related documentation

- [Adventure Panel Reference](adventure-panel.md) — play loop, review, export
- [Projects & Source Sync](user-projects-and-sync.md) — ChatGPT Projects
- [Troubleshooting](troubleshooting.md) — blank WebView, bridge failures
- [Injected Assets](injected-assets.md) — technical details of JS/CSS files
