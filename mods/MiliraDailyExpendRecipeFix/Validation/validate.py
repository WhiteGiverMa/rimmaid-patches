#!/usr/bin/env python3
"""Offline validator for the Milira Daily Expend recipe patch.

It loads the upstream mod XML, replays About/../Patches/Recipes.xml against a
merged Defs tree, then checks the resulting nutrition accounting. It never
starts the game and never writes to the upstream mod.

Usage:
    python Validation/validate.py [--mod-root PATH] [--patch PATH]
"""

import argparse
import math
import os
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PATCH = os.path.join(HERE, "..", "Patches", "Recipes.xml")
DEFAULT_MOD = r"A:/SteamLibrary/steamapps/workshop/content/294100/3604490167"

# Nutrition of every def/category referenced by the recipes.
NUT = {
    "RawRice": 0.05,
    "RawPotatoes": 0.05,
    "Milk": 0.05,
    "Meat_Chicken": 0.05,
    "Meat_Pig": 0.05,
    "Milira_SunLightFruit": 0.05,
    "Milira_SunLightSoyBean": 0.05,
    "Milira_SunLightOyster": 0.25,
    "Milira_SunLightabalone": 0.25,
    "Milira_KitchenWaste": 0.01,
    "Milira_SunLightFuel": 0.0,
    "Milira_SunPlateSteel": 0.0,
    "Wastepack": 0.0,
}
NUT_PATCHED = {"Milira_SunLightSoySauce": 0.1, "Milira_SunLightFruitJam": 0.1}
CAT = {"MeatRaw": 0.05, "AnimalProductRaw": 0.05, "PlantFoodRaw": 0.05, "Fish": 0.25}
PRODUCT = {
    "Milira_CalidorBento": 0.9,
    "Milira_CalidorEightTreasurePorridge": 0.5,
    "Milira_IceCream": 0.2,
    "Milira_KitchenWaste": 0.01,
    "Milira_MashedPotatoes": 0.5,
    "Milira_MiliraRainbowCandy": 0.01,
    "Milira_MiTiaoQiang": 2.0,
    "Milira_SpecialSauceGrilledChickenWings": 0.34,
    "Milira_SunLightFruitBeer": 0.08,
    "Milira_SunLightFruitCola": 0.1,
    "Milira_SunLightFruitJam": 0.1,
    "Milira_SunLightFruitPie": 0.4,
    "Milira_SunLightSoySauce": 0.1,
    "Milira_WestSkySeaFishinSauce": 0.6,
}

NUTRITION_GETTER = "IngredientValueGetter_Nutrition"

# Expected final state: def -> (getter, [(kind, key, nutrition_count, items)])
# kind is "def" or "cat"; items is the item count the player should pay.
EXPECTED = {
    "Milira_MakeCalidorBento": (
        NUTRITION_GETTER,
        [
            ("def", "RawRice", 0.25, 5),
            ("def", "Milira_SunLightSoySauce", 0.1, 1),
            ("cat", "MeatRaw|AnimalProductRaw", 0.25, 5),
        ],
    ),
    "Milira_MakeMoreCalidorBento": (
        NUTRITION_GETTER,
        [
            ("def", "RawRice", 1.25, 25),
            ("def", "Milira_SunLightSoySauce", 0.2, 2),
            ("cat", "MeatRaw|AnimalProductRaw", 1.25, 25),
        ],
    ),
    "Milira_MakeCalidorEightTreasurePorridge": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.05, 1),
            ("cat", "PlantFoodRaw", 0.25, 5),
        ],
    ),
    "Milira_MakeIceCream": (NUTRITION_GETTER, [("def", "Milk", 0.1, 2)]),
    "Milira_MakeMoreIceCream": (NUTRITION_GETTER, [("def", "Milk", 0.5, 10)]),
    "Milira_MakeMashedPotatoes": (
        NUTRITION_GETTER,
        [
            ("def", "RawPotatoes", 0.15, 3),
            ("def", "Milk", 0.15, 3),
        ],
    ),
    "Milira_MakeMoreMashedPotatoes": (
        NUTRITION_GETTER,
        [
            ("def", "RawPotatoes", 0.75, 15),
            ("def", "Milk", 0.75, 15),
        ],
    ),
    "Milira_MakeMiliraRainbowCandy": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.05, 1),
        ],
    ),
    "Milira_MakeKitchenWaste": (
        None,
        [
            ("cat", "MeatRaw|AnimalProductRaw|PlantFoodRaw", 1.0, 1),
        ],
    ),
    "Milira_MakeMoreKitchenWaste": (
        None,
        [
            ("cat", "MeatRaw|AnimalProductRaw|PlantFoodRaw", 5.0, 5),
        ],
    ),
    "Milira_BrewSunLightFruitBeer": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.05, 1),
        ],
    ),
    "Milira_BrewMoreSunLightFruitBeer": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.25, 5),
        ],
    ),
    "Milira_MakeSunLightFruitCola": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.05, 1),
            ("def", "Milira_SunLightFruitJam", 0.1, 1),
        ],
    ),
    "Milira_MakeMoreSunLightFruitCola": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.25, 5),
            ("def", "Milira_SunLightFruitJam", 0.5, 5),
        ],
    ),
    "Milira_MakeSunLightFruitJam": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.1, 2),
        ],
    ),
    "Milira_MakeMoreSunLightFruitJam": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.5, 10),
        ],
    ),
    "Milira_MakeSunLightFruitPie": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.05, 1),
            ("def", "Milira_SunLightFruitJam", 0.2, 2),
            ("cat", "PlantFoodRaw", 0.1, 2),
        ],
    ),
    "Milira_MakeMoreSunLightFruitPie": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightFruit", 0.25, 5),
            ("def", "Milira_SunLightFruitJam", 1.0, 10),
            ("cat", "PlantFoodRaw", 0.5, 10),
        ],
    ),
    "Milira_MakeSunLightSoySauce": (
        NUTRITION_GETTER,
        [
            ("def", "RawRice", 0.05, 1),
            ("def", "Milira_SunLightSoyBean", 0.05, 1),
        ],
    ),
    "Milira_MakeMoreSunLightSoySauce": (
        NUTRITION_GETTER,
        [
            ("def", "RawRice", 0.5, 10),
            ("def", "Milira_SunLightSoyBean", 0.5, 10),
        ],
    ),
    "Milira_MakeWestSkySeaFishinSauce": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightSoySauce", 0.1, 1),
            ("cat", "Fish", 0.25, 1),
        ],
    ),
    "Milira_MakeMoreWestSkySeaFishinSauce": (
        NUTRITION_GETTER,
        [
            ("def", "Milira_SunLightSoySauce", 0.2, 2),
            ("cat", "Fish", 1.25, 5),
        ],
    ),
    "Milira_MakeSpecialSauceGrilledChickenWings": (
        None,
        [
            ("def", "Milira_SunLightSoySauce", 1.0, 1),
            ("def", "Milira_SunLightFuel", 1.0, 1),
            ("def", "Meat_Chicken", 2.0, 2),
        ],
    ),
    "Milira_MakeMoreSpecialSauceGrilledChickenWings": (
        None,
        [
            ("def", "Milira_SunLightSoySauce", 1.0, 1),
            ("def", "Milira_SunLightFuel", 1.0, 1),
            ("def", "Meat_Chicken", 10.0, 10),
        ],
    ),
    "Milira_MakeMiTiaoQiang": (
        None,
        [
            ("def", "Meat_Chicken", 5.0, 5),
            ("def", "Meat_Pig", 5.0, 5),
            ("def", "Milira_SunLightOyster", 5.0, 5),
            ("def", "Milira_SunLightabalone", 5.0, 5),
            ("def", "Milira_SunLightSoySauce", 1.0, 1),
            ("def", "Milira_SunPlateSteel", 1.0, 1),
            ("cat", "Fish", 5.0, 5),
        ],
    ),
}
NOT_TOUCHED = {
    "Milira_MakeSunLightFuelWithKitchenWaste",
    "Milira_MakeSunLightFuelWithWastePack",
}
# Candy is intentionally a low-nutrition joy item: 1 fruit -> 1 candy.
RATIO_RANGE = {"Milira_MakeMiliraRainbowCandy": (0.15, 0.25)}

failures = []


def fail(msg):
    failures.append(msg)


def load_defs(mod_root):
    root = ET.Element("Defs")
    defs_dir = os.path.join(mod_root, "1.6", "Defs")
    if not os.path.isdir(defs_dir):
        raise SystemExit(f"missing upstream Defs directory: {defs_dir}")
    for dirpath, _, files in os.walk(defs_dir):
        for name in sorted(files):
            if not name.endswith(".xml"):
                continue
            tree = ET.parse(os.path.join(dirpath, name))
            for node in tree.getroot():
                if node.tag in ("ThingDef", "RecipeDef"):
                    root.append(node)
    return root


def resolve(tree, xpath):
    """Resolve the small subset of XPath used by this patch."""
    if not xpath.startswith("/Defs/"):
        raise ValueError(f"unsupported xpath: {xpath}")
    parts = xpath[len("/Defs/") :].split("/")
    head = parts[0]
    tag, pred = head, None
    if "[" in head:
        tag, pred = head[:-1].split("[", 1)
        if not (pred.startswith('defName="') and pred.endswith('"')):
            raise ValueError(f"unsupported predicate: {pred}")
        pred = pred[len('defName="') : -1]
    nodes = [
        n
        for n in tree
        if n.tag == tag and (pred is None or n.findtext("defName") == pred)
    ]
    for part in parts[1:]:
        idx = None
        if "[" in part:
            name, rest = part[:-1].split("[", 1)
            idx = int(rest) - 1
            part = name
        nxt = []
        for node in nodes:
            kids = [c for c in node if c.tag == part]
            nxt.extend(kids if idx is None else kids[idx : idx + 1])
        nodes = nxt
    return nodes


def apply_operation(tree, op):
    cls = op.get("Class")
    if cls == "PatchOperationSequence":
        operations = op.find("operations")
        total = 0
        for child in list(operations) if operations is not None else []:
            if child.tag == "li":
                total += apply_operation(tree, child)
        return total
    if cls == "PatchOperationConditional":
        if resolve(tree, op.findtext("xpath")):
            match = op.find("match")
            return apply_operation(tree, match) if match is not None else 0
        nomatch = op.find("nomatch")
        return apply_operation(tree, nomatch) if nomatch is not None else 0
    xpath = op.findtext("xpath")
    value = op.find("value")
    nodes = resolve(tree, xpath)
    if len(nodes) != 1:
        fail(f"xpath matched {len(nodes)} nodes (expected 1): {xpath}")
        return 0
    target = nodes[0]
    if cls == "PatchOperationAdd":
        for child in list(value):
            target.append(child)
        return 1
    if cls == "PatchOperationReplace":
        parent = None
        for candidate in tree.iter():
            if target in list(candidate):
                parent = candidate
                break
        if parent is None:
            fail(f"cannot find parent for replace: {xpath}")
            return 0
        children = list(value)
        if len(children) != 1:
            fail(f"replace value must contain exactly one element: {xpath}")
            return 0
        idx = list(parent).index(target)
        parent.remove(target)
        parent.insert(idx, children[0])
        return 1
    fail(f"unsupported patch class: {cls}")
    return 0


def apply_patch(tree, patch_path):
    patch = ET.parse(patch_path).getroot()
    applied = 0
    for op in patch:
        if op.tag == "Operation":
            applied += apply_operation(tree, op)
    return applied


def unit_nutrition(kind, key, getter):
    # Volume getter values every non-stuff item at 1, regardless of nutrition.
    if not getter:
        return 1.0
    if kind == "def":
        return NUT_PATCHED.get(key, NUT.get(key))
    return CAT[key.split("|")[0]]


def recipe_snapshot(recipe):
    getter = recipe.findtext("ingredientValueGetterClass")
    ings = []
    for li in recipe.findall("ingredients/li"):
        f = li.find("filter")
        defs = (
            [x.text.strip() for x in f.findall("thingDefs/li")] if f is not None else []
        )
        cats = (
            [x.text.strip() for x in f.findall("categories/li")]
            if f is not None
            else []
        )
        count = float(li.findtext("count"))
        ings.append((defs, cats, count))
    products = [(x.tag, float(x.text)) for x in recipe.find("products")]
    return getter, ings, products


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mod-root", default=DEFAULT_MOD)
    ap.add_argument("--patch", default=DEFAULT_PATCH)
    args = ap.parse_args()

    tree = load_defs(args.mod_root)
    applied = apply_patch(tree, args.patch)
    print(f"patch operations applied: {applied}")

    by_name = {r.findtext("defName"): r for r in tree if r.tag == "RecipeDef"}

    # Patch-level invariants.
    for item, expected in NUT_PATCHED.items():
        node = next(
            (t for t in tree if t.tag == "ThingDef" and t.findtext("defName") == item),
            None,
        )
        if node is None:
            fail(f"missing ThingDef: {item}")
            continue
        actual = float(node.findtext("statBases/Nutrition"))
        if abs(actual - expected) > 1e-9:
            fail(f"{item} Nutrition = {actual}, expected {expected}")
    for name in NOT_TOUCHED:
        if name not in by_name:
            fail(f"missing recipe: {name}")

    print(f"{'recipe':46} {'getter':6} {'input':>6} {'output':>6} {'ratio':>5}  items")
    for name, (getter, expected_ings) in EXPECTED.items():
        recipe = by_name.get(name)
        if recipe is None:
            fail(f"missing recipe: {name}")
            continue
        actual_getter, ings, products = recipe_snapshot(recipe)
        if (actual_getter or None) != (getter or None):
            fail(f"{name}: getter {actual_getter!r}, expected {getter!r}")
        if len(ings) != len(expected_ings):
            fail(f"{name}: {len(ings)} ingredients, expected {len(expected_ings)}")
            continue

        total_input = 0.0
        item_desc = []
        for (defs, cats, count), (kind, key, exp_count, exp_items) in zip(
            ings, expected_ings
        ):
            if abs(count - exp_count) > 1e-9:
                fail(f"{name}: count {count}, expected {exp_count} ({key})")
            actual_key = defs[0] if kind == "def" else "|".join(cats)
            if kind == "def" and defs != [key]:
                fail(f"{name}: ingredient def {defs}, expected {key}")
            if kind == "cat" and set(cats) != set(key.split("|")):
                fail(f"{name}: ingredient categories {cats}, expected {key}")
            unit = unit_nutrition(kind, actual_key, getter)
            items = math.ceil(count / unit - 1e-9)
            if items != exp_items:
                fail(
                    f"{name}: would consume {items} items of {actual_key}, expected {exp_items}"
                )
            total_input += count
            item_desc.append(f"{actual_key.split('|')[0]}:{items}")

        product_def, product_n = products[0]
        product_total = PRODUCT[product_def] * product_n
        ratio = product_total / total_input if total_input else float("inf")
        if getter:
            lo, hi = RATIO_RANGE.get(name, (0.6, 2.2))
            if not (lo <= ratio <= hi):
                fail(f"{name}: nutrition ratio {ratio:.2f} outside {lo:.2f}-{hi:.2f}")
            if total_input <= 0:
                fail(f"{name}: zero nutrition input with nutrition getter")
        print(
            f"{name:46} {'nut' if getter else 'vol':6} {total_input:6.2f} {product_total:6.2f} "
            f"{ratio if getter else float('nan'):5.2f}  {' '.join(item_desc)}"
        )

    # Volume recipes must not keep fractional counts.
    for name, recipe in by_name.items():
        if recipe.findtext("ingredientValueGetterClass") == NUTRITION_GETTER:
            continue
        for li in recipe.findall("ingredients/li"):
            c = float(li.findtext("count"))
            if abs(c - round(c)) > 1e-9:
                fail(f"{name}: volume getter still has fractional count {c}")

    if failures:
        print("\nFAIL")
        for msg in failures:
            print(" -", msg)
        return 1
    print(
        "\nPASS: every xpath matched once; getters, counts, item cost and nutrition ratios verified."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
