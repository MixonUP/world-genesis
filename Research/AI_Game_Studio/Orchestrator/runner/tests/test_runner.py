"""Synthetic decision-contract tests. No production receipts or QA are fabricated."""
import copy
from datetime import date
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import runner

AS_OF = date(2026, 11, 12)


def synthetic_ready():
    data = runner.template()
    data.update(synthetic=True, state="vertical_slice", producer_id="synthetic-producer")
    data["project"].update(id="SYNTHETIC-DEMO", name="SYNTHETIC ready example — not a real business",
                           audience="SYNTHETIC human-test cohort", build_id="SYNTHETIC-BUILD-1",
                           readiness="synthetic_only", historical_release_note="No real release or QA in this fixture")
    e = data["economics"]
    e.update(currency="USD", price_gross=10, refund_rate=0.1, platform_fee_rate=0.2,
             payment_fee_rate=0.05, tax_reserve_per_unit=0.25, variable_cash_cost_per_unit=1,
             variable_non_cash_cost_per_unit=0.5, fixed_cash_cost=100, fixed_non_cash_cost=50,
             planned_units=100, opening_cash=5000, additional_funding=0, peak_cash_need=1000,
             min_cash_buffer=100, api_actual_cash_total=7, api_equivalent_estimate=100)
    data["budget"] = {"currency": "USD", "cap": 1000, "spent": 10, "reserved": 25,
                      "period_start": "2026-11-01", "period_end": "2026-11-30", "scope": "synthetic_only"}
    for key, kinds in runner.REQUIREMENTS.items():
        kind = sorted(kinds)[0]
        if key == "native_qa":
            kind = "native_player_qa"
        if key == "playable":
            kind = "native_playtest"
        ref = "SYNTHETIC-" + key
        item = {"id": ref, "status": "verified", "kind": kind,
                "locator": "https://example.invalid/SYNTHETIC/" + key,
                "verified_by": "synthetic-reviewer", "verified_at": "2026-11-01",
                "expires_at": "2027-01-01", "note": "SYNTHETIC fixture; not real evidence"}
        if key == "demand":
            item["human_count"] = 5
        if key in runner.BUILD_REQUIREMENTS:
            item["build_id"] = data["project"]["build_id"]
        data["evidence"].append(item)
        data["requirements"][key] = [ref]
    data["review"] = {"reviewer_id": "synthetic-reviewer", "evidence_id": "SYNTHETIC-independent_review"}
    data["actions"] = [
        {"id": "SYNTHETIC-paid", "kind": "external_spend", "stage": "production",
         "estimated_cash_cost": 25, "reservation": 25,
         "authorization": {"granted": True, "action_id": "SYNTHETIC-paid", "max_cash_cost": 25,
                           "authorized_by": "synthetic-user"}},
        {"id": "SYNTHETIC-code", "kind": "reversible_local", "stage": "prototype",
         "estimated_cash_cost": 0, "reservation": 0, "authorization": None},
    ]
    return data


def evidence(data, name):
    return next(item for item in data["evidence"] if item["id"] == "SYNTHETIC-" + name)


class DecisionTests(unittest.TestCase):
    def setUp(self):
        self.data = synthetic_ready()

    def report(self):
        return runner.assess(self.data, ROOT, AS_OF)

    def test_ready_is_read_only_and_not_a_profit_guarantee(self):
        before = copy.deepcopy(self.data)
        result = self.report()
        self.assertEqual(self.data, before)
        self.assertEqual(result["allowed_next_states"], ["production"])
        self.assertEqual(result["actions"][0]["status"], "PASS")
        self.assertFalse(result["assessment_is_authorization"])
        self.assertFalse(result["completion_claim"])
        self.assertEqual(result["mode"], "synthetic_demo")

    def test_cash_economic_cost_and_ceil_break_even_are_distinct(self):
        econ = self.report()["economics"]
        self.assertEqual(econ["cash_contribution_per_unit"], Decimal("5.5"))
        self.assertEqual(econ["economic_contribution_per_unit"], Decimal("5"))
        self.assertEqual(econ["cash_break_even_units"], 19)
        self.assertEqual(econ["economic_break_even_units"], 30)
        self.assertEqual(econ["forecast_cash_result"], Decimal("450"))
        self.assertEqual(econ["forecast_economic_result"], Decimal("350"))

    def test_missing_demand_blocks_scaling_but_not_bounded_local_work(self):
        self.data["requirements"]["demand"] = []
        result = self.report()
        self.assertEqual(result["gates"]["G1"]["status"], "UNKNOWN")
        self.assertEqual(result["allowed_next_states"], [])
        self.assertEqual(result["actions"][0]["status"], "UNKNOWN")
        self.assertEqual(result["actions"][1]["status"], "PASS")
        self.assertTrue(any("prototype/R&D" in task for task in result["allowed_tasks_without_new_spend"]))

    def test_bounded_research_spend_has_own_authorization_not_a_demand_gate(self):
        self.data["requirements"]["demand"] = []
        self.data["actions"][0]["stage"] = "bounded_research"
        self.assertEqual(self.report()["actions"][0]["status"], "PASS")

    def test_bots_followers_and_zero_humans_do_not_prove_demand(self):
        for kind in ("bot_playtest", "follower_count"):
            with self.subTest(kind=kind):
                evidence(self.data, "demand")["kind"] = kind
                self.assertEqual(self.report()["gates"]["G1"]["status"], "FAIL")
        evidence(self.data, "demand").update(kind="human_validation", human_count=0)
        self.assertEqual(self.report()["gates"]["G1"]["status"], "FAIL")

    def test_verified_contract_is_an_alternative_to_human_test(self):
        evidence(self.data, "demand").update(kind="verified_contract", human_count=0)
        self.assertEqual(self.report()["gates"]["G1"]["status"], "PASS")

    def test_nonpositive_margin_has_no_finite_break_even(self):
        self.data["economics"]["variable_cash_cost_per_unit"] = 20
        result = self.report()
        self.assertEqual(result["gates"]["G3"]["status"], "FAIL")
        self.assertIsNone(result["economics"]["cash_break_even_units"])
        self.assertIsNone(result["economics"]["economic_break_even_units"])
        self.assertEqual(result["allowed_next_states"], [])

    def test_cash_positive_but_economic_negative_is_not_pass(self):
        self.data["economics"]["variable_non_cash_cost_per_unit"] = 6
        self.assertEqual(self.report()["gates"]["G3"]["status"], "FAIL")

    def test_economic_unit_must_be_declared_and_b2b_is_not_silently_supported(self):
        self.data["economics"]["unit_type"] = None
        self.assertEqual(self.report()["economics"]["status"], "UNKNOWN")
        self.data["economics"]["unit_type"] = "b2b_contract"
        with self.assertRaises(runner.ManifestError):
            runner.validate(self.data)

    def test_positive_unit_margin_does_not_hide_forecast_loss_or_zero_demand(self):
        for units in (0, 1, 19):
            with self.subTest(units=units):
                self.data["economics"]["planned_units"] = units
                result = self.report()
                self.assertTrue(result["economics"]["positive_unit_contribution"])
                self.assertEqual(result["gates"]["G3"]["status"], "FAIL")
                self.assertEqual(result["allowed_next_states"], [])
                self.assertEqual(result["actions"][0]["status"], "FAIL")

    def test_unknown_budget_blocks_spend_not_local_work_or_earlier_gate(self):
        self.data["budget"]["cap"] = None
        self.data["state"] = "prototype"
        result = self.report()
        self.assertEqual(result["allowed_next_states"], ["vertical_slice"])
        self.assertEqual(result["gates"]["G3"]["status"], "UNKNOWN")
        self.assertEqual(result["actions"][0]["status"], "UNKNOWN")
        self.assertEqual(result["actions"][1]["status"], "PASS")

    def test_monthly_budget_expiry_does_not_roll_over(self):
        result = runner.assess(self.data, ROOT, date(2026, 12, 1))
        self.assertEqual(result["gates"]["G3"]["checks"]["bounded_budget"]["status"], "FAIL")
        self.assertEqual(result["actions"][0]["status"], "FAIL")
        self.assertEqual(result["actions"][1]["status"], "PASS")

    def test_unknown_prior_spend_is_not_zero_or_free_monthly_allowance(self):
        self.data["budget"].update(cap=500, spent=None)
        result = self.report()
        self.assertEqual(result["gates"]["G3"]["checks"]["bounded_budget"]["status"], "UNKNOWN")
        self.assertEqual(result["actions"][0]["status"], "UNKNOWN")

    def test_expired_prepared_missing_or_wrong_build_evidence_does_not_pass(self):
        original = copy.deepcopy(self.data)
        for change in ({"status": "prepared"}, {"kind": None}, {"expires_at": "2026-11-11"},
                       {"verified_at": "2026-11-13"}, {"locator": "missing-SYNTHETIC.artifact"},
                       {"build_id": "SYNTHETIC-OLD-BUILD"}):
            with self.subTest(change=change):
                self.data = copy.deepcopy(original)
                evidence(self.data, "playable").update(change)
                self.assertEqual(self.report()["gates"]["G2"]["status"], "UNKNOWN")
                self.assertEqual(self.report()["allowed_next_states"], [])

    def test_local_artifact_digest_is_checked_read_only(self):
        with tempfile.TemporaryDirectory() as folder:
            artifact = Path(folder) / "SYNTHETIC-evidence.txt"
            artifact.write_text("SYNTHETIC fixture", encoding="utf-8")
            item = evidence(self.data, "rights")
            item.update(locator=artifact.name, sha256=hashlib.sha256(artifact.read_bytes()).hexdigest())
            self.assertEqual(runner.assess(self.data, folder, AS_OF)["gates"]["G3"]["status"], "PASS")
            artifact.write_text("SYNTHETIC changed fixture", encoding="utf-8")
            self.assertEqual(runner.assess(self.data, folder, AS_OF)["gates"]["G3"]["status"], "FAIL")

    def test_equivalent_estimate_cannot_replace_actual_api_cash(self):
        self.data["economics"]["api_actual_cash_total"] = None
        self.data["economics"]["api_equivalent_estimate"] = 9999
        result = self.report()
        self.assertEqual(result["gates"]["G2"]["status"], "UNKNOWN")
        self.assertEqual(result["economics"]["forecast_cash_result"], Decimal("450"))

    def test_insufficient_cash_plan_fails_even_when_profit_forecast_positive(self):
        self.data["economics"]["opening_cash"] = 10
        self.assertEqual(self.report()["gates"]["G3"]["status"], "FAIL")

    def test_no_spend_with_blanket_or_wrong_action_authorization(self):
        original = copy.deepcopy(self.data)
        for auth, expected in ((None, "UNKNOWN"),
                               ({"granted": False}, "FAIL"),
                               ({"granted": True, "action_id": "another", "max_cash_cost": 100, "authorized_by": "user"}, "FAIL"),
                               ({"granted": True, "action_id": "SYNTHETIC-paid", "max_cash_cost": 24, "authorized_by": "user"}, "FAIL")):
            with self.subTest(auth=auth):
                self.data = copy.deepcopy(original)
                self.data["actions"][0]["authorization"] = auth
                self.assertEqual(self.report()["actions"][0]["status"], expected)

    def test_reservation_and_total_cap_are_both_required(self):
        self.data["actions"][0]["reservation"] = None
        self.assertEqual(self.report()["actions"][0]["status"], "UNKNOWN")
        self.data["actions"][0]["reservation"] = 24
        self.assertEqual(self.report()["actions"][0]["status"], "FAIL")
        self.data["actions"][0]["reservation"] = 25
        self.data["budget"]["cap"] = 34
        result = self.report()
        self.assertEqual(result["actions"][0]["status"], "FAIL")
        self.assertEqual(result["actions"][0]["checks"]["budget"]["status"], "FAIL")

    def test_reservation_covers_max_authorized_exposure_not_only_estimate(self):
        self.data["actions"][0]["authorization"]["max_cash_cost"] = 30
        self.assertEqual(self.report()["actions"][0]["status"], "FAIL")
        self.data["actions"][0]["reservation"] = 30
        self.data["budget"]["reserved"] = 30
        self.assertEqual(self.report()["actions"][0]["status"], "PASS")

    def test_two_action_authorization_caps_cannot_exceed_shared_reservations(self):
        second = copy.deepcopy(self.data["actions"][0])
        second["id"] = "SYNTHETIC-paid-2"
        second["authorization"]["action_id"] = second["id"]
        self.data["actions"] = [self.data["actions"][0], second]
        for item in self.data["actions"]:
            item["authorization"]["max_cash_cost"] = 30
        self.data["budget"].update(cap=50, spent=0, reserved=50)
        result = self.report()
        self.assertEqual([item["status"] for item in result["actions"]], ["FAIL", "FAIL"])

    def test_native_qa_cannot_be_replaced_by_concept_or_other_platform(self):
        self.data["state"] = "production"
        for kind in ("concept_screenshot", "native_browser_qa"):
            with self.subTest(kind=kind):
                evidence(self.data, "native_qa")["kind"] = kind
                result = self.report()
                self.assertEqual(result["gates"]["G4"]["status"], "FAIL")
                self.assertEqual(result["allowed_next_states"], [])

    def test_duplicate_ids_negative_cost_self_review_and_guarantee_rejected(self):
        mutations = [lambda d: d["evidence"].append(copy.deepcopy(d["evidence"][0])),
                     lambda d: d["actions"].append(copy.deepcopy(d["actions"][0])),
                     lambda d: d["economics"].update(fixed_cash_cost=-1),
                     lambda d: d["review"].update(reviewer_id=d["producer_id"]),
                     lambda d: d["claims"].update(profit_guaranteed=True),
                     lambda d: d["requirements"].update(rights=["missing-ID"]),
                     lambda d: d["budget"].update(reserved=1)]
        for index, mutate in enumerate(mutations):
            with self.subTest(index=index):
                data = synthetic_ready()
                mutate(data)
                with self.assertRaises(runner.ManifestError):
                    runner.validate(data)

    def test_torn_state_claim_does_not_mutate_or_skip_missing_previous_gates(self):
        self.data["state"] = "production"
        self.data["requirements"]["demand"] = []
        result = self.report()
        self.assertEqual(result["original_state"], "production")
        self.assertEqual(self.data["state"], "production")
        self.assertEqual(result["state_integrity"], "UNKNOWN")
        self.assertEqual(result["allowed_next_states"], [])

    def test_release_and_operate_require_their_actual_evidence(self):
        self.data["state"] = "release_candidate"
        evidence(self.data, "payment")["status"] = "prepared"
        self.assertEqual(self.report()["allowed_next_states"], [])
        evidence(self.data, "payment")["status"] = "verified"
        self.assertEqual(self.report()["allowed_next_states"], ["released"])
        self.data["state"] = "released"
        evidence(self.data, "receipts")["kind"] = "projected_receipts"
        self.assertEqual(self.report()["gates"]["G6"]["status"], "FAIL")
        self.assertEqual(self.report()["allowed_next_states"], [])

    def test_on_hold_stopped_and_operate_never_mean_full_success(self):
        for state in ("on_hold", "stopped", "operate"):
            with self.subTest(state=state):
                self.data["state"] = state
                result = self.report()
                self.assertEqual(result["allowed_next_states"], [])
                self.assertFalse(result["completion_claim"])
                if state != "operate":
                    self.assertEqual(result["actions"][0]["status"], "FAIL")

    def test_init_preserves_history_with_shared_ceiling_and_unknown_actual_spend(self):
        data = runner.template()
        data["synthetic"] = True  # All test fixtures, including this copy, are labelled.
        runner.validate(data)
        self.assertEqual(data["project"]["engine"]["version"], "6000.0.45f1")
        self.assertIn("v0.10", data["project"]["historical_release_note"])
        self.assertIsNone(data["budget"]["cap"])
        self.assertIsNone(data["budget"]["spent"])
        self.assertIsNone(data["budget"]["reserved"])
        self.assertIn("shared", data["budget"]["scope"])
        self.assertEqual(data["evidence"], [])
        self.assertEqual(data["project"]["readiness"], "research_prepared")

    def test_cli_six_synthetic_scenarios_and_manifest_immutability(self):
        scenarios = {"ready": lambda d: None,
                     "missing-demand": lambda d: d["requirements"].update(demand=[]),
                     "negative-margin": lambda d: d["economics"].update(variable_cash_cost_per_unit=20),
                     "unknown-budget": lambda d: d["budget"].update(cap=None),
                     "stale-evidence": lambda d: evidence(d, "playable").update(expires_at="2020-01-01"),
                     "torn-state": lambda d: (d.update(state="production"), d["requirements"].update(demand=[]))}
        with tempfile.TemporaryDirectory() as folder:
            for name, mutate in scenarios.items():
                with self.subTest(name=name):
                    data = synthetic_ready()
                    mutate(data)
                    path = Path(folder) / ("SYNTHETIC-" + name + ".json")
                    path.write_text(json.dumps(data), encoding="utf-8")
                    before = path.read_bytes()
                    process = subprocess.run([sys.executable, str(ROOT / "runner.py"), "assess", str(path), "--as-of", "2026-11-12"],
                                             capture_output=True, text=True, encoding="utf-8", check=False)
                    self.assertEqual(process.returncode, 0, process.stderr)
                    result = json.loads(process.stdout)
                    self.assertEqual(result["mode"], "synthetic_demo")
                    self.assertEqual(bool(result["allowed_next_states"]), name == "ready")
                    self.assertEqual(path.read_bytes(), before)

    def test_cli_rejects_truncated_duplicate_key_and_nonfinite_json(self):
        with tempfile.TemporaryDirectory() as folder:
            for content in ('{"synthetic":true,', '{"synthetic":true,"state":"discovery","state":"production"}', '{"synthetic":true,"n":NaN}'):
                path = Path(folder) / "SYNTHETIC-invalid.json"
                path.write_text(content, encoding="utf-8")
                process = subprocess.run([sys.executable, str(ROOT / "runner.py"), "validate", str(path)],
                                         capture_output=True, text=True, check=False)
                self.assertEqual(process.returncode, 2)
                self.assertEqual(json.loads(process.stdout)["status"], "INVALID")


if __name__ == "__main__":
    unittest.main()
