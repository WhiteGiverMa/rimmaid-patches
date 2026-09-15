# FortifiedCE Minify 空引用防护：历史备份，不推荐使用

[English](README.md) | 简体中文

本目录保留原始的本地实现和 DLL。导入时它在本地 mod 列表中处于启用状态，这只是安装事实，**不能证明它能修复所报告的异常**。本次迁移没有启用、禁用、重建或修复这个已安装的 mod。

## 已知错误的机制

原始描述声称：从 `MinifyUtility.MakeMinified` 的 Harmony prefix 返回 `false` 会跳过原方法和所有 postfix。这个说法是错的：**Harmony 的 postfix 仍然会运行**。保留下来的 prefix 只是把不可缩小物品的结果设为 null，无法保护 `FortifiedCE.Harmony_MinifyUtility.PostFix` 不去解引用这个 null。

所报告的失败链路是：一个 null 的 `MinifiedThing` 结果到达了第三方的 postfix。未来的修复需要防护那个真正的 postfix 或其消费 null 的逻辑，然后验证当前依赖和游戏内路径。本次备份迁移没有做这样的修复。

## 内容与依赖

- Package ID 未变：`local.meidocho.fortifiedce_minifynullguard`。
- `Source/MinifyNullGuard.cs` 是原始运行时逻辑，其中误导性的注释已修正。
- `Assemblies/BugFix_FortifiedCE_MinifyNullGuard.dll` 是保留的原始二进制。
- 依赖：Harmony 和 Fortified Features Framework（`AOBA.Framework`，创意工坊 `3498575851`）。

不要把这个目录当作推荐的修复来安装。保留它只是为了追溯之前的本地实验。
