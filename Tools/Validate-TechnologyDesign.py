"""Validate prepared technology data/math; does not test or implement Unity gameplay."""
from __future__ import annotations

import csv
import hashlib
import json
import math
import re
from collections import Counter, defaultdict, deque
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "SourceAssets" / "Design"
OUTPUT = ROOT / "Evidence" / "Design" / "technology-check.json"
Q = 0.88
RESEARCH_CYCLE_SECONDS = 10
ECONOMIC_DAY_SECONDS = 600
WEIGHTS = {"resident": 0.1, "priest": 1.0, "artisan": 2.0, "scholar": 10.0}
ARTISANS = {
    "butcher", "cook", "miller", "baker", "charcoal_burner", "smelter",
    "tanner", "flax_worker", "weaver", "blacksmith", "carpenter", "armorer",
    "tailor", "builder", "potter", "mintmaster", "glassmaker",
}
BRANCHES = {
    "food", "materials", "metals", "textiles", "transport", "defense",
    "health_civics", "knowledge",
}
EFFECTS = {
    "unlock", "shelf_life_multiplier", "active_work_multiplier",
    "storage_capacity_multiplier", "item_durability_multiplier",
    "local_trade_rule", "caravan_route_rule", "regional_trade_rule",
    "supply_contract_rule", "fortification_rule", "water_hygiene_rule",
    "civic_accounting_rule", "disease_risk_multiplier", "civic_services_rule",
    "housing_comfort_rule", "road_speed_multiplier", "learning_rate_multiplier", "water_access_rule",
    "husbandry_rule",
}
SURVIVAL_RECIPES = {
    "forage_berries", "fetch_water", "craft_primitive_tool", "saw_planks",
    "plant_wheat", "fell_tree", "collect_branches", "collect_stone",
    "collect_flint", "herbal_care", "camp_saw_planks", "split_firewood", "branch_firewood", "camp_stone_block",
}


def items(value: str) -> list[str]:
    return [part for part in value.split(";") if part]


def quantities(value: str) -> dict[str, int]:
    return {key: int(amount) for key, amount in (p.split("=") for p in items(value))}


def total(weight: float, effective_count: float) -> float:
    if not math.isfinite(effective_count) or effective_count < 0:
        raise ValueError("Effective population must be finite and nonnegative")
    return -weight * math.expm1(effective_count * math.log(Q)) / (1.0 - Q)


def role_group(role: str) -> str:
    if role in ("priest", "scholar"):
        return role
    return "artisan" if role in ARTISANS else "resident"


def integrated_reachability(tech, goods, recipes, grades, roles, tiers, bootstrap, wildlife, military, variants=()):
    """Monotone closure with real cross-catalog gates; quantities/time are not simulated."""
    available = {r["ref_id"] for r in bootstrap if r["kind"] == "inventory"}
    habitat = {r["ref_id"] for r in bootstrap if r["kind"] == "habitat"}
    adults = sum(int(r["amount"]) for r in bootstrap if r["kind"] == "population")
    recipe_gates = {ref: tid for tid, row in tech.items() for ref in items(row["unlock_recipes"])}
    grade_gates = {ref: tid for tid, row in tech.items() for ref in items(row["unlock_building_grades"])}
    known, made, built, trained = set(), set(), {}, set()
    traces, stages, failures = {}, [], []
    tools = {1: "primitive_tool", 2: "iron_tool", 3: "steel_tool"}
    animal_outputs = {good for row in wildlife for good in quantities(row["death_output"])}
    animal_outputs.update(good for row in variants for good in quantities(row["extra_death_output"]))
    for stage_row in sorted(tiers, key=lambda row: int(row["tier"])):
        stage = int(stage_row["tier"])
        if stage > 1:
            missing_goods = set(quantities(stage_row["required_goods"])) - available
            missing_buildings = [ref for ref in quantities(stage_row["required_buildings"]) if built.get(ref.split("@")[0], 0) < int(ref.split("@")[1])]
            if missing_goods or missing_buildings:
                failures.append({"target_stage": stage, "missing_goods": sorted(missing_goods), "missing_buildings": missing_buildings})
                break
        iterations = 0
        while True:
            before = (len(available), len(known), len(made), sum(built.values()), len(trained))
            iterations += 1
            # Basic residents provide science before any university exists.
            for tid, row in sorted(tech.items()):
                if adults > 0 and int(row["min_settlement_tier"]) <= stage and set(items(row["prerequisites"])) <= known and set(quantities(row.get("research_inputs", ""))) <= available:
                    known.add(tid)
                    traces.setdefault(f"tech:{tid}", {"stage": stage, "iteration": iterations, "requires": items(row["prerequisites"]), "material_inputs": quantities(row.get("research_inputs", "")), "source": "resident_background_learning"})
            for ref, row in sorted(grades.items(), key=lambda pair: (int(pair[1]["grade"]), pair[1]["building_id"])):
                bid, grade = row["building_id"], int(row["grade"])
                if adults > 0 and int(roles["builder"]["min_settlement_tier"]) <= stage and int(row["min_settlement_tier"]) <= stage and built.get(bid, 0) == grade - 1 and set(quantities(row["inputs"])) <= available and (ref not in grade_gates or grade_gates[ref] in known):
                    built[bid] = grade
                    traces.setdefault(f"building:{ref}", {"stage": stage, "iteration": iterations, "inputs": sorted(quantities(row["inputs"])), "technology": grade_gates.get(ref)})
            # Conditional ecological premise: appropriate prey exists on the seeded map.
            if built.get("ranger_lodge", 0) >= 1 and "primitive_tool" in available and int(roles["hunter"]["min_settlement_tier"]) <= stage:
                for good in animal_outputs:
                    available.add(good)
                    traces.setdefault(f"good:{good}", {"stage": stage, "iteration": iterations, "source": "accessible_wildlife_with_hunter_and_tool"})
            if built.get("pasture", 0) >= 1:
                for animal in wildlife:
                    if set(quantities(animal.get("periodic_inputs", ""))) <= available:
                        for good in quantities(animal.get("periodic_output", "")):
                            available.add(good)
                            traces.setdefault(f"good:{good}", {"stage": stage, "iteration": iterations, "source": "conditional_tamed_fed_adult_animal", "animal": animal["species_id"]})
            for rid, row in sorted(recipes.items()):
                tg = int(row["min_tool_grade"])
                if adults > 0 and int(row["min_settlement_tier"]) <= stage and int(roles[row["role_id"]]["min_settlement_tier"]) <= stage and built.get(row["building_id"], 0) >= int(row["min_building_grade"]) and set(quantities(row["inputs"])) <= available and (not tg or tools[tg] in available) and (not row["source_node"] or row["source_node"] in habitat) and (rid not in recipe_gates or recipe_gates[rid] in known):
                    made.add(rid)
                    traces.setdefault(f"recipe:{rid}", {"stage": stage, "iteration": iterations, "inputs": sorted(quantities(row["inputs"])), "building": f'{row["building_id"]}@{row["min_building_grade"]}', "tool": tools.get(tg), "technology": recipe_gates.get(rid)})
                    for good in quantities(row["outputs"]):
                        available.add(good)
                        traces.setdefault(f"good:{good}", {"stage": stage, "iteration": iterations, "source": rid})
            for mid, row in sorted(military.items()):
                if adults > 0 and int(row["min_settlement_tier"]) <= stage and row["required_technology_id"] in known and int(roles[row["profession_id"]]["min_settlement_tier"]) <= stage and built.get(row["training_building_id"], 0) >= int(row["min_building_grade"]) and set(quantities(row["equipment"])) <= available:
                    trained.add(mid)
                    traces.setdefault(f"military:{mid}", {"stage": stage, "iteration": iterations, "technology": row["required_technology_id"], "equipment": sorted(quantities(row["equipment"])), "training_seconds": float(row["training_seconds"])})
            if before == (len(available), len(known), len(made), sum(built.values()), len(trained)):
                break
        stages.append({"stage": stage, "closure_iterations": iterations, "researchable_technologies": len(known), "producible_goods": len(available), "executable_recipes": len(made), "buildable_types": len(built), "buildable_grades": sum(built.values()), "trainable_military_roles": sorted(trained)})
    missing = {"technologies": sorted(set(tech) - known), "goods": sorted(set(goods) - available), "recipes": sorted(set(recipes) - made), "building_grades": sorted(ref for ref, row in grades.items() if built.get(row["building_id"], 0) < int(row["grade"])), "military_roles": sorted(set(military) - trained)}
    milestones = ["building:hearth@1", "tech:m02_carpentry", "recipe:make_furniture", "tech:k01_teaching", "building:university@1", "recipe:smelt_iron", "recipe:craft_iron_tool", "recipe:make_cart", "building:caravanserai@1", "recipe:refine_steel", "recipe:forge_steel_armor", "building:university@5", "good:coin", "recipe:mint_gold", "recipe:mint_silver", "good:paper", "good:book", "tech:k03_natural_studies"] + [f"military:{mid}" for mid in military]
    return {"passed": not failures and not any(missing.values()), "stage_gate_failures": failures, "stages": stages, "unreachable": missing, "milestone_witnesses": {key: traces.get(key) for key in milestones}, "assumptions": ["At least one living adult can learn in the background without a university", "Bootstrap habitat sources and huntable wildlife are physically accessible", "Finite quantities, population growth, work schedules, travel, food reserves and durations are not simulated", "All predecessor building grades, technology prerequisites, tools, recipe inputs and production stations must be structurally reachable before use"]}


def main() -> int:
    results: dict[str, dict[str, int]] = defaultdict(lambda: {"passed": 0, "failed": 0})
    errors: list[str] = []
    source_hashes: dict[str, str] = {}

    def check(condition: bool, category: str, description: str) -> None:
        results[category]["passed" if condition else "failed"] += 1
        if not condition:
            errors.append(f"{category}: {description}")

    def read(name: str) -> list[dict[str, str]]:
        path = DATA / name
        source_hashes[str(path.relative_to(ROOT)).replace("\\", "/")] = hashlib.sha256(path.read_bytes()).hexdigest()
        with path.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        check(bool(rows), "catalogs", f"empty catalog {name}")
        check(all(None not in r and None not in r.values() for r in rows), "catalogs", f"malformed CSV row {name}")
        return rows

    def indexed(rows: list[dict[str, str]], column: str) -> dict[str, dict[str, str]]:
        answer = {r[column]: r for r in rows}
        check(len(answer) == len(rows), "unique_ids", f"duplicate {column}")
        return answer

    try:
        tech_rows = read("technologies.csv")
        tech = indexed(tech_rows, "technology_id")
        goods = indexed(read("goods.csv"), "good_id")
        recipes = indexed(read("recipes.csv"), "recipe_id")
        buildings = indexed(read("buildings.csv"), "building_id")
        grades_rows = read("building_tiers.csv")
        grades = {f'{r["building_id"]}@{r["grade"]}': r for r in grades_rows}
        check(len(grades) == len(grades_rows), "unique_ids", "duplicate building grade")
        roles = indexed(read("roles.csv"), "role_id")
        military = indexed(read("military_loadouts.csv"), "military_role_id")
        tiers = read("settlement_tiers.csv")
        bootstrap = read("bootstrap.csv")
        wildlife = read("wildlife.csv")
        variants = read("wildlife_variants.csv")
        read("species_behaviors.csv")
        read("crops.csv")
        read("carrying_equipment.csv")
        read("natural_sources.csv")
        read("resource_deposits.csv")
        read("resource_regions.csv")
        minting_policy = read("minting_policy.csv")

        check(len(tech) == 73, "shape", "expected 73 technologies including roads, bridges and carpenter siege production")
        branch_counts = Counter(r["branch_id"] for r in tech_rows)
        check(set(branch_counts) == BRANCHES, "shape", "expected eight named branches")
        expected_branch_counts = {"food": 8, "materials": 13, "metals": 9, "textiles": 5, "transport": 11, "defense": 13, "health_civics": 6, "knowledge": 8}
        check(dict(branch_counts) == expected_branch_counts, "shape", "unexpected per-branch technology counts")
        check(not any(r["kind"] == "world_treasury" or (r["kind"] == "inventory" and r["ref_id"] == "coin") for r in bootstrap), "currency", "coins must originate from metal-backed minting")
        check(all(int(r["world_treasury_grant"]) == 0 for r in tiers), "currency", "obsolete treasury grant")
        check(len(minting_policy) == 1 and minting_policy[0]["initial_world_coins"] == "0", "currency", "invalid money origin policy")
        for rid, metal, amount in (("mint_gold", "gold_ingot", 100), ("mint_silver", "silver_ingot", 10)):
            row = recipes.get(rid, {})
            check(row.get("building_id") == "mint" and row.get("role_id") == "mintmaster" and row.get("world_effect") == "mint_issue", "currency", f"invalid mint station or event {rid}")
            check(quantities(row.get("inputs", "")) == {metal: 1, "charcoal": 1} and quantities(row.get("outputs", "")) == {"coin": amount}, "currency", f"unbacked mint batch {rid}")
            check(rid in items(tech.get("z08_coinage", {}).get("unlock_recipes", "")), "currency", f"mint missing research gate {rid}")
        check({rid for rid, row in recipes.items() if "coin" in quantities(row["outputs"])} == {"mint_gold", "mint_silver"}, "currency", "unauthorized coin-producing recipe")
        recipe_owner: dict[str, str] = {}
        grade_owner: dict[str, str] = {}
        military_owner: dict[str, str] = {}
        children: dict[str, list[str]] = defaultdict(list)
        indegree: dict[str, int] = {}
        for tid, row in tech.items():
            check(bool(re.fullmatch(r"[a-z][a-z0-9_]*", tid)), "unique_ids", f"invalid identifier {tid}")
            check(bool(row["name_ru"]), "shape", f"empty name {tid}")
            stage = int(row["min_settlement_tier"])
            cost, effect = float(row["cost_points"]), float(row["effect_value"])
            check(1 <= stage <= 5, "values", f"invalid settlement stage {tid}")
            check(math.isfinite(cost) and cost > 0, "values", f"invalid cost {tid}")
            check(math.isfinite(effect) and effect > 0, "values", f"invalid effect {tid}")
            check(row["effect_id"] in EFFECTS, "values", f"unknown effect {tid}")
            check(row["status"] == "prepared", "status", f"must remain prepared, not implemented: {tid}")
            check(row["priority_tag"] in {"food", "production", "transport", "safety", "culture"}, "values", f"invalid priority {tid}")
            prerequisites = items(row["prerequisites"])
            indegree[tid] = len(prerequisites)
            check(len(prerequisites) == len(set(prerequisites)), "dag", f"duplicate prerequisite {tid}")
            for parent in prerequisites:
                check(parent in tech and parent != tid, "dag", f"invalid prerequisite {tid} -> {parent}")
                if parent in tech:
                    children[parent].append(tid)
                    check(int(tech[parent]["min_settlement_tier"]) <= stage, "stage_order", f"future-stage prerequisite {tid} -> {parent}")
            for column, catalog in (("recipe_refs", recipes), ("good_refs", goods), ("building_refs", grades)):
                refs = items(row[column])
                check(len(refs) == len(set(refs)), "references", f"duplicate refs {tid}:{column}")
                for ref in refs:
                    check(ref in catalog, "references", f"missing {column} {tid} -> {ref}")
            for good, amount in quantities(row.get("research_inputs", "")).items():
                check(good in goods and amount > 0, "research_materials", f"invalid research consumable {tid}: {good}")
            for ref in items(row["building_refs"]):
                if ref in grades:
                    check(int(grades[ref]["min_settlement_tier"]) <= stage, "stage_order", f"building reference later than tech {tid} -> {ref}")
            for column, refs_column, catalog, owner in (
                ("unlock_recipes", "recipe_refs", recipes, recipe_owner),
                ("unlock_building_grades", "building_refs", grades, grade_owner),
            ):
                for ref in items(row[column]):
                    check(ref in items(row[refs_column]), "unlocks", f"unlock not in refs {tid} -> {ref}")
                    check(ref not in owner, "unlocks", f"multiple owners for {ref}")
                    owner[ref] = tid
                    if ref in catalog:
                        check(int(catalog[ref]["min_settlement_tier"]) <= stage, "unlocks", f"unlock later than technology {tid} -> {ref}")
            for ref in items(row["unlock_military_roles"]):
                check(ref in military, "military", f"unknown military role {tid} -> {ref}")
                check(ref not in military_owner, "military", f"multiple training unlocks for {ref}")
                military_owner[ref] = tid
                if ref in military:
                    loadout = military[ref]
                    check(loadout["required_technology_id"] == tid, "military", f"reverse training reference mismatch {ref}")
                    check(int(loadout["min_settlement_tier"]) == stage, "military", f"training tier mismatch {ref}")
                    check(loadout["profession_id"] == "guard" and loadout["profession_id"] in roles, "military", f"invalid military profession {ref}")
                    station = f'{loadout["training_building_id"]}@{loadout["min_building_grade"]}'
                    check(station in items(row["building_refs"]), "military", f"training building missing from tech refs {ref}")
                    kit = quantities(loadout["equipment"])
                    check(bool(kit) and all(quantity > 0 for quantity in kit.values()), "military", f"invalid equipment quantities {ref}")
                    for good in kit:
                        check(good in goods and good in items(row["good_refs"]), "military", f"loadout item missing from technology refs {ref}: {good}")
                    check(float(loadout["training_seconds"]) > 0, "military", f"training must take time {ref}")
                    check(loadout["mounted"] == "false", "military", f"unimplemented mount implied {ref}")
                    if ref == "archer":
                        check(kit.get("arrows", 0) >= 1 and int(loadout["ammo_per_ranged_attack"]) == 1, "military", "archer must consume real arrows")
        check(set(military_owner) == set(military), "military", "every military role needs a research unlock")

        pending = deque(sorted(tid for tid, degree in indegree.items() if degree == 0))
        topological: list[str] = []
        while pending:
            tid = pending.popleft()
            topological.append(tid)
            for child in sorted(children[tid]):
                indegree[child] -= 1
                if indegree[child] == 0:
                    pending.append(child)
        check(len(topological) == len(tech), "dag", "cycle or missing prerequisite")
        for recipe in sorted(SURVIVAL_RECIPES):
            check(recipe in recipes, "bootstrap", f"unknown survival recipe {recipe}")
            check(recipe not in recipe_owner, "bootstrap", f"survival recipe tech-locked {recipe}")
        check("university@1" in grade_owner and grade_owner["university@1"] == "k01_teaching", "bootstrap", "university G1 must unlock at teaching")
        check("temple@1" not in grade_owner, "bootstrap", "temple G1 must be available without research")
        for ref in ("house@1", "well@1", "fishing_hut@1"):
            check(ref not in grade_owner, "bootstrap", f"primitive infrastructure locked: {ref}")
        for building in ("storehouse", "granary"):
            for grade, gate in enumerate(("f01_preservation", "m10_comfort_housing", "m11_town_houses", "m12_stone_houses"), 1):
                check(grade_owner.get(f"{building}@{grade}") == gate, "storage", f"incorrect storage grade gate: {building}@{grade}")
        comfort = read("housing_comfort.csv")
        check({r["grade"] for r in comfort} == {"1", "2", "3", "4", "5"}, "housing", "five comfort grades required")
        for row in comfort:
            ref = f'{row["building_id"]}@{row["grade"]}'
            tid = row["required_technology_id"]
            check(ref in grades and (not tid or tid in tech), "housing", f"unknown comfort reference {ref}")
            check(grade_owner.get(ref, "") == tid, "housing", f"comfort gate differs from technology {ref}")

        # Tier-gate reachability, not a proof of resource quantities or real map access.
        advancement_checks = []
        for tier_row in tiers:
            target = int(tier_row["tier"])
            if target == 1:
                continue
            prior = target - 1
            researchable: set[str] = set()
            for tid in topological:
                row = tech[tid]
                if int(row["min_settlement_tier"]) <= prior and set(items(row["prerequisites"])).issubset(researchable):
                    researchable.add(tid)

            def grade_available(ref: str) -> bool:
                if ref not in grades or int(grades[ref]["min_settlement_tier"]) > prior:
                    return False
                bid, grade = ref.split("@")
                return all(grade_owner.get(f"{bid}@{g}") in researchable or f"{bid}@{g}" not in grade_owner for g in range(1, int(grade) + 1))

            allowed_recipes = {
                rid: row for rid, row in recipes.items()
                if int(row["min_settlement_tier"]) <= prior
                and (rid not in recipe_owner or recipe_owner[rid] in researchable)
                and grade_available(f'{row["building_id"]}@{row["min_building_grade"]}')
            }
            for good in quantities(tier_row["required_goods"]):
                producers = sorted(rid for rid, row in allowed_recipes.items() if good in quantities(row["outputs"]))
                check(bool(producers), "advancement_gates", f"S{target} needs {good}, unavailable at S{prior}")
                advancement_checks.append({"target_stage": target, "good": good, "producers_before_transition": producers})
            for ref in quantities(tier_row["required_buildings"]):
                check(grade_available(ref), "advancement_gates", f"S{target} needs building {ref}, unavailable at S{prior}")

        integrated = integrated_reachability(tech, goods, recipes, grades, roles, tiers, bootstrap, wildlife, military, variants)
        check(integrated["passed"], "integrated_reachability", f"combined tech/economy deadlock: {integrated['stage_gate_failures']} {integrated['unreachable']}")
        check(integrated["milestone_witnesses"]["building:university@1"] is not None and integrated["milestone_witnesses"]["building:university@1"]["stage"] == 2, "integrated_reachability", "first university must be buildable at S2 through pre-university learning")
        coin_witness = integrated["milestone_witnesses"]["good:coin"]
        check(coin_witness is not None and coin_witness["stage"] == 3 and coin_witness["source"] in {"mint_gold", "mint_silver"}, "currency", "coin must first become reachable through S3 minting")
        for mid, row in military.items():
            witness = integrated["milestone_witnesses"][f"military:{mid}"]
            check(witness is not None and witness["stage"] == int(row["min_settlement_tier"]), "integrated_reachability", f"military kit and training station unreachable at declared stage {mid}")
        # Counterexamples verify the joint validator actually catches cross-system cycles.
        sensitivity_checks = []
        for tid, new_stage, blocked_stage in (("t02_linen", 3, 3), ("z02_ironworking", 4, 4)):
            mutated = {key: dict(row) for key, row in tech.items()}
            mutated[tid]["min_settlement_tier"] = str(new_stage)
            negative = integrated_reachability(mutated, goods, recipes, grades, roles, tiers, bootstrap, wildlife, military, variants)
            caught = not negative["passed"] and bool(negative["stage_gate_failures"]) and negative["stage_gate_failures"][0]["target_stage"] == blocked_stage
            check(caught, "integrated_counterexamples", f"failed to detect injected {tid}/S{new_stage} progression cycle")
            sensitivity_checks.append({"in_memory_mutation": f"{tid}.min_settlement_tier={new_stage}", "expected_blocked_stage": blocked_stage, "detected": caught, "failure": negative["stage_gate_failures"]})
        mutated = {key: dict(row) for key, row in tech.items()}
        mutated["k06_writing_materials"]["research_inputs"] = "book=1"
        negative = integrated_reachability(mutated, goods, recipes, grades, roles, tiers, bootstrap, wildlife, military, variants)
        caught = not negative["passed"] and "k06_writing_materials" in negative["unreachable"]["technologies"] and "book" in negative["unreachable"]["goods"]
        check(caught, "integrated_counterexamples", "failed to detect writing/book bootstrap cycle")
        sensitivity_checks.append({"in_memory_mutation": "k06_writing_materials.research_inputs=book=1", "detected": caught, "unreachable_technologies": negative["unreachable"]["technologies"]})

        for grade in range(1, 6):
            ref = f"university@{grade}"
            check(ref in grades, "education", f"missing {ref}")
            if ref in grades:
                expected_stage = max(2, grade)
                check(int(grades[ref]["min_settlement_tier"]) == expected_stage, "education", f"university stage mismatch {ref}")
                check(int(grades[ref]["worker_slots"]) == [1, 1, 2, 2, 3][grade - 1], "education", f"update documented worker slots {ref}")
                check(grade_owner.get(ref, "").startswith(f"k0{grade}_"), "education", f"wrong university unlock {ref}")
        for role in ARTISANS | {"scholar", "priest"}:
            check(role in roles, "role_groups", f"unknown classified profession {role}")
        groups = {role: role_group(role) for role in roles}
        check(sum(Counter(groups.values()).values()) == len(roles), "role_groups", "each profession must map to exactly one group")
        check(role_group("") == "resident", "role_groups", "unassigned adult maps to resident")
        check(role_group("scholar") != "resident" and role_group("priest") != "resident", "role_groups", "specialists receive no resident baseline")

        check(RESEARCH_CYCLE_SECONDS == 10 and ECONOMIC_DAY_SECONDS == 600, "time", "v0.4 research cycle is 10s; economic day remains 600s")
        check(Q == 0.88, "formula", "user decay must apply to every role")
        formula_samples = {}
        for group, weight in WEIGHTS.items():
            samples = []
            check(total(weight, 0) == 0, "formula", f"zero population {group}")
            for n in (1, 2, 3, 10, 20, 50):
                direct = sum(weight * Q ** i for i in range(n))
                closed = total(weight, n)
                check(math.isclose(direct, closed, rel_tol=1e-12, abs_tol=1e-12), "formula", f"geometric identity {group} n={n}")
                check(math.isclose(closed - total(weight, n - 1), weight * Q ** (n - 1), rel_tol=1e-10, abs_tol=1e-12), "formula", f"marginal identity {group} n={n}")
                samples.append({"effective_count": n, "total_points_per_cycle": closed, "marginal_points_per_cycle": weight * Q ** (n - 1)})
            previous_delta = float("inf")
            for n in range(1, 101):
                result = total(weight, n)
                delta = result - total(weight, n - 1)
                check(delta > 0, "monotonicity", f"non-positive marginal {group} n={n}")
                check(delta < previous_delta, "concavity", f"non-diminishing marginal {group} n={n}")
                check(math.isfinite(result) and result < weight / (1 - Q), "ceiling", f"invalid finite ceiling {group} n={n}")
                previous_delta = delta
            for n in (0.0, 0.01, 0.5, 1.0, 2.5, 10.0, 50.0):
                check(total(weight, n + 0.0) == total(weight, n), "activity", f"inactive addition changes research {group} n={n}")
                check(total(weight, n + 0.125) > total(weight, n), "activity", f"partial addition is not positive {group} n={n}")
                check(total(weight, n + 0.25) - total(weight, n) > total(weight, n + 0.5) - total(weight, n + 0.25), "activity", f"continuous sum not concave {group} n={n}")
            check(total(weight, 0.5 + 0.5) == total(weight, 1.0), "activity", f"split activity changes research {group}")
            check(math.isclose(total(weight, 1) * 10 / RESEARCH_CYCLE_SECONDS, weight), "time", f"one full worker research cycle {group}")
            formula_samples[group] = {"weight": weight, "q": Q, "ceiling_points_per_cycle": weight / (1 - Q), "samples": samples}

        global_ceiling = sum(WEIGHTS.values()) / (1 - Q)
        check(math.isclose(global_ceiling, 109.16666666666667, abs_tol=1e-10), "ceiling", "global limit mismatch")
        for n in (1, 2, 3, 10, 20, 50):
            check(sum(total(w, n) for w in WEIGHTS.values()) < global_ceiling, "ceiling", f"finite groups exceed bound n={n}")
        check(math.isclose(total(10, 1), 10.0), "user_examples", "first scholar")
        check(math.isclose(total(10, 2) - total(10, 1), 8.8), "user_examples", "second scholar")
        check(math.isclose(total(10, 3) - total(10, 2), 7.744), "user_examples", "third scholar")
        check(math.isclose(100 * total(10, 1) / 100, 10), "user_examples", "10 percent only at cost 100")
        check(math.isclose(100 * total(10, 1) / 200, 5), "user_examples", "cost 200 means 5 percent")
        check(math.isclose(total(10, 1) * 5 / RESEARCH_CYCLE_SECONDS, 5), "time", "half-cycle full then inactive differs from continuous fractional count")
        for invalid in (-1, float("nan"), float("inf")):
            try:
                total(10, invalid)
                check(False, "invalid_input", f"accepted invalid count {invalid}")
            except ValueError:
                check(True, "invalid_input", f"rejected {invalid}")

        report = {
            "schema_version": 1,
            "design_version": "technology-design-v4-social-agriculture",
            "design_status": "prepared_not_implemented",
            "validation_scope": "CSV references, dependency DAG, stage gates and proposed math only; no Unity or gameplay validation",
            "created_at": datetime.now(timezone(timedelta(hours=3))).isoformat(timespec="seconds"),
            "passed": not errors,
            "counts": {"technologies": len(tech), "branches": len(branch_counts), "dependency_edges": sum(len(items(row["prerequisites"])) for row in tech_rows), "recipe_unlocks": len(recipe_owner), "building_grade_unlocks": len(grade_owner), "military_training_unlocks": len(military_owner), "goods": len(goods), "recipes": len(recipes), "buildings": len(buildings), "building_grades": len(grades), "roles": len(roles)},
            "branches": dict(sorted(branch_counts.items())),
            "checks": dict(results),
            "check_count": sum(sum(result.values()) for result in results.values()),
            "failed_count": len(errors),
            "errors": errors,
            "topological_order": topological,
            "research_cycle_seconds": RESEARCH_CYCLE_SECONDS,
            "economic_day_seconds": ECONOMIC_DAY_SECONDS,
            "runtime_group_scope": "resident and actual adult Process work; priest/scholar require future professions",
            "eligible_adult_age": 16,
            "role_to_group": dict(sorted(groups.items())),
            "formula_samples": formula_samples,
            "fractional_scholar_half_activity_rate": total(10, 0.5),
            "global_ceiling_points_per_cycle": global_ceiling,
            "progress_examples": {"one_scholar_cost_100_percent_per_cycle": 10, "one_scholar_cost_200_percent_per_cycle": 5},
            "advancement_gate_checks": advancement_checks,
            "integrated_structural_reachability": integrated,
            "integrated_validator_counterexamples": sensitivity_checks,
            "limits": ["Integrated closure includes technology, stages, tool types, building upgrades, recipe inputs and military equipment; it does not simulate finite stocks, terrain access, population or scheduling", "Rates and costs have not been gameplay balanced", "No game simulation, UI, saves or research runtime executed"],
            "source_sha256": source_hashes,
            "validator_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        }
    except (OSError, ValueError, KeyError, TypeError) as exc:
        errors.append(f"validation_exception: {type(exc).__name__}: {exc}")
        report = {"schema_version": 1, "design_status": "prepared_not_implemented", "passed": False, "errors": errors, "source_sha256": source_hashes}

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "counts": report.get("counts", {}), "checks": report.get("check_count", 0), "errors": errors, "report": str(OUTPUT.relative_to(ROOT))}, ensure_ascii=False, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
