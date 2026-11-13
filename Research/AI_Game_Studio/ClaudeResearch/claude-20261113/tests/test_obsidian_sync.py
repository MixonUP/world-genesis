"""Tests for orchestrator/obsidian_sync.py using temporary synthetic vaults only."""
from __future__ import annotations

import ast
import io
import json
from contextlib import redirect_stdout
from pathlib import Path
import sys
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MODULE / "orchestrator"))
import obsidian_sync as S  # noqa: E402

NOTE = "---\ntitle: Hub\ntype: project\ntags:\n- games\n---\n\n# Hub\n\nSee [[01_Project/Existing]].\n"
SECTION = "## 2026-11-14 — synthetic section\n\n- line\n"


class SyncTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.vault = root / "vault"
        (self.vault / "01_Project").mkdir(parents=True)
        (self.vault / "07_Journal").mkdir()
        (self.vault / "01_Project" / "Existing.md").write_bytes(b"# Existing\r\n\r\nhuman text\r\n")
        (self.vault / "00_HOME.md").write_bytes(b"# Home\r\n- anchor line\r\n- tail\r\n")
        self.pack = root / "pack"
        (self.pack / "notes").mkdir(parents=True)
        (self.pack / "notes" / "Hub.md").write_text(NOTE, encoding="utf-8")
        (self.pack / "notes" / "sec.md").write_text(SECTION, encoding="utf-8")
        (self.pack / "notes" / "home.md").write_text("- [[01_Project/Studio/Hub|Hub]]\n", encoding="utf-8")
        self.manifest = {
            "schema": S.MANIFEST_SCHEMA, "default_eol": "crlf",
            "allowed_prefixes": ["01_Project/Studio/", "01_Project/Existing.md", "07_Journal/", "00_HOME.md"],
            "managed": [{"src": "notes/Hub.md", "dst": "01_Project/Studio/Hub.md"}],
            "appends": [
                {"src": "notes/sec.md", "dst": "01_Project/Existing.md", "marker": "AGS_TEST_SECTION"},
                {"src": "notes/sec.md", "dst": "07_Journal/2026-11-14.md", "marker": "AGS_TEST_JOURNAL",
                 "create_title": "2026-11-14"},
                {"src": "notes/home.md", "dst": "00_HOME.md", "marker": "AGS_TEST_HOME", "anchor": "anchor line"},
            ],
        }
        self.write_manifest()

    def tearDown(self):
        self.tmp.cleanup()

    def write_manifest(self):
        self.mpath = self.pack / "manifest.json"
        self.mpath.write_text(json.dumps(self.manifest), encoding="utf-8")

    def run_cli(self, *extra):
        buf = io.StringIO()
        with redirect_stdout(buf):
            code = S.main([str(self.mpath), "--vault", str(self.vault), *extra])
        return code, json.loads(buf.getvalue())

    def snapshot(self):
        return {p.relative_to(self.vault).as_posix(): p.read_bytes() for p in self.vault.rglob("*") if p.is_file()}

    def test_dry_run_writes_nothing(self):
        before = self.snapshot()
        code, rep = self.run_cli()
        self.assertEqual(code, 0)
        self.assertEqual(rep["status"], "DRY_RUN")
        self.assertEqual(rep["counts"], {"APPEND": 2, "CREATE": 2})
        self.assertEqual(before, self.snapshot())

    def test_apply_is_idempotent_and_preserves_human_text_and_crlf(self):
        code, rep = self.run_cli("--apply")
        self.assertEqual(code, 0, rep)
        existing = (self.vault / "01_Project" / "Existing.md").read_bytes()
        self.assertTrue(existing.startswith(b"# Existing\r\n\r\nhuman text\r\n"))
        self.assertIn(b"<!-- AGS_TEST_SECTION -->\r\n", existing)
        hub = (self.vault / "01_Project" / "Studio" / "Hub.md").read_bytes()
        self.assertIn(b"managed_by: ags-studio-orchestrator\r\n", hub)
        self.assertNotIn(b"\n", hub.replace(b"\r\n", b""))
        home = (self.vault / "00_HOME.md").read_bytes().decode("utf-8").split("\r\n")
        self.assertEqual(home[1], "- anchor line")
        self.assertEqual(home[3], "<!-- AGS_TEST_HOME -->")
        self.assertEqual(home[-2], "- tail")
        after_first = self.snapshot()
        code, rep = self.run_cli("--apply")
        self.assertEqual(code, 0)
        self.assertEqual(rep["counts"], {"UNCHANGED": 4})
        self.assertEqual(after_first, self.snapshot())

    def test_source_change_updates_managed_note(self):
        self.run_cli("--apply")
        (self.pack / "notes" / "Hub.md").write_text(NOTE + "\nNew line.\n", encoding="utf-8")
        code, rep = self.run_cli("--apply")
        self.assertEqual(code, 0)
        self.assertIn({"action": "UPDATE", "dst": "01_Project/Studio/Hub.md", "kind": "managed"}, rep["actions"])

    def test_foreign_front_matter_key_is_preserved(self):
        self.run_cli("--apply")
        hub = self.vault / "01_Project" / "Studio" / "Hub.md"
        hub.write_bytes(hub.read_bytes().replace(b"managed_by:", b"permalink: studio/hub\r\nmanaged_by:", 1))
        code, rep = self.run_cli("--apply")
        self.assertEqual((code, rep["counts"]), (0, {"UNCHANGED": 4}))
        (self.pack / "notes" / "Hub.md").write_text(NOTE + "\nNew line.\n", encoding="utf-8")
        code, rep = self.run_cli("--apply")
        self.assertEqual(code, 0)
        text = hub.read_bytes()
        self.assertIn(b"permalink: studio/hub\r\n", text)
        self.assertIn(b"New line.", text)
        self.assertEqual(self.run_cli("--apply")[1]["counts"], {"UNCHANGED": 4})

    def test_human_edit_of_managed_note_is_conflict_and_untouched(self):
        self.run_cli("--apply")
        hub = self.vault / "01_Project" / "Studio" / "Hub.md"
        hub.write_bytes(hub.read_bytes() + b"my own note\r\n")
        edited = hub.read_bytes()
        (self.pack / "notes" / "Hub.md").write_text(NOTE + "\nNew line.\n", encoding="utf-8")
        code, rep = self.run_cli("--apply")
        self.assertEqual(code, 1)
        self.assertEqual(rep["status"], "REFUSED")
        self.assertEqual(hub.read_bytes(), edited)

    def test_existing_unmanaged_note_is_conflict(self):
        (self.vault / "01_Project" / "Studio").mkdir()
        (self.vault / "01_Project" / "Studio" / "Hub.md").write_text("# mine\n", encoding="utf-8")
        code, rep = self.run_cli()
        self.assertEqual(code, 1)
        self.assertEqual(rep["actions"][0]["action"], "CONFLICT")

    def test_missing_wikilink_blocks_apply(self):
        (self.pack / "notes" / "Hub.md").write_text(NOTE.replace("Existing", "Nope"), encoding="utf-8")
        code, rep = self.run_cli("--apply")
        self.assertEqual(code, 1)
        self.assertEqual(rep["missing_wikilinks"], [{"in": "01_Project/Studio/Hub.md", "link": "01_Project/Nope"}])
        self.assertFalse((self.vault / "01_Project" / "Studio").exists())

    def test_table_escaped_pipe_and_code_fence_links(self):
        text = NOTE + "\n| a |\n|---|\n| [[01_Project/Existing\\|alias]] |\n\n```\n[[not/a/link]]\n```\n"
        (self.pack / "notes" / "Hub.md").write_text(text, encoding="utf-8")
        code, rep = self.run_cli()
        self.assertEqual(rep["missing_wikilinks"], [])
        self.assertEqual(code, 0)

    def test_ambiguous_anchor_is_conflict(self):
        (self.vault / "00_HOME.md").write_bytes(b"- anchor line\r\n- anchor line again\r\n")
        code, rep = self.run_cli()
        self.assertEqual(code, 1)
        home = [a for a in rep["actions"] if a["dst"] == "00_HOME.md"][0]
        self.assertEqual(home["action"], "CONFLICT")

    def test_unsafe_paths_and_prefixes_rejected(self):
        for dst in ["../outside.md", "/abs.md", "C:/x.md", "01_Project\\x.md", "02_Other/x.md",
                    "01_Project/Studio/x.txt", "01_Project/Studio/a:b.md"]:
            with self.subTest(dst):
                self.manifest["managed"][0]["dst"] = dst
                self.write_manifest()
                code, rep = self.run_cli()
                self.assertEqual(code, 2)
                self.assertEqual(rep["status"], "INVALID")

    def test_reserved_front_matter_and_missing_front_matter(self):
        (self.pack / "notes" / "Hub.md").write_text(NOTE.replace("type: project", "type: project\nmanaged_by: x"),
                                                    encoding="utf-8")
        self.assertEqual(self.run_cli()[0], 2)
        (self.pack / "notes" / "Hub.md").write_text("# no front\n", encoding="utf-8")
        self.assertEqual(self.run_cli()[0], 2)

    def test_receipt_contained_and_no_absolute_vault_path(self):
        code, rep = self.run_cli("--receipt", str(Path(self.tmp.name) / "r.json"))
        self.assertEqual(code, 2)
        self.assertNotIn(str(self.vault), json.dumps(self.run_cli()[1]))

    def test_no_network_or_process_imports(self):
        tree = ast.parse((MODULE / "orchestrator" / "obsidian_sync.py").read_text(encoding="utf-8"))
        names = set()
        for n in ast.walk(tree):
            if isinstance(n, ast.Import):
                names |= {a.name.split(".")[0] for a in n.names}
            elif isinstance(n, ast.ImportFrom):
                names.add((n.module or "").split(".")[0])
        self.assertFalse(names & {"socket", "subprocess", "urllib", "http", "webbrowser", "os", "shutil"})


if __name__ == "__main__":
    unittest.main()
