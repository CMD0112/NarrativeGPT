#!/usr/bin/env python3
"""
Sync chatgpt-wrapper documentation into the NarrativeGPT Obsidian vault.

Converts repo markdown to Obsidian-optimized notes with:
- YAML frontmatter (title, tags, aliases, source path)
- Wikilinks for internal cross-references
- Folder MOCs with backlink hubs
- Preserved external URLs and mermaid blocks
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from datetime import date
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DOCS_ROOT = REPO_ROOT / "docs"
VAULT_ROOT = Path(r"e:\Documents\Obsidian\Obsidian Vaults\NarrativeGPT")
SYNC_DATE = date.today().isoformat()

# Repo relative path -> vault subfolder
FOLDER_MAP = {
    "docs/INDEX.md": "00 Hub",
    "README.md": "00 Hub",
    "AGENTS.md": "00 Hub",
    "docs/user": "01 User Guides",
    "docs/developer": "02 Developer",
    "docs/reference": "03 Reference",
    "docs/settings": "04 Settings",
    "docs/adr": "05 ADRs",
    "docs/plans": "06 Plans",
    "docs/linear": "07 Linear Workflow",
    "docs/Enhancements": "08 Enhancements",
}

TAG_BY_FOLDER = {
    "00 Hub": ["cgw", "hub", "moc"],
    "01 User Guides": ["cgw", "user-guide"],
    "02 Developer": ["cgw", "developer"],
    "03 Reference": ["cgw", "reference"],
    "04 Settings": ["cgw", "settings"],
    "05 ADRs": ["cgw", "adr"],
    "06 Plans": ["cgw", "plan"],
    "07 Linear Workflow": ["cgw", "linear", "workflow"],
    "08 Enhancements": ["cgw", "enhancement", "spike"],
}

MOC_TITLES = {
    "00 Hub": "ChatGPT Wrapper — Hub",
    "01 User Guides": "User Guides MOC",
    "02 Developer": "Developer MOC",
    "03 Reference": "Reference MOC",
    "04 Settings": "Settings MOC",
    "05 ADRs": "ADRs MOC",
    "06 Plans": "Plans MOC",
    "07 Linear Workflow": "Linear Workflow MOC",
    "08 Enhancements": "Enhancements MOC",
}

CMD_PATTERN = re.compile(r"(?<![/\[])\bCMD-(\d+)\b(?!\]\()")
BROKEN_CMD_URL_PATTERN = re.compile(
    r"https://linear\.app/cmd0112/issue/\[CMD-(\d+)\]\(https://linear\.app/cmd0112/issue/CMD-\1\)"
)
BROKEN_CMD_LINK_PATTERN = re.compile(
    r"\[CMD-(\d+)\]\(https://linear\.app/cmd0112/issue/\[CMD-\1\]\(https://linear\.app/cmd0112/issue/CMD-\1\)\)"
)
WIKILINK_WITH_ALIAS = re.compile(r"\[\[([^|\]#]+)(?:#([^|\]]+))?\|([^\]]+)\]\]")
CODE_FILE_LINK = re.compile(
    r"\[`([^`]+)`\]\(([^)]+\.(?:cs|xaml|js|yml|yaml|ps1))\)"
)
BARE_CODE_FILE_LINK = re.compile(
    r"\[(`?[^`\]]+`?)\]\(([^)]+\.(?:cs|xaml|js|yml|yaml|ps1))\)"
)
BROKEN_WIKI_MD_LINK = re.compile(r"\[\[([^\]]+)\]\(([^)]+\.md[^)]*)\)\]")
NESTED_LABEL_LINK = re.compile(r"\[([^\]]*\[[^\]]+\]\([^)]+\)[^\]]*)\]\(([^)]+)\)")
LINK_PATTERN = re.compile(r"(?<!!)\[([^\]]+)\]\(([^)]*)\)")
EMPTY_LINK_PATTERN = re.compile(r"(?<!!)\[([^\]]+)\]\(\)")
FRONTMATTER_PATTERN = re.compile(r"^---\s*\n.*?\n---\s*\n", re.DOTALL)
H1_PATTERN = re.compile(r"^#\s+(.+?)\s*$", re.MULTILINE)
MD_LINK_IN_TITLE = re.compile(r"\[([^\]]+)\]\([^)]+\)")

FOLDER_MOC_LINKS = {
    "user/": "[[User Guides MOC|user/]]",
    "developer/": "[[Developer MOC|developer/]]",
    "reference/": "[[Reference MOC|reference/]]",
    "settings/": "[[Settings MOC|settings/]]",
    "adr/": "[[ADRs MOC|adr/]]",
    "plans/": "[[Plans MOC|plans/]]",
    "linear/": "[[Linear Workflow MOC|linear/]]",
    "Enhancements/": "[[Enhancements MOC|Enhancements/]]",
}


@dataclass
class DocRecord:
    repo_path: Path
    rel_path: str
    vault_folder: str
    title: str
    note_name: str
    tags: list[str] = field(default_factory=list)
    cmd_ids: list[str] = field(default_factory=list)


def slug_to_title(slug: str) -> str:
    name = slug.removesuffix(".md")
    return name.replace("-", " ").replace("_", " ").title()


def extract_h1(content: str, fallback: str) -> str:
    if match := H1_PATTERN.search(content):
        title = match.group(1).strip()
        # Strip embedded markdown links from titles for note filenames
        title = MD_LINK_IN_TITLE.sub(r"\1", title)
        title = re.sub(r"\s+", " ", title).strip()
        return title
    return fallback


def normalize_lookup_key(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", text.lower())


def strip_existing_frontmatter(content: str) -> str:
    return FRONTMATTER_PATTERN.sub("", content, count=1).lstrip("\n")


def github_anchor_to_obsidian(anchor: str) -> str:
    """Best-effort: GitHub slugs often match Obsidian heading slugs."""
    return anchor.strip("#")


def vault_folder_for(rel_path: str) -> str:
    rel_norm = rel_path.replace("\\", "/")
    for prefix, folder in sorted(FOLDER_MAP.items(), key=lambda x: -len(x[0])):
        if prefix.endswith(".md"):
            if rel_norm == prefix:
                return folder
        elif rel_norm.startswith(prefix + "/"):
            return folder
    return "00 Hub"


def collect_source_files() -> list[Path]:
    files: list[Path] = []
    files.append(REPO_ROOT / "README.md")
    files.append(REPO_ROOT / "AGENTS.md")
    for path in sorted(DOCS_ROOT.rglob("*.md")):
        files.append(path)
    return files


def note_name_for_title(title: str) -> str:
    # Obsidian note filename from title; avoid invalid Windows chars
    invalid = '<>:"/\\|?*'
    name = title
    for ch in invalid:
        name = name.replace(ch, "")
    return name.strip() or "Untitled"


def build_registry(files: list[Path]) -> tuple[dict[str, DocRecord], dict[str, DocRecord]]:
    registry: dict[str, DocRecord] = {}
    lookup: dict[str, DocRecord] = {}
    title_counts: dict[str, int] = {}

    for repo_path in files:
        rel = repo_path.relative_to(REPO_ROOT).as_posix()
        raw = repo_path.read_text(encoding="utf-8")
        body = strip_existing_frontmatter(raw)
        fallback = slug_to_title(repo_path.name)
        title = extract_h1(body, fallback)
        vault_folder = vault_folder_for(rel)
        tags = list(TAG_BY_FOLDER.get(vault_folder, ["cgw"]))
        cmd_ids = sorted(set(CMD_PATTERN.findall(body)), key=int)

        if "adr" in repo_path.parts:
            if "adr" not in tags:
                tags.append("adr")
        if "plans" in repo_path.parts:
            if "plan" not in tags:
                tags.append("plan")

        for cmd in cmd_ids:
            tags.append(f"cmd-{cmd}")

        note_name = note_name_for_title(title)
        if note_name in title_counts:
            title_counts[note_name] += 1
            note_name = f"{note_name} ({slug_to_title(repo_path.stem)})"
        else:
            title_counts[note_name] = 1

        record = DocRecord(
            repo_path=repo_path,
            rel_path=rel,
            vault_folder=vault_folder,
            title=title,
            note_name=note_name,
            tags=sorted(set(tags)),
            cmd_ids=cmd_ids,
        )
        registry[rel] = record
        registry[repo_path.name] = record
        registry[repo_path.stem] = record

        for key in (
            record.title,
            record.note_name,
            repo_path.stem,
            slug_to_title(repo_path.stem),
            repo_path.name,
        ):
            lookup[normalize_lookup_key(key)] = record

    return registry, lookup


def resolve_link_target(
    link_path: str,
    source_rel: str,
    registry: dict[str, DocRecord],
    lookup: dict[str, DocRecord],
) -> DocRecord | None:
    link_path = link_path.strip()
    if not link_path or link_path.startswith("#"):
        return None

    if "#" in link_path:
        link_path, _anchor = link_path.split("#", 1)

    if link_path.startswith(("http://", "https://", "mailto:")):
        return None

    source_dir = (REPO_ROOT / source_rel).parent
    if link_path.startswith("/"):
        candidate = REPO_ROOT / link_path.lstrip("/")
    else:
        candidate = (source_dir / link_path).resolve()

    try:
        rel = candidate.relative_to(REPO_ROOT).as_posix()
    except ValueError:
        rel = ""

    if rel and rel in registry:
        return registry[rel]

    name = Path(link_path).name
    if name in registry:
        return registry[name]

    stem = Path(link_path).stem
    if stem in registry:
        return registry[stem]

    # Common typo: ../foo-adr.md from Enhancements should be ../adr/foo-adr.md
    if stem.endswith("-adr") and (source_dir / link_path).name == name:
        adr_candidate = REPO_ROOT / "docs" / "adr" / name
        if adr_candidate.exists():
            adr_rel = adr_candidate.relative_to(REPO_ROOT).as_posix()
            if adr_rel in registry:
                return registry[adr_rel]

    return lookup.get(normalize_lookup_key(stem)) or lookup.get(
        normalize_lookup_key(name.removesuffix(".md"))
    )


def resolve_empty_link_target(
    text: str,
    source_rel: str,
    registry: dict[str, DocRecord],
    lookup: dict[str, DocRecord],
) -> DocRecord | None:
    text = text.strip()
    if not text:
        return None

    if text.endswith(".md"):
        source_dir = Path(source_rel).parent
        same_folder = (source_dir / text).as_posix()
        if same_folder in registry:
            return registry[same_folder]
        if text in registry:
            return registry[text]
        stem = Path(text).stem
        if stem in registry:
            return registry[stem]

    candidates = [text, re.sub(r"\s*CMD-\d+\s*", " ", text).strip()]
    for candidate in candidates:
        key = normalize_lookup_key(candidate)
        if key in lookup:
            return lookup[key]

    cmd_match = re.search(r"CMD-(\d+)", text, re.IGNORECASE)
    if cmd_match:
        cmd_tag = f"cmd-{cmd_match.group(1)}"
        seen_paths: set[str] = set()
        matches: list[DocRecord] = []
        for record in registry.values():
            rel = getattr(record, "rel_path", "")
            if not rel.startswith("docs/") or rel in seen_paths:
                continue
            if cmd_tag not in record.tags:
                continue
            seen_paths.add(rel)
            matches.append(record)
        def score(record: DocRecord) -> int:
            text_key = normalize_lookup_key(
                re.sub(r"cmd\d+", "", text, flags=re.IGNORECASE)
            )
            stem_key = normalize_lookup_key(record.repo_path.stem)
            if not text_key:
                return 0
            score_val = 0
            for part in re.split(r"[\s\-]+", text.lower()):
                part_key = normalize_lookup_key(part)
                if part_key and part_key in stem_key:
                    score_val += 2
            if text_key in stem_key or stem_key in text_key:
                score_val += 5
            return score_val

        for prefix in ("docs/Enhancements/", "docs/adr/", "docs/plans/"):
            prefix_matches = [m for m in matches if m.rel_path.startswith(prefix)]
            if prefix_matches:
                return max(prefix_matches, key=score)
        if matches:
            return max(matches, key=score)

    # Partial match on stems
    for norm, record in lookup.items():
        for candidate in candidates:
            ckey = normalize_lookup_key(candidate)
            if ckey and (ckey in norm or norm in ckey):
                return record

    return None


def is_path_like_alias(alias: str) -> bool:
    alias = alias.strip()
    if not alias:
        return True
    lowered = alias.lower()
    if lowered.endswith(".md") or ".md " in lowered or ".md§" in lowered.replace(" ", ""):
        return True
    if "/" in alias or "\\" in alias:
        return True
    if lowered.startswith("docs/") or lowered.startswith("enhancements/"):
        return True
    if re.match(r"^[\w\-./]+\.md(?:\s|$)", lowered):
        return True
    if re.match(r"^[\w\-]+\.md", lowered):
        return True
    return False


def clean_display_alias(alias: str) -> str:
    """Reduce filename-style aliases to human-readable section labels."""
    alias = alias.strip()
    alias = re.sub(r"^\[([^\]]+)\]$", r"\1", alias)
    section = re.search(r"(§[^\]|]+)", alias)
    if section:
        return section.group(1).strip()
    if re.match(r"^§", alias):
        return alias
    # "adventure-panel.md §4" -> "§4"
    md_section = re.search(r"\.md\s*(§.+)$", alias, re.IGNORECASE)
    if md_section:
        return md_section.group(1).strip()
    # "§5 Prompt packets" style from source tables
    plain_section = re.search(r"^(§\d+[^\|]*)", alias)
    if plain_section and ".md" in alias.lower():
        return plain_section.group(1).strip()
    return alias


def make_wikilink(target: DocRecord, display: str = "", anchor: str = "") -> str:
    base = f"[[{target.note_name}"
    if anchor:
        base += f"#{github_anchor_to_obsidian(anchor)}"
    display = display.strip()
    display = re.sub(r"^\[([^\]]+)\]$", r"\1", display)
    if not display or display in (target.note_name, target.title):
        return f"{base}]]"
    if is_path_like_alias(display):
        display = clean_display_alias(display)
        if not display or is_path_like_alias(display):
            return f"{base}]]"
        if display in (target.note_name, target.title):
            return f"{base}]]"
    if normalize_lookup_key(display) == normalize_lookup_key(target.note_name):
        return f"{base}]]"
    return f"{base}|{display}]]"


def fix_nested_label_links(
    content: str,
    source_rel: str,
    registry: dict[str, DocRecord],
    lookup: dict[str, DocRecord],
) -> str:
    """Repair links whose label text contains nested markdown links."""

    def repl(match: re.Match[str]) -> str:
        raw_label = match.group(1)
        url = match.group(2).strip()
        label = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", raw_label).strip()
        anchor = ""
        path_part = url
        if "#" in url:
            path_part, anchor = url.split("#", 1)
        if path_part.endswith(".md") or ".md#" in url:
            target = resolve_link_target(path_part, source_rel, registry, lookup)
            if target:
                return make_wikilink(target, label, anchor)
        return match.group(0)

    return NESTED_LABEL_LINK.sub(repl, content)


def fix_broken_nested_links(
    content: str,
    source_rel: str,
    registry: dict[str, DocRecord],
    lookup: dict[str, DocRecord],
) -> str:
    """Repair `[[label](path.md#anchor)]` artifacts from source docs."""

    def repl(match: re.Match[str]) -> str:
        display = match.group(1)
        url = match.group(2)
        anchor = ""
        path_part = url
        if "#" in url:
            path_part, anchor = url.split("#", 1)
        target = resolve_link_target(path_part, source_rel, registry, lookup)
        if not target:
            return match.group(0)
        return make_wikilink(target, display, anchor)

    return BROKEN_WIKI_MD_LINK.sub(repl, content)


def convert_empty_links(
    content: str,
    source_rel: str,
    registry: dict[str, DocRecord],
    lookup: dict[str, DocRecord],
) -> str:
    def replace_empty(match: re.Match[str]) -> str:
        text = match.group(1)
        target = resolve_empty_link_target(text, source_rel, registry, lookup)
        if not target:
            return match.group(0)
        display = text.removesuffix(".md") if text.endswith(".md") else text
        if display == target.note_name or display == target.title:
            return f"[[{target.note_name}]]"
        return make_wikilink(target, display)

    return EMPTY_LINK_PATTERN.sub(replace_empty, content)


def convert_links(
    content: str,
    source_rel: str,
    registry: dict[str, DocRecord],
    lookup: dict[str, DocRecord],
) -> str:
    def replace_link(match: re.Match[str]) -> str:
        text = match.group(1)
        url = match.group(2).strip()

        if url.startswith(("http://", "https://", "mailto:")):
            return match.group(0)

        anchor = ""
        path_part = url
        if "#" in url:
            path_part, anchor = url.split("#", 1)

        if path_part.startswith("#") or not path_part:
            return match.group(0)

        target = resolve_link_target(path_part, source_rel, registry, lookup)
        if not target and url != path_part:
            target = resolve_link_target(url, source_rel, registry, lookup)
        if not target:
            return match.group(0)

        return make_wikilink(target, text, anchor)

    return LINK_PATTERN.sub(replace_link, content)


def convert_cmd_references(content: str) -> str:
    """Wrap bare CMD-NNN with Linear links where not already linked."""

    def repl(match: re.Match[str]) -> str:
        cmd_id = match.group(1)
        return f"[CMD-{cmd_id}](https://linear.app/cmd0112/issue/CMD-{cmd_id})"

    return CMD_PATTERN.sub(repl, content)


def repair_cmd_links(content: str) -> str:
    """Fix nested CMD markdown links produced inside Linear URLs."""
    content = BROKEN_CMD_LINK_PATTERN.sub(
        r"[CMD-\1](https://linear.app/cmd0112/issue/CMD-\1)", content
    )
    content = BROKEN_CMD_URL_PATTERN.sub(
        r"https://linear.app/cmd0112/issue/CMD-\1", content
    )
    return content


def optimize_index_layout_table(content: str) -> str:
    for folder, moc in FOLDER_MOC_LINKS.items():
        content = content.replace(f"[`{folder}`]({folder})", moc)
    return content


def add_see_also_section(content: str, related: list[str]) -> str:
    if not related:
        return content
    block = "\n\n---\n\n## See also\n\n"
    block += "\n".join(f"- {link}" for link in related)
    if "## See also" in content:
        return content
    return content.rstrip() + block


def infer_related_wikilinks(record: DocRecord, registry: dict[str, DocRecord]) -> list[str]:
    """Suggest related notes from same folder and ADR/plan pairs."""
    related: list[str] = []
    stem = record.repo_path.stem

    for rel, other in registry.items():
        if "/" not in rel or rel != other.rel_path:
            continue
        if other.rel_path == record.rel_path:
            continue
        if other.vault_folder == record.vault_folder:
            if len(related) < 5:
                related.append(f"[[{other.note_name}]]")

    # ADR <-> plan pairing
    if stem.endswith("-adr"):
        plan_stem = stem.replace("-adr", "-implementation-plan")
        if plan_stem.endswith("-adr"):
            plan_stem = stem.removesuffix("-adr") + "-implementation-plan"
        for rel, other in registry.items():
            if rel == other.rel_path and other.repo_path.stem == plan_stem:
                related.insert(0, f"[[{other.note_name}]]")
    elif "plan" in stem or "implementation-plan" in stem:
        adr_stem = stem.replace("-implementation-plan", "-adr").replace("-plan", "-adr")
        for rel, other in registry.items():
            if rel == other.rel_path and other.repo_path.stem == adr_stem:
                related.insert(0, f"[[{other.note_name}]]")

    # Hub links
    hub = "[[Documentation Index]]" if record.vault_folder != "00 Hub" else "[[ChatGPT Wrapper — Hub]]"
    moc = MOC_TITLES.get(record.vault_folder)
    if moc and record.vault_folder != "00 Hub":
        related.insert(0, f"[[{moc}]]")
    if hub not in related:
        related.append(hub)

    # Deduplicate preserving order
    seen: set[str] = set()
    unique: list[str] = []
    for item in related:
        if item not in seen:
            seen.add(item)
            unique.append(item)
    return unique[:12]


def yaml_scalar(value: str) -> str:
    """Quote YAML string values when special characters would break parsing."""
    if value == "":
        return '""'
    if value.strip() != value:
        return json_quote(value)
    special = set(':{}[]&*#?|>-!%@`",\'')
    if any(ch in value for ch in special):
        return json_quote(value)
    if value.lower() in {"true", "false", "null", "yes", "no", "on", "off"}:
        return json_quote(value)
    return value


def json_quote(value: str) -> str:
    escaped = value.replace("\\", "\\\\").replace('"', '\\"')
    return f'"{escaped}"'


def build_frontmatter(record: DocRecord) -> str:
    aliases = [record.repo_path.stem, record.repo_path.name]
    if slug_to_title(record.repo_path.stem) != record.title:
        aliases.append(slug_to_title(record.repo_path.stem))
    if record.rel_path == "docs/INDEX.md":
        aliases.extend(
            [
                "ChatGPT Wrapper — Documentation Index",
                "INDEX",
                "INDEX.md",
            ]
        )

    lines = [
        "---",
        f"title: {yaml_scalar(record.title)}",
        f"source: {yaml_scalar(f'chatgpt-wrapper/{record.rel_path}')}",
        f"synced: {SYNC_DATE}",
        "tags:",
    ]
    for tag in record.tags:
        lines.append(f"  - {tag}")
    lines.append("aliases:")
    for alias in sorted(set(aliases)):
        lines.append(f"  - {yaml_scalar(alias)}")
    lines.append("---")
    lines.append("")
    return "\n".join(lines)


def add_source_callout(content: str, record: DocRecord) -> str:
    callout = (
        f"> [!info] Canonical source\n"
        f"> Repo path: `chatgpt-wrapper/{record.rel_path}`  \n"
        f"> Synced: {SYNC_DATE} — edit the repo copy for developer workflow; "
        f"this vault note is the Obsidian-optimized mirror.\n\n"
    )
    body = strip_existing_frontmatter(content)
    if body.startswith("> [!info] Canonical source"):
        return body
    return callout + body


def enhance_obsidian_markdown(body: str) -> str:
    """Light touch-ups for Obsidian callouts and horizontal rules."""
    body = body.replace("\n---\n\n## ", "\n\n## ")

    for label in ("Related", "Related docs", "Related canon", "See also"):
        body = re.sub(
            rf"^\*\*{re.escape(label)}:\*\*\s*(.+)$",
            rf"> [!tip] {label}\n> \1",
            body,
            flags=re.MULTILINE,
        )

    # Blank line before callouts that follow metadata lines
    body = re.sub(
        r"(\*\*[^*]+\*\*[^\n]*)\n(> \[!(?:tip|info|note|warning|abstract)\])",
        r"\1\n\n\2",
        body,
    )
    return body


def polish_wikilink_aliases(content: str) -> str:
    def repl(match: re.Match[str]) -> str:
        note = match.group(1).strip()
        anchor = (match.group(2) or "").strip()
        alias = match.group(3).strip()
        alias = re.sub(r"^\[([^\]]+)\]$", r"\1", alias)

        if normalize_lookup_key(alias) == normalize_lookup_key(note):
            alias = ""

        if alias and is_path_like_alias(alias):
            cleaned = clean_display_alias(alias)
            alias = "" if is_path_like_alias(cleaned) else cleaned
        elif alias and re.match(r"^[a-z0-9]+(-[a-z0-9]+){2,}$", alias.lower()):
            # Filename stems used as display aliases (no spaces)
            alias = ""

        if not alias:
            if anchor:
                return f"[[{note}#{anchor}]]"
            return f"[[{note}]]"
        if anchor:
            return f"[[{note}#{anchor}|{alias}]]"
        return f"[[{note}|{alias}]]"

    return WIKILINK_WITH_ALIAS.sub(repl, content)


def escape_wikilink_pipes_in_tables(content: str) -> str:
    """Escape alias pipes in wikilinks inside markdown table rows.

    Unescaped `[[Note|alias]]` inside `| ... |` rows breaks because `|`
    is the table column delimiter. Obsidian requires `[[Note\\|alias]]`.
    """

    def escape_line(line: str) -> str:
        if not line.strip().startswith("|"):
            return line

        def repl(match: re.Match[str]) -> str:
            if "\\|" in match.group(0):
                return match.group(0)
            note = match.group(1)
            anchor = match.group(2) or ""
            alias = match.group(3)
            if anchor:
                return f"[[{note}#{anchor}\\|{alias}]]"
            return f"[[{note}\\|{alias}]]"

        return WIKILINK_WITH_ALIAS.sub(repl, line)

    return "\n".join(escape_line(line) for line in content.splitlines())


def resolve_repo_code_path(link_path: str, source_rel: str) -> str | None:
    link_path = link_path.replace("\\", "/")
    source_dir = (REPO_ROOT / source_rel).parent
    candidates = [
        (source_dir / link_path).resolve(),
    ]
    for root in ("ChatGPTWrapper", "ChatGPTWrapper.Core", ".github", "ChatGPT_files"):
        idx = link_path.find(root)
        if idx >= 0:
            candidates.append((REPO_ROOT / link_path[idx:]).resolve())

    for candidate in candidates:
        try:
            rel = candidate.relative_to(REPO_ROOT)
        except ValueError:
            continue
        if candidate.exists():
            return rel.as_posix()

    for root in ("ChatGPTWrapper", "ChatGPTWrapper.Core", ".github", "ChatGPT_files"):
        idx = link_path.find(root)
        if idx >= 0:
            return link_path[idx:].replace("\\", "/")
    return None


def convert_code_file_links(content: str, source_rel: str) -> str:
    """Turn repo source links into backticked paths (non-navigable in Obsidian)."""

    def repl(match: re.Match[str]) -> str:
        label = match.group(1).strip("`").strip()
        link_path = match.group(2)
        repo_path = resolve_repo_code_path(link_path, source_rel)
        if not repo_path:
            return match.group(0)
        if label and label not in (Path(repo_path).name, repo_path):
            return f"`{label}` (`chatgpt-wrapper/{repo_path}`)"
        return f"`chatgpt-wrapper/{repo_path}`"

    content = CODE_FILE_LINK.sub(repl, content)
    content = BARE_CODE_FILE_LINK.sub(repl, content)
    return content


def polish_obsidian_content(content: str, source_rel: str) -> str:
    content = polish_wikilink_aliases(content)
    content = escape_wikilink_pipes_in_tables(content)
    content = convert_code_file_links(content, source_rel)
    # Normalize wiki links that still use old index title as target
    content = content.replace(
        "[[ChatGPT Wrapper — Documentation Index",
        "[[Documentation Index",
    )
    return content


def write_note(record: DocRecord, body: str) -> Path:
    out_dir = VAULT_ROOT / "ChatGPT Wrapper" / record.vault_folder
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{record.note_name}.md"
    out_path.write_text(body, encoding="utf-8")
    return out_path


def build_folder_mocs(records: list[DocRecord]) -> None:
    by_folder: dict[str, list[DocRecord]] = {}
    for rec in records:
        by_folder.setdefault(rec.vault_folder, []).append(rec)

    for folder, items in by_folder.items():
        if folder == "00 Hub":
            continue
        moc_title = MOC_TITLES.get(folder, f"{folder} MOC")
        sorted_items = sorted(items, key=lambda r: r.note_name.lower())
        lines = [
            "---",
            f"title: {moc_title}",
            "tags:",
            "  - cgw",
            "  - moc",
            f"  - {TAG_BY_FOLDER.get(folder, ['cgw'])[1] if len(TAG_BY_FOLDER.get(folder, [])) > 1 else 'hub'}",
            "---",
            "",
            f"# {moc_title}",
            "",
            f"> [!abstract] Map of content",
            f"> Backlink hub for **{folder}** notes in the ChatGPT Wrapper documentation vault.",
            "",
            "## Notes in this folder",
            "",
        ]
        for rec in sorted_items:
            cmd_badge = f" — CMD-{', CMD-'.join(rec.cmd_ids)}" if rec.cmd_ids else ""
            lines.append(f"- [[{rec.note_name}]]{cmd_badge}")

        lines.extend(
            [
                "",
                "## Navigation",
                "",
                "- [[Documentation Index]]",
                "- [[ChatGPT Wrapper — Hub]]",
                "",
                "---",
                "",
                "## Backlinks",
                "",
                f"*Obsidian will list incoming links to `{moc_title}` in the backlinks panel.*",
                "",
            ]
        )

        moc_path = VAULT_ROOT / "ChatGPT Wrapper" / folder / f"_{moc_title}.md"
        moc_path.write_text("\n".join(lines), encoding="utf-8")


def build_main_hub(records: list[DocRecord]) -> None:
    """Create ChatGPT Wrapper — Hub note linking all MOCs."""
    title_by_stem = {r.repo_path.stem: r.note_name for r in records}

    def note(stem: str, fallback: str) -> str:
        return title_by_stem.get(stem, fallback)

    lines = [
        "---",
        "title: ChatGPT Wrapper — Hub",
        "tags:",
        "  - cgw",
        "  - hub",
        "  - moc",
        "aliases:",
        "  - CGW Hub",
        "  - ChatGPT Wrapper Hub",
        "---",
        "",
        "# ChatGPT Wrapper — Hub",
        "",
        "> [!abstract] NarrativeGPT vault",
        "> Obsidian-optimized mirror of **chatgpt-wrapper** repository documentation. "
        "For lore and worldbuilding, use a separate folder outside `ChatGPT Wrapper/`.",
        "",
        "## Start here",
        "",
        "- [[Documentation Index]] — full index (mirrors `docs/INDEX.md`)",
        "- [[README]] — build, run, publish",
        "- [[AGENTS]] — AI agent instructions",
        "",
        "## Maps of content",
        "",
    ]
    for folder in sorted(MOC_TITLES.keys()):
        if folder == "00 Hub":
            continue
        lines.append(f"- [[{MOC_TITLES[folder]}]]")

    lines.extend(
        [
            "",
            "## By audience",
            "",
            "### Use the app",
            "",
            f"- [[{note('user-guide', 'User Guide')}]]",
            f"- [[{note('adventure-panel', 'Adventure Panel Reference')}]]",
            f"- [[{note('troubleshooting', 'Troubleshooting')}]]",
            "",
            "### Modify the code",
            "",
            f"- [[{note('architecture', 'System Architecture')}]]",
            f"- [[{note('webview-bridges', 'WebView Bridges')}]]",
            f"- [[{note('data-model-reference', 'Data Model Reference')}]]",
            f"- [[{note('testing', 'Testing')}]]",
            "",
            "### Decisions & delivery",
            "",
            "- [[ADRs MOC]]",
            "- [[Plans MOC]]",
            "- [[Enhancements MOC]]",
            "",
            "## External",
            "",
            "- [Linear — ChatGPT Wrapper](https://linear.app/cmd0112/project/chatgpt-wrapper-b2ae13366b93)",
            "- [GitHub — NarrativeGPT](https://github.com/CMD0112/NarrativeGPT)",
            "",
        ]
    )

    hub_path = VAULT_ROOT / "ChatGPT Wrapper" / "00 Hub" / "ChatGPT Wrapper — Hub.md"
    hub_path.parent.mkdir(parents=True, exist_ok=True)
    hub_path.write_text("\n".join(lines), encoding="utf-8")


def update_vault_agents() -> None:
    content = """---
title: NarrativeGPT Vault — Agent Instructions
tags:
  - cgw
  - meta
  - agents
---

# NarrativeGPT Vault — Agent Instructions

> [!important] Vault purpose
> **NarrativeGPT** is an Obsidian vault. The `ChatGPT Wrapper/` folder contains Obsidian-optimized mirrors of repo documentation.

## Layout

| Location | Purpose |
|----------|---------|
| [[ChatGPT Wrapper — Hub]] | Entry point for product documentation |
| `ChatGPT Wrapper/01 User Guides/` | End-user and author guides |
| `ChatGPT Wrapper/02 Developer/` | Architecture, build, test, bridges |
| `ChatGPT Wrapper/03 Reference/` | Data model, services, UI catalogs |
| `ChatGPT Wrapper/05 ADRs/` | Architecture decision records |
| `ChatGPT Wrapper/06 Plans/` | Implementation plans |
| `ChatGPT Wrapper/08 Enhancements/` | Spikes and backlog trackers |
| *(future)* `Worldbuilding/` | Lore and narrative notes (not repo mirrors) |

## Conventions

- Internal links use **wikilinks**: `[[Note Title]]`, `[[Note#Heading]]`
- Tags: `#cgw`, `#adr`, `#plan`, `#cmd-NNN`
- Canonical editable source for product docs: `chatgpt-wrapper/docs/` in the repo
- Each mirrored note has a `> [!info] Canonical source` callout with repo path

## Related

- [[Documentation Index]]
- Repo: `chatgpt-wrapper/AGENTS.md`
"""
    (VAULT_ROOT / "AGENTS.md").write_text(content, encoding="utf-8")


def main() -> None:
    files = collect_source_files()
    registry, lookup = build_registry(files)

    # Deduplicate records (registry has alias keys)
    unique_records: dict[str, DocRecord] = {}
    for key, rec in registry.items():
        if "/" in key or key.endswith(".md"):
            unique_records[rec.rel_path] = rec

    records = list(unique_records.values())
    written: list[Path] = []

    for record in records:
        raw = record.repo_path.read_text(encoding="utf-8")
        body = strip_existing_frontmatter(raw)
        body = convert_cmd_references(body)
        body = fix_nested_label_links(body, record.rel_path, registry, lookup)
        body = fix_broken_nested_links(body, record.rel_path, registry, lookup)
        body = convert_empty_links(body, record.rel_path, registry, lookup)
        body = convert_links(body, record.rel_path, registry, lookup)
        body = repair_cmd_links(body)
        body = polish_obsidian_content(body, record.rel_path)
        body = enhance_obsidian_markdown(body)
        body = add_source_callout(body, record)

        if record.rel_path == "docs/INDEX.md":
            body = optimize_index_layout_table(body)

        # Rename INDEX to Documentation Index for clarity
        if record.rel_path == "docs/INDEX.md":
            record.note_name = "Documentation Index"
            record.title = "Documentation Index"

        if record.rel_path == "README.md":
            record.note_name = "README"

        if record.rel_path == "AGENTS.md":
            record.note_name = "AGENTS"

        related = infer_related_wikilinks(record, {k: v for k, v in registry.items() if "/" in k})
        body = add_see_also_section(body, related)

        full = build_frontmatter(record) + body
        written.append(write_note(record, full))

    build_folder_mocs(records)
    build_main_hub(records)
    update_vault_agents()

    print(f"Synced {len(written)} notes to {VAULT_ROOT / 'ChatGPT Wrapper'}")
    for folder in sorted(MOC_TITLES.keys()):
        count = sum(1 for r in records if r.vault_folder == folder)
        print(f"  {folder}: {count} notes")


if __name__ == "__main__":
    main()
