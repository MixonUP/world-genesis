"""Offline, read-only decision support. No API clients, agents or spending."""
from __future__ import annotations

import argparse
import calendar
from datetime import date, datetime, timezone
from decimal import Decimal, ROUND_CEILING
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlparse

STATES = ("discovery", "demand_validation", "prototype", "vertical_slice",
          "production", "release_candidate", "released", "operate")
GATES = tuple(f"G{i}" for i in range(7))
REQUIREMENTS = {
    "demand": {"human_validation", "verified_contract"},
    "feasibility": {"feasibility_report"},
    "playable": {"native_playtest", "browser_playtest"},
    "cost_benchmark": {"cost_benchmark"},
    "cash_plan": {"cash_plan"},
    "rights": {"rights_audit"},
    "independent_review": {"independent_review"},
    "native_qa": {"native_player_qa", "native_browser_qa"},
    "legal": {"legal_check"}, "platform": {"platform_check"},
    "payment": {"payment_check"}, "build": {"release_build"},
    "rollback": {"rollback_test"}, "release_evidence": {"release_check"},
    "receipts": {"actual_receipts"}, "refunds": {"actual_refund_report"},
    "support": {"actual_support_report"},
}
BUILD_REQUIREMENTS = {"playable", "native_qa", "build", "rollback", "release_evidence"}
ECON_NUMBERS = (
    "price_gross", "refund_rate", "platform_fee_rate", "payment_fee_rate",
    "tax_reserve_per_unit", "variable_cash_cost_per_unit",
    "variable_non_cash_cost_per_unit", "fixed_cash_cost", "fixed_non_cash_cost",
    "planned_units", "opening_cash", "additional_funding", "peak_cash_need",
    "min_cash_buffer", "api_actual_cash_total", "api_equivalent_estimate",
)
CORE_ECON = ECON_NUMBERS[:10]
ROOT_KEYS = {"schema_version", "synthetic", "project", "state", "producer_id",
             "evidence", "requirements", "economics", "budget", "actions", "review", "claims"}


class ManifestError(ValueError):
    pass


def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float, Decimal)):
        raise ManifestError("Expected a JSON number, not a boolean or numeric string")
    result = Decimal(str(value))
    if not result.is_finite():
        raise ManifestError("Numbers must be finite")
    return result


def day(value):
    if not isinstance(value, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", value):
        raise ManifestError("Dates must use YYYY-MM-DD")
    try:
        return date.fromisoformat(value)
    except ValueError as exc:
        raise ManifestError(str(exc)) from exc


def nonempty(value):
    return isinstance(value, str) and bool(value.strip())


def object_keys(value, allowed, where):
    if not isinstance(value, dict):
        raise ManifestError(f"{where}: expected object")
    unknown = set(value) - set(allowed)
    if unknown:
        raise ManifestError(f"{where}: unsupported keys {sorted(unknown)}")


def nullable_texts(obj, fields, where):
    for key in fields:
        if obj.get(key) is not None and not nonempty(obj[key]):
            raise ManifestError(f"{where}.{key}: expected nonempty string or null")


def nullable_numbers(obj, fields, where):
    for key in fields:
        if obj.get(key) is not None and number(obj[key]) < 0:
            raise ManifestError(f"{where}.{key}: negative amounts are invalid")


def validate(data):
    """Validate structure and contradictory claims, not business readiness."""
    object_keys(data, ROOT_KEYS, "manifest")
    required = ROOT_KEYS - {"synthetic"}
    if required - set(data):
        raise ManifestError(f"Missing root fields: {sorted(required - set(data))}")
    if isinstance(data["schema_version"], bool) or data["schema_version"] != 1:
        raise ManifestError("schema_version must be 1")
    if not isinstance(data.get("synthetic", False), bool):
        raise ManifestError("synthetic must be boolean")
    if data["state"] not in (*STATES, "on_hold", "stopped"):
        raise ManifestError("Unknown state")
    if not nonempty(data["producer_id"]):
        raise ManifestError("producer_id is required")
    project = data["project"]
    object_keys(project, {"id", "name", "audience", "platform", "engine", "build_id",
                          "readiness", "historical_release_note"}, "project")
    nullable_texts(project, set(project) - {"engine"}, "project")
    object_keys(project.get("engine", {}), {"name", "version"}, "project.engine")
    nullable_texts(project.get("engine", {}), {"name", "version"}, "project.engine")
    claims = data["claims"]
    object_keys(claims, {"profit_guaranteed"}, "claims")
    if claims.get("profit_guaranteed") is not False:
        raise ManifestError("claims.profit_guaranteed must be false; profit guarantees are rejected")
    evidence = data["evidence"]
    if not isinstance(evidence, list):
        raise ManifestError("evidence must be an array")
    evidence_ids = set()
    for item in evidence:
        object_keys(item, {"id", "status", "kind", "locator", "verified_by", "verified_at",
                           "expires_at", "human_count", "build_id", "sha256", "note"}, "evidence[]")
        if not nonempty(item.get("id")) or item["id"] in evidence_ids:
            raise ManifestError("Missing or duplicate evidence ID")
        evidence_ids.add(item["id"])
        if item.get("status") not in {"prepared", "verified", "failed", "stale"}:
            raise ManifestError(f"{item['id']}: invalid evidence status")
        nullable_texts(item, {"kind", "locator", "verified_by", "build_id", "note"}, item["id"])
        for field in ("verified_at", "expires_at"):
            if item.get(field) is not None:
                day(item[field])
        if item.get("human_count") is not None:
            count = number(item["human_count"])
            if count < 0 or count != count.to_integral_value():
                raise ManifestError("human_count must be a nonnegative integer")
        if item.get("sha256") is not None and not re.fullmatch(r"[a-fA-F0-9]{64}", item["sha256"]):
            raise ManifestError("sha256 must contain 64 hex characters")
        if item.get("kind") == "independent_review" and item.get("verified_by") == data["producer_id"]:
            raise ManifestError("Independent reviewer cannot equal producer")
    requirements = data["requirements"]
    object_keys(requirements, REQUIREMENTS, "requirements")
    for key, refs in requirements.items():
        if not isinstance(refs, list) or any(not nonempty(ref) for ref in refs):
            raise ManifestError(f"requirements.{key}: expected array of evidence IDs")
        if len(refs) != len(set(refs)):
            raise ManifestError(f"requirements.{key}: duplicate references")
        if set(refs) - evidence_ids:
            raise ManifestError(f"requirements.{key}: dangling evidence reference")
    review = data["review"]
    object_keys(review, {"reviewer_id", "evidence_id"}, "review")
    nullable_texts(review, review, "review")
    if review.get("reviewer_id") == data["producer_id"]:
        raise ManifestError("reviewer_id cannot equal producer_id")
    if review.get("evidence_id") is not None and review["evidence_id"] not in evidence_ids:
        raise ManifestError("review.evidence_id is dangling")
    economics = data["economics"]
    object_keys(economics, {*ECON_NUMBERS, "currency", "unit_type"}, "economics")
    nullable_texts(economics, {"currency", "unit_type"}, "economics")
    if economics.get("unit_type") not in {None, "paid_copy"}:
        raise ManifestError("This v1 economics model supports paid_copy only; B2B contracts need a separate model")
    nullable_numbers(economics, ECON_NUMBERS, "economics")
    for key in ("refund_rate", "platform_fee_rate", "payment_fee_rate"):
        if economics.get(key) is not None and number(economics[key]) > 1:
            raise ManifestError(f"{key} must be between 0 and 1")
    if all(economics.get(k) is not None for k in ("platform_fee_rate", "payment_fee_rate")):
        if number(economics["platform_fee_rate"]) + number(economics["payment_fee_rate"]) > 1:
            raise ManifestError("Combined fee rates exceed 1")
    if economics.get("planned_units") is not None:
        units = number(economics["planned_units"])
        if units != units.to_integral_value():
            raise ManifestError("planned_units must be an integer")
    budget = data["budget"]
    object_keys(budget, {"currency", "cap", "spent", "reserved", "period_start", "period_end", "scope"}, "budget")
    nullable_texts(budget, {"currency", "scope"}, "budget")
    nullable_numbers(budget, {"cap", "spent", "reserved"}, "budget")
    for key in ("period_start", "period_end"):
        if budget.get(key) is not None:
            day(budget[key])
    if budget.get("period_start") and budget.get("period_end") and day(budget["period_start"]) > day(budget["period_end"]):
        raise ManifestError("Budget period starts after it ends")
    if budget.get("currency") and economics.get("currency") and budget["currency"] != economics["currency"]:
        raise ManifestError("budget and economics currencies differ; no implicit FX conversion")
    if not isinstance(data["actions"], list):
        raise ManifestError("actions must be an array")
    action_ids = set()
    reserved_sum = Decimal(0)
    for action in data["actions"]:
        object_keys(action, {"id", "kind", "stage", "estimated_cash_cost", "reservation", "authorization"}, "actions[]")
        if not nonempty(action.get("id")) or action["id"] in action_ids:
            raise ManifestError("Missing or duplicate action ID")
        action_ids.add(action["id"])
        if action.get("kind") not in {"reversible_local", "external_spend"}:
            raise ManifestError("Unknown action kind")
        if action.get("stage") not in (*STATES, "bounded_research"):
            raise ManifestError("Unknown action stage")
        nullable_numbers(action, {"estimated_cash_cost", "reservation"}, action["id"])
        if action["kind"] == "reversible_local" and action.get("estimated_cash_cost") != 0:
            raise ManifestError("reversible_local must have zero external cash cost; labor goes in economics")
        if action.get("reservation") is not None:
            reserved_sum += number(action["reservation"])
        auth = action.get("authorization")
        if auth is not None:
            object_keys(auth, {"granted", "action_id", "max_cash_cost", "authorized_by"}, "authorization")
            if not isinstance(auth.get("granted"), bool):
                raise ManifestError("authorization.granted must be boolean")
            nullable_texts(auth, {"action_id", "authorized_by"}, "authorization")
            nullable_numbers(auth, {"max_cash_cost"}, "authorization")
    if budget.get("reserved") is not None and reserved_sum > number(budget["reserved"]):
        raise ManifestError("Action reservations exceed the recorded reservation ledger (torn state)")


def check(status, reason):
    return {"status": status, "reason": reason}


def combine(checks):
    values = list(checks.values()) if isinstance(checks, dict) else list(checks)
    statuses = [value["status"] for value in values]
    return "FAIL" if "FAIL" in statuses else "UNKNOWN" if "UNKNOWN" in statuses else "PASS"


def evaluate_economics(econ):
    missing = [key for key in (*CORE_ECON, "currency", "unit_type") if econ.get(key) is None]
    result = {"status": "UNKNOWN", "missing": missing, "forecast_only": True,
              "unit_type": econ.get("unit_type"),
              "api_actual_cash_total": econ.get("api_actual_cash_total"),
              "api_equivalent_estimate": econ.get("api_equivalent_estimate"),
              "api_note": "Equivalent estimates are not receipts and are never added to actual cash totals."}
    if missing:
        return result
    n = {key: number(econ[key]) for key in CORE_ECON}
    net = n["price_gross"] * (1 - n["refund_rate"]) * (1 - n["platform_fee_rate"] - n["payment_fee_rate"])
    cash = net - n["tax_reserve_per_unit"] - n["variable_cash_cost_per_unit"]
    economic = cash - n["variable_non_cash_cost_per_unit"]
    fixed_economic = n["fixed_cash_cost"] + n["fixed_non_cash_cost"]
    forecast_cash = n["planned_units"] * cash - n["fixed_cash_cost"]
    forecast_economic = n["planned_units"] * economic - fixed_economic
    def break_even(fixed, margin):
        return int((fixed / margin).to_integral_value(rounding=ROUND_CEILING)) if margin > 0 else None
    viable = cash > 0 and economic > 0 and n["planned_units"] > 0 and forecast_cash >= 0 and forecast_economic >= 0
    result.update(status="PASS" if viable else "FAIL",
                  positive_unit_contribution=cash > 0 and economic > 0,
                  forecast_break_even=forecast_cash >= 0 and forecast_economic >= 0,
                  planned_units_positive=n["planned_units"] > 0,
                  net_revenue_per_unit=net, cash_contribution_per_unit=cash,
                  economic_contribution_per_unit=economic,
                  cash_break_even_units=break_even(n["fixed_cash_cost"], cash),
                  economic_break_even_units=break_even(fixed_economic, economic),
                  forecast_cash_result=forecast_cash, forecast_economic_result=forecast_economic)
    return result


def assess(data, base_dir, as_of=None):
    validate(data)
    today = as_of or datetime.now(timezone.utc).date()
    base_dir = Path(base_dir)
    evidence = {item["id"]: item for item in data["evidence"]}
    def one_evidence(ref, requirement):
        item = evidence[ref]
        if item["status"] == "failed":
            return check("FAIL", f"{ref}: failed evidence")
        if item["status"] != "verified":
            return check("UNKNOWN", f"{ref}: {item['status']} is not verified")
        if not item.get("kind"):
            return check("UNKNOWN", f"{ref}: evidence kind is unknown")
        if item.get("kind") not in REQUIREMENTS[requirement]:
            return check("FAIL", f"{ref}: kind cannot prove {requirement}")
        if not all(item.get(key) for key in ("locator", "verified_by", "verified_at", "expires_at")):
            return check("UNKNOWN", f"{ref}: missing provenance, verification date or expiry")
        if day(item["verified_at"]) > today or day(item["expires_at"]) < today or day(item["expires_at"]) < day(item["verified_at"]):
            return check("UNKNOWN", f"{ref}: stale or future-dated evidence")
        locator = item["locator"]
        parsed = urlparse(locator)
        if parsed.scheme in {"http", "https"}:
            if not parsed.netloc or parsed.username or parsed.password:
                return check("FAIL", f"{ref}: invalid public URL locator")
        else:
            # A Windows drive letter is a path; other URI schemes are unsupported.
            if parsed.scheme and not re.match(r"^[A-Za-z]:[\\/]", locator):
                return check("FAIL", f"{ref}: unsupported locator scheme")
            path = Path(locator)
            if not path.is_absolute():
                path = base_dir / path
            if not path.is_file():
                return check("UNKNOWN", f"{ref}: local artifact missing")
            if item.get("sha256"):
                digest = hashlib.sha256(path.read_bytes()).hexdigest()
                if digest.lower() != item["sha256"].lower():
                    return check("FAIL", f"{ref}: artifact digest mismatch")
        if requirement == "demand" and item["kind"] == "human_validation" and number(item.get("human_count", 0)) < 1:
            return check("FAIL", f"{ref}: no real-human participants recorded")
        if requirement in BUILD_REQUIREMENTS:
            build = data["project"].get("build_id")
            if not build or item.get("build_id") != build:
                return check("UNKNOWN", f"{ref}: evidence is not bound to the current build")
        if requirement == "native_qa":
            expected = "native_browser_qa" if data["project"].get("platform") == "web" else "native_player_qa"
            if item["kind"] != expected:
                return check("FAIL", f"{ref}: QA does not match target platform")
        if requirement == "independent_review":
            review = data["review"]
            if review.get("evidence_id") != ref or review.get("reviewer_id") != item["verified_by"]:
                return check("UNKNOWN", f"{ref}: independent reviewer identity/reference missing or mismatched")
        return check("PASS", f"{ref}: accepted from supplied record; authenticity not independently checked")
    def requirement(name):
        refs = data["requirements"].get(name, [])
        if not refs:
            return check("UNKNOWN", f"{name}: no evidence referenced")
        values = [one_evidence(ref, name) for ref in refs]
        # Failed contradictory evidence is not hidden by an easier passing record.
        return {"status": combine(values), "reason": "; ".join(v["reason"] for v in values)}
    project = data["project"]
    identity = {key: check("PASS" if nonempty(project.get(key)) else "UNKNOWN", f"project.{key}")
                for key in ("id", "name", "audience", "platform")}
    economics = evaluate_economics(data["economics"])
    econ_check = check(economics["status"], "Complete economics, positive contribution/units and nonnegative forecast cash/economic results required")
    budget = data["budget"]
    if any(budget.get(key) is None for key in ("currency", "cap", "spent", "reserved", "period_start", "period_end")):
        budget_check = check("UNKNOWN", "A null cap/ledger/currency/period never permits external spend")
    elif not day(budget["period_start"]) <= today <= day(budget["period_end"]):
        budget_check = check("FAIL", "Budget cap is not active on the assessment date; no automatic rollover")
    elif number(budget["spent"]) + number(budget["reserved"]) > number(budget["cap"]):
        budget_check = check("FAIL", "Spent plus reserved exceeds total cap")
    else:
        budget_check = check("PASS", "Bounded cash ledger supplied; this is not per-action authorization")
    cash_keys = ("opening_cash", "additional_funding", "peak_cash_need", "min_cash_buffer")
    econ = data["economics"]
    if any(econ.get(key) is None for key in cash_keys):
        cash_check = check("UNKNOWN", "Cash-plan numbers missing")
    elif number(econ["opening_cash"]) + number(econ["additional_funding"]) < number(econ["peak_cash_need"]) + number(econ["min_cash_buffer"]):
        cash_check = check("FAIL", "Available cash does not cover peak need and buffer")
    else:
        cash_check = check("PASS", "Cash-plan coverage supplied; forecast, not guaranteed income")
    actual_cost_check = check("UNKNOWN" if econ.get("api_actual_cash_total") is None else "PASS",
                             "Actual API cash must be recorded (0 if none); an equivalent estimate cannot substitute")
    gate_checks = [identity, {"demand": requirement("demand")},
                   {**{key: requirement(key) for key in ("feasibility", "playable", "cost_benchmark")},
                    "actual_api_cash": actual_cost_check},
                   {**{key: requirement(key) for key in ("cash_plan", "rights", "independent_review")},
                    "unit_economics": econ_check, "cash_coverage": cash_check, "bounded_budget": budget_check},
                   {"native_qa": requirement("native_qa")},
                   {key: requirement(key) for key in ("legal", "platform", "payment", "build", "rollback", "release_evidence")},
                   {key: requirement(key) for key in ("receipts", "refunds", "support")}]
    gates = {gate: {"status": combine(checks), "checks": checks} for gate, checks in zip(GATES, gate_checks)}
    state = data["state"]
    inactive = state in {"on_hold", "stopped"}
    index = STATES.index(state) if not inactive else None
    predecessors = GATES[:index] if index is not None else ()
    integrity = combine(gates[g] for g in predecessors) if not inactive else "UNKNOWN"
    candidate = STATES[index + 1] if index is not None and index < len(STATES) - 1 else None
    needed = GATES[:index + 1] if candidate else ()
    may_advance = bool(candidate) and all(gates[g]["status"] == "PASS" for g in needed)
    action_results = []
    for action in data["actions"]:
        checks = {}
        if inactive:
            checks["state"] = check("FAIL", f"{state}: no active execution; manual resume/decision needed")
        if action["kind"] == "reversible_local":
            checks["scope"] = check("PASS", "Existing task authorization covers reversible local work; no new cash spend")
        else:
            checks["budget"] = budget_check
            auth = action.get("authorization")
            if auth is None:
                checks["authorization"] = check("UNKNOWN", "Missing per-action external-spend authorization")
            elif not auth["granted"]:
                checks["authorization"] = check("FAIL", "External spend explicitly not authorized")
            elif auth.get("action_id") != action["id"] or not auth.get("authorized_by"):
                checks["authorization"] = check("FAIL", "Authorization must identify this action and its authorizer")
            elif auth.get("max_cash_cost") is None or action.get("estimated_cash_cost") is None:
                checks["authorization"] = check("UNKNOWN", "Action amount/cap unknown")
            elif number(auth["max_cash_cost"]) < number(action["estimated_cash_cost"]):
                checks["authorization"] = check("FAIL", "Action exceeds its authorization cap")
            else:
                checks["authorization"] = check("PASS", "Per-action authorization recorded, not authenticated by this checker")
            if action.get("reservation") is None or action.get("estimated_cash_cost") is None:
                checks["reservation"] = check("UNKNOWN", "Action reservation or cash estimate missing")
            elif number(action["reservation"]) < max(number(action["estimated_cash_cost"]),
                    number(auth["max_cash_cost"]) if auth and auth.get("max_cash_cost") is not None else Decimal(0)):
                checks["reservation"] = check("FAIL", "Reservation must cover both estimate and full authorization cap")
            else:
                checks["reservation"] = check("PASS", "Reservation covers the full authorized cash exposure")
            stage = action["stage"]
            count = 1 if stage == "bounded_research" else max(1, STATES.index(stage))
            for gate in GATES[:count]:
                checks[gate] = check(gates[gate]["status"], f"Dependent {stage} spend requires {gate}")
        action_results.append({"id": action["id"], "status": combine(checks), "checks": checks})
    gaps = [{"gate": gate, "requirement": name, **value}
            for gate, group in gates.items() for name, value in group["checks"].items() if value["status"] != "PASS"]
    tasks = ["Review preserved evidence and explicitly decide whether to resume or stop"] if inactive else [
        "Inspect evidence and correct incomplete records without new external spend",
        "Prepare real-human demand tests within existing authorization",
        "Perform bounded reversible prototype/R&D work without new external spend",
    ]
    if may_advance:
        tasks.append(f"Prepare transition to {candidate}; checker does not change state")
    return {"schema_version": 1, "assessment_date": today.isoformat(), "mode": "synthetic_demo" if data.get("synthetic") else "decision_support",
            "original_state": state, "state_integrity": integrity,
            "candidate_next_state": candidate, "allowed_next_states": [candidate] if may_advance else [],
            "next_gate_status": combine(gates[g] for g in needed) if candidate else "UNKNOWN",
            "completion_claim": False, "assessment_is_authorization": False,
            "gates": gates, "gaps": gaps, "economics": economics,
            "actions": action_results, "allowed_tasks_without_new_spend": tasks,
            "limitations": ["Trusted-input checker, not security enforcement. Identity, receipt authenticity and remote URLs are not verified.",
                            "PASS means supplied records satisfy checks; no sale, profit, platform approval or execution is guaranteed.",
                            "No external calls, agent launch, state mutation, reservation creation or payment occurs."]}


def template():
    today = datetime.now(timezone.utc).date()
    month_start = today.replace(day=1).isoformat()
    month_end = today.replace(day=calendar.monthrange(today.year, today.month)[1]).isoformat()
    return {"schema_version": 1, "synthetic": False,
            "project": {"id": "world-genesis", "name": "World Genesis", "audience": None,
                        "platform": "windows_x64", "engine": {"name": "Unity", "version": "6000.0.45f1"},
                        "build_id": None, "readiness": "research_prepared",
                        "historical_release_note": "v0.10 was released previously per Docs/STATE.md. Discovery here re-evaluates a new investment decision; historical tests were not rerun."},
            "state": "discovery", "producer_id": "project-producer",
            "evidence": [], "requirements": {key: [] for key in REQUIREMENTS},
            "economics": {"currency": None, "unit_type": "paid_copy", **{key: None for key in ECON_NUMBERS}},
            "budget": {"currency": "USD", "cap": None, "spent": None, "reserved": None,
                       "period_start": month_start, "period_end": month_end,
                       "scope": "shared_development_ceiling_to_be_supplied"},
            "actions": [], "review": {"reviewer_id": None, "evidence_id": None},
            "claims": {"profit_guaranteed": False}}


def load_manifest(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ManifestError(f"Duplicate JSON key: {key}")
            result[key] = value
        return result
    def invalid_constant(value):
        raise ManifestError(f"Non-finite JSON number: {value}")
    try:
        return json.loads(Path(path).read_text(encoding="utf-8-sig"), object_pairs_hook=unique,
                          parse_float=Decimal, parse_constant=invalid_constant)
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ManifestError(f"Cannot read intact JSON manifest: {exc}") from exc


def dump(value):
    return json.dumps(value, ensure_ascii=False, indent=2, default=lambda obj: str(obj) if isinstance(obj, Decimal) else None)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    init = commands.add_parser("init", help="Print research template; shared monthly ceiling, unknown ledger, no new QA claims")
    init.add_argument("--output", type=Path, help="Optional new file; refuses to overwrite")
    for command in ("validate", "assess"):
        item = commands.add_parser(command)
        item.add_argument("manifest", type=Path)
        if command == "assess":
            item.add_argument("--as-of", help="YYYY-MM-DD; defaults to current UTC date")
    args = parser.parse_args(argv)
    try:
        if args.command == "init":
            text = dump(template()) + "\n"
            if args.output:
                with args.output.open("x", encoding="utf-8") as stream:
                    stream.write(text)
                print(dump({"status": "CREATED", "path": str(args.output)}))
            else:
                print(text, end="")
            return 0
        data = load_manifest(args.manifest)
        validate(data)
        if args.command == "validate":
            print(dump({"status": "VALID", "readiness_assessed": False}))
            return 0
        result = assess(data, args.manifest.parent, day(args.as_of) if args.as_of else None)
        print(dump(result))
        return 0  # Completed assessment, not readiness. Consumers must inspect gate/action statuses.
    except (ManifestError, OSError, TypeError) as exc:
        print(dump({"status": "INVALID", "error": str(exc)}))
        return 2


if __name__ == "__main__":
    sys.exit(main())
