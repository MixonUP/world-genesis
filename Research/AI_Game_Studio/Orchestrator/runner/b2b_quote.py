"""Read-only B2B quote arithmetic; no contract, tax, budget or execution approval."""
from __future__ import annotations

import argparse
from decimal import Decimal
from pathlib import Path
import sys

from runner import ManifestError, dump, load_manifest, nonempty, number, object_keys

NUMERIC_FIELDS = ("estimated_total_hours", "hourly_value", "direct_cash_cost",
                  "fee_rate", "tax_reserve_rate", "target_margin", "quoted_price")
RATE_FIELDS = ("fee_rate", "tax_reserve_rate", "target_margin")
BASE_FIELDS = NUMERIC_FIELDS[:-1]


def calculate(data):
    object_keys(data, {*NUMERIC_FIELDS, "schema_version", "synthetic", "currency", "accepted_change_scope"}, "quote")
    if isinstance(data.get("schema_version", 1), bool) or data.get("schema_version", 1) != 1:
        raise ManifestError("schema_version must be 1")
    if not isinstance(data.get("synthetic", False), bool):
        raise ManifestError("synthetic must be boolean")
    if data.get("currency") is not None and not nonempty(data["currency"]):
        raise ManifestError("currency must be a nonempty string or null")
    scope = data.get("accepted_change_scope")
    if scope is not None and not isinstance(scope, bool):
        raise ManifestError("accepted_change_scope must be boolean or null")
    values = {}
    for key in NUMERIC_FIELDS:
        value = data.get(key)
        values[key] = None if value is None else number(value)
        if values[key] is not None and values[key] < 0:
            raise ManifestError(f"{key}: negative values are invalid")
    for key in RATE_FIELDS:
        if values[key] is not None and values[key] > 1:
            raise ManifestError(f"{key}: rate must be between 0 and 1")
    denominator = None
    if all(values[key] is not None for key in RATE_FIELDS):
        denominator = Decimal(1) - sum(values[key] for key in RATE_FIELDS)
        if denominator <= 0:
            raise ManifestError("1 - fee_rate - tax_reserve_rate - target_margin must be positive")
    missing = [key for key in (*NUMERIC_FIELDS, "currency") if data.get(key) is None]
    labor = None
    if values["estimated_total_hours"] is not None and values["hourly_value"] is not None:
        labor = values["estimated_total_hours"] * values["hourly_value"]
    floor = None
    if all(values[key] is not None for key in BASE_FIELDS):
        floor = Decimal(format((labor + values["direct_cash_cost"]) / denominator, "f"))
    cash = profit = margin = None
    if all(values[key] is not None for key in ("quoted_price", "fee_rate", "tax_reserve_rate", "direct_cash_cost")):
        cash = values["quoted_price"] * (1 - values["fee_rate"] - values["tax_reserve_rate"]) - values["direct_cash_cost"]
        if labor is not None:
            profit = cash - labor
            if values["quoted_price"] > 0:
                margin = profit / values["quoted_price"]
    comparison = "UNKNOWN"
    if floor is not None and values["quoted_price"] is not None:
        comparison = "AT_OR_ABOVE_FLOOR" if values["quoted_price"] >= floor else "BELOW_FLOOR"
    return {
        "schema_version": 1,
        "status": "UNKNOWN" if missing else "CALCULATED",
        "mode": "synthetic_demo" if data.get("synthetic") else "quote_arithmetic",
        "currency": data.get("currency"), "missing": missing,
        "estimated_total_hours": values["estimated_total_hours"], "labor_value": labor,
        "direct_cash_cost": values["direct_cash_cost"], "denominator": denominator,
        "price_floor": floor, "quoted_price": values["quoted_price"],
        "cash_contribution_after_fee_tax_reserve_and_direct_cost": cash,
        "projected_economic_profit": profit, "projected_economic_margin": margin,
        "quote_comparison": comparison,
        "scope_acknowledgement": "UNKNOWN" if scope is None else "RECORDED_ACCEPTED" if scope else "RECORDED_NOT_ACCEPTED",
        "production_ready": None, "assessment_is_authorization": False,
        "monthly_budget_checked": False, "profit_guaranteed": False,
        "limitations": [
            "Hours must include QA, revisions, delivery and support; omitted work is not estimated automatically.",
            "Cash contribution excludes labor value and is not net profit. Economic profit remains a projection.",
            "Tax reserve is a supplied assumption, not verified legal tax treatment. Fee/tax rates here apply to gross quote.",
            "A scope flag is not an authenticated contract, customer commitment, change order or permission to contact anyone.",
            "No monthly/shared-budget check, spending authorization, payment, credit-risk check or production gate is performed.",
        ],
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args(argv)
    try:
        print(dump(calculate(load_manifest(args.manifest))))
        return 0  # Arithmetic completed; inspect status/missing and manual contract gates.
    except (ManifestError, OSError, TypeError) as exc:
        print(dump({"status": "INVALID", "error": str(exc)}))
        return 2


if __name__ == "__main__":
    sys.exit(main())
