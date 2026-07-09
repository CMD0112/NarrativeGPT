# Adventure domain extraction (Phase 6 prep)

## Completed

- **`ChatGPTWrapper.Adventure.AdventureRootPaths`** — decouples `AdventureBundle.DirectoryPath` from WPF `AppDirectories`; WinUI registers a default resolver at startup; WPF dialogs register full resolver via `AdventurePathBootstrap`.
- **`ChatGPTWrapper` WPF project** — converted to **class library** (`OutputType=Library`); WinUI is the sole executable host.
- **`ChatGPTWrapper.WpfIsland`** — removed from solution (CMD-518).

## Remaining (follow-up)

Adventure **models**, **stores**, and most **services** still compile inside `ChatGPTWrapper/` (WPF library) because they depend on `AppDirectories`, `WrapperSettingsStore`, and WPF/WebView2 tab types. Next extraction slice:

1. Move `AppDirectories` + location stores to `ChatGPTWrapper.Shell` or `Core`.
2. Link-compile `Adventure/Stores` into `ChatGPTWrapper.Adventure`.
3. Split WPF-coupled services (`PlayTabPinService`, `ThreadTabBindingService`, utility worker hosts) into `ChatGPTWrapper/` UI adapters.

See [winui-shell-migration-adr.md](../adr/winui-shell-migration-adr.md) Phase 6 gate.
