# Injected Web Assets

JavaScript and CSS bundled from `ChatGPT_files/` and copied to `wrapper-assets/` at build time (`ChatGPTWrapper.csproj` `Content` item).

Runtime path beside executable: `wrapper-assets/{filename}`

User CSS (not bundled): `%LocalAppData%\ChatGPTWrapper\styles\user-overrides.css`

---

## Build pipeline

```xml
<Content Include="..\ChatGPT_files\**\*"
         Link="wrapper-assets\%(RecursiveDir)%(Filename)%(Extension)"
         CopyToOutputDirectory="PreserveNewest" />
```

`WrapperAssetBundle` and `WrapperAssetCache` load file contents for injection.

---

## Asset catalog

### Kernel and infrastructure

| File | Injected by | Purpose |
|------|-------------|---------|
| `cgw-page-kernel.js` | `ChatGptPageHost` | Message routing, bridge bootstrap |
| `cgw-bridge-kernel.js` | (included in kernel payload) | Shared bridge utilities |
| `cgw-composer-dom.js` | Adventure bridge | Composer DOM probing/filling |
| `cgw-conversation-stream.js` | API bridge | Stream helpers |

### Bridges

| File | Feature ID | Channel | Global API |
|------|------------|---------|------------|
| `chatgpt-api-bridge.js` | `api-bridge` | `cgw-api` | `__cgwApiInvoke(cmd)` |
| `adventure-bridge.js` | `adventure-bridge` | `cgw-play` | `__cgwAdventureHandleCommand(cmd)` |

See [WebView Bridges](webview-bridges.md) for command reference.

### Play UI

| File | Feature ID | Purpose |
|------|------------|---------|
| `cgw-play-compose.js` | `play-compose` | In-page Do/Say/Story composer |
| `cgw-play-compose.css` | `play-compose` | Composer overlay styles |

### Display / reading

| File | Feature ID | Purpose |
|------|------------|---------|
| `continuous-transcript-view.js` | `continuous-view` | Collapse thread to prose |
| `continuous-transcript-view.css` | `continuous-view` | Transcript layout |
| `continuous-format.js` | `continuous-view` | Paragraph/speaker formatting |
| `continuous-format-settings.js` | `continuous-view` | Format settings from C# JSON |
| `chrome-preferences.js` | `continuous-view` | Unified preference apply from C# |
| `continuous-phrase-highlights.js` | `continuous-view` | Phrase match decoration |
| `cgw-context-tags.js` | `context-tags` | Context tag strip in thread |
| `cgw-context-tags.css` | `context-tags` | Tag styles |
| `cgw-packet-display.js` | `continuous-view` / display | Packet section rendering |

### Style overrides

| File | Feature ID | Purpose |
|------|------------|---------|
| `wrapper-overrides.css` | `style` | General ChatGPT UI tweaks |
| `prose-enhancements.css` | `style` | Typography when prose enhancements on |

### Third-party (vendored)

| File | Used by | Purpose |
|------|---------|---------|
| `marked.min.js` | Packet/display | Markdown rendering |
| `purify.min.js` | Packet/display | HTML sanitization |

---

## CSS override layers (load order)

1. ChatGPT native styles
2. `wrapper-overrides.css` (bundled)
3. `prose-enhancements.css` (if enabled)
4. Feature CSS (`continuous-transcript-view.css`, `cgw-play-compose.css`, `cgw-context-tags.css`)
5. `user-overrides.css` (local app data)

`ChatGptStyleInjection` injects bundled + user CSS on injectable pages.

---

## Key JavaScript globals

Set or consumed across assets:

| Global | Set by | Purpose |
|--------|--------|---------|
| `__cgwApplyChromePreferences(payload)` | C# `ChromePreferencesApplier` | Unified apply for format, CV flags, highlights, packet display |
| `__cgwFormatSettingsRevision` | chrome-preferences.js | Bumps when settings change; invalidates extract cache |
| `__cgwSetContinuousView(enabled)` | chrome-preferences / CV | Toggle continuous mode |
| `__cgwContinuousViewEnabled` | continuous-transcript-view | Mode flag |
| `__cgwContinuousViewSchedule()` | multiple | Trigger transcript rebuild |
| `__cgwProseEnhancementsEnabled` | C# | Prose CSS flag |
| `__cgwPhraseHighlightsEnabled` | C# | Highlight rules active |
| `__cgwPhraseHighlightStyleFp` | C# | Rule fingerprint for rebuild |
| `__cgwWrapperComposer` | play-compose | Wrapper composer instance |
| `__cgwApiBridgeVersion` | api-bridge | Version stamp |
| `__cgwAdventureBridgeVersion` | adventure-bridge | Version stamp |

---

## Injection gate

Scripts inject only when `ChatGptPageGate.IsInjectable(url)` is true (trusted `chatgpt.com` origins).

On `NavigationCompleted`, `ChatGptPageHost` re-applies kernel + features.

---

## Testing

| Test class | Assets covered |
|------------|----------------|
| `BridgeAssetTests` | `chatgpt-api-bridge.js` exports |
| `PlayComposeAssetTests` | `cgw-play-compose.js`/`.css` contract |
| `PacketDisplayAssetTests` | packet-display, context-tags, continuous view |
| `ChromePreferencesTests` | `chrome-preferences.js`, format apply pipeline |
| `PacketDisplayParityTests` | C#/JS parity via fixtures |

Fixtures: `tests/ChatGPTWrapper.ApiDiagnostics/Fixtures/`

---

## Related documentation

- [WebView Bridges](webview-bridges.md)
- [User Guide — Continuous view](user-guide.md#continuous-transcript-view)
- [Architecture — Page integration](architecture.md#page-integration-layer)
- [Build & Deploy](build-and-deploy.md)
