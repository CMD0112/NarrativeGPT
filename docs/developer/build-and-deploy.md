# Build & Deploy

ChatGPT Wrapper is a **Windows desktop WPF** application. There is no Docker, cloud deploy, or Linux target in this repository.

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| [.NET 9 SDK](https://dotnet.microsoft.com/download) | `net9.0-windows` target |
| [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) | Usually installed with Edge; required at runtime |
| Windows 10/11 x64 | WPF + WebView2 |

---

## Build commands

### Main application

```powershell
# From your clone root:
dotnet build ChatGPTWrapper\ChatGPTWrapper.csproj
```

### Full solution

```powershell
dotnet build chatgpt-wrapper.sln
```

Builds all four projects: main app, Core, SessionHost stub, ApiDiagnostics tests.

### Run (development)

```powershell
dotnet run --project ChatGPTWrapper\ChatGPTWrapper.csproj
```

First run creates `%LocalAppData%\ChatGPTWrapper\` directories via `AppDirectories.EnsureCreated()`.

---

## Output layout (dev build)

```
ChatGPTWrapper\bin\Debug\net9.0-windows\
├── ChatGPT Wrapper.exe
├── ChatGPT Wrapper.dll
├── Microsoft.Web.WebView2.*.dll
└── wrapper-assets\          # Copied from ChatGPT_files\
    ├── chatgpt-api-bridge.js
    ├── adventure-bridge.js
    └── ...
```

---

## Portable distribution

Script: `ChatGPTWrapper\publish-distributable.ps1`

```powershell
.\ChatGPTWrapper\publish-distributable.ps1
```

### What it does

1. `dotnet publish` — **self-contained** `win-x64`, Release
2. Output folder: `ChatGPTWrapper\dist\ChatGPT-Wrapper-windows-x64\`
3. Removes `createdump.exe` from output
4. Writes `README.txt` with WebView2 requirement note
5. Creates `ChatGPTWrapper\dist\ChatGPT-Wrapper-windows-x64.zip`

### Publish properties

| Setting | Value |
|---------|-------|
| `-r win-x64` | 64-bit Windows |
| `--self-contained true` | Bundles .NET runtime |
| `PublishTrimmed` | `false` |
| PDB/XML | Stripped in `PrepareShareablePublish` target |

### End-user requirements

- Windows 10/11 x64
- WebView2 Runtime (Edge usually provides it)
- **No separate .NET install** needed (self-contained)

---

## Project references

```
ChatGPTWrapper
  └── ChatGPTWrapper.Core
  └── Microsoft.Web.WebView2 1.0.3912.50

ChatGPTWrapper.SessionHost
  └── ChatGPTWrapper.Core     (standalone, not used by main app)

ChatGPTWrapper.ApiDiagnostics
  └── ChatGPTWrapper
  └── xUnit, Microsoft.NET.Test.Sdk
```

---

## SessionHost stub

`ChatGPTWrapper.SessionHost` builds as a console exe for future **out-of-process** WebView isolation.

```powershell
dotnet run --project ChatGPTWrapper.SessionHost\ChatGPTWrapper.SessionHost.csproj
```

Listens on named pipe `ChatGPTWrapper.SessionHost`. Currently returns `oop_host_not_configured` for all RPC methods.

Main app uses in-process `ChatGptSessionHost` instead.

---

## CI/CD

**Not configured.** No `.github/workflows/`, Azure Pipelines, or similar.

Recommended: see [Testing — Recommended CI](testing.md#recommended-ci-not-yet-configured).

---

## Versioning

`ChatGPTWrapper.csproj`: `<Version>1.0.0</Version>`

Assembly name: `ChatGPT Wrapper` (space in exe name).

---

## Data paths (runtime)

Not part of build output — created on first run:

`%LocalAppData%\ChatGPTWrapper\`

See [Data Model Reference](../reference/data-model-reference.md#on-disk-layout).

---

## Related documentation

- [README](../README.md) — quick start
- [Testing](testing.md)
- [Architecture](architecture.md)
- [Injected Assets](injected-assets.md) — build copy of `ChatGPT_files/`
