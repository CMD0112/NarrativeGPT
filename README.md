# ChatGPT Wrapper

Desktop wrapper for [ChatGPT](https://chatgpt.com) using .NET 9 WPF and Microsoft WebView2.

## Documentation

**Full documentation:** [docs/INDEX.md](docs/INDEX.md)

- **Users:** [user guide](docs/user-guide.md) · [Adventures](docs/adventure-panel.md) · [Projects & sync](docs/user-projects-and-sync.md) · [Instruction contract guide](docs/instruction-contract-guide.md) · [troubleshooting](docs/troubleshooting.md)
- **Developers:** [architecture](docs/architecture.md) · [utility jobs](docs/utility-job-orchestration.md) · [bridges](docs/webview-bridges.md) · [data models](docs/data-model-reference.md) · [services](docs/services-reference.md) · [testing](docs/testing.md)

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually installed with Microsoft Edge)

## Build and run

```powershell
cd E:\Documents\Code\chatgpt-wrapper
dotnet build ChatGPTWrapper\ChatGPTWrapper.csproj
dotnet run --project ChatGPTWrapper\ChatGPTWrapper.csproj
```

## Portable distribution

```powershell
.\ChatGPTWrapper\publish-distributable.ps1
```

Output: `ChatGPTWrapper\dist\ChatGPT-Wrapper-windows-x64\` and a `.zip` alongside it.

## Adventures (AI Dungeon-style)

Use **Adventures** in the toolbar for local-first interactive fiction with optional ChatGPT Project linking.

- State under `%LocalAppData%\ChatGPTWrapper\adventures\`
- Play automation via WebView2 (`adventure-bridge.js`)
- Export, libraries, search, response review, branching, and generation jobs

**Docs:** [Adventure Panel Reference](docs/adventure-panel.md) · [Projects & sync](docs/user-projects-and-sync.md) · [Phased roadmap](docs/AI-DUNGEON-PHASED-PLAN.md)

**Quick smoke tests:** Phase 1 (play loop) and Phase 2 (Projects API) checklists are in [docs/user-projects-and-sync.md](docs/user-projects-and-sync.md) and [docs/adventure-panel.md](docs/adventure-panel.md#manual-play-mode-smoke-checklist).

## Data location

App data (WebView2 profile, optional user CSS, adventures) is stored under:

`%LocalAppData%\ChatGPTWrapper\`

This matches the folder name used by the reference build in `cursor-wrapper`, so cookies and settings can carry over if you used that project before.

## Custom CSS

Bundled overrides ship in `ChatGPT_files/wrapper-overrides.css` and are copied to `wrapper-assets\` at build time. For changes that survive app updates, add:

`%LocalAppData%\ChatGPTWrapper\styles\user-overrides.css`
