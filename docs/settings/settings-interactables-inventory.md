# Settings & Interactables Inventory

Master catalog of user-facing settings, toggles, menus, dialogs, and in-session controls across the ChatGPT Wrapper. Produced for [CMD-255](https://linear.app/cmd0112/issue/CMD-255) under epic [CMD-254](https://linear.app/cmd0112/issue/CMD-254).

**Related:** [Settings UX taxonomy](settings-ux-taxonomy.md) · [Audit findings](settings-interactables-audit.md) · [UI Components](../reference/ui-components.md) · [Appearance & theme](appearance-theme-settings.md)

---

## Column legend

| Column | Meaning |
|--------|---------|
| **Control** | User-visible label or action |
| **Surface** | Browse / Adventures / Play / Design / Global |
| **Entry path** | How the user reaches it |
| **Owner** | View or dialog |
| **Persistence** | Store and JSON key(s) |
| **Scope** | global / wrapper / adventure / session / send / per-mode |
| **Status** | active / legacy / duplicate / dead |

---

## Persistence map

| Store | File | Scope |
|-------|------|-------|
| `UiChromeStore` | `%LocalAppData%\ChatGPTWrapper\ui-chrome.json` | Global wrapper chrome |
| `ThemeSettings` | nested in `ui-chrome.json` → `theme` | Global shell appearance |
| `TranscriptViewModeSettings` | `nativeSettings`, `continuousSettings`, `weaveSettings` | Per transcript mode |
| `ContinuousViewFormatSettings` | nested in each mode bucket | Per-mode format |
| `FormatProfile` | `formatProfiles[]`, `activeFormatProfileId` | Global format library |
| `WrapperSettingsStore` | `wrapper-settings.json` | Global paths |
| `AdventureSettings` | `{adventure}/adventure.json` → `settings` | Per adventure |
| `PlayTurnOverrides` | `adventure.json` → `settings.playTurnOverrides` | Next send only |
| `SessionNarratorOverrides` | `adventure.json` → `settings.sessionNarratorOverrides` | Per play session |
| `UtilityJobOverrides` | `adventure.json` → `settings.utilityJobOverrides` | Per job |
| WebView `localStorage` | per WebView profile | Session-only UI flags |

---

## 1. Global shell (MainWindow)

| Control | Surface | Entry path | Owner | Persistence | Scope | Status |
|---------|---------|------------|-------|-------------|-------|--------|
| Browse mode | Browse | Toolbar pill | MainWindow | — | — | active |
| Adventures mode | Adventures | Toolbar pill | MainWindow | — | — | active |
| Play pill | Play | Shell context bar | MainWindow | — | — | active |
| Design pill | Design | Shell context bar | MainWindow | — | — | active |
| Native transcript | Global | View menu | MainWindow | `ui-chrome.json` → `transcriptViewMode` | global | active |
| Continuous transcript | Global | View menu | MainWindow | same | global | active |
| Weave transcript | Global | View menu | MainWindow | same | global | active |
| Format… | Global | View menu | ContinuousViewFormatDialog | `ui-chrome.json` (per-mode buckets) | per-mode | active |
| Preferences… | Global | ⋯ overflow menu | PreferencesHubDialog | routes to child dialogs | global | active |
| New tab | Browse | Tab strip | MainWindow | — | — | active |
| Reload tab | Browse | Tab strip | MainWindow | — | — | active |
| Bridge status dot | Play/Design | Status bar click | PlayPromptInjectionDialog (Session) | — | — | active |
| Link status text | Play/Design | Status bar click | SourceManagerDialog | — | — | active |

---

## 2. Preferences hub

| Control | Surface | Entry path | Owner | Persistence | Scope | Status |
|---------|---------|------------|-------|-------------|-------|--------|
| Reading mode summary | Global | Preferences hub | PreferencesHubDialog | display only | global | active |
| Appearance & theme… | Global | Preferences hub | ThemeCustomizationDialog | `ui-chrome.json` → `theme` | global | active |
| Reading & format… | Global | Preferences hub / View → Format… | ContinuousViewFormatDialog | `ui-chrome.json` | per-mode | active |
| Storage & paths… | Global | Preferences hub | WrapperSettingsDialog | `wrapper-settings.json` | wrapper | active |
| Play settings shortcuts | Play/Design | Preferences hub (active adventure) | PlayPromptInjectionDialog | adventure.json | adventure | active |
| Jump: Behavior / Layout / Sources / Session | Play/Design | Preferences hub | PlayPromptInjectionDialog tabs | adventure.json | adventure/session | active |

---

## 3. Wrapper settings

| Control | Surface | Entry path | Owner | Persistence | Scope | Status |
|---------|---------|------------|-------|-------------|-------|--------|
| Adventures folder path | Global | Preferences → Wrapper settings | WrapperSettingsDialog | `wrapper-settings.json` → `adventuresDirectoryOverride` | wrapper | active |
| Browse… | Global | Wrapper settings | WrapperSettingsDialog | same | wrapper | active |
| Default | Global | Wrapper settings | WrapperSettingsDialog | same | wrapper | active |

---

## 4. Appearance & theme (ThemeCustomizationDialog)

| Control | Surface | Entry path | Owner | Persistence | Scope | Status |
|---------|---------|------------|-------|-------------|-------|--------|
| Preset search/filter | Global | Appearance & theme | ThemeCustomizationDialog | `theme.activePresetId`, `userPresets[]` | global | active |
| Built-in presets | Global | Presets tab | ThemeCustomizationDialog | same | global | active |
| User presets CRUD | Global | Presets tab | ThemeCustomizationDialog | same | global | active |
| Semantic color tokens | Global | Colors tab | ThemeCustomizationDialog | `theme.customOverrides` | global | active |
| Theme color picker (Pick…) | Global | Colors tab | ThemeColorPickerDialog via ColorPickerWorkflow | same + `recentPickerColors[]` | global | active |
| Shell typography | Global | Typography tab | ThemeCustomizationDialog | `theme.fontFamily`, sizes | global | active |
| Spacing & shape | Global | Spacing tab | ThemeCustomizationDialog | `theme.space*`, `radius*` | global | active |
| Import/export theme JSON | Global | Advanced tab | ThemeCustomizationDialog | `ui-chrome.json` | global | active |
| Open user-overrides.css | Global | Advanced tab | ThemeCustomizationDialog | `%LocalAppData%\...\styles\` | global | active |

---

## 5. Format dialog (ContinuousViewFormatDialog)

| Control | Surface | Entry path | Owner | Persistence | Scope | Status |
|---------|---------|------------|-------|-------------|-------|--------|
| Format profile picker | Global | Format dialog header | ContinuousViewFormatDialog | `activeFormatProfileId`, `formatProfiles[]` | per-mode | active |
| Compact/Default/Relaxed presets | Global | Reading layout | ContinuousViewFormatDialog | `continuousViewFormat` | per-mode | active |
| User/assistant typography sliders | Global | Your/Assistant messages | ContinuousViewFormatDialog | `continuousViewFormat.*` | per-mode | active |
| Font family pickers | Global | Typography sections | ContinuousViewFormatDialog | `*FontFamily` fields | per-mode | active |
| Code & heading fonts | Global | Code & headings | ContinuousViewFormatDialog | `codeFontFamily`, `headingFontFamily` | per-mode | active |
| Browse system font | Global | Font family custom | FormatSystemFontPickerWindow | custom stack string | per-mode | active |
| Named font weights | Global | Font weight combos | ContinuousViewFormatDialog | `*FontWeight` | per-mode | active |
| Color pickers (role tokens) | Global | Colors tab / Pick… | ThemeColorPickerDialog via ColorPickerWorkflow | `*Color` fields; `recentPickerColors[]` in chrome | per-mode + global recent | active |
| Thread display toggles | Global | Thread display | ContinuousViewFormatDialog | per-mode bucket flags | per-mode | active |
| Phrase highlights editor | Global | Highlights tab | PhraseHighlightsEditorControl | `phraseHighlightRules[]` | per-mode | active |
| Format preview phrase sample | Global | Format dialog preview | FormatPreviewControl | live rules from Highlights tab | per-mode | active |
| Entity phrase highlight card | Adventure | EntityEditDialog / EntityEditFormHost | `phraseHighlightRules[]` via chrome | per-mode | active |
| Advanced numeric bounds | Global | Advanced | ContinuousViewFormatDialog | `allowFormatValuesOutsideRecommendedRange` | per-mode | active |
| Import/export format JSON | Global | Advanced | ContinuousViewFormatDialog | profiles / working copy | per-mode | active |

---

## 6. Play settings dialog (PlayPromptInjectionDialog)

**Entry paths:** Play header **Play settings…**; Preferences hub **Play session settings…**; Session expander **Session…**; status bar bridge dot; review routing.

| Tab (XAML) | Control (representative) | Persistence key | Scope | Status |
|------------|--------------------------|-----------------|-------|--------|
| **Injection** | Preset (Compact/Standard/Full/Custom) | `injectionPolicy.injectionPresetId`, preset fields | adventure | active |
| **Injection** | Section includes (summary, state, memory, transcript, cards, sources, attachment guidance) | `injectionPolicy.*` | adventure | active |
| **Injection** | Max packet slider | `maxPacketChars` | adventure | active |
| **Injection** | Transcript max turns | `injectionPolicy.transcriptMaxTurns` | adventure | active |
| **Injection** | Use context tags / section injection v2 | `useContextTags`, `useSectionInjection` | adventure | active |
| **Injection** | Live preview panel | — | send | active |
| **Play packet** | Continuation queue | runtime + adventure | send | active |
| **Next send** | Fallback player line | adventure | send | active |
| **Next send** | Turn overrides (length, detail, tone) | `playTurnOverrides` | send | active |
| **Next send** | Packet preview / View full | — | send | active |
| **World** | Summary, location, objectives | `summary.json`, scenario | adventure | active |
| **World** | Author's note | `scenario.json` | adventure | active |
| **AI Actions** | Per-job overrides | `utilityJobOverrides` | adventure | active |
| **AI Actions** | Run job buttons | — | session | active |
| **Session** | Utility sessions | `utilitySessions` | session | active |
| **Session** | Tab pins | thread registry | session | active |
| **Session** | Utility delivery / peek | `hideInlineUtilityDuringPlay`, `showInlineUtilityTraffic` | session | active |
| **Play surface** | Layout presets (Writer/GM/Minimal) | `playLayoutPresetId` | adventure | active |
| **Play surface** | Tab placement (Ref/State/Warnings/Notes) | `playTabPlacement` | adventure | active |
| **Play surface** | Quick action visibility | `playSurfaceActions` | adventure | active |
| **Settings** | Perspective, tense, detail, tone, difficulty, violence | `adventure.json` settings | adventure | active |
| **Settings** | Content boundaries, portrayal rules | settings lists | adventure | active |
| **Settings** | Max packet characters | `maxPacketChars` | adventure | active |
| **Settings** | Enable WebView automation | `adventureAutomationEnabled` | adventure | active |
| **Settings** | Prefer DOM composer send | `preferDomPlaySend` | adventure | active |
| **Settings** | Use custom wrapper composer | `useWrapperComposer` | adventure | **legacy** |
| **Settings** | Force fat packets | `forceFatPackets` | adventure | active |
| **Settings** | Use context tags | `useContextTags` | adventure | active |
| **Settings** | Auto-* toggles (entities, memory, summary, continuity) | `auto*` fields | adventure | active |
| **Settings** | Attachment context mode | `attachmentContextMode` | adventure | active |
| **Memory & cards** | Pinned memory, cards review | `memory.json`, `cards.json` | adventure | active |
| **Sources** | Published checkboxes | source manifest | adventure | active |
| **Sources** | Sync / compare shortcuts | — | adventure | duplicate |
| **Sources** | Instruction designer | — | adventure | active |
| **History** | Send history viewer | — | session | active |

---

## 7. Play in-session controls (AdventurePlayView)

| Control | Entry path | Owner | Persistence | Scope | Status |
|---------|------------|-------|-------------|-------|--------|
| Play settings… | Header | PlayPromptInjectionDialog | adventure.json | adventure | duplicate |
| Sources… | Header | SourceManagerDialog | — | adventure | active |
| Link Project / Link now | Header / banner | ProjectWorkspaceDialog | adventure.json | adventure | active |
| Rename / Continue design | More… | dialogs / mode switch | adventure.json | adventure | active |
| **Injection** (cockpit) | Preset combo, Summary/Transcript/Memory toggles | `injectionPolicy` | adventure | active |
| **Injection** (cockpit) | Live preview control | — | send | active |
| Narrator scene profiles | Session cockpit | in-panel | session overrides | session | active |
| Narrator inherit/preset combos | Session cockpit | in-panel | session/adventure/send | send/session | active |
| Advanced… (narrator) | Session cockpit | NarratorAdvancedDialog | turn overrides | send | duplicate |
| AI tools (Process, Memories, etc.) | Session cockpit | generation jobs | — | session | active |
| Reviews expander | Session cockpit | Play settings tabs | — | session | active |
| Reference / Warnings / State / Notes tabs | Side panel | tab hosts | `playTabPlacement` | adventure | active |
| Entity double-click | Reference tab | EntityEditDialog | entities.json | adventure | active |
| Search / Export | Footer | SearchDialog / export | — | session | active |
| More actions… menu | Footer | various dialogs | — | session | active |

---

## 8. Adventures dashboard

| Control | Entry path | Owner | Persistence | Status |
|---------|------------|-------|-------------|--------|
| New adventure | Toolbar New ▾ | ScenarioCreationDialog | creates adventure | active |
| Design with AI… | Toolbar / More / empty state | AdventureDesignWizard | — | active |
| Play | Toolbar / row / dbl-click | mode switch | — | active |
| Rename / Delete / Archive | Toolbar / context | AdventureRenameDialog / ops | adventure.json | active |
| Link Project… | More / context | ProjectWorkspaceDialog | adventure.json | active |
| Libraries… | More | LibrariesDialog | libraries store | active |
| Import backup / folder | More | file dialogs | — | active |
| Draft adventure framework… | More | generation job | sources/drafts | duplicate |
| Wrapper settings… / Storage settings… | More / footer | WrapperSettingsDialog | wrapper-settings.json | **duplicate** |
| Search / Sort / Show archived | Filter bar | in-view | — | active |

---

## 9. Design mode

| Control | Entry path | Owner | Persistence | Status |
|---------|------------|-------|-------------|--------|
| Wizard steps (Concept→Review) | Design view | AdventureDesignView | design workspace | active |
| Pipeline checklist | Design view | draft panel | sources | active |
| Instruction designer | Design sources | InstructionDesignerDialog | instructions | active |
| JSON import review | Design sources | JsonImportReviewDialog | entities/scenario | active |
| Cast / entity panel | Cast step | EntityReferencePanel | entities.json | active |
| Draft framework job | Design thread | generation job | sources/drafts | duplicate |

---

## 10. Project & source dialogs

| Dialog | Primary entry | Persistence | Status |
|--------|---------------|-------------|--------|
| ProjectWorkspaceDialog | Link Project | adventure.json link fields | active |
| SourceManagerDialog | Play Sources / status bar | source files + manifest | active |
| SourceSyncDialog | Source manager / Play settings | — | active |
| SourceCompareDialog | Source manager | — | active |
| InstructionDesignerDialog | Play/Design sources | adventure settings + snippet | active |
| SyncFromThreadDialog | Play More / drift prompt | log.json | active |

---

## 11. Utility & entity dialogs

| Dialog | Entry | Status |
|--------|-------|--------|
| EntityEditDialog | Reference double-click | active |
| EntityMergeDialog / RetireDialog / RenameWizardDialog | Reference context | active |
| PlayHandoffDialog | More actions | active |
| RecapDialog | AI tools | active |
| SearchDialog | Footer | active |
| RandomTableDialog | More actions | active |
| CanonInboxDialog | More actions | active |
| ContextViewerDialog | Packet preview | active |
| JsonImportReviewDialog | Design import | active |

---

## 12. Dead / legacy surfaces (no external entry)

| Surface | Owner | Notes | Status |
|---------|-------|-------|--------|
| Standalone phrase highlights | PhraseHighlightsDialog | Embedded in Format dialog | **dead** |
| Adventure settings shim | AdventureSettingsDialog | Forwards to Play settings | **dead** |
| Project link wizard | ProjectLinkWizard | Superseded by ProjectWorkspaceDialog | **dead** |
| Response review modal | ResponseReviewDialog | Debug/legacy | **dead** |
| Edit turn modal | EditTurnDialog | Story log legacy | **dead** |
| Legacy continuous checkbox | ui-chrome.json | `continuousViewEnabled` migrated | **legacy** |
| Separate utility thread delivery | adventure.json | Migrated to inline | **legacy** |
| ApiSync publish mode | adventure.json | Forced Manual | **legacy** |

---

## 13. Schema-only legacy fields (no UI)

| Field | File | Migration |
|-------|------|-----------|
| `useWrapperComposer` | adventure.json | UI removed; runtime ignores true |
| `continuousViewEnabled` | ui-chrome.json | Migrated to `transcriptViewMode` |
| `utilityDeliveryMode: SeparateThread` | adventure.json | Migrated to InlinePlayThread |
| `sourcePublishMode: ApiSync` | adventure.json | Forced Manual on save |

---

## Maintenance

Update this inventory when adding settings UI, new dialogs, or deprecating controls. Cross-check against [ui-components.md](../reference/ui-components.md) and [data-model-reference.md](../reference/data-model-reference.md).

**Audit recommendations:** [settings-interactables-audit.md](settings-interactables-audit.md)
