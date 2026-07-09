#!/usr/bin/env python3
"""Fix Mermaid init directives missing closing %%."""

from __future__ import annotations

from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DOCS_ROOT = REPO_ROOT / "docs"


def fix_text(text: str) -> tuple[str, int]:
    count = 0
    out: list[str] = []
    for line in text.splitlines(keepends=False):
        if line.startswith("%%{init:") and not line.endswith("%%"):
            out.append(line + "%%")
            count += 1
        else:
            out.append(line)
    trailing_newline = text.endswith("\n")
    joined = "\n".join(out)
    if trailing_newline:
        joined += "\n"
    return joined, count


def main() -> None:
    total = 0
    for fp in sorted(DOCS_ROOT.rglob("*.md")):
        original = fp.read_text(encoding="utf-8")
        updated, n = fix_text(original)
        if n:
            fp.write_text(updated, encoding="utf-8")
            print(f"{fp.relative_to(REPO_ROOT)}: {n}")
            total += n
    print(f"Fixed {total} init line(s).")


if __name__ == "__main__":
    main()
