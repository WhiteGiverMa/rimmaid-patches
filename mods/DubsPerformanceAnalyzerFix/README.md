# Dubs Performance Analyzer Null-Def Fix

[中文说明](README.zh-CN.md)

Harmony sidecar for Dubs Performance Analyzer on RimWorld 1.6.

`Tick Things` assumes every ticking `Thing` has a non-null `def` and `def.thingClass`. A malformed
ThingDef can provide a valid ticker type yet have no runtime class, causing DPA's default grouping
(`def.thingClass.Name`) to throw even though `Thing.DoTick` itself can run.

The patch tells DPA not to profile such a malformed entry and logs its Def provenance once; it does
not change its actual game tick. A separate guard skips only truly def-less Things, whose original
`Thing.DoTick` would otherwise fail at `def.tickerType`.
