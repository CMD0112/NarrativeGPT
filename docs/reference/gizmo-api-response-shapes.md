# Gizmo / Project API — Response Shapes

How ChatGPT project (gizmo) JSON is parsed into app models. The web API returns **inconsistent nesting** — `GizmoResponseParser` normalizes it.

**Models:** `ChatGptApiModels.cs` (`GizmoSummary`, `GizmoFileRef`, `GizmoConversationRef`, `ProjectSettingsDetail`)  
**Parser:** `GizmoResponseParser.cs` (gizmo/file shapes); `ChatGptProjectApiService.ParseProjectSettings` (settings UI)

*Last synced with code: 2026-07-03.*

---

## Normalized app models

### GizmoSummary

| Property | Source JSON fields | Notes |
|----------|-------------------|-------|
| `Id` | `id` on gizmo node | Required; parse fails without it |
| `Title` | `display.name`, then `name` | Fallback: `"Project"` |
| `Instructions` | `instructions` | Project system instructions |
| `Files` | See file arrays below | Deduped list of `GizmoFileRef` |

### GizmoFileRef

| Property | Source JSON fields | Notes |
|----------|-------------------|-------|
| `FileId` | `file_id`, then `id` | `id` accepted only if looks like `file_*` or `file-*` |
| `Name` | `name` | Fallback: `FileId` |
| `Location` | `location`, `uri`, `asset_pointer` | Default from `DefaultUpsertFileLocation` if absent |
| `Size` | `size`, `bytes`, `size_bytes` | Optional |
| `FromLibraryUpload` | set by upload path | True for `FilesLibraryUpload` responses |

### GizmoConversationRef

| Property | Typical source |
|----------|----------------|
| `Id` | conversation `id` |
| `Title` | `title` |
| `UpdatedAt` | `update_time` / timestamp fields |

Parsed in `ChatGptProjectApiService` conversation list handlers (not `GizmoResponseParser`).

### ProjectSettingsDetail

Returned by `GetProjectSettingsAsync` and `UpdateProjectSettingsAsync` (`GET` / `PATCH` `/backend-api/projects/{id}`).

| Property | PATCH request field | Source JSON (GET / PATCH response) |
|----------|---------------------|-------------------------------------|
| `ProjectId` | — | `id` on gizmo node, or request gizmo id |
| `Name` | `name` | `name` (flat) or `display.name` (wrapped) |
| `Instructions` | `instructions` | `instructions` |
| `Emoji` | `emoji` (optional) | `emoji` or `display.emoji` |
| `Theme` | `theme` (optional) | `theme` or `display.theme` (hex color) |

PATCH responses often use **Pattern D** (`resource.gizmo` with inner `gizmo` + `display`). Flat `name` / `instructions` / `emoji` / `theme` at the top level are also accepted on GET when returned that way.

---

## Gizmo nesting patterns

ChatGPT wraps project objects inconsistently. The parser handles all of these:

### Pattern A — double wrap (sidebar common)

```json
{
  "gizmo": {
    "gizmo": {
      "id": "g-…",
      "display": { "name": "My Adventure" },
      "instructions": "…"
    },
    "files": [ … ]
  }
}
```

Resolution: inner `gizmo` node for id/title/instructions; outer `gizmo` for file arrays.

### Pattern B — single wrap

```json
{
  "gizmo": {
    "id": "g-…",
    "name": "My Adventure",
    "files": [ … ]
  }
}
```

### Pattern C — flat item

```json
{
  "id": "g-…",
  "display": { "name": "My Adventure" },
  "training_files": [ … ]
}
```

### Pattern D — upsert response

```json
{
  "resource": {
    "gizmo": {
      "gizmo": { "id": "g-…", … }
    }
  }
}
```

`TryExtractUpsertGizmoId` and `TryParseGizmoFromUpsert` read `resource.gizmo` with the same inner/outer rules.

---

## File array property names

The parser scans these array properties on gizmo nodes (and nested `version` / `latest_version` objects):

| Property | Typical role |
|----------|--------------|
| `files` | Primary project sources |
| `training_files` | Training/knowledge attachments |
| `knowledge_files` | Knowledge base files |
| `file_requirements` | Required file refs |
| `resources` | Generic resource list |

Arrays may appear at multiple nesting levels; duplicates are merged by `FileId`.

### Single file object shape

```json
{
  "file_id": "file-…",
  "name": "lore/world.md",
  "size": 4096,
  "location": "fs"
}
```

Alternate id field:

```json
{
  "id": "file-…",
  "name": "character.md"
}
```

Nested file wrapper:

```json
{
  "file": {
    "file_id": "file-…",
    "text": "inline content …"
  }
}
```

---

## Bootstrap response

`ParseBootstrapGizmos` reads top-level arrays:

| Property | Content |
|----------|---------|
| `gizmos` | Project items |
| `items` | Alternate list key |
| `resources` | Resource entries |

Additionally walks the full JSON tree (depth ≤ 12) for any object with an `id` field that parses as a gizmo.

Results are **deduped by gizmo id** (last write wins).

---

## Sidebar response

`ParseSidebarItems` iterates the sidebar `items` array and calls `TryParseSidebarItem` per element. Malformed items are skipped with `ProjectLinkDiagnostics` log entries.

---

## Inline content extraction

When download paths fail, the parser can extract content embedded in API responses.

### Inline text / bytes

`TryExtractInlineFileContent` walks the tree for a matching `file_id` / `id` and reads:

| Property | Encoding |
|----------|----------|
| `text`, `content`, `source`, `body` | UTF-8 string |
| `base64`, `data` | Base64 decode |

### Inline download URL

`TryExtractInlineDownloadPath` reads `download_url`, `url`, or `href` on the matching file node. Only same-origin `/backend-api/…` paths or `chatgpt.com` absolute URLs are accepted (`NormalizeSameOriginDownloadPath`).

---

## Deep file discovery

`CollectFileRefsDeep` walks arbitrary JSON (e.g. conversation trees, probe responses) up to depth 12 and collects objects that look like upload file refs (`file_*` / `file-*` ids).

Used when file lists are buried in non-standard response shapes.

---

## Title resolution order

1. `display.name` on gizmo node
2. `display.name` on files context (outer wrap)
3. `name` on gizmo node
4. `name` on files context
5. Fallback: `"Project"`

---

## Location resolution order

For each file object:

1. `location` string property
2. `uri`
3. `asset_pointer`
4. `ChatGptProjectApiService.DefaultUpsertFileLocation` constant

`location: "fs"` triggers project-first download path ordering in `BuildFileDownloadPathCandidates`.

---

## Mapping to local manifest

Remote file refs become `SourceManifestEntry` fields during sync:

| GizmoFileRef | SourceManifestEntry |
|--------------|---------------------|
| `FileId` | `remoteFileId` |
| `Name` | matched to `relativePath` under `sources/` |
| `Size` | optional verification |
| `Location` | informs download candidate selection |

See [data-model-reference.md](data-model-reference.md#sourcemanifest) and [user-projects-and-sync.md](../user/user-projects-and-sync.md).

---

## Related

- [chatgpt-api-endpoints-reference.md](chatgpt-api-endpoints-reference.md)
- [api-and-data-models-index.md](api-and-data-models-index.md)
- [chatgpt-api-integration.md](../developer/chatgpt-api-integration.md)
- [data-model-audit-cmd86.md](data-model-audit-cmd86.md)
