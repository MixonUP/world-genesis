"""Tests for orchestrator/studio.py using the SYNTHETIC example_data only."""
from __future__ import annotations

import io
import json
from contextlib import redirect_stdout
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

MODULE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MODULE / "orchestrator"))
import obsidian_sync as S  # noqa: E402
import studio as ST  # noqa: E402

EXAMPLE = MODULE / "orchestrator" / "example_data"


class StudioTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.data = root / "data"
        shutil.copytree(EXAMPLE, self.data)
        self.vault = root / "vault"
        (self.vault / "01_Project").mkdir(parents=True)
        (self.vault / "01_Project" / "AI_Game_Studio_Research.md").write_text("# log\n", encoding="utf-8")
        (self.vault / "01_Project" / "Agent_Inbox.md").write_text("# inbox\n", encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def cli(self, mod, args):
        buf = io.StringIO()
        with redirect_stdout(buf):
            code = mod.main(args)
        return code, json.loads(buf.getvalue())

    def test_example_is_synthetic_and_public_safe(self):
        text = "".join(p.read_text(encoding="utf-8") for p in EXAMPLE.rglob("*") if p.is_file())
        self.assertIn("SYNTHETIC", text)
        for private in ("$500", "Армени", "Armenia", "OneDrive", "Михаил"):
            self.assertNotIn(private, text)

    def test_render_and_sync_roundtrip_idempotent(self):
        code, rep = self.cli(ST, ["render", "--data", str(self.data)])
        self.assertEqual((code, rep["status"]), (0, "RENDERED"), rep)
        manifest = str(self.data / "obsidian_pack" / "manifest.json")
        code, rep = self.cli(S, [manifest, "--vault", str(self.vault), "--apply"])
        self.assertEqual(code, 0, rep)
        dash = (self.vault / "01_Project" / "AI_Game_Studio" / "Dashboard.md").read_text(encoding="utf-8")
        self.assertIn("Внешний результат", dash)
        self.assertLess(dash.index("Внешний результат"), dash.index("Следующие шаги"))
        ready = dash.split("## Следующие шаги")[1].split("\n## ")[0]
        self.assertIn("T01", ready)
        self.assertNotIn("T02", ready)  # T02 depends on T01
        verdict = next((self.vault / "01_Project" / "AI_Game_Studio" / "Verdicts").glob("V01_*.md")).read_text(encoding="utf-8")
        self.assertIn("synthetic reviewer", verdict)
        code, rep = self.cli(ST, ["render", "--data", str(self.data)])
        code, rep = self.cli(S, [manifest, "--vault", str(self.vault), "--apply"])
        self.assertEqual(set(rep["counts"]), {"UNCHANGED"})

    def test_rendering_is_deterministic(self):
        data = ST.load_state(self.data / "studio_state.json")
        self.assertEqual(ST.render(data, self.data), ST.render(data, self.data))

    def test_state_validation_errors(self):
        state = json.loads((self.data / "studio_state.json").read_text(encoding="utf-8"))
        bad = [
            lambda s: s["tasks"][0].__setitem__("verdict", "V99"),
            lambda s: s["tasks"][0].__setitem__("depends_on", ["T02"]),  # cycle T01<->T02
            lambda s: s["verdicts"][0].__setitem__("status", "MAYBE"),
            lambda s: s["tasks"][0].__setitem__("owner", "stranger"),
            lambda s: s["verdicts"][1].__setitem__("id", "V01"),
        ]
        for mutate in bad:
            s = json.loads(json.dumps(state))
            mutate(s)
            (self.data / "studio_state.json").write_text(json.dumps(s), encoding="utf-8")
            code, rep = self.cli(ST, ["check", "--data", str(self.data)])
            self.assertEqual(code, 2, rep)


if __name__ == "__main__":
    unittest.main()
