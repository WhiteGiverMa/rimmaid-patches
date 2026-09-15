# Building the patch monorepo

[中文构建说明](BUILDING.zh-CN.md)

The 17 included `.csproj` files target .NET Framework 4.8 and were build-checked with .NET SDK 10.0.401 on the original Windows environment. Default outputs go to each project's ignored `bin/`, not the retained `Assemblies/` copies. Builds are not gameplay tests.

## Requirements and paths

- Install a compatible .NET SDK and .NET Framework 4.8 reference assemblies.
- Supply your own RimWorld, Harmony and any referenced mod DLLs. Projects using NuGet references also need those packages restored.
- The three CE addons accept `-p:CombatExtendedDll=<external DLL path>`; their former sibling-directory reference was invalid after the move and has been replaced.
- `LocalFix_MiliraFlightNullCheck` accepts `-p:HarmonyDll=<external DLL path>`; its default now uses Harmony's `Current/Assemblies` directory rather than a nonexistent `1.6` directory.
- Some projects expose `GameRoot` / `WorkshopRoot`; many legacy `HintPath` values still use literal paths. **This migration does not make all projects universally portable.** Inspect each project and override the properties it actually consumes, or update its literal references locally.
- `Directory.Build.props.example` illustrates optional local properties. Copying it to ignored `Directory.Build.props` affects only projects consuming those properties; unconditional values in a project can override the imported defaults. Command-line properties override project assignments.

```text
dotnet build <path-to-csproj> -c Release
dotnet build mods/CEBreachingFix/Source/CEBreachingFix/CEBreachingFix.csproj -c Release -p:CombatExtendedDll=D:/ReferenceMods/CombatExtended.dll
```

## Deployment is explicit

Only BossgroupConcurrentSummons, DMSWorkTypeCacheFix and KeyzAllowUtilitiesFinishOffFix retain a deployment target. It requires `-p:DeployLocal=true`; it does not run by default. Set `DeployRoot` deliberately before opting in. All other projects require manual packaging of build outputs.

Safe sandbox example (writes only to the explicitly selected sandbox):

```text
dotnet build mods/BossgroupConcurrentSummons/1.6/Source/BossgroupConcurrentSummons.csproj -c Release -p:DeployLocal=true -p:DeployRoot=D:/PatchSandbox/BossgroupConcurrentSummons
```

Never copy the repository root into RimWorld's Mods directory. For an actual release, update the intended mod DLL, its source and `checksums.sha256` together after validation. Do not replace the archived DLL merely because the recovered source compiles.

## Coverage and limits

All 17 projects build: 14 mod/recovery projects, the CeleTech harness, ConduitRoomStatsFix recovery and GizmoDiag recovery. Two older mods have loose `.cs` files but no project; the XML-only mods require no C# build.

WVC, ConduitRoomStatsFix and GizmoDiag source was reconstructed from existing binaries. Their successful compilation does not establish binary equivalence or game correctness. The CeleTech harness retains original machine-specific runtime lookup paths; building it alone is not an end-to-end check of a clone's DLL.

Run `pwsh -File tools/verify-repository.ps1` on a staged tree or fresh Git clone to check package IDs, XML, excluded content and retained DLL hashes. LSP diagnostics were unavailable for the external workspace; actual compiler and archive checks were used instead.
