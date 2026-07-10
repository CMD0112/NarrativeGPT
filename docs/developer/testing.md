# Testing (agents & developers)

ChatGPT Wrapper tests live in `tests/ChatGPTWrapper.ApiDiagnostics`. **Agents drafting new tests must follow the logged + file-lock paradigm below.**

Run:

```powershell
.\tests\ChatGPTWrapper.ApiDiagnostics\scripts\run-tests.ps1
```

## Tiers

| Tier | Filter | Notes |
|------|--------|-------|
| Unit | `Category=Unit` | Default CI |
| Integration | `Category=Integration` | WebView2 harness (e.g. Play compose) |
| Live | `Category=Live` | Requires `CGW_RUN_LIVE_API_TESTS=1` + login |
| Performance | `Category=Performance` | Report-only; opt-in |

## File-lock paradigm

- `xunit.runner.json` — no parallel assembly/collections
- `FileLockGate` — serializes appdata; WebView profile mutex for live tests
- `FileLockAwareCollection` — shared collection for disk-backed tests

**Do not** set `AppDirectories.TestRootOverride` manually without `AppDirectories.ResetStoresForTests()` and an isolated root.

## Logged test paradigm (required for flow/disk tests)

Production observability is JSONL diagnostics (`wrapper-diagnostics.jsonl`, `play-send-trace.jsonl`, `sync-trace.jsonl`). Tests should mirror `--extended-diagnostics` and assert traces where relevant.

### Choose a harness

| Scenario | Use |
|----------|-----|
| Send/sync/orchestration/persistence | **`LoggedTestBase`** |
| Explicit session control | **`DiagnosticTestSession.Enter(typeof(MyTests))`** + `IDisposable` |
| Existing `IClassFixture` style | **`[Collection(FileLockAwareCollectionNames.Name)]`** + **`IClassFixture<FileLockAwareFixture>`** (`fixture.Traces`) |
| Pure functions, no I/O | Plain test class, no fixture |
| Testing non-extended log modes | `DiagnosticTestSession` + per-test `DiagnosticsOptions.ResetForTests()` and delete trace files |

### Trace assertions

```csharp
Session.ReloadTraces();
Traces.PlaySend.Sequence("send_run_start", "packet_prepared", "send_run_end");
Traces.Unified.ContainsEvent("session_start", channel: "program");
Traces.Unified.NoErrors();
```

Helpers: `DiagnosticTraceReader`, `DiagnosticTraceAssert`, `DiagnosticTraceBundle.FormatFailureDigest()`.

### Tags

- `[Trait("Category", "Unit")]` (or Integration/Live/Performance)
- `[Trait("Diagnostics", "Logged")]` when using extended diagnostics or trace assertions

### Environment

| Variable | Default | Purpose |
|----------|---------|---------|
| `CGW_TEST_EXTENDED_DIAGNOSTICS` | `1` in `run-tests.ps1` | Extended JSONL in tests |
| `CGW_TEST_PRESERVE_LOGS` | off | Copy artifacts to `%TEMP%\cgw-test-artifacts\{class}\` on dispose |

Filter logged tests: `--filter "Diagnostics=Logged"`.

## Reference tests

| File | Demonstrates |
|------|----------------|
| `Unit/DiagnosticTestParadigmTests.cs` | Session, sequence assert, failure digest |
| `Unit/DiagnosticsLogTests.cs` | Extended vs standard modes |
| `Unit/PlaySendTraceTests.cs` | Play-send JSONL assertions |

## Agent checklist (new test file)

1. Classify: pure logic vs disk/trace vs live WebView
2. Apply harness from table above — **no** ad-hoc `%TEMP%` roots
3. Add traits (`Category`, `Diagnostics=Logged` if applicable)
4. After act: `ReloadTraces()` + assert expected events / `NoErrors()`
5. Run `dotnet test` with a narrow filter on the new class

## Troubleshooting

- **File lock on build** — stop `testhost` / `ChatGPT Wrapper`; use `run-tests.ps1`
- **Stale traces** — call `ReloadTraces()` before assert; delete trace files when testing non-extended mode
- **Live WebView** — one `LiveWebView` collection at a time; close diagnostic windows

See also [tests/ChatGPTWrapper.ApiDiagnostics/README.md](../../tests/ChatGPTWrapper.ApiDiagnostics/README.md).
