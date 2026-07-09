#!/usr/bin/env python3
"""Audit mermaid blocks for common spacing / crowding issues."""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

REPO_DOCS = Path(__file__).resolve().parents[1] / "docs"
MERMAID_RE = re.compile(r"```mermaid\n(.*?)\n```", re.DOTALL)
LONG_LABEL_RE = re.compile(r"(?:\[[^\]]{45,}\]|\([^)]{45,}\)|\{[^}]{45,}\})")
SUBGRAPH_RE = re.compile(r"\bsubgraph\b", re.I)


@dataclass
class Finding:
    path: str
    line: int
    diagram_type: str
    issues: list[str] = field(default_factory=list)
    snippet: str = ""


def diagram_type(block: str) -> str:
    first = next((ln.strip() for ln in block.splitlines() if ln.strip()), "")
    return first.split()[0] if first else "unknown"


def audit_block(block: str) -> list[str]:
    issues: list[str] = []
    dtype = diagram_type(block)

    if dtype in {"flowchart", "graph"}:
        if len(block) > 1500:
            issues.append("very large flowchart — likely crowded at default spacing")
        if SUBGRAPH_RE.search(block) and "padding" not in block and "init:" not in block[:120]:
            issues.append("subgraph(s) without padding/init tuning")
        if block.count("subgraph") >= 3:
            issues.append("many nested subgraphs — spacing often needs init block")

    long_labels = LONG_LABEL_RE.findall(block)
    if long_labels:
        issues.append(f"{len(long_labels)} long node label(s) (45+ chars)")

    for ln in block.splitlines():
        s = ln.strip()
        if not s or s.startswith("%%"):
            continue
        if ("-->" in s or "---" in s) and len(s) > 100:
            issues.append(f"long edge definition ({len(s)} chars)")
            break

    if "&" in block and "<br" not in block and "\\n" not in block:
        issues.append("ampersand in label without <br/> line break")

    if re.search(r"\|[^|]{40,}\|", block):
        issues.append("long sequence/participant label")

    if dtype == "sequenceDiagram" and block.count("participant") >= 6:
        issues.append("many participants — may need autonumber spacing or shorter aliases")

    return issues


def main() -> None:
    findings: list[Finding] = []
    block_count = 0
    for fp in sorted(REPO_DOCS.rglob("*.md")):
        text = fp.read_text(encoding="utf-8")
        for m in MERMAID_RE.finditer(text):
            block_count += 1
            block = m.group(1).strip()
            issues = audit_block(block)
            if issues:
                line = text[: m.start()].count("\n") + 1
                findings.append(
                    Finding(
                        path=str(fp.relative_to(REPO_DOCS.parent)),
                        line=line,
                        diagram_type=diagram_type(block),
                        issues=issues,
                        snippet="\n".join(block.splitlines()[:4]),
                    )
                )

    print(f"Mermaid blocks: {block_count}")
    print(f"Flagged: {len(findings)}\n")
    for f in findings:
        print(f"{f.path}:{f.line} [{f.diagram_type}]")
        for issue in f.issues:
            print(f"  - {issue}")
        print()


if __name__ == "__main__":
    main()
