# Dubs Performance Analyzer 空引用 Def 修复

[English](README.md) | 简体中文

RimWorld 1.6 上针对 Dubs Performance Analyzer（DPA）的 Harmony sidecar 补丁。

## 问题

DPA 的 `Tick Things` 面板假设每个参与 tick 的 `Thing` 都有非空的 `def` 和 `def.thingClass`。一个格式异常的 ThingDef 可以提供有效的 ticker 类型，却没有运行时类，这会让 DPA 的默认分组逻辑（`def.thingClass.Name`）抛出异常，尽管 `Thing.DoTick` 本身可以正常运行。

## 补丁行为

补丁让 DPA 跳过对这类格式异常条目的性能分析，并只记录一次该 Def 的来源信息；它不会改变游戏实际的 tick 行为。另一个独立的防护只跳过真正没有 `def` 的 Thing，这类 Thing 的原始 `Thing.DoTick` 本来就会在 `def.tickerType` 处失败。

## 依赖与更新检查

- 依赖：Harmony、Dubs Performance Analyzer。具体声明见 `About/About.xml`。
- 更新检查：分别确认 DPA 的分组保护和真正 def-less Thing 的 tick 保护是否仍有必要；上游仅修好其中一项，不代表整个补丁都能退役。
