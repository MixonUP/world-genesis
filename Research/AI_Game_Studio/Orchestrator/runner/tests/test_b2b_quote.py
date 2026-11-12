"""All B2B inputs and results are SYNTHETIC; no real customers or tax assumptions."""
import copy
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import b2b_quote
from runner import ManifestError, load_manifest


class B2BQuoteTests(unittest.TestCase):
    def setUp(self):
        self.path = ROOT / "examples" / "b2b_quote.synthetic.json"
        self.data = load_manifest(self.path)
        self.assertTrue(self.data["synthetic"])

    def test_normal_quote_cli_and_no_input_mutation(self):
        before_data = copy.deepcopy(self.data)
        before_hash = hashlib.sha256(self.path.read_bytes()).hexdigest()
        result = b2b_quote.calculate(self.data)
        self.assertEqual(result["price_floor"], Decimal("550"))
        self.assertEqual(result["projected_economic_profit"], Decimal("110"))
        self.assertEqual(result["cash_contribution_after_fee_tax_reserve_and_direct_cost"], Decimal("410"))
        self.assertEqual(result["projected_economic_margin"], Decimal("0.2"))
        self.assertEqual(result["status"], "CALCULATED")
        self.assertFalse(result["monthly_budget_checked"])
        self.assertFalse(result["assessment_is_authorization"])
        self.assertIsNone(result["production_ready"])
        process = subprocess.run([sys.executable, str(ROOT / "b2b_quote.py"), str(self.path)],
                                 capture_output=True, text=True, encoding="utf-8", check=True)
        cli_result = json.loads(process.stdout)
        self.assertEqual(cli_result["status"], "CALCULATED")
        self.assertEqual(cli_result["price_floor"], "550")
        self.assertEqual(self.data, before_data)
        self.assertEqual(hashlib.sha256(self.path.read_bytes()).hexdigest(), before_hash)

    def test_rework_can_turn_an_apparently_good_quote_into_a_loss(self):
        self.data["estimated_total_hours"] = 40
        self.data["accepted_change_scope"] = False
        before = copy.deepcopy(self.data)
        result = b2b_quote.calculate(self.data)
        self.assertEqual(result["price_floor"], Decimal("1050"))
        self.assertEqual(result["projected_economic_profit"], Decimal("-190"))
        self.assertEqual(result["cash_contribution_after_fee_tax_reserve_and_direct_cost"], Decimal("410"))
        self.assertEqual(result["quote_comparison"], "BELOW_FLOOR")
        self.assertEqual(result["scope_acknowledgement"], "RECORDED_NOT_ACCEPTED")
        self.assertEqual(self.data, before)

    def test_unknowns_are_not_silently_zero_and_optional_quote_keeps_known_floor(self):
        for key in b2b_quote.BASE_FIELDS:
            with self.subTest(key=key):
                data = copy.deepcopy(self.data)
                data[key] = None
                result = b2b_quote.calculate(data)
                self.assertEqual(result["status"], "UNKNOWN")
                self.assertIsNone(result["price_floor"])
                self.assertIn(key, result["missing"])
        self.data.pop("quoted_price")
        result = b2b_quote.calculate(self.data)
        self.assertEqual(result["status"], "UNKNOWN")
        self.assertEqual(result["price_floor"], Decimal("550"))
        self.assertIsNone(result["projected_economic_profit"])
        self.assertEqual(result["scope_acknowledgement"], "UNKNOWN")

    def test_invalid_negative_cost_rates_denominator_and_boolean_numbers(self):
        changes = ({"estimated_total_hours": -1}, {"direct_cash_cost": -1},
                   {"quoted_price": -1}, {"fee_rate": Decimal("1.1")},
                   {"fee_rate": Decimal("0.5"), "tax_reserve_rate": Decimal("0.3")},
                   {"target_margin": Decimal("0.9")}, {"hourly_value": True},
                   {"estimated_total_hours": "20"}, {"hourly_value": float("nan")},
                   {"accepted_change_scope": "yes"})
        for change in changes:
            with self.subTest(change=change):
                data = copy.deepcopy(self.data)
                data.update(change)
                with self.assertRaises(ManifestError):
                    b2b_quote.calculate(data)


if __name__ == "__main__":
    unittest.main()
