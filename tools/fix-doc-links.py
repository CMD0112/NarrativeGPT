#!/usr/bin/env python3
"""Fix markdown links after docs/ subdirectory reorganization."""
from __future__ import annotations

import os
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"

RELOC: dict[str, str] = {
    "adventure-panel.md": "user/adventure-panel.md",
    "user-guide.md": "user/user-guide.md",
    "user-projects-and-sync.md": "user/user-projects-and-sync.md",
    "troubleshooting.md": "user/troubleshooting.md",
    "instruction-contract-guide.md": "user/instruction-contract-guide.md",
    "instruction-sources-paradigm.md": "user/instruction-sources-paradigm.md",
    "instruction-channels.md": "user/instruction-channels.md",
    "narrator-settings.md": "user/narrator-settings.md",
    "prompt-construction-guide.md": "user/prompt-construction-guide.md",
    "entity-canon-change-paradigm.md": "user/entity-canon-change-paradigm.md",
    "architecture.md": "developer/architecture.md",
    "adventure-developer-reference.md": "developer/adventure-developer-reference.md",
    "build-and-deploy.md": "developer/build-and-deploy.md",
    "testing.md": "developer/testing.md",
    "webview-bridges.md": "developer/webview-bridges.md",
    "chatgpt-api-integration.md": "developer/chatgpt-api-integration.md",
    "injected-assets.md": "developer/injected-assets.md",
    "utility-job-orchestration.md": "developer/utility-job-orchestration.md",
    "data-model-reference.md": "reference/data-model-reference.md",
    "data-model-audit-cmd86.md": "reference/data-model-audit-cmd86.md",
    "services-reference.md": "reference/services-reference.md",
    "ui-components.md": "reference/ui-components.md",
    "adventure-thread-registry.md": "reference/adventure-thread-registry.md",
    "canon-schema.md": "reference/canon-schema.md",
    "appearance-theme-settings.md": "settings/appearance-theme-settings.md",
    "settings-interactables-inventory.md": "settings/settings-interactables-inventory.md",
    "settings-interactables-audit.md": "settings/settings-interactables-audit.md",
    "settings-ux-taxonomy.md": "settings/settings-ux-taxonomy.md",
    "play-design-surface-convergence-adr.md": "adr/play-design-surface-convergence-adr.md",
    "play-send-orchestration-adr.md": "adr/play-send-orchestration-adr.md",
    "injection-policy-adr.md": "adr/injection-policy-adr.md",
    "narrator-revision-adr.md": "adr/narrator-revision-adr.md",
    "utility-job-context-assembly-adr.md": "adr/utility-job-context-assembly-adr.md",
    "play-thread-utility-orchestration-adr.md": "adr/play-thread-utility-orchestration-adr.md",
    "local-semantic-retrieval-adr.md": "adr/local-semantic-retrieval-adr.md",
    "user-message-edit-adr.md": "adr/user-message-edit-adr.md",
    "utility-worker-lane-adr.md": "adr/utility-worker-lane-adr.md",
    "utility-delivery-pivot-adr.md": "adr/utility-delivery-pivot-adr.md",
    "narrative-flight-recorder-adr.md": "adr/narrative-flight-recorder-adr.md",
    "play-thread-canonical-adr.md": "adr/play-thread-canonical-adr.md",
    "play-send-orchestration-implementation-plan.md": "plans/play-send-orchestration-implementation-plan.md",
    "play-thread-utility-orchestration-plan.md": "plans/play-thread-utility-orchestration-plan.md",
    "injection-policy-implementation-plan.md": "plans/injection-policy-implementation-plan.md",
    "utility-worker-lane-plan.md": "plans/utility-worker-lane-plan.md",
    "play-message-edit-refinement-plan.md": "plans/play-message-edit-refinement-plan.md",
    "runtime-canon-schema-plan.md": "plans/runtime-canon-schema-plan.md",
    "linear-issue-reference.md": "linear/linear-issue-reference.md",
    "linear-integration.md": "linear/linear-integration.md",
    "chat-file-io-feasibility.md": "Enhancements/chat-file-io-feasibility.md",
}

# basename -> absolute path under docs/
DOC_INDEX: dict[str, Path] = {}


def build_index() -> None:
    for path in DOCS.rglob("*.md"):
        rel = path.relative_to(DOCS).as_posix()
        name = path.name
        if name not in DOC_INDEX:
            DOC_INDEX[name] = path
        # prefer explicit reloc target for relocated files
    for old, new in RELOC.items():
        DOC_INDEX[old] = DOCS / new


def resolve_target(link_path: str) -> Path | None:
    if not link_path or link_path.startswith(("http://", "https://", "linear://", "mailto:")):
        return None
    if link_path.startswith("../") or link_path.startswith("ChatGPTWrapper/") or link_path.startswith(".github/"):
        return None
    if "/" in link_path:
        if link_path.startswith(("Enhancements/", "plans/", "user/", "developer/", "reference/", "settings/", "adr/", "linear/")):
            candidate = DOCS / link_path
            return candidate if candidate.exists() else None
        return None
    if link_path in DOC_INDEX:
        return DOC_INDEX[link_path]
    for sub in ("Enhancements", "plans"):
        candidate = DOCS / sub / link_path
        if candidate.exists():
            return candidate
    if link_path == "INDEX.md":
        return DOCS / "INDEX.md"
    return None


LINK_RE = re.compile(r"\]\(([^)\s]+)\)")


def fix_file(path: Path) -> bool:
    text = path.read_text(encoding="utf-8")
    changed = False

    def repl(match: re.Match[str]) -> str:
        nonlocal changed
        full = match.group(1)
        if full == "":
            return match.group(0)
        hash_idx = full.find("#")
        path_part = full[:hash_idx] if hash_idx >= 0 else full
        anchor = full[hash_idx:] if hash_idx >= 0 else ""
        target = resolve_target(path_part)
        if target is None:
            return match.group(0)
        rel = os.path.relpath(target, path.parent).replace("\\", "/")
        changed = True
        return f"]({rel}{anchor})"

    new_text = LINK_RE.sub(repl, text)
    if changed:
        path.write_text(new_text, encoding="utf-8")
    return changed


def global_docs_path_replace(text: str) -> str:
    for old, new in RELOC.items():
        text = text.replace(f"docs/{old}", f"docs/{new}")
    return text


def main() -> None:
    build_index()
    backup = ROOT / "docs-backup-20260629-184711"
    # Restore content from backup for relocated files (good links, audit content)
    if backup.exists():
        for old, new in RELOC.items():
            src = backup / old
            dst = DOCS / new
            if src.exists() and dst.parent.exists():
                dst.write_text(src.read_text(encoding="utf-8"), encoding="utf-8")

    # New enhancement files not in backup — keep current if present
    for extra in (
        "Enhancements/local-generative-assist-use-cases.md",
        "Enhancements/local-inference-quality-guide.md",
        "Enhancements/utility-inference-routing-tracker.md",
    ):
        p = DOCS / extra
        if not p.exists():
            backup_p = ROOT / "docs" / extra
            # already on disk from prior session

    # Global docs/ path updates across repo
    for ext in (".md", ".mdc", ".cs", ".xaml", ".json", ".py", ".yml", ".yaml", ".ps1"):
        for f in ROOT.rglob(f"*{ext}"):
            if "docs-backup-" in f.as_posix() or ".git" in f.parts or "node_modules" in f.parts:
                continue
            try:
                content = f.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            updated = global_docs_path_replace(content)
            if updated != content:
                f.write_text(updated, encoding="utf-8")

    # Fix relative links in all docs markdown
    fixed = 0
    for md in DOCS.rglob("*.md"):
        if fix_file(md):
            fixed += 1
    print(f"Fixed relative links in {fixed} files.")


if __name__ == "__main__":
    main()
