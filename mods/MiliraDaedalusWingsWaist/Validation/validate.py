#!/usr/bin/env python3
"""Offline validator for the Milira Daedalus Wings rear-mount patch.

It loads the real Milira 1.6 Defs plus the relevant RimWorld Core body and
apparel Defs, replays Patches/DaedalusWingsWaist.xml against a merged tree,
and checks the gameplay-relevant consequences:

  - the wings moved from Torso to a new conceptual coverage=0 part;
  - that group is only present on the new part, so no normally hit part can
    make ArmorUtility.ApplyArmor call TakeDamage on the wings;
  - HasPartsToWear still sees the part;
  - the wings still conflict with a second pair of wings, but stack with
    Waist and Torso Belt utility apparel (the old Torso pair used to conflict);
  - only Milira_Body was changed; Human and Milian bodies have no new part.

It never starts the game and never writes to the upstream mod.

Usage:
    python Validation/validate.py [--mod-root PATH] [--core-root PATH] [--patch PATH]
"""

import argparse
import os
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PATCH = os.path.join(HERE, "..", "Patches", "DaedalusWingsWaist.xml")
DEFAULT_MOD = r"A:/SteamLibrary/steamapps/workshop/content/294100/3256974620"
DEFAULT_CORE = r"A:/SteamLibrary/steamapps/common/RimWorld/Data/Core"

NEW_GROUP = "Milira_DaedalusMount"
NEW_PART = "Milira_DaedalusMount"
WINGS = "Milira_DaedalusWings"
MILIRA_BODY = "Milira_Body"

DEF_TAGS = ("ThingDef", "BodyDef", "BodyPartDef", "BodyPartGroupDef")

failures = []


def fail(message):
    failures.append(message)


# ---------------------------------------------------------------- XML loading


def load_defs(paths):
    root = ET.Element("Defs")
    seen = set()
    for path in paths:
        if os.path.isfile(path):
            files = [path]
        else:
            files = []
            for dirpath, _, names in os.walk(path):
                for name in sorted(names):
                    if name.endswith(".xml"):
                        files.append(os.path.join(dirpath, name))
        for file_path in files:
            tree = ET.parse(file_path)
            for node in tree.getroot():
                if node.tag not in DEF_TAGS:
                    continue
                key = (node.tag, node.findtext("defName") or node.get("Name"))
                if key in seen:
                    continue
                seen.add(key)
                root.append(node)
    return root


def index_defs(root):
    index = {}
    for node in root:
        key = node.findtext("defName") or node.get("Name")
        if key:
            index[(node.tag, key)] = node
    return index


def find_def(index, tag, name):
    node = index.get((tag, name))
    if node is None:
        fail(f"missing {tag}: {name}")
    return node


# ---------------------------------------------------------------- patch replay


def parse_step(step):
    tag = step
    predicate = None
    if "[" in step and step.endswith("]"):
        tag, predicate = step[:-1].split("[", 1)
    return tag, predicate


def predicate_ok(node, predicate):
    if predicate.isdigit():
        return True
    if '="' not in predicate:
        raise ValueError(f"unsupported predicate: {predicate}")
    child, value = predicate[:-1].split('="', 1)
    return node.findtext(child) == value


def resolve(tree, xpath):
    if not xpath.startswith("/Defs/"):
        raise ValueError(f"unsupported xpath: {xpath}")
    nodes = [tree]
    for step in xpath[len("/Defs/") :].split("/"):
        tag, predicate = parse_step(step)
        next_nodes = []
        for node in nodes:
            for child in node:
                if child.tag != tag:
                    continue
                if predicate and not predicate_ok(child, predicate):
                    continue
                next_nodes.append(child)
        nodes = next_nodes
    return nodes


def find_parent(tree, target):
    for node in tree.iter():
        if target in list(node):
            return node
    return None


def apply_operation(tree, op, applied):
    cls = op.get("Class")
    if cls == "PatchOperationConditional":
        matched = bool(resolve(tree, op.findtext("xpath")))
        branch = op.find("match") if matched else op.find("nomatch")
        if branch is not None:
            apply_operation(tree, branch, applied)
        return
    xpath = op.findtext("xpath")
    value = op.find("value")
    nodes = resolve(tree, xpath)
    if len(nodes) != 1:
        fail(f"xpath matched {len(nodes)} nodes (expected 1): {xpath}")
        return
    target = nodes[0]
    if cls == "PatchOperationAdd":
        for child in list(value):
            target.append(child)
        applied.append(cls)
        return
    if cls == "PatchOperationReplace":
        parent = find_parent(tree, target)
        if parent is None:
            fail(f"cannot find parent for replace: {xpath}")
            return
        children = list(value)
        if len(children) != 1:
            fail(f"replace value must contain exactly one element: {xpath}")
            return
        position = list(parent).index(target)
        parent.remove(target)
        parent.insert(position, children[0])
        applied.append(cls)
        return
    fail(f"unsupported patch class: {cls}")


def apply_patch(tree, patch_path):
    applied = []
    for op in ET.parse(patch_path).getroot():
        if op.tag == "Operation":
            apply_operation(tree, op, applied)
    return applied


# ---------------------------------------------------------------- game model


def apparel_props(node, index, seen=None):
    """Resolve layers/bodyPartGroups through ParentName inheritance."""
    seen = seen or set()
    layers, groups = [], []
    parent_name = node.get("ParentName")
    if parent_name and parent_name not in seen:
        parent = index.get(("ThingDef", parent_name))
        if parent is not None:
            seen.add(parent_name)
            layers, groups = apparel_props(parent, index, seen)
    apparel = node.find("apparel")
    if apparel is not None:
        if apparel.find("layers") is not None:
            layers = [x.text.strip() for x in apparel.findall("layers/li")]
        if apparel.find("bodyPartGroups") is not None:
            groups = [x.text.strip() for x in apparel.findall("bodyPartGroups/li")]
    return layers, groups


def part_record(node):
    return {
        "def": node.findtext("def"),
        "coverage": float(node.findtext("coverage", "1")),
        "depth": node.findtext("depth", "Outside"),
        "groups": [x.text.strip() for x in node.findall("groups/li")],
    }


def body_parts(body):
    parts = []
    core = body.find("corePart")
    if core is None:
        return parts

    def walk(node):
        parts.append(part_record(node))
        for child in node.findall("parts/li"):
            walk(child)

    walk(core)
    return parts


def interfering(groups, parts, relevant):
    matched = [part for part in parts if set(part["groups"]) & set(groups)]
    return {group for part in matched for group in part["groups"]} & relevant


def can_wear_together(a, b, parts, relevant):
    if not (set(a["layers"]) & set(b["layers"])):
        return True
    if set(a["groups"]) & interfering(b["groups"], parts, relevant):
        return False
    if set(b["groups"]) & interfering(a["groups"], parts, relevant):
        return False
    return True


# ---------------------------------------------------------------- checks


def check(condition, message):
    if not condition:
        fail(message)
    return condition


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mod-root", default=DEFAULT_MOD)
    parser.add_argument("--core-root", default=DEFAULT_CORE)
    parser.add_argument("--patch", default=DEFAULT_PATCH)
    args = parser.parse_args()

    milira_defs = os.path.join(args.mod_root, "1.6", "Defs")
    core_paths = [
        os.path.join(args.core_root, "Defs", "Bodies"),
        os.path.join(args.core_root, "Defs", "ThingDefs_Misc", "Apparel_Belts.xml"),
    ]
    for path in [milira_defs, args.patch] + core_paths:
        if not os.path.exists(path):
            raise SystemExit(f"missing input: {path}")

    local_defs = os.path.join(HERE, "..", "Defs")
    original = load_defs([milira_defs, local_defs] + core_paths)
    original_index = index_defs(original)

    wings_original = find_def(original_index, "ThingDef", WINGS)
    if wings_original is not None:
        layers, groups = apparel_props(wings_original, original_index)
        print(f"upstream {WINGS}: layers={layers} groups={groups}")
        check(
            groups == ["Torso"],
            f"{WINGS} upstream groups changed from [Torso] to {groups}",
        )

    milira_body = find_def(original_index, "BodyDef", MILIRA_BODY)
    if milira_body is not None:
        before_parts = body_parts(milira_body)
        check(
            not any(part["def"] == NEW_PART for part in before_parts),
            f"{MILIRA_BODY} already contains {NEW_PART} before the patch",
        )

    patched = load_defs([milira_defs, local_defs] + core_paths)
    applied = apply_patch(patched, args.patch)
    print(f"patch operations applied: {len(applied)} ({', '.join(applied)})")
    check(len(applied) == 3, f"expected 3 applied operations, got {len(applied)}")
    index = index_defs(patched)

    # Def declarations.
    group_def = find_def(index, "BodyPartGroupDef", NEW_GROUP)
    part_def = find_def(index, "BodyPartDef", NEW_PART)
    if group_def is not None:
        check(
            group_def.findtext("label") == "rear hover mount",
            "new group label is not 'rear hover mount'",
        )
    if part_def is not None:
        check(
            part_def.findtext("conceptual") == "true",
            "new BodyPartDef is not conceptual",
        )

    # Both Milira and Human gain exactly one conceptual part; Milian does not.
    def check_mount_body(body_name, expected_mount):
        body = find_def(index, "BodyDef", body_name)
        if body is None:
            return []
        body_part_list = body_parts(body)
        mounts = [part for part in body_part_list if part["def"] == NEW_PART]
        if not expected_mount:
            check(not mounts, f"{body_name} unexpectedly contains {NEW_PART}")
            return body_part_list
        check(
            len(mounts) == 1,
            f"{body_name}: expected exactly one {NEW_PART} part, got {len(mounts)}",
        )
        if len(mounts) == 1:
            part = mounts[0]
            check(
                part["coverage"] == 0,
                f"{body_name}: {NEW_PART} coverage is {part['coverage']}, expected 0",
            )
            check(
                part["depth"] == "Outside",
                f"{body_name}: {NEW_PART} depth is {part['depth']}, expected Outside",
            )
            check(
                part["groups"] == [NEW_GROUP],
                f"{body_name}: {NEW_PART} groups are {part['groups']}",
            )

        # The new group must not appear on any other part: CoversBodyPart would
        # otherwise return true for a real hit location and ApplyArmor would
        # damage the wings again.
        leaks = [
            part
            for part in body_part_list
            if part["def"] != NEW_PART and NEW_GROUP in part["groups"]
        ]
        check(
            not leaks,
            f"{body_name}: {NEW_GROUP} leaks onto other body parts: "
            f"{[x['def'] for x in leaks]}",
        )
        hit_parts = [
            part
            for part in body_part_list
            if part["coverage"] > 0 and NEW_GROUP in part["groups"]
        ]
        check(
            not hit_parts,
            f"{body_name}: {NEW_GROUP} is on hittable parts: "
            f"{[x['def'] for x in hit_parts]}",
        )

        # HasPartsToWear: the part exists in GetNotMissingParts.
        check(
            any(part["def"] == NEW_PART for part in body_part_list),
            f"{body_name}: HasPartsToWear would fail (no part for group)",
        )

        # ApparelProperties.GetCoveredOuterPartsString only lists Outside parts.
        covered = [
            part
            for part in body_part_list
            if part["depth"] == "Outside" and NEW_GROUP in part["groups"]
        ]
        check(
            len(covered) == 1 and covered[0]["def"] == NEW_PART,
            f"{body_name}: the apparel info card would show "
            f"{[x['def'] for x in covered]} instead of {NEW_PART}",
        )
        return body_part_list

    milira_body = find_def(index, "BodyDef", MILIRA_BODY)
    parts = check_mount_body(MILIRA_BODY, expected_mount=True)
    human_parts = check_mount_body("Human", expected_mount=True)
    check_mount_body("Milian_Body", expected_mount=False)

    # The wings now point at the new group.
    wings = find_def(index, "ThingDef", WINGS)
    wings_layers, wings_groups = (
        apparel_props(wings, index) if wings is not None else ([], [])
    )
    print(f"patched {WINGS}: layers={wings_layers} groups={wings_groups}")
    check(
        wings_groups == [NEW_GROUP],
        f"{WINGS} groups are {wings_groups}, expected [{NEW_GROUP}]",
    )
    check(
        wings_layers == ["Belt"], f"{WINGS} layers are {wings_layers}, expected [Belt]"
    )

    # Conflict model on the patched body.
    def props(name):
        node = index.get(("ThingDef", name))
        if node is None:
            return None
        layers, groups = apparel_props(node, index)
        return {"name": name, "layers": layers, "groups": groups}

    tested = [
        WINGS,
        "Milira_SupercomputingPack",
        "Milira_DropBeaconPack",
        "Milira_CapacitorI",
        "Milira_ConceptWeaponry_Cyclops",
        "Apparel_ShieldBelt",
    ]
    items = {name: props(name) for name in tested}
    for name, item in items.items():
        check(item is not None, f"missing apparel for conflict test: {name}")
        if item is not None:
            print(f"  {name}: layers={item['layers']} groups={item['groups']}")

    relevant = {group for item in items.values() if item for group in item["groups"]}

    def conflict(a, b):
        return not can_wear_together(items[a], items[b], parts, relevant)

    checks = [
        (WINGS, WINGS, True, "two pairs of wings must conflict"),
        (
            WINGS,
            "Milira_SupercomputingPack",
            False,
            "wings must stack with the Waist supercomputing pack",
        ),
        (
            WINGS,
            "Milira_DropBeaconPack",
            False,
            "wings must stack with the Torso drop beacon",
        ),
        (
            WINGS,
            "Milira_ConceptWeaponry_Cyclops",
            False,
            "wings must stack with the ConceptWeaponry belt",
        ),
        (WINGS, "Apparel_ShieldBelt", False, "wings must stack with the shield belt"),
        (
            "Milira_SupercomputingPack",
            "Apparel_ShieldBelt",
            True,
            "two Waist belts must still conflict",
        ),
        (
            "Milira_DropBeaconPack",
            "Milira_ConceptWeaponry_Cyclops",
            True,
            "two Torso belts must still conflict",
        ),
    ]
    for a, b, expected, message in checks:
        actual = conflict(a, b)
        check(
            actual == expected, f"{message} (got {'conflict' if actual else 'stack'})"
        )
        print(f"  {'CONFLICT' if actual else 'stack   '}  {a} + {b}")

    # Before/after: the wings used to conflict with Torso utility belts.
    original_wings = props_original(original_index, WINGS)
    original_pendant = props_original(original_index, "Milira_DropBeaconPack")
    if original_wings and original_pendant:
        old_parts = body_parts(
            milira_body
        )  # patched body is a superset; old groups unaffected
        old_relevant = (
            set(original_wings["groups"])
            | set(original_pendant["groups"])
            | {group for part in old_parts for group in part["groups"]}
        )
        old_conflict = not can_wear_together(
            original_wings, original_pendant, old_parts, old_relevant
        )
        print(
            f"  upstream Torso wings + Torso pendant: {'CONFLICT' if old_conflict else 'stack'}"
        )
        check(
            old_conflict,
            "upstream wings no longer conflicted with the Torso pendant; recheck the baseline",
        )

    if failures:
        print("\nFAIL")
        for message in failures:
            print(" -", message)
        return 1
    print(
        "\nPASS: wings moved to a conceptual coverage=0 rear mount on Milira and Human; "
        "no hittable part shares the group; the apparel card lists the mount; "
        "same wings conflict; Waist/Torso belts stack."
    )
    return 0


def props_original(index, name):
    node = index.get(("ThingDef", name))
    if node is None:
        return None
    layers, groups = apparel_props(node, index)
    return {"name": name, "layers": layers, "groups": groups}


if __name__ == "__main__":
    sys.exit(main())
