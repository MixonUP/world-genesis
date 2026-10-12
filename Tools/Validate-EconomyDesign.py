"""Validate proposed CSV design, not the running Unity economy. Python stdlib only."""
import argparse
import csv
import json
import math
from pathlib import Path


def validate(root):
    errors = []
    counts = {}

    def check(ok, message):
        if not ok:
            errors.append(message)

    def table(name, key):
        path = root / name
        with path.open(encoding="utf-8-sig", newline="") as handle:
            rows = list(csv.DictReader(handle))
        result = {}
        for row in rows:
            identity = row[key]
            check(identity and identity.strip() == identity, f"{name}: invalid ID {identity!r}")
            check(identity not in result, f"{name}: duplicate {identity}")
            result[identity] = row
        counts[name] = len(rows)
        return result

    def number(value, context, minimum=0, integer=False):
        try:
            result = float(value)
            check(math.isfinite(result) and result >= minimum, f"{context}: invalid number {value}")
            if integer:
                check(result.is_integer(), f"{context}: integer required")
            return result
        except (ValueError, TypeError):
            errors.append(f"{context}: invalid number {value!r}")
            return 0

    goods = table("goods.csv", "good_id")
    roles = table("roles.csv", "role_id")
    buildings = table("buildings.csv", "building_id")
    recipes = table("recipes.csv", "recipe_id")
    sources = table("natural_sources.csv", "node_id")
    wildlife = table("wildlife.csv", "species_id")
    bootstrap = table("bootstrap.csv", "entry_id")
    stages = table("settlement_tiers.csv", "tier")
    military = table("military_loadouts.csv", "military_role_id")
    civil_defense = table("civil_defense.csv", "policy_id")
    technologies = table("technologies.csv", "technology_id")
    deposits = table("resource_deposits.csv", "node_id")
    regions = table("resource_regions.csv", "region_id")
    minting = table("minting_policy.csv", "policy_id")
    comfort = table("housing_comfort.csv", "grade")
    variants = table("wildlife_variants.csv", "variant_id")
    behavior = table("species_behaviors.csv", "species_id")
    crops = table("crops.csv", "crop_id")
    equipment = table("carrying_equipment.csv", "equipment_id")
    with (root / "building_tiers.csv").open(encoding="utf-8-sig", newline="") as f:
        tiers = list(csv.DictReader(f))
    counts["building_tiers.csv"] = len(tiers)

    def quantities(text, context, known=goods):
        result = {}
        for token in filter(None, text.split(";")):
            try:
                identity, value = token.split("=")
            except ValueError:
                errors.append(f"{context}: bad amount {token}")
                continue
            check(identity in known, f"{context}: unknown ID {identity}")
            check(identity not in result, f"{context}: repeated ID {identity}")
            result[identity] = number(value, context, minimum=1, integer=True)
        return result

    for identity, row in goods.items():
        for col in ("load_units", "nutrition", "shelf_life_seconds", "base_price_coins"):
            number(row[col], f"goods {identity}/{col}", 0.000001 if col == "load_units" else 0)
        check(row["origin"] in {"recipe", "wildlife", "mint"}, f"{identity}: origin")
    for identity, role in roles.items():
        for station in role["buildings"].split(";"):
            check(station in buildings, f"role {identity}: unknown building {station}")
    for identity, source in sources.items():
        number(source["initial_charges"], identity, 1, True)
        amount = number(source["regrow_charges"], identity, 0, True)
        delay = number(source["regrow_seconds"], identity, 0)
        check((amount == 0) == (delay == 0), f"{identity}: incomplete regrowth pair")
        check(amount <= float(source["initial_charges"]), f"{identity}: regeneration exceeds capacity")
    for identity, row in regions.items():
        for column in ("specialization", "needs_import"):
            check(set(row[column].split(";")) <= set(goods), f"region {identity}: unknown goods")
        number(row["patch_scale_tiles"], identity, 8, True)
    for identity, row in deposits.items():
        check(identity in sources, f"deposit {identity}: unknown natural source")
        check(row["regrowth_mode"] == "depleted_cooldown", f"{identity}: unsupported deposit renewal")
        check(bool(row["biomes"]), f"{identity}: no terrain constraint")
        actual_weights = {k: int(v) for k, v in (token.split("=") for token in row["region_weights"].split(";"))}
        check(set(actual_weights) == set(regions), f"{identity}: incomplete regional weights")
        check(all(v >= 0 for v in actual_weights.values()) and max(actual_weights.values()) > min(actual_weights.values()), f"{identity}: uniform or invalid regional distribution")
        check(row["guarantee_near_start"] == ("true" if identity == "mature_tree" else "false"), f"{identity}: every town must not receive every deposit")
        number(row["minimum_spacing_tiles"], identity, 1, True)
        low = number(row["cluster_nodes_min"], identity, 1, True)
        high = number(row["cluster_nodes_max"], identity, 1, True)
        check(low <= high, f"{identity}: reversed cluster bounds")
        jitter = number(row["delay_jitter_fraction"], identity, 0)
        check(jitter <= .25, f"{identity}: excessive timer jitter")
        check(identity not in sources or float(sources[identity]["regrow_seconds"]) > 0, f"{identity}: instant deposit regeneration")
    check(len({sources[n]["regrow_seconds"] for n in deposits if n in sources}) >= 8, "deposit cooldowns must preserve different resource scarcity")
    animal_outputs = set()
    live_animal_outputs = set()
    for identity, row in wildlife.items():
        animal_outputs.update(quantities(row["death_output"], identity))
        periodic = quantities(row["periodic_output"], identity)
        live_animal_outputs.update(periodic)
        delay = number(row["periodic_seconds"], identity, 0)
        feed = quantities(row["periodic_inputs"], identity)
        check(bool(periodic) == bool(delay) == bool(feed), f"{identity}: live product requires feed and time")
        for field in ("group_min", "group_max", "birth_cooldown_seconds", "cap_per_256_habitat_tiles"):
            number(row[field], f"{identity}/{field}", 1, True)
        check(int(row["group_min"]) <= int(row["group_max"]), f"{identity}: group bounds")
    for identity, row in variants.items():
        check(row["species_id"] in wildlife, f"{identity}: unknown base species")
        chance = number(row["spawn_chance"], identity, 0.000001)
        check(chance <= .01, f"{identity}: rare variant must remain rare")
        number(row["respawn_min_seconds"], identity, 600)
        number(row["max_per_region"], identity, 1, True)
        number(row["health_multiplier"], identity, 1)
        number(row["damage_multiplier"], identity, 1)
        output = quantities(row["extra_death_output"], identity)
        check(bool(output) and "coin" not in output, f"{identity}: boss cannot mint currency")
        animal_outputs.update(output)
        quantities(row["tame_inputs"], identity)
        check(set(filter(None, row["tame_technology_ids"].split(";"))) <= set(technologies), f"{identity}: unknown tame research")
        check(bool(row["tame_inputs"]) == bool(row["tame_technology_ids"]), f"{identity}: special taming needs food and research")
    check(set(behavior) == set(wildlife), "all species require one behavior card")
    for identity, row in behavior.items():
        quantities(row["tame_food_inputs"], identity)
        check(set(filter(None, row["food_good_refs"].split(";"))) <= set(goods), f"{identity}: unknown diet good")
        check(row["handler_role_id"] in roles, f"{identity}: unknown handler role")
        check(row["tameable"] in {"true", "false"}, f"{identity}: tameable flag")
        work = number(row["tame_work_seconds"], identity, 0)
        check((work > 0 and bool(row["tame_food_inputs"])) == (row["tameable"] == "true"), f"{identity}: taming needs real feed and work")
        number(row["chase_limit_seconds"], identity, 1)
        number(row["max_neighbors"], identity, 1, True)
        for field in ("separation_weight", "alignment_weight", "cohesion_weight"):
            number(row[field], identity, 0)
    for identity, row in crops.items():
        check(row["role_id"] in roles and row["building_id"] in buildings, f"{identity}: crop role/station")
        quantities(row["inputs"], identity)
        quantities(row["repeat_inputs"], identity)
        check(bool(quantities(row["outputs"], identity)), f"{identity}: crop output")
        check(not row["recipe_id"] or row["recipe_id"] in recipes, f"{identity}: unknown crop recipe")
        check(not row["required_technology_id"] or row["required_technology_id"] in technologies, f"{identity}: unknown crop technology")
        check(row["cycle"] in {"annual", "perennial", "regrowing_node"}, f"{identity}: cycle")
        number(row["active_work_seconds"], identity, .1)
        number(row["growth_seconds"], identity, 1)
        if row["cycle"] == "perennial": number(row["max_harvests"], identity, 1, True)
        if row["recipe_id"] in recipes:
            recipe = recipes[row["recipe_id"]]
            check(row["inputs"] == recipe["inputs"] and row["outputs"] == recipe["outputs"], f"{identity}: crop/recipe amounts disagree")
    expected_capacity = {"basket_small": 10, "basket_woven": 15, "basket_framed": 25, "donkey_pack": 50, "horse_pack": 75, "saddle_bags": 0, "trade_cart": 150}
    check(set(equipment) == set(expected_capacity), "unexpected transport equipment IDs")
    for identity, row in equipment.items():
        check(number(row["total_capacity"], identity, 0) == expected_capacity.get(identity), f"{identity}: wrong total carrying capacity")
        check(number(row["capacity_bonus"], identity, 0) == (25 if identity == "saddle_bags" else 0), f"{identity}: invalid stacking bonus")
        check(bool(quantities(row["inputs"], identity)), f"{identity}: free equipment")
        check(row["role_id"] in roles and row["building_id"] in buildings, f"{identity}: equipment role/station")
        if row["role_id"] in roles: check(row["building_id"] in roles[row["role_id"]]["buildings"].split(";"), f"{identity}: role cannot use station")
        check(row["required_technology_id"] in technologies or identity == "basket_small" and not row["required_technology_id"], f"{identity}: unknown equipment tech")
        check(row["unique_owner"] == "true", f"{identity}: nonunique equipment")
        animals = set(filter(None, row["animal_species_ids"].split(";")))
        check(animals <= set(wildlife), f"{identity}: unknown animal")
        check(bool(animals) == (row["requires_animal"] == "1"), f"{identity}: missing draft/pack animal requirement")
        prior = row["consumed_previous_id"]
        check(not prior or prior in equipment and expected_capacity.get(prior, 0) < expected_capacity.get(identity, 0), f"{identity}: invalid consumed upgrade")
        number(row["work_seconds"], identity, .1)
        check(not row["output_good_id"] or row["output_good_id"] in goods, f"{identity}: unknown equipment good")
    if "trade_cart" in equipment:
        check(equipment["trade_cart"]["inputs"] == recipes["make_cart"]["inputs"] and equipment["trade_cart"]["work_seconds"] == recipes["make_cart"]["work_seconds"], "cart instance recipe differs from canonical make_cart")

    tier_lookup = {}
    for tier in tiers:
        identity = tier["building_id"]
        check(identity in buildings, f"tier: unknown building {identity}")
        grade = int(number(tier["grade"], identity, 1, True))
        check(grade <= 5, f"{identity}: grade > 5")
        key = (identity, grade)
        check(key not in tier_lookup, f"duplicate building tier {key}")
        tier_lookup[key] = tier
        tier["_inputs"] = quantities(tier["inputs"], str(key))
        number(tier["work_seconds"], str(key), 0.1)
        check(int(tier["min_settlement_tier"]) >= max(grade, int(buildings.get(identity, {}).get("min_settlement_tier", 1))), f"{key}: early unlock")
    for identity, row in buildings.items():
        check({g for b, g in tier_lookup if b == identity} == {1, 2, 3, 4, 5}, f"{identity}: must have five grades")
        quantities(row["base_inputs"], identity)
        number(row["base_work_seconds"], identity, 0.1)

    check(set(comfort) == {"1", "2", "3", "4", "5"}, "house comfort must have five grades")
    previous_comfort = -1
    for grade in sorted(comfort, key=int):
        row = comfort[grade]
        value = number(row["base_comfort"], f"comfort {grade}", 0)
        check(previous_comfort < value <= 1, f"comfort {grade}: must increase within 0..1")
        previous_comfort = value
        check(row["building_id"] == "house" and row["ownership"] == "private_adult" and row["status"] == "prepared", f"comfort {grade}: invalid ownership/status")
        number(row["rest_multiplier"], f"comfort {grade} rest", 1)
        capacity = number(row["residents_capacity"], f"comfort {grade} residents", 1, True)
        tier = tier_lookup.get(("house", int(grade)), {})
        check(capacity == float(tier.get("special_capacity", -1)), f"comfort {grade}: capacity differs from house definition")
        tid = row["required_technology_id"]
        check((not tid and grade == "1") or (tid in technologies and f"house@{grade}" in technologies[tid]["unlock_building_grades"].split(";")), f"comfort {grade}: wrong technology gate")

    recipe_outputs = set()
    for identity, row in recipes.items():
        role, building = row["role_id"], row["building_id"]
        check(role in roles and building in buildings, f"{identity}: bad role/building")
        row["_inputs"] = quantities(row["inputs"], identity)
        row["_outputs"] = quantities(row["outputs"], identity)
        recipe_outputs.update(row["_outputs"])
        number(row["work_seconds"], identity, 0.1)
        number(row["passive_seconds"], identity, 0)
        check(bool(row["_outputs"] or row["world_effect"]), f"{identity}: no result")
        if role in roles:
            check(building in roles[role]["buildings"].split(";"), f"{identity}: role cannot access station")
            check(int(row["min_settlement_tier"]) >= int(roles[role]["min_settlement_tier"]), f"{identity}: early role")
        level = int(row["min_building_grade"])
        station = tier_lookup.get((building, level))
        check(station is not None, f"{identity}: bad station grade")
        if station:
            check(int(row["min_settlement_tier"]) >= int(station["min_settlement_tier"]), f"{identity}: unlock before building")
        check(0 <= int(row["min_tool_grade"]) <= 3, f"{identity}: bad tool grade")
        node = row["source_node"]
        charges = number(row["source_charges"], identity, 0, True)
        check((node in sources and charges > 0) if node else charges == 0, f"{identity}: source/charges mismatch")
        check(bool(row["_inputs"] or node), f"{identity}: unexplained free production")
        if "coin" in row["_outputs"]:
            metal, amount = ("gold_ingot", 100) if identity == "mint_gold" else ("silver_ingot", 10)
            check(identity in {"mint_gold", "mint_silver"} and building == "mint" and role == "mintmaster" and row["world_effect"] == "mint_issue", f"{identity}: unauthorized money creation")
            check(row["_inputs"] == {metal: 1, "charcoal": 1} and row["_outputs"] == {"coin": amount}, f"{identity}: mint must atomically consume its exact metal and fuel")
            check(identity in technologies.get("z08_coinage", {}).get("unlock_recipes", "").split(";"), f"{identity}: mint not research-gated")
        check("coin" not in row["_inputs"], f"{identity}: unspecified coin conversion or destruction")
    for identity, row in goods.items():
        expected = animal_outputs | live_animal_outputs if row["origin"] == "wildlife" else recipe_outputs
        check(identity in expected, f"{identity}: no production source")
    for identity, row in technologies.items():
        quantities(row.get("research_inputs", ""), f"research materials {identity}")
    check(set(minting) == {"metal_backed_minting"}, "missing canonical minting policy")
    for identity, row in minting.items():
        check(row["initial_world_coins"] == "0" and row["initial_town_grant"] == "0", "unbacked starting currency is forbidden")
        check(row["gold_recipe"] == "mint_gold" and row["silver_recipe"] == "mint_silver", "invalid mint recipe refs")
        check(row["gold_units_per_batch"] == row["silver_units_per_batch"] == "1" and row["gold_coin_output"] == "100" and row["silver_coin_output"] == "10", "mint policy disagrees with recipes")
        for field in ("coins_per_adult_per_day_cap", "target_coins_per_adult", "turnover_reserve_days", "day_seconds"):
            number(row[field], f"mint {field}", 1, True)
        check(row["day_seconds"] == "600", "mint day must use the common economic clock")

    check(set(civil_defense) == {"emergency_civil_defense"}, "missing emergency civilian defense policy")
    for identity, row in civil_defense.items():
        check(row["min_settlement_tier"] == "1" and row["requires_technology"] == "false", f"{identity}: self-defense must be available from founding without research")
        check(row["preserve_profession"] == "true" and row["preserve_job"] == "true", f"{identity}: militia must return to civilian work")
        check(row["equipment_policy"] == "existing_equipped_tool_or_unarmed", f"{identity}: invalid equipment source")
        check(not row["granted_goods"] and row["granted_armor"] == "false", f"{identity}: emergency militia creates free gear")
        number(row["threat_radius_tiles"], identity, 0.1)
        number(row["clear_threat_grace_seconds"], identity, 0.1)

    for identity, row in military.items():
        stage = int(number(row["min_settlement_tier"], identity, 2, True))
        tech = technologies.get(row["required_technology_id"])
        check(tech is not None, f"{identity}: missing military research")
        if tech:
            check(int(tech["min_settlement_tier"]) <= stage, f"{identity}: recruitment before research stage")
            check(identity in tech.get("unlock_military_roles", "").split(";"), f"{identity}: no specific military unlock")
        role = roles.get(row["profession_id"])
        check(role is not None, f"{identity}: missing profession")
        if role:
            check(int(role["min_settlement_tier"]) >= 2, f"{identity}: profession allowed in civilian founding camp")
            check(int(role["min_settlement_tier"]) <= stage, f"{identity}: early profession")
            check(row["training_building_id"] in role["buildings"].split(";"), f"{identity}: training station not assigned to role")
        grade = int(number(row["min_building_grade"], identity, 1, True))
        station = tier_lookup.get((row["training_building_id"], grade))
        check(station is not None, f"{identity}: missing training station")
        if station:
            check(int(station["min_settlement_tier"]) <= stage, f"{identity}: early training grade")
        row["_equipment"] = quantities(row["equipment"], identity)
        check(bool(row["_equipment"]), f"{identity}: empty loadout creates free armed role")
        check(bool(set(row["_equipment"]) & {"spear", "bow", "sword"}), f"{identity}: missing weapon")
        number(row["training_seconds"], identity, 0.1)
        ammo = number(row["ammo_per_ranged_attack"], identity, 0, True)
        if "bow" in row["_equipment"]:
            check(ammo >= 1 and row["_equipment"].get("arrows", 0) >= ammo, f"{identity}: ranged attack without finite ammo")
        else:
            check(ammo == 0, f"{identity}: unexpected ranged ammunition")
        check(row["mounted"] == "false", f"{identity}: mounted unit needs future animal/saddle definitions")

    available = set()
    habitat = set()
    for identity, row in bootstrap.items():
        number(row["amount"], identity, 1, True)
        kind, ref = row["kind"], row["ref_id"]
        if kind == "inventory":
            check(ref in goods, f"{identity}: bad bootstrap good")
            check(ref != "coin", f"{identity}: money may only originate from metal-backed minting")
            available.add(ref)
        elif kind == "habitat":
            check(ref in sources, f"{identity}: bad bootstrap habitat")
            habitat.add(ref)
        else:
            check(kind == "population", f"{identity}: bad bootstrap kind")
    # Structure-only closure: finite starting counts, travel, hunger and probabilities
    # are NOT simulated. Ecological carcass sources are admitted only after hunting is possible.
    built = {}
    reachable_recipes = set()
    tools = {1: "primitive_tool", 2: "iron_tool", 3: "steel_tool"}
    stage_report = []
    check(set(stages) == {"1", "2", "3", "4", "5"}, "settlement must have five tiers")
    for stage in range(1, 6):
        s = stages[str(stage)]
        check(s["world_treasury_grant"] == "0", f"stage {stage}: obsolete unbacked grant")
        gate_goods = quantities(s["required_goods"], f"stage {stage}")
        check(set(gate_goods) <= available, f"stage {stage}: circular goods gate {set(gate_goods)-available}")
        for token in filter(None, s["required_buildings"].split(";")):
            spec, amount = token.split("=")
            b, g = spec.split("@")
            number(amount, f"stage {stage} building count", 1, True)
            check(b in buildings, f"stage {stage}: unknown gate building {b}")
            # Initial hearth is built during stage 1.
            if stage > 1:
                check(built.get(b, 0) >= int(g), f"stage {stage}: circular building gate {spec}")
        changed = True
        while changed:
            before = (len(available), sum(built.values()), len(reachable_recipes))
            for (b, grade), row in sorted(tier_lookup.items(), key=lambda x: (x[0][1], x[0][0])):
                if int(row["min_settlement_tier"]) <= stage and set(row["_inputs"]) <= available and built.get(b, 0) == grade-1:
                    built[b] = grade
            if "ranger_lodge" in built and "primitive_tool" in available:
                available.update(g for g in animal_outputs if goods[g]["origin"] == "wildlife")
            if "pasture" in built:
                for animal in wildlife.values():
                    if set(quantities(animal["periodic_inputs"], "live source")) <= available:
                        available.update(quantities(animal["periodic_output"], "live source"))
            for rid, row in recipes.items():
                tg = int(row["min_tool_grade"])
                if int(row["min_settlement_tier"]) <= stage and built.get(row["building_id"], 0) >= int(row["min_building_grade"]) and set(row["_inputs"]) <= available and (not tg or tools[tg] in available) and (not row["source_node"] or row["source_node"] in habitat):
                    reachable_recipes.add(rid)
                    available.update(row["_outputs"])
            changed = before != (len(available), sum(built.values()), len(reachable_recipes))
        for identity, row in military.items():
            if int(row["min_settlement_tier"]) == stage:
                check(set(row["_equipment"]) <= available, f"{identity}: loadout cannot be produced at unlock stage")
        stage_report.append({"tier": stage, "reachable_goods": len(available), "reachable_recipes": len(reachable_recipes), "reachable_buildings": len(built)})
    check(set(recipes) <= reachable_recipes, f"unreachable recipes: {sorted(set(recipes)-reachable_recipes)}")
    check(set(goods) <= available, f"unreachable goods: {sorted(set(goods)-available)}")
    check(all(built.get(b) == 5 for b in buildings), "some building grade 5 unreachable")

    return {"scope": "proposed economy design, not Unity simulation", "passed": not errors,
            "counts": counts, "structural_reachability": stage_report, "errors": errors,
            "limitations": ["No finite-stock scheduling, pathfinding, hunger or population simulation", "No native or artistic acceptance"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1] / "SourceAssets" / "Design")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    result = validate(args.root)
    output = json.dumps(result, ensure_ascii=False, indent=2)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(output + "\n", encoding="utf-8")
    print(output)
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
