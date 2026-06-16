# User Guide — Browse Mode & Shell Features

This guide covers the **ChatGPT Wrapper shell** outside the Adventures subsystem. For interactive fiction, see [Adventure Panel Reference](adventure-panel.md). For ChatGPT Projects, see [Projects & Source Sync](user-projects-and-sync.md).

---

## Application modes

The toolbar has three mode buttons:

| Mode | Layout | Use when |
|------|--------|----------|
| **Browse** | Full-width ChatGPT browser tabs | General ChatGPT use with wrapper enhancements |
| **Adventures** | Left: adventure dashboard · Right: ChatGPT tabs | Managing adventures, linking projects |
| **Play** | Left: play companion panel · Right: pinned ChatGPT tab | Active adventure play with automation |

Switching to **Play** collapses the adventure list to a single active adventure and pins (or creates) a dedicated ChatGPT tab for that adventure's conversation.

---

## Chat tabs

- **New tab** (+): Opens another ChatGPT tab in the same WebView2 profile.
- **Close tab**: Closes the selected tab (play-pinned tabs may be protected during active play).
- **Session persistence**: Cookies and login state are stored in `%LocalAppData%\ChatGPTWrapper\WebView2UserData`. Sign in once; sessions survive app restarts.
- **Compatibility**: The app data folder name matches the reference `cursor-wrapper` build, so cookies may carry over if you used that project before.

---

## Continuous transcript view

ChatGPT's default UI shows each message in separate bubbles. **Continuous view** collapses the conversation into a readable prose transcript — especially useful for long Adventures or reading-heavy chats.

### Enable

- Toolbar checkbox: **Continuous view**
- Or open **Format…** (gear next to the checkbox) and enable it there

Settings persist in `%LocalAppData%\ChatGPTWrapper\ui-chrome.json`.

### Format settings dialog

**Format…** opens `ContinuousViewFormatDialog` with tabs for **Continuous view** (layout/typography/code sliders), **Highlights**, **Thread behavior**, and **Presets**.

| Setting | Applies when | Effect |
|---------|----------------|--------|
| **Layout / typography / code sliders** | Continuous view **on** | CSS variables on `#cgw-continuous-view` (spacing, fonts, headings) |
| **Phrase highlights** | Continuous view **on** | Color-code matching phrases in the overlay |
| **Show images** | Continuous view **on** | Inline images vs filename placeholders |
| **Composer clearance** | Continuous view **on** | Min/max padding above the composer |
| **Enhanced prose** | Native thread + CV | `prose-enhancements.css` on stock bubbles; enhanced CV sliders when on |
| **Hide assistant edit artifacts** | Continuous view **on** | Hides edit/regenerate clutter in the overlay |
| **Hide packet context in thread** | Play tab thread | Shows player line only in ChatGPT (default: on) |
| **Expand hidden context** | Play tab thread | Collapsed adventure context when tags are hidden |

Use **Preview in chat** in the dialog to live-apply settings (requires Continuous view). **Apply** commits without closing; **OK** saves to `ui-chrome.json`.

Presets can be exported/imported as JSON from the Presets tab.

### In-page behavior

When enabled, `continuous-transcript-view.js`:

- Rebuilds the thread DOM into continuous prose blocks
- Supports peek/edit/regenerate actions on individual turns (where ChatGPT exposes turn IDs)
- Integrates phrase highlights and prose enhancements
- Reschedules on navigation and DOM mutations

---

## Phrase highlights

Highlight specific words or phrases in the continuous transcript (e.g. character names, locations).

### Configure

1. Enable **Phrase highlights** in the Format dialog (or standalone **Highlights…** if exposed in toolbar)
2. Add rules in `PhraseHighlightsEditorControl`:
   - **Phrase** — text to match (case-sensitive option available)
   - **Color** — highlight color
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

`ChatGptStyleInjection` injects bundled + user CSS on trusted `chatgpt.com` pages.

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
| `continuousViewEnabled` | `false` | Continuous transcript mode |
| `proseEnhancementsEnabled` | `false` | Prose typography enhancements |
| `hideAssistantEditArtifacts` | `false` | Strip edit UI from transcript |
| `hideContextTagsInThread` | `true` | Hide `[[cgw:…]]` markers |
| `expandHiddenContextInThread` | `true` | Expandable context when tags hidden |
| `phraseHighlightsEnabled` | `false` | Phrase highlight rules active |
| `phraseHighlightRules` | `[]` | Array of `{ phrase, color, … }` |
| `continuousViewFormat` | defaults | Paragraph/speaker/code formatting options |

---

## Keyboard and window chrome

The main window uses custom theme resources (`Themes/WrapperChrome.xaml`, `WrapperTokens.xaml`, `WrapperControls.xaml`) for a consistent dark wrapper chrome around the embedded browser.

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
