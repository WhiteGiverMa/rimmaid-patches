# Rimmaid patch repository conventions

[中文约定](AGENTS.zh-CN.md)

## Scope

- Each directory under `mods/` is an independently loadable RimWorld mod. Never merge package IDs or load-order metadata across directories.
- `upstream-patches/` contains diffs and reconstruction notes only; do not vendor complete third-party repositories.
- `archive/` is not recommended for installation. `tools/` may be noisy diagnostics rather than gameplay fixes.

## Privacy

- Never commit saves, game logs, `ModsConfig.xml`, `Prefs.xml`, `.agents/`, `.omo/`, or local STATUS/handoff files.
- Public documentation must use generic pawn/item examples rather than names or IDs from private saves.

## Build and deployment

- Keep deployable release DLLs when the mod has one; ignore PDB, `bin/`, and `obj/` output.
- Existing package IDs are persistent compatibility identifiers and must not be renamed casually.
- Many legacy csproj files contain the original maintainer's absolute RimWorld paths. Preserve behavior unless deliberately making a project portable; never assume those paths exist on another machine.

## Patch maintenance

- Document the triggering dependency and upstream-retirement signal for every patch.
- After a dependency update, revalidate XML patch targets and Harmony method signatures before rebuilding.
- Prefer a dedicated sidecar mod. Direct Workshop edits belong only in `upstream-patches/workshop-xml-edits/` as recovery instructions.
