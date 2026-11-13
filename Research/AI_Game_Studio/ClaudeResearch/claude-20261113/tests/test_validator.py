"""Unit tests for validator.py.

In-memory registries below are test data. Some set registry "synthetic": false
only to exercise the PASS path of the rules; they are never written to
examples/ and never describe real project results.
"""
from __future__ import annotations

import ast
import copy
from datetime import date
import hashlib
import io
import json
from contextlib import redirect_stdout
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

MODULE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MODULE))
import validator as V  # noqa: E402

MINI = MODULE / "tests" / "fixtures" / "mini_catalog.synthetic.json"
REAL_CATALOG = MODULE.parents[1] / "Orchestrator" / "COMPONENT_REQUIREMENTS.json"
REAL_CHAIN = MODULE / "examples" / "portfolio_chain.real.json"
TRACE = MODULE / "COMPONENT_TRACEABILITY.json"
AS_OF = date(2026, 11, 13)


def mini_hash():
    return V.sha256_lf(MINI.read_bytes())


def base():
    h = mini_hash()
    return {
        "schema": V.REGISTRY_SCHEMA, "registry_id": "unit-test", "synthetic": False, "as_of": "2026-11-13",
        "entities": [
            {"type": "Source", "id": "SRC-A", "title": "doc", "locator": "https://example.org/doc",
             "source_kind": "official_doc", "author": None, "published": None, "accessed": "2026-11-13",
             "version": None, "access_method": "web_fetch", "study_depth": "full_text", "relevance": 1.0,
             "reliability": "primary", "rights": None, "limitations": ["test"]},
            {"type": "Claim", "id": "CLM-A", "title": "a", "statement": "A holds", "claim_kind": "fact",
             "source_refs": [{"source": "SRC-A", "locator": "#s1"}], "scope": "test", "confidence": None,
             "verification": "source_read", "limitations": []},
            {"type": "Claim", "id": "CLM-B", "title": "b", "statement": "not A", "claim_kind": "fact",
             "source_refs": [{"source": "SRC-A", "locator": "#s2"}], "scope": "test", "confidence": "low",
             "confidence_basis": "single read", "verification": "source_read", "limitations": []},
            {"type": "RequirementRef", "id": "C01-R02", "title": "req", "component": "C01", "gate": "G2",
             "catalog": {"path": "tests/fixtures/mini_catalog.synthetic.json", "revision": "test", "sha256_lf": h},
             "checked_property": "p", "accepted_evidence_classes": ["automated_test"], "target_revision": "r2"},
            {"type": "Artifact", "id": "AFT-A", "title": "build", "kind": "build", "locator": "builds/a.zip",
             "revision": "r2", "sha256": None, "origin": "test",
             "license": {"status": "verified", "terms": "MIT", "scope": "code"}, "lifecycle": "verified"},
            {"type": "Evidence", "id": "EVD-A", "title": "tests", "criterion": "c", "artifact_ref": "AFT-A",
             "artifact_revision": "r2", "evidence_class": "automated_test", "method": "unittest",
             "evidence_status": "executed", "result": "PASS", "date": "2026-11-13", "valid_until": "2026-12-13",
             "producer": "p1", "reviewer": None, "observations": "", "publication": "public_ok", "limitations": []},
            {"type": "Capability", "id": "CAP-A", "title": "tool", "name": "tool", "cap_kind": "cli",
             "location": None, "version": None, "owner_agent": "codex", "inputs": [], "outputs": [],
             "declared": True, "available": "not_checked", "benchmarked": "none", "invocation_verified": False,
             "dependencies": [], "limitations": []},
            {"type": "WorkPacket", "id": "WP-A", "title": "a", "goal": "g", "owner": "codex",
             "owned_paths": ["Portfolio/x/"], "inputs": ["i"], "outputs": ["o"], "acceptance": ["a"],
             "dependencies_note": "n", "stop_conditions": ["s"], "evidence_plan": ["e"], "handoff": "h",
             "wp_status": "proposed"},
            {"type": "WorkPacket", "id": "WP-B", "title": "b", "goal": "g", "owner": "claude",
             "owned_paths": ["Research/y/**"], "inputs": ["i"], "outputs": ["o"], "acceptance": ["a"],
             "dependencies_note": "n", "stop_conditions": ["s"], "evidence_plan": ["e"], "handoff": "h",
             "wp_status": "proposed"},
        ],
        "edges": [{"type": "verified_by", "from": "C01-R02", "to": "EVD-A"}],
    }


def ent(reg, eid):
    return next(e for e in reg["entities"] if e["id"] == eid)


def catalog():
    return V.load_catalog(MINI)


def run(reg, cat=True):
    return V.assess(copy.deepcopy(reg), catalog() if cat else None, AS_OF)


def codes(report, code):
    return [f for f in report["findings"] if f["code"] == code]


class ContractTests(unittest.TestCase):
    def test_base_registry_passes_requirement(self):
        r = run(base())
        self.assertEqual(r["requirements"]["C01-R02"]["verdict"], "PASS")
        self.assertFalse(r["is_authorization"])
        self.assertFalse(r["authenticates_evidence"])

    def test_duplicate_id_rejected(self):
        reg = base()
        reg["entities"].append(copy.deepcopy(ent(reg, "CLM-A")))
        with self.assertRaisesRegex(V.RegistryError, "Duplicate entity id"):
            V.validate_registry(reg)

    def test_unknown_edge_type_and_wrong_endpoints(self):
        reg = base()
        reg["edges"].append({"type": "causes", "from": "CLM-A", "to": "CLM-B"})
        with self.assertRaisesRegex(V.RegistryError, "unknown edge type"):
            V.validate_registry(reg)
        reg = base()
        reg["edges"].append({"type": "verified_by", "from": "C01-R02", "to": "CLM-A"})
        with self.assertRaisesRegex(V.RegistryError, "not allowed from RequirementRef to Claim"):
            V.validate_registry(reg)

    def test_dangling_references(self):
        reg = base()
        reg["edges"].append({"type": "supports", "from": "CLM-A", "to": "DEC-MISSING"})
        with self.assertRaisesRegex(V.RegistryError, "dangling"):
            V.validate_registry(reg)
        reg = base()
        ent(reg, "CLM-A")["source_refs"][0]["source"] = "SRC-NOPE"
        with self.assertRaisesRegex(V.RegistryError, "missing Source"):
            V.validate_registry(reg)

    def test_requirement_must_exist_in_catalog(self):
        reg = base()
        req = ent(reg, "C01-R02")
        req["id"] = "C01-R09"
        reg["edges"][0]["from"] = "C01-R09"
        r = run(reg)
        self.assertTrue(codes(r, "REQ_UNKNOWN_IN_CATALOG"))
        self.assertEqual(r["finding_counts"].get("error"), 1)

    def test_invented_requirement_numbering_rejected(self):
        reg = base()
        ent(reg, "C01-R02")["id"] = "REQ-1"
        with self.assertRaisesRegex(V.RegistryError, "existing Cxx-Rxx"):
            V.validate_registry(reg)

    def test_catalog_revision_mismatch_and_missing_catalog_stay_open(self):
        reg = base()
        ent(reg, "C01-R02")["catalog"]["sha256_lf"] = "0" * 64
        self.assertTrue(codes(run(reg), "CATALOG_HASH_MISMATCH"))
        self.assertTrue(codes(run(base(), cat=False), "CATALOG_NOT_SUPPLIED"))

    def test_requirement_text_is_not_duplicated(self):
        reg = base()
        ent(reg, "C01-R02")["statement"] = "copied text"
        with self.assertRaisesRegex(V.RegistryError, "unsupported keys"):
            V.validate_registry(reg)

    def test_relevance_is_categorical_not_probability(self):
        reg = base()
        ent(reg, "SRC-A")["relevance"] = 0.95
        with self.assertRaisesRegex(V.RegistryError, "categorical"):
            V.validate_registry(reg)

    def test_video_metadata_cannot_claim_timecodes(self):
        reg = base()
        s = ent(reg, "SRC-A")
        s.update(source_kind="video", access_method="oembed_metadata", study_depth="metadata",
                 locator="https://www.youtube.com/watch?v=x",
                 video_layers={"metadata": True, "transcript": False, "reproduced": False,
                               "viewed_fragments": [{"start": "00:01:00", "end": "00:02:00",
                                                     "observed_by": "x", "observed_on": "2026-11-13"}]})
        with self.assertRaisesRegex(V.RegistryError, "metadata-only"):
            V.validate_registry(reg)

    def test_community_contact_is_rejected(self):
        reg = base()
        s = ent(reg, "SRC-A")
        s.update(source_kind="community", community={"specialization": "x", "practice_access": "x",
                                                     "signal_vs_ads": "x", "usage_rules": "x", "contacted": True})
        with self.assertRaisesRegex(V.RegistryError, "contacted must be false"):
            V.validate_registry(reg)


class GraphTests(unittest.TestCase):
    def test_dependency_cycle_blocks_order(self):
        reg = base()
        reg["edges"] += [{"type": "depends_on", "from": "WP-A", "to": "WP-B"},
                         {"type": "depends_on", "from": "WP-B", "to": "WP-A"}]
        r = run(reg)
        cyc = codes(r, "DEPENDENCY_CYCLE")
        self.assertEqual(len(cyc), 1)
        self.assertIsNone(r["execution_order"])
        self.assertEqual(r["finding_counts"]["error"], 1)

    def test_knowledge_feedback_loop_is_accepted(self):
        reg = base()
        reg["entities"].append({"type": "Decision", "id": "DEC-A", "title": "d", "question": "q",
                                "options": ["x", "y"], "assumptions": [], "decision": "x",
                                "decision_status": "accepted", "unknowns": [], "owner": "o", "revisit_when": []})
        # Evidence supports a claim, claim supports decision, a packet derives from the decision and
        # produced the artifact that the evidence checks: a legitimate feedback loop across edge types.
        reg["edges"] += [{"type": "supports", "from": "EVD-A", "to": "CLM-A"},
                         {"type": "supports", "from": "CLM-A", "to": "DEC-A"},
                         {"type": "derived_from", "from": "WP-A", "to": "DEC-A"},
                         {"type": "produced_by", "from": "AFT-A", "to": "WP-A"},
                         {"type": "verified_by", "from": "AFT-A", "to": "EVD-A"}]
        r = run(reg)
        self.assertFalse(codes(r, "DEPENDENCY_CYCLE"))
        self.assertFalse(codes(r, "PROVENANCE_CYCLE"))
        self.assertEqual(r["requirements"]["C01-R02"]["verdict"], "PASS")

    def test_provenance_cycle_is_error(self):
        reg = base()
        reg["edges"] += [{"type": "derived_from", "from": "CLM-A", "to": "CLM-B"},
                         {"type": "derived_from", "from": "CLM-B", "to": "CLM-A"}]
        self.assertTrue(codes(run(reg), "PROVENANCE_CYCLE"))

    def test_ownership_overlap_flagged(self):
        reg = base()
        ent(reg, "WP-B")["owned_paths"] = ["Portfolio/x/sub/file.cs"]
        self.assertTrue(codes(run(reg), "OWNERSHIP_OVERLAP"))
        self.assertFalse(codes(run(base()), "OWNERSHIP_OVERLAP"))


class EvidenceTests(unittest.TestCase):
    def verdict(self, mutate):
        reg = base()
        mutate(reg)
        r = run(reg)
        return r["requirements"]["C01-R02"], r

    def test_missing_evidence_open(self):
        v, _ = self.verdict(lambda reg: reg.__setitem__("edges", []))
        self.assertEqual(v["verdict"], "OPEN")

    def test_other_revision_expired_future_stale_invalidated_stay_open(self):
        cases = {
            "revision_mismatch": lambda r: ent(r, "EVD-A").__setitem__("artifact_revision", "r1"),
            "expired": lambda r: ent(r, "EVD-A").__setitem__("valid_until", "2026-11-12") or
                                  ent(r, "EVD-A").__setitem__("date", "2026-11-01"),
            "future_dated": lambda r: ent(r, "EVD-A").update(date="2026-11-20", valid_until="2026-12-20"),
            "record_status=stale": lambda r: ent(r, "EVD-A").__setitem__("record_status", "stale"),
            "target_revision_unknown": lambda r: ent(r, "C01-R02").__setitem__("target_revision", None),
            "no_validity_limit_recorded": lambda r: ent(r, "EVD-A").__setitem__("valid_until", None),
        }
        for reason, mutate in cases.items():
            with self.subTest(reason):
                v, _ = self.verdict(mutate)
                self.assertEqual(v["verdict"], "OPEN")
                self.assertTrue(any(reason in x for x in v["evidence"][0]["reasons"]), v)
        def invalidate(r):
            r["entities"].append({"type": "Evidence", "id": "EVD-NEW", "title": "n", "criterion": "c",
                                  "artifact_ref": None, "artifact_revision": None, "evidence_class": "automated_test",
                                  "method": "m", "evidence_status": "executed", "result": "FAIL",
                                  "date": "2026-11-13", "valid_until": None, "producer": "p", "reviewer": None,
                                  "observations": "", "publication": "public_ok", "limitations": []})
            r["edges"].append({"type": "invalidates", "from": "EVD-NEW", "to": "EVD-A"})
        v, _ = self.verdict(invalidate)
        self.assertEqual(v["verdict"], "OPEN")
        self.assertIn("EVD-A:invalidated_by=EVD-NEW", v["evidence"][0]["reasons"])

    def test_synthetic_prepared_historical_metadata_never_pass(self):
        cases = {
            "synthetic_evidence": lambda r: ent(r, "EVD-A").__setitem__("synthetic", True),
            "synthetic_registry": lambda r: r.__setitem__("synthetic", True),
            "synthetic_artifact": lambda r: ent(r, "AFT-A").__setitem__("synthetic", True),
            "prepared": lambda r: ent(r, "EVD-A").update(evidence_status="prepared", result="UNKNOWN"),
            "historical": lambda r: ent(r, "EVD-A").__setitem__("evidence_status", "historical_import"),
            "metadata": lambda r: (ent(r, "EVD-A").__setitem__("evidence_class", "metadata_review"),
                                   ent(r, "C01-R02")["accepted_evidence_classes"].append("metadata_review")),
            "bot_not_human": lambda r: ent(r, "C01-R02").__setitem__("accepted_evidence_classes", ["human_playtest"]),
        }
        for name, mutate in cases.items():
            with self.subTest(name):
                v, _ = self.verdict(mutate)
                self.assertEqual(v["verdict"], "OPEN", name)
        v, _ = self.verdict(cases["historical"])
        self.assertTrue(v["historical_pass"])

    def test_prepared_with_pass_result_is_rejected(self):
        reg = base()
        ent(reg, "EVD-A")["evidence_status"] = "prepared"
        with self.assertRaisesRegex(V.RegistryError, "must have result UNKNOWN"):
            V.validate_registry(reg)

    def test_qualifying_fail_beats_pass(self):
        def add_fail(r):
            e = copy.deepcopy(ent(r, "EVD-A"))
            e.update(id="EVD-F", result="FAIL")
            r["entities"].append(e)
            r["edges"].append({"type": "verified_by", "from": "C01-R02", "to": "EVD-F"})
        v, _ = self.verdict(add_fail)
        self.assertEqual(v["verdict"], "FAIL")

    def test_independent_review_required(self):
        def need(r):
            ent(r, "C01-R02")["requires_independent_review"] = True
            ent(r, "EVD-A")["reviewer"] = "p1"
        v, _ = self.verdict(need)
        self.assertEqual(v["verdict"], "OPEN")
        self.assertIn("no_independent_reviewer", v["evidence"][0]["reasons"])


class KnowledgeAndRightsTests(unittest.TestCase):
    def test_contradiction_kept_open_then_resolved(self):
        reg = base()
        reg["edges"].append({"type": "contradicts", "from": "CLM-A", "to": "CLM-B"})
        r = run(reg)
        self.assertTrue(codes(r, "CONTRADICTION_OPEN"))
        self.assertEqual(r["entity_counts"]["Claim"], 2)  # both retained
        reg["entities"].append({"type": "Decision", "id": "DEC-R", "title": "r", "question": "q",
                                "options": ["A", "not A"], "assumptions": [], "decision": "A in scope X",
                                "decision_status": "proposed", "unknowns": [], "owner": "o",
                                "revisit_when": [], "resolves": ["CLM-A", "CLM-B"]})
        self.assertTrue(codes(run(reg), "CONTRADICTION_OPEN"))  # proposed does not resolve
        ent(reg, "DEC-R")["decision_status"] = "accepted"
        r = run(reg)
        self.assertTrue(codes(r, "CONTRADICTION_RESOLVED"))
        self.assertEqual(r["contradictions"][0]["resolved_by"], ["DEC-R"])

    def test_null_stays_unknown_not_zero(self):
        r = run(base())
        u = [x for x in r["unknown_values"] if x["id"] == "CLM-A" and x["field"] == "confidence"]
        self.assertEqual(u, [{"id": "CLM-A", "field": "confidence", "value": None}])
        text = V.dump(r)
        self.assertIn('"value": null', text)

    def test_numeric_strings_and_booleans_are_not_numbers(self):
        reg = base()
        reg["entities"].append({"type": "Experiment", "id": "EXP-A", "title": "e", "question": "q",
                                "hypothesis": "h", "baseline": "b", "protocol": "p", "success_criterion": None,
                                "criterion_fixed_on": None, "sample": {"n": True, "unit": "run"},
                                "resource_bounds": {"cash": None, "currency": None, "hours": None},
                                "experiment_status": "proposed", "results": None, "limitations": []})
        with self.assertRaisesRegex(V.RegistryError, "booleans/strings rejected"):
            V.validate_registry(reg)
        ent(reg, "EXP-A")["sample"]["n"] = "3"
        with self.assertRaisesRegex(V.RegistryError, "booleans/strings rejected"):
            V.validate_registry(reg)

    def test_rights_unknown_and_accepted_without_human(self):
        reg = base()
        a = ent(reg, "AFT-A")
        a["license"]["status"] = "unknown"
        a["lifecycle"] = "accepted"
        r = run(reg)
        for code in ("RIGHTS_UNKNOWN", "RIGHTS_BLOCK_ACCEPTANCE", "ACCEPTANCE_UNSUPPORTED"):
            self.assertTrue(codes(r, code), code)

    def test_synthetic_accepted_is_error(self):
        reg = base()
        ent(reg, "AFT-A").update(lifecycle="accepted", synthetic=True)
        self.assertTrue(codes(run(reg), "SYNTHETIC_ACCEPTED"))

    def test_capability_states(self):
        r = run(base())
        self.assertEqual(r["capabilities"][0]["state"], "declared_only")
        self.assertTrue(codes(r, "CAPABILITY_DECLARED_ONLY"))
        reg = base()
        ent(reg, "CAP-A")["invocation_verified"] = True
        self.assertTrue(codes(run(reg), "CAPABILITY_INCONSISTENT"))
        reg = base()
        ent(reg, "CAP-A")["available"] = "unavailable"
        self.assertTrue(codes(run(reg), "CAPABILITY_UNAVAILABLE"))

    def test_queries(self):
        reg = base()
        reg["entities"].append({"type": "Decision", "id": "DEC-A", "title": "d", "question": "q",
                                "options": ["x", "y"], "assumptions": [], "decision": "x",
                                "decision_status": "accepted", "unknowns": [], "owner": "o", "revisit_when": []})
        reg["edges"] += [{"type": "supports", "from": "CLM-A", "to": "DEC-A"},
                         {"type": "supports", "from": "EVD-A", "to": "CLM-A"},
                         {"type": "produced_by", "from": "AFT-A", "to": "CAP-A"}]
        nodes, edges = V.validate_registry(reg)
        why = V.query_why(nodes, edges, "DEC-A")
        self.assertEqual({w["basis"] for w in why}, {"CLM-A", "EVD-A", "SRC-A"})
        impact = {x["id"] for x in V.query_impact(nodes, edges, "CAP-A")}
        self.assertEqual(impact, {"AFT-A", "EVD-A", "C01-R02", "CLM-A", "DEC-A"})


class SafetyTests(unittest.TestCase):
    def test_unsafe_locators(self):
        bad = ["../secret.txt", "a/../../b", "/etc/passwd", "C:/Users/x", "C:\\Users\\x", "~/x",
               "file:///etc/passwd", "http://example.org", "https://user:pw@example.org/x", "a\\b"]
        for loc in bad:
            with self.subTest(loc):
                with self.assertRaises(V.RegistryError):
                    V.check_locator(loc, "t")
        self.assertEqual(V.check_locator("Portfolio/a.json", "t"), "path")
        self.assertEqual(V.check_locator("https://example.org/a", "t"), "url")

    def test_unknown_schema_and_malformed_inputs(self):
        reg = base()
        reg["schema"] = "ags.research-registry/2"
        with self.assertRaisesRegex(V.RegistryError, "Unsupported registry schema"):
            V.validate_registry(reg)
        with tempfile.TemporaryDirectory() as d:
            for name, text, msg in [("dup.json", '{"a":1,"a":2}', "Duplicate JSON key"),
                                    ("nan.json", '{"a": NaN}', "Non-finite"),
                                    ("trunc.json", '{"a": [1,', "malformed JSON")]:
                p = Path(d) / name
                p.write_text(text, encoding="utf-8")
                with self.assertRaisesRegex(V.RegistryError, msg):
                    V.load_json(p)
            with self.assertRaisesRegex(V.RegistryError, "not a readable file"):
                V.load_json(Path(d) / "missing.json")

    def test_cli_invalid_input_exit_code_and_message(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "bad.json"
            p.write_text(json.dumps({"schema": "nope"}), encoding="utf-8")
            buf = io.StringIO()
            with redirect_stdout(buf):
                code = V.main(["check", str(p)])
            self.assertEqual(code, 2)
            out = json.loads(buf.getvalue())
            self.assertEqual(out["status"], "INVALID")
            self.assertIn("Unsupported registry schema", out["error"])

    def test_output_containment_is_separate_from_locator_check(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaisesRegex(V.RegistryError, "outside module directory"):
                V.safe_output(Path(d) / "r.json", [], False)
        with self.assertRaisesRegex(V.RegistryError, "outside module directory"):
            V.safe_output(MODULE / ".." / "escape.json", [], False)
        with self.assertRaisesRegex(V.RegistryError, "overwrite an input"):
            V.safe_output(REAL_CHAIN, [REAL_CHAIN], True)
        with self.assertRaisesRegex(V.RegistryError, "pass --overwrite"):
            V.safe_output(MODULE / "COMPONENT_TRACEABILITY.md", [], False)
        with self.assertRaisesRegex(V.RegistryError, ".json or .md"):
            V.safe_output(MODULE / "Evidence" / "x.py", [], False)

    def test_artifact_hash_check_is_read_only_and_contained(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "builds").mkdir()
            (root / "builds" / "a.zip").write_bytes(b"abc")
            reg = base()
            ent(reg, "AFT-A")["sha256"] = hashlib.sha256(b"abc").hexdigest()
            r = V.assess(copy.deepcopy(reg), catalog(), AS_OF, root)
            self.assertTrue(codes(r, "ARTIFACT_HASH_MATCH"))
            ent(reg, "AFT-A")["sha256"] = "0" * 64
            self.assertTrue(codes(V.assess(copy.deepcopy(reg), catalog(), AS_OF, root), "ARTIFACT_HASH_MISMATCH"))
            ent(reg, "AFT-A")["locator"] = "builds/missing.zip"
            self.assertTrue(codes(V.assess(copy.deepcopy(reg), catalog(), AS_OF, root), "ARTIFACT_MISSING"))

    def test_resolved_root_escape_detected_even_if_lexical_check_bypassed(self):
        """Defence in depth: containment is checked on the resolved path, independently."""
        with tempfile.TemporaryDirectory() as d:
            root = Path(d) / "root"
            root.mkdir()
            (Path(d) / "secret.bin").write_bytes(b"s")
            reg = base()
            ent(reg, "AFT-A").update(locator="../secret.bin", sha256="0" * 64)
            original = V.check_locator
            bypass = lambda value, where: "path" if value.startswith("..") else original(value, where)
            with mock.patch.object(V, "check_locator", side_effect=bypass):
                with self.assertRaisesRegex(V.RegistryError, "escapes artifact root"):
                    V.assess(copy.deepcopy(reg), catalog(), AS_OF, root)

    def test_symlink_escape_detected(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "builds").mkdir()
            reg = base()
            ent(reg, "AFT-A")["sha256"] = "0" * 64
            outside = Path(d).parent / f"outside-{root.name}.bin"
            link = root / "builds" / "link.bin"
            outside.write_bytes(b"x")
            try:
                link.symlink_to(outside)
            except (OSError, NotImplementedError):
                outside.unlink(missing_ok=True)
                self.skipTest("symlinks not permitted on this system (escape check not exercised)")
            try:
                ent(reg, "AFT-A")["locator"] = "builds/link.bin"
                with self.assertRaisesRegex(V.RegistryError, "escapes artifact root"):
                    V.assess(copy.deepcopy(reg), catalog(), AS_OF, root)
            finally:
                outside.unlink(missing_ok=True)

    def test_no_network_subprocess_or_gui_imports(self):
        tree = ast.parse((MODULE / "validator.py").read_text(encoding="utf-8"))
        imported = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported |= {a.name.split(".")[0] for a in node.names}
            elif isinstance(node, ast.ImportFrom):
                imported.add((node.module or "").split(".")[0])
        forbidden = {"socket", "subprocess", "urllib.request", "http", "requests", "webbrowser",
                     "tkinter", "ctypes", "os", "shutil", "multiprocessing", "asyncio"}
        self.assertFalse(imported & forbidden, imported & forbidden)

    def test_cli_runs_with_network_and_processes_blocked_and_inputs_unchanged(self):
        if not REAL_CATALOG.is_file():
            self.skipTest("real catalog absent")
        before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in (REAL_CHAIN, REAL_CATALOG, TRACE)}
        boom = mock.Mock(side_effect=AssertionError("forbidden side effect"))
        outputs = []
        with mock.patch.object(socket, "socket", boom), mock.patch.object(subprocess, "Popen", boom), \
                mock.patch("webbrowser.open", boom):
            for _ in range(2):
                buf = io.StringIO()
                with redirect_stdout(buf):
                    code = V.main(["check", str(REAL_CHAIN), "--catalog", str(REAL_CATALOG)])
                self.assertEqual(code, 0)
                outputs.append(buf.getvalue())
        self.assertEqual(outputs[0], outputs[1], "output must be reproducible")
        after = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in before}
        self.assertEqual(before, after)
        boom.assert_not_called()


class ReviewRegressionTests(unittest.TestCase):
    """Regressions for findings of the independent read-only review (2026-11-14)."""

    def verdict(self, mutate):
        reg = base()
        mutate(reg)
        return run(reg)["requirements"]["C01-R02"]

    def test_h1_superseded_by_edge_only_does_not_pass(self):
        def m(r):
            e = copy.deepcopy(ent(r, "EVD-A"))
            e.update(id="EVD-B", result="UNKNOWN")
            r["entities"].append(e)
            r["edges"].append({"type": "supersedes", "from": "EVD-B", "to": "EVD-A"})
        v = self.verdict(m)
        self.assertEqual(v["verdict"], "OPEN")
        self.assertTrue(any("superseded_by=EVD-B" in x for x in v["evidence"][0]["reasons"]))

    def test_h2_artifact_state_is_checked(self):
        def invalidate(r):
            r["entities"].append(dict(copy.deepcopy(ent(r, "AFT-A")), id="AFT-X"))
            r["edges"].append({"type": "invalidates", "from": "AFT-X", "to": "AFT-A"})
        cases = {
            "withdrawn": lambda r: ent(r, "AFT-A").__setitem__("record_status", "withdrawn"),
            "invalidated": invalidate,
            "artifact_revision_mismatch": lambda r: ent(r, "AFT-A").__setitem__("revision", "r1"),
            "no_artifact_ref": lambda r: ent(r, "EVD-A").__setitem__("artifact_ref", None),
        }
        for name, m in cases.items():
            with self.subTest(name):
                self.assertEqual(self.verdict(m)["verdict"], "OPEN")

    def test_h3_wrong_json_types_exit_2_not_traceback(self):
        mutations = [lambda r: ent(r, "CLM-A").__setitem__("type", ["Claim"]),
                     lambda r: ent(r, "CLM-A").__setitem__("record_status", ["x"]),
                     lambda r: ent(r, "CLM-A").__setitem__("claim_kind", ["fact"]),
                     lambda r: r["edges"].append({"type": ["x"], "from": "CLM-A", "to": "CLM-B"}),
                     lambda r: r["edges"].append({"type": "supports", "from": {}, "to": "CLM-B"})]
        with tempfile.TemporaryDirectory() as d:
            for i, m in enumerate(mutations):
                reg = base()
                m(reg)
                f = Path(d) / f"r{i}.json"
                f.write_text(json.dumps(reg), encoding="utf-8")
                buf = io.StringIO()
                with redirect_stdout(buf):
                    self.assertEqual(V.main(["check", str(f)]), 2)
                self.assertEqual(json.loads(buf.getvalue())["status"], "INVALID")

    def test_h4_deep_inputs_do_not_crash(self):
        with tempfile.TemporaryDirectory() as d:
            for name, text in [("nest.json", "[" * 50000 + "]" * 50000),
                               ("bigint.json", '{"schema": ' + "9" * 5000 + "}")]:
                f = Path(d) / name
                f.write_text(text, encoding="utf-8")
                with self.assertRaises(V.RegistryError):
                    V.load_json(f)
        reg = base()
        wp = ent(reg, "WP-A")
        for i in range(1100):
            reg["entities"].append(dict(copy.deepcopy(wp), id=f"WP-N{i:04d}", owner="codex"))
            if i:
                reg["edges"].append({"type": "depends_on", "from": f"WP-N{i:04d}", "to": f"WP-N{i - 1:04d}"})
        r = run(reg)
        self.assertFalse(codes(r, "DEPENDENCY_CYCLE"))
        self.assertEqual(len(r["execution_order"]), 1100)

    def test_m1_utf8_output_on_legacy_code_page(self):
        reg = base()
        reg["registry_id"] = "unit → test ✓"
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "u.json"
            f.write_text(json.dumps(reg, ensure_ascii=False), encoding="utf-8")
            raw = io.BytesIO()
            fake = io.TextIOWrapper(raw, encoding="cp1251")
            with mock.patch.object(sys, "stdout", fake):
                code = V.main(["check", str(f), "--catalog", str(MINI)])
            self.assertEqual(code, 0)
            self.assertIn("✓", json.loads(raw.getvalue().decode("utf-8"))["registry_id"])

    def test_m2_promises_ignore_noncurrent_evidence(self):
        reg = base()
        reg["entities"].append({"type": "Claim", "id": "CLM-P", "title": "p", "statement": "we can",
                                "claim_kind": "client_promise", "source_refs": [{"source": "SRC-A", "locator": "x"}],
                                "scope": "s", "confidence": None, "verification": "source_read", "limitations": []})
        reg["edges"].append({"type": "verified_by", "from": "CLM-P", "to": "EVD-A"})
        nodes, edges = V.validate_registry(copy.deepcopy(reg))
        self.assertEqual(V.query_promises(nodes, edges, run(reg))[0]["status"], "supported_by_current_checks")
        ent(reg, "EVD-A").update(valid_until="2026-11-13", date="2026-11-01", record_status="withdrawn")
        nodes, edges = V.validate_registry(copy.deepcopy(reg))
        r = V.assess(copy.deepcopy(reg), catalog(), date(2026, 12, 1))
        self.assertEqual(V.query_promises(nodes, edges, r, date(2026, 12, 1))[0]["status"], "unsupported")

    def test_m3_withdrawn_decision_does_not_resolve(self):
        reg = base()
        reg["edges"].append({"type": "contradicts", "from": "CLM-A", "to": "CLM-B"})
        reg["entities"].append({"type": "Decision", "id": "DEC-X", "title": "x", "question": "q",
                                "options": ["a", "b"], "assumptions": [], "decision": "a",
                                "decision_status": "accepted", "unknowns": [], "owner": "o",
                                "revisit_when": [], "resolves": ["CLM-A", "CLM-B"], "record_status": "withdrawn"})
        self.assertTrue(codes(run(reg), "CONTRADICTION_OPEN"))

    def test_m4_acceptance_requires_current_independent_evidence_for_this_artifact(self):
        reg = base()
        reg["entities"].append(dict(copy.deepcopy(ent(reg, "AFT-A")), id="AFT-B", lifecycle="accepted"))
        ent(reg, "EVD-A").update(evidence_class="human_art_acceptance", reviewer="p1")
        reg["edges"].append({"type": "verified_by", "from": "AFT-B", "to": "EVD-A"})
        self.assertTrue([f for f in codes(run(reg), "ACCEPTANCE_UNSUPPORTED") if f["subject"] == "AFT-B"])
        ent(reg, "EVD-A").update(artifact_ref="AFT-B", reviewer="user")
        self.assertFalse([f for f in codes(run(reg), "ACCEPTANCE_UNSUPPORTED") if f["subject"] == "AFT-B"])

    def test_m5_malformed_traceability_and_catalog(self):
        trace = {"schema": V.TRACE_SCHEMA, "catalog": {"path": "x.json", "revision": "r", "sha256_lf": mini_hash(),
                                                       "components": 2, "requirements": 3}, "rows": 5}
        with self.assertRaises(V.RegistryError):
            V.validate_traceability(trace, catalog())
        row = {"requirement": "C00-R01", "component": "C00", "gate": "G0", "r_blocks": 5, "coverage": "no_data",
               "module_checks": [], "input": "i", "output": "o", "check": "c", "next_consumer": "n", "note": "n"}
        trace["rows"] = [row]
        with self.assertRaises(V.RegistryError):
            V.validate_traceability(trace, catalog())
        row.update(r_blocks=["R01"], coverage=["no_data"])
        with self.assertRaises(V.RegistryError):
            V.validate_traceability(trace, catalog())
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "cat.json"
            f.write_text(json.dumps({"schema_version": 1, "components": [{"id": "C00", "requirements": 5}]}),
                         encoding="utf-8")
            with self.assertRaises(V.RegistryError):
                V.load_catalog(f)

    def test_m6_all_nulls_reported(self):
        reg = base()
        ent(reg, "EVD-A")["valid_until"] = None
        fields = {(u["id"], u["field"]) for u in run(reg)["unknown_values"]}
        for key in [("EVD-A", "valid_until"), ("EVD-A", "reviewer"), ("SRC-A", "author"), ("AFT-A", "sha256"),
                    ("CAP-A", "location")]:
            self.assertIn(key, fields)

    def test_l1_ownership_normalisation(self):
        for a, b in [("./Research/y", "Research/y/z"), ("**", "Research/y/z"), ("research/Y", "Research/y")]:
            with self.subTest((a, b)):
                reg = base()
                ent(reg, "WP-A")["owned_paths"] = [a]
                ent(reg, "WP-B")["owned_paths"] = [b]
                self.assertTrue(codes(run(reg), "OWNERSHIP_OVERLAP"))

    def test_l3_output_alternate_stream_and_overwrite_scope(self):
        with self.assertRaisesRegex(V.RegistryError, "alternate data streams"):
            V.safe_output(MODULE / "Evidence" / "r.json:x.json", [], False)
        with self.assertRaisesRegex(V.RegistryError, "Overwrite is limited"):
            V.safe_output(MODULE / "CONTRACTS.md", [], True)
        V.safe_output(MODULE / "COMPONENT_TRACEABILITY.md", [], True)  # permitted target; nothing is written

    def test_l4_reviewer_identity_is_case_insensitive(self):
        def m(r):
            ent(r, "C01-R02")["requires_independent_review"] = True
            ent(r, "EVD-A")["reviewer"] = " P1 "
        self.assertEqual(self.verdict(m)["verdict"], "OPEN")

    def test_l7_non_ascii_digits_rejected(self):
        self.assertIsNone(V.REQ_ID.fullmatch("C٠١-R٠٢"))


class RealDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not REAL_CATALOG.is_file():
            raise unittest.SkipTest("real catalog absent")
        cls.catalog = V.load_catalog(REAL_CATALOG)

    def test_catalog_counts_recomputed(self):
        self.assertEqual(len(self.catalog["components"]), 16)
        self.assertEqual(len(self.catalog["requirements"]), 49)

    def test_traceability_covers_every_requirement(self):
        result = V.validate_traceability(V.load_json(TRACE), self.catalog)
        self.assertEqual(result["status"], "CONSISTENT", result["problems"])
        self.assertEqual(result["matrix_rows"], len(self.catalog["requirements"]))
        self.assertEqual(len(result["components_covered"]), 16)

    def test_traceability_detects_missing_and_extra_rows(self):
        trace = V.load_json(TRACE)
        trace["rows"] = trace["rows"][1:]
        self.assertEqual(V.validate_traceability(trace, self.catalog)["status"], "INCONSISTENT")
        trace = V.load_json(TRACE)
        trace["rows"][0] = dict(trace["rows"][0], requirement="C99-R01")
        res = V.validate_traceability(trace, self.catalog)
        self.assertTrue(any("absent from catalog" in p for p in res["problems"]))
        trace = V.load_json(TRACE)
        row = next(r for r in trace["rows"] if r["coverage"] == "interface_only")
        row["module_checks"] = ["REQ_OPEN"]
        with self.assertRaisesRegex(V.RegistryError, "only module_check rows"):
            V.validate_traceability(trace, self.catalog)

    def test_real_portfolio_chain_keeps_unrun_gates_open(self):
        r = V.assess(V.load_json(REAL_CHAIN), self.catalog, date(2026, 11, 14))
        self.assertEqual(r["requirements"]["C01-R03"]["verdict"], "PASS")
        req = r["requirements"]
        self.assertEqual(req["C07-R03"]["verdict"], "OPEN")
        self.assertTrue(req["C07-R03"]["historical_pass"])
        self.assertEqual(req["C05-R03"]["verdict"], "OPEN")
        self.assertEqual(req["C08-R02"]["verdict"], "OPEN")
        self.assertEqual(r["finding_counts"].get("error", 0), 0)
        promises = V.query_promises(*V.validate_registry(V.load_json(REAL_CHAIN)), r)
        self.assertEqual(promises[0]["status"], "historical_support_only")

    def test_synthetic_fixtures_never_pass(self):
        for p in sorted((MODULE / "examples" / "synthetic").glob("*.json")):
            with self.subTest(p.name):
                data = V.load_json(p)
                self.assertTrue(data["synthetic"])
                try:
                    r = V.assess(data, self.catalog, AS_OF)
                except V.RegistryError:
                    continue  # negative fixtures are expected to be invalid
                self.assertNotIn("PASS", r["requirement_verdicts"])
                self.assertEqual(r["mode"], "synthetic_fixture")


if __name__ == "__main__":
    unittest.main()
