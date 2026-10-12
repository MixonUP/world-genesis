"""Mutation checks for prepared design data. No Unity/gameplay is executed."""
from pathlib import Path
from datetime import datetime, timezone, timedelta
import csv
import hashlib
import importlib.util
import json
import shutil
import tempfile

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "SourceAssets" / "Design"
spec = importlib.util.spec_from_file_location("economy_validator", ROOT / "Tools" / "Validate-EconomyDesign.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def mutate(directory, name, key, identity, column, value):
    path = directory / name
    with path.open(encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    row = next(row for row in rows if row[key] == identity)
    row[column] = value
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)


def main():
    cases = [
        ("free_basket", "carrying_equipment.csv", "equipment_id", "basket_small", "inputs", ""),
        ("stacked_capacity", "carrying_equipment.csv", "equipment_id", "basket_woven", "total_capacity", "26"),
        ("nonunique_basket", "carrying_equipment.csv", "equipment_id", "basket_small", "unique_owner", "false"),
        ("upgrade_cycle", "carrying_equipment.csv", "equipment_id", "basket_woven", "consumed_previous_id", "basket_framed"),
        ("cart_without_animal", "carrying_equipment.csv", "equipment_id", "trade_cart", "requires_animal", "0"),
        ("cart_recipe_disagreement", "carrying_equipment.csv", "equipment_id", "trade_cart", "inputs", "plank=1"),
        ("boss_mints_money", "wildlife_variants.csv", "variant_id", "ancient_stag", "extra_death_output", "coin=100"),
        ("milk_without_feed", "wildlife.csv", "species_id", "cow", "periodic_inputs", ""),
        ("unknown_crop_output", "crops.csv", "crop_id", "wheat", "outputs", "unregistered_crop=2"),
        ("no_house_upgrade_gate", "housing_comfort.csv", "grade", "2", "required_technology_id", ""),
        ("nonfinite_comfort", "housing_comfort.csv", "grade", "2", "base_comfort", "NaN"),
    ]
    pristine = module.validate(DATA)
    results = []
    for name, file, key, identity, column, value in cases:
        # The temporary directory is created by this process, never a supplied path.
        with tempfile.TemporaryDirectory(prefix="world-genesis-design-check-") as temporary:
            directory = Path(temporary).resolve()
            assert directory.parent == Path(tempfile.gettempdir()).resolve()
            assert directory.name.startswith("world-genesis-design-check-")
            for source in DATA.glob("*.csv"):
                shutil.copyfile(source, directory / source.name)
            mutate(directory, file, key, identity, column, value)
            result = module.validate(directory)
            results.append({"case": name, "rejected": not result["passed"], "errors": result["errors"]})
    report = {
        "scope": "prepared design validator sensitivity, not game simulation",
        "created_at": datetime.now(timezone(timedelta(hours=3))).isoformat(timespec="seconds"),
        "passed": pristine["passed"] and all(row["rejected"] for row in results),
        "positive_catalog_passed": pristine["passed"],
        "negative_cases": results,
        "source_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(DATA.glob("*.csv"))},
    }
    output = ROOT / "Evidence" / "Design" / "agriculture-social-validator-mutations.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "negative_cases_rejected": sum(r["rejected"] for r in results), "total": len(results), "report": str(output.relative_to(ROOT))}))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
