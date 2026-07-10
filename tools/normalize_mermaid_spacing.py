#!/usr/bin/env python3
"""Add Mermaid spacing init blocks to repo documentation diagrams."""

from __future__ import annotations

import json
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DOCS_ROOT = REPO_ROOT / "docs"

MERMAID_BLOCK_RE = re.compile(r"```mermaid\n(.*?)\n```", re.DOTALL)
INIT_RE = re.compile(r"^\s*%%\{init:", re.MULTILINE)
SUBGRAPH_RE = re.compile(r"\bsubgraph\b", re.I)
PARTICIPANT_RE = re.compile(r"^\s*participant\b", re.I | re.MULTILINE)
LABEL_IN_BRACKETS_RE = re.compile(
    r"(\[[^\[\]<]{45,}\]|\([^()<]{45,}\))"
)
SUBGRAPH_TITLE_RE = re.compile(
    r"(subgraph\s+\w+\s*\[)([^\]]{45,})(\])", re.I
)
PARTICIPANT_ALIAS_RE = re.compile(
    r"^(\s*participant\s+\S+\s+as\s+)(.+)$", re.I | re.MULTILINE
)


def wrap_label_text(label: str) -> str:
    if len(label) <= 44 or "<br" in label:
        return label
    for sep in (" — ", " · ", " / ", ": ", " - "):
        if sep in label:
            left, right = label.split(sep, 1)
            if len(left) >= 8:
                return f"{left}<br/>{sep.strip() + ' ' if sep.strip() else ''}{right}".replace(
                    "<br/> ", "<br/>"
                )
    words = label.split()
    if len(words) >= 4:
        mid = len(words) // 2
        return " ".join(words[:mid]) + "<br/>" + " ".join(words[mid:])
    return label


def wrap_labels_in_block(block: str) -> str:
    updated = block

    def subgraph_repl(match: re.Match[str]) -> str:
        prefix, title, suffix = match.groups()
        return prefix + wrap_label_text(title) + suffix

    updated = SUBGRAPH_TITLE_RE.sub(subgraph_repl, updated)

    def bracket_repl(match: re.Match[str]) -> str:
        token = match.group(1)
        if token.startswith("[") and token.endswith("]"):
            inner = token[1:-1]
            return "[" + wrap_label_text(inner) + "]"
        if token.startswith("(") and token.endswith(")"):
            inner = token[1:-1]
            return "(" + wrap_label_text(inner) + ")"
        return token

    updated = LABEL_IN_BRACKETS_RE.sub(bracket_repl, updated)

    def participant_repl(match: re.Match[str]) -> str:
        prefix, alias = match.groups()
        return prefix + wrap_label_text(alias.strip())

    updated = PARTICIPANT_ALIAS_RE.sub(participant_repl, updated)
    return updated


def flowchart_init(subgraphs: int, size: int) -> str:
    if subgraphs >= 3 or size > 1500:
        cfg = {
            "flowchart": {
                "nodeSpacing": 58,
                "rankSpacing": 68,
                "padding": 20,
                "subGraphTitleMargin": 16,
                "diagramPadding": 12,
                "htmlLabels": True,
            },
            "themeVariables": {"fontSize": "12px"},
        }
    elif subgraphs >= 1:
        cfg = {
            "flowchart": {
                "nodeSpacing": 50,
                "rankSpacing": 56,
                "padding": 16,
                "subGraphTitleMargin": 12,
                "diagramPadding": 8,
                "htmlLabels": True,
            },
            "themeVariables": {"fontSize": "13px"},
        }
    else:
        cfg = {
            "flowchart": {
                "nodeSpacing": 42,
                "rankSpacing": 48,
                "padding": 12,
                "diagramPadding": 8,
                "htmlLabels": True,
            },
            "themeVariables": {"fontSize": "13px"},
        }
    return f"%%{{init: {json.dumps(cfg, separators=(',', ':'))} }}%%\n"


def sequence_init(participants: int) -> str:
    cfg = {
        "sequence": {
            "actorMargin": 70 if participants >= 7 else 58,
            "boxMargin": 12,
            "messageMargin": 42,
            "mirrorActors": False,
            "useMaxWidth": True,
            "wrap": True,
        },
        "themeVariables": {"fontSize": "13px"},
    }
    return f"%%{{init: {json.dumps(cfg, separators=(',', ':'))} }}%%\n"


def infer_init(block: str) -> str | None:
    stripped = block.strip()
    if not stripped or INIT_RE.search(stripped):
        return None

    first = next((ln.strip() for ln in stripped.splitlines() if ln.strip()), "")
    kind = first.split()[0] if first else ""

    if kind in {"flowchart", "graph"}:
        return flowchart_init(len(SUBGRAPH_RE.findall(stripped)), len(stripped))
    if kind == "sequenceDiagram":
        count = len(PARTICIPANT_RE.findall(stripped))
        if count >= 5:
            return sequence_init(count)
        return sequence_init(4)
    return None


def normalize_block(block: str) -> tuple[str, bool]:
    wrapped = wrap_labels_in_block(block.strip())
    changed = wrapped != block.strip()
    init = infer_init(wrapped)
    if init:
        return init + wrapped + "\n", True
    return wrapped + "\n", changed


def normalize_markdown(text: str) -> tuple[str, int]:
    changed = 0

    def repl(match: re.Match[str]) -> str:
        nonlocal changed
        new_block, did_change = normalize_block(match.group(1))
        if did_change:
            changed += 1
        return f"```mermaid\n{new_block}```"

    return MERMAID_BLOCK_RE.sub(repl, text), changed


def main() -> None:
    total_blocks = 0
    total_changed = 0
    files_changed = 0

    for fp in sorted(DOCS_ROOT.rglob("*.md")):
        original = fp.read_text(encoding="utf-8")
        updated, changed = normalize_markdown(original)
        block_count = len(MERMAID_BLOCK_RE.findall(original))
        total_blocks += block_count
        if updated != original:
            fp.write_text(updated, encoding="utf-8")
            files_changed += 1
            total_changed += changed
            print(f"{fp.relative_to(REPO_ROOT)}: {changed}/{block_count} diagram(s)")

    print(f"\nDone. {total_changed} diagram(s) updated across {files_changed} file(s) ({total_blocks} total).")


if __name__ == "__main__":
    main()
