# FortifiedCE Minify NullGuard: historical backup, not recommended

[中文说明](README.zh-CN.md)

This directory preserves the original local implementation and DLL. It was enabled in the local mod list at import time; that is an installation fact, **not evidence that it fixes the reported exception**. The migration does not enable, disable, rebuild, or repair the installed mod.

## Known incorrect mechanism

The original description said returning `false` from a `MinifyUtility.MakeMinified` Harmony prefix would skip the original and all postfixes. That claim is wrong: **Harmony postfixes still run**. The preserved prefix sets a null result for non-minifiable things and cannot protect `FortifiedCE.Harmony_MinifyUtility.PostFix` from dereferencing that null.

The reported failure involved a null `MinifiedThing` result reaching the third-party postfix. A future repair would need to guard that actual postfix or its null-consuming logic, then verify the current dependency and the in-game path. No such repair was performed during this backup migration.

## Contents and dependency

- Package ID is unchanged: `local.meidocho.fortifiedce_minifynullguard`.
- `Source/MinifyNullGuard.cs` is the original runtime logic with misleading comments corrected.
- `Assemblies/BugFix_FortifiedCE_MinifyNullGuard.dll` is the retained original binary.
- Dependency: Harmony and Fortified Features Framework (`AOBA.Framework`, Workshop `3498575851`).

Do not install this directory as a recommended fix. Keep it only for tracing the previous local experiment.
