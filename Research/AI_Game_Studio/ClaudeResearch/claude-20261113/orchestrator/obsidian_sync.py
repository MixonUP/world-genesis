"""Studio orchestrator: sort canonical Markdown notes into an Obsidian vault.

Python 3.10+ standard library only. No network, subprocess or GUI.

Safety model
------------
* The vault path is given explicitly on the command line; nothing else is written.
* Every destination is a vault-relative POSIX path under an allowed prefix from
  the manifest; resolved paths must stay inside the vault.
* ``managed`` notes are owned by this tool. They carry ``managed_by`` and
  ``rendered_sha256`` in front matter. If a person edited the note after the
  last sync, or the file exists without ``managed_by``, the tool reports a
  CONFLICT and does not touch it.
* ``append`` targets (project log, journal, HOME) are never rewritten: a section
  wrapped in ``<!-- MARKER -->`` is appended (or inserted after an anchor line)
  once. If the marker already exists the target is skipped.
* Default is a dry run. ``--apply`` writes. Line endings follow the existing
  file (CRLF in this vault) or the manifest default for new files.
"""
from __future__ import annotations

import argparse
from datetime import date
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys

MANIFEST_SCHEMA = "ags.obsidian-sync/1"
TOOL_ID = "ags-studio-orchestrator"
MODULE_ROOT = Path(__file__).resolve().parent
WIKILINK = re.compile(r"\[\[([^\]|#\\]+)\\?(?:#[^\]|]*)?(?:\|[^\]]*)?\]\]")  # tolerates Obsidian's "\|" in tables
FENCE = re.compile(r"^```.*?^```", re.S | re.M)
FRONT = re.compile(r"\A---\n(.*?)\n---\n", re.S)


class SyncError(ValueError):
    """Invalid manifest, unsafe path or unreadable input (exit 2)."""


# ---------------------------------------------------------------- helpers
def sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def norm_lf(text: str) -> str:
    return text.replace("\r\n", "\n").replace("\r", "\n")


def read_text(path: Path) -> str:
    try:
        return path.read_bytes().decode("utf-8-sig")
    except (OSError, UnicodeError) as exc:
        raise SyncError(f"Cannot read {path.name}: {exc}") from exc


def safe_rel(value, where, prefixes=None):
    if not isinstance(value, str) or not value.strip() or "\x00" in value:
        raise SyncError(f"{where}: path must be a nonempty string")
    if "\\" in value or value.startswith(("/", "~")) or re.match(r"^[A-Za-z]:", value) or ":" in value:
        raise SyncError(f"{where}: path must be vault-relative POSIX without ':' or '\\': {value!r}")
    parts = PurePosixPath(value).parts
    if ".." in parts or "." in parts:
        raise SyncError(f"{where}: '.'/'..' segments are not allowed: {value!r}")
    if prefixes is not None and not any(value == p or (p.endswith("/") and value.startswith(p)) for p in prefixes):
        raise SyncError(f"{where}: {value!r} is outside allowed prefixes {prefixes}")
    if not value.endswith(".md"):
        raise SyncError(f"{where}: only .md notes are written")
    return value


def inside(root: Path, rel: str) -> Path:
    root = root.resolve()
    target = (root / rel).resolve()
    if root not in target.parents:
        raise SyncError(f"{rel}: resolves outside the vault")
    return target


def split_front(text: str):
    m = FRONT.match(text)
    if not m:
        return None, text
    return m.group(1), text[m.end():]


def front_value(front: str, key: str):
    m = re.search(rf"^{re.escape(key)}:\s*(.*)$", front or "", re.M)
    return m.group(1).strip() if m else None


def render_managed(src_text: str, src_rel: str) -> str:
    """Add ownership keys to front matter; rendered_sha256 covers the body."""
    text = norm_lf(src_text)
    front, body = split_front(text)
    if front is None:
        raise SyncError(f"{src_rel}: managed note needs YAML front matter (title/type/tags)")
    for key in ("title", "type", "tags"):
        if front_value(front, key) is None:
            raise SyncError(f"{src_rel}: front matter lacks '{key}'")
    for key in ("managed_by", "rendered_sha256", "source"):
        if front_value(front, key) is not None:
            raise SyncError(f"{src_rel}: '{key}' is reserved for the orchestrator")
    body = body.rstrip("\n") + "\n"
    front = (front.rstrip("\n") + f"\nmanaged_by: {TOOL_ID}\nsource: {src_rel}\n"
             f"rendered_sha256: {sha(body)}")
    return f"---\n{front}\n---\n{body}"


def to_eol(text: str, eol: str) -> str:
    return text.replace("\n", "\r\n") if eol == "crlf" else text


def detect_eol(raw: str, default: str) -> str:
    if "\r\n" in raw:
        return "crlf"
    if "\n" in raw:
        return "lf"
    return default


# ---------------------------------------------------------------- manifest
def load_manifest(path: Path):
    try:
        data = json.loads(read_text(path))
    except json.JSONDecodeError as exc:
        raise SyncError(f"Manifest is not valid JSON: {exc}") from exc
    if not isinstance(data, dict) or data.get("schema") != MANIFEST_SCHEMA:
        raise SyncError(f"Unsupported manifest schema; supported: {MANIFEST_SCHEMA}")
    allowed = {"schema", "description", "default_eol", "allowed_prefixes", "managed", "appends"}
    if set(data) - allowed:
        raise SyncError(f"Manifest: unsupported keys {sorted(set(data) - allowed)}")
    prefixes = data.get("allowed_prefixes")
    if not isinstance(prefixes, list) or not prefixes or not all(isinstance(p, str) and p for p in prefixes):
        raise SyncError("Manifest: allowed_prefixes must be a nonempty list")
    for p in prefixes:
        if "\\" in p or p.startswith(("/", "~")) or ".." in PurePosixPath(p).parts or ":" in p:
            raise SyncError(f"Manifest: unsafe prefix {p!r}")
    if data.get("default_eol", "crlf") not in {"crlf", "lf"}:
        raise SyncError("Manifest: default_eol must be crlf or lf")
    base = path.resolve().parent
    seen = set()
    managed = []
    for i, item in enumerate(data.get("managed", [])):
        if not isinstance(item, dict) or set(item) != {"src", "dst"}:
            raise SyncError(f"managed[{i}]: needs exactly src and dst")
        dst = safe_rel(item["dst"], f"managed[{i}].dst", prefixes)
        src = safe_rel(item["src"], f"managed[{i}].src")
        if dst in seen:
            raise SyncError(f"Duplicate destination {dst}")
        seen.add(dst)
        managed.append({"src": src, "dst": dst, "src_path": inside(base, src)})
    appends = []
    for i, item in enumerate(data.get("appends", [])):
        keys = {"src", "dst", "marker", "anchor", "create_title"}
        if not isinstance(item, dict) or not {"src", "dst", "marker"} <= set(item) or set(item) - keys:
            raise SyncError(f"appends[{i}]: keys must be within {sorted(keys)} incl. src, dst, marker")
        dst = safe_rel(item["dst"], f"appends[{i}].dst", prefixes)
        if dst in seen:
            raise SyncError(f"Destination {dst} is both managed and append target")
        if not isinstance(item["marker"], str) or not re.fullmatch(r"[A-Z0-9_]{6,80}", item["marker"]):
            raise SyncError(f"appends[{i}].marker must match [A-Z0-9_]{{6,80}}")
        anchor = item.get("anchor")
        if anchor is not None and (not isinstance(anchor, str) or not anchor.strip()):
            raise SyncError(f"appends[{i}].anchor must be a nonempty string")
        title = item.get("create_title")
        if title is not None and (not isinstance(title, str) or not title.strip()):
            raise SyncError(f"appends[{i}].create_title must be a nonempty string")
        src = safe_rel(item["src"], f"appends[{i}].src")
        appends.append({"src": src, "dst": dst, "marker": item["marker"], "anchor": anchor,
                        "create_title": title, "src_path": inside(base, src)})
    if not managed and not appends:
        raise SyncError("Manifest lists nothing to sync")
    return {"prefixes": prefixes, "eol": data.get("default_eol", "crlf"), "managed": managed, "appends": appends}


# ---------------------------------------------------------------- planning
def plan(manifest, vault: Path):
    if not vault.is_dir():
        raise SyncError("Vault directory does not exist")
    actions = []
    managed_targets = {m["dst"] for m in manifest["managed"]} | {
        a["dst"] for a in manifest["appends"] if a["create_title"]}
    for m in manifest["managed"]:
        desired = render_managed(read_text(m["src_path"]), m["src"])
        target = inside(vault, m["dst"])
        if not target.exists():
            actions.append({"dst": m["dst"], "kind": "managed", "action": "CREATE",
                            "content": desired, "eol": manifest["eol"]})
            continue
        raw = read_text(target)
        current = norm_lf(raw)
        front, body = split_front(current)
        if front_value(front, "managed_by") != TOOL_ID:
            actions.append({"dst": m["dst"], "kind": "managed", "action": "CONFLICT",
                            "reason": "existing note is not managed by the orchestrator"})
            continue
        if front_value(front, "rendered_sha256") != sha(body.rstrip("\n") + "\n"):
            actions.append({"dst": m["dst"], "kind": "managed", "action": "CONFLICT",
                            "reason": "note was edited by a person after the last sync; merge manually"})
            continue
        # Keep single-line front-matter keys added by other vault tools (e.g. "permalink:").
        d_front, d_body = split_front(desired)
        known = {line.split(":", 1)[0] for line in d_front.split("\n") if re.match(r"^[A-Za-z_]+:", line)}
        foreign = [line for line in front.split("\n")
                   if re.match(r"^[A-Za-z_]+:\s*\S", line) and line.split(":", 1)[0] not in known]
        if foreign:
            desired = "---\n" + d_front + "\n" + "\n".join(foreign) + "\n---\n" + d_body
        n_front, n_body = split_front(desired)
        # Foreign keys may sit anywhere in the front matter: compare lines regardless of order.
        # Other tools may also drop the final newline; that is not a content change either.
        if n_body.rstrip("\n") == body.rstrip("\n") and sorted(n_front.split("\n")) == sorted(front.split("\n")):
            actions.append({"dst": m["dst"], "kind": "managed", "action": "UNCHANGED"})
        else:
            actions.append({"dst": m["dst"], "kind": "managed", "action": "UPDATE",
                            "content": desired, "eol": detect_eol(raw, manifest["eol"])})
    for a in manifest["appends"]:
        section = norm_lf(read_text(a["src_path"])).strip("\n")
        if f"<!-- {a['marker']} -->" in section:
            raise SyncError(f"{a['src']}: do not include the marker; the orchestrator adds it")
        block = f"<!-- {a['marker']} -->\n{section}\n<!-- /{a['marker']} -->"
        target = inside(vault, a["dst"])
        if not target.exists():
            if not a["create_title"]:
                actions.append({"dst": a["dst"], "kind": "append", "action": "CONFLICT",
                                "reason": "append target missing and create_title not set"})
                continue
            content = f"# {a['create_title']}\n\n{block}\n"
            actions.append({"dst": a["dst"], "kind": "append", "action": "CREATE",
                            "content": content, "eol": manifest["eol"]})
            continue
        raw = read_text(target)
        current = norm_lf(raw)
        if f"<!-- {a['marker']} -->" in current:
            actions.append({"dst": a["dst"], "kind": "append", "action": "UNCHANGED",
                            "reason": "marker already present"})
            continue
        if a["anchor"]:
            lines = current.split("\n")
            idx = [i for i, line in enumerate(lines) if a["anchor"] in line]
            if len(idx) != 1:
                actions.append({"dst": a["dst"], "kind": "append", "action": "CONFLICT",
                                "reason": f"anchor found {len(idx)} times; expected exactly once"})
                continue
            new_lines = lines[:idx[0] + 1] + ["", block] + lines[idx[0] + 1:]
            content = "\n".join(new_lines)
        else:
            content = current.rstrip("\n") + "\n\n" + block + "\n"
        actions.append({"dst": a["dst"], "kind": "append", "action": "APPEND",
                        "content": content, "eol": detect_eol(raw, manifest["eol"])})
    # Wikilinks in content we would write must resolve to vault notes or managed notes.
    missing = []
    for act in actions:
        if "content" not in act:
            continue
        for link in sorted(set(WIKILINK.findall(FENCE.sub("", act["content"])))):
            rel = link.strip()
            candidates = [rel, rel + ".md"]
            if not any((vault / c).is_file() or c in managed_targets for c in candidates):
                missing.append({"in": act["dst"], "link": rel})
    return actions, missing


def apply(actions, vault: Path):
    written = []
    for act in actions:
        if act["action"] not in {"CREATE", "UPDATE", "APPEND"}:
            continue
        target = inside(vault, act["dst"])
        target.parent.mkdir(parents=True, exist_ok=True)
        data = to_eol(act["content"], act["eol"]).encode("utf-8")
        tmp = target.with_name(target.name + ".ags-tmp")
        tmp.write_bytes(data)
        tmp.replace(target)
        written.append({"dst": act["dst"], "action": act["action"],
                        "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)})
    return written


def summary(actions, missing, written=None):
    counts = {}
    for a in actions:
        counts[a["action"]] = counts.get(a["action"], 0) + 1
    return {"report_schema": "ags.obsidian-sync-report/1",
            "counts": dict(sorted(counts.items())),
            "actions": [{k: v for k, v in a.items() if k not in {"content", "eol"}} for a in actions],
            "missing_wikilinks": missing,
            "written": written or [],
            "vault": "<vault>"}


def emit(text):
    buf = getattr(sys.stdout, "buffer", None)
    if buf is not None:
        sys.stdout.flush()
        buf.write(text.encode("utf-8"))
        buf.flush()
    else:
        sys.stdout.write(text)


def dump(value):
    return json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description="Sort canonical studio notes into an Obsidian vault")
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--vault", type=Path, required=True, help="explicit Obsidian vault root")
    parser.add_argument("--apply", action="store_true", help="write; default is a dry run")
    parser.add_argument("--receipt", type=Path, help="write a JSON receipt inside this module directory")
    args = parser.parse_args(argv)
    try:
        manifest = load_manifest(args.manifest)
        actions, missing = plan(manifest, args.vault)
        conflicts = [a for a in actions if a["action"] == "CONFLICT"]
        written = None
        if args.apply:
            if conflicts or missing:
                emit(dump({"status": "REFUSED", "reason": "resolve conflicts/missing links first",
                           **summary(actions, missing)}))
                return 1
            written = apply(actions, args.vault)
        report = {"status": "APPLIED" if args.apply else "DRY_RUN", "date": date.today().isoformat(),
                  **summary(actions, missing, written)}
        if args.receipt:
            target = args.receipt.resolve()
            if MODULE_ROOT.parent not in target.parents or target.suffix != ".json":
                raise SyncError("Receipt must be a .json file inside the research module")
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(dump(report), encoding="utf-8", newline="\n")
        emit(dump(report))
        return 1 if conflicts or missing else 0
    except SyncError as exc:
        emit(dump({"status": "INVALID", "error": str(exc)}))
        return 2


if __name__ == "__main__":
    sys.exit(main())
