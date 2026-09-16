# CeleTech 重复装备组件修复

适用：RimWorld 1.6、CeleTech Arsenal MKIII（工坊 `3446237098`）。本目录是独立 Mod；加载顺序保持在 CeleTech 之后，保留原 packageId。

## 1.1.1 修正

旧版 XML 将 `PatchOperationAttributeSet` 的字段写成了 `<name>`，实际游戏字段为 `<attribute>`。因此唐刀与七星剑的 `comps` 未设置 `Inherit="False"`，原版 Equippable 仍被继承，后续 Add 又造成重复 Styleable、Forbiddable 和 Quality。结果是重复 Verb ID，以及同一物品 `sourcePrecept` 路径重复注册。

本版仅修正这一 XML 字段。原有五种枪械的重复 Gizmo 删除、传奇武器卸装回调补丁及美术语法保留不变；**游戏 DLL 未更换**。当前无需先手工修改存档；预期可直接读取原存档，但尚未完成游戏内验证。重启后生效。之前的 PowerShell 模拟器复用了同一错误假设，已经纠正，并增加真实游戏 XML 加载链验证，不能再把模拟器通过当成游戏兼容性证明。

## 验证

在 `Validation/Harness` 下运行：

```powershell
dotnet build -c Release -p:DeployLocal=false
dotnet exec --runtimeconfig XmlRuntime.runtimeconfig.json bin/Release/CeleTechEquippableHarness.exe --xml ../../Patches/FixDuplicateEquippableComps.xml
.\bin\Release\CeleTechEquippableHarness.exe
```

XML 模式需要 .NET 10，调用本机真实 `DirectXmlToObject.ObjectFromXml<PatchOperation>`、`PatchOperation.Apply` 与 `XmlInheritance.Resolve`。旧 XML 可重复得到属性操作异常及两种传奇武器重复组件；修后四个操作成功，七种目标武器各一个 Equippable，传奇武器辅助组件各一个。回调模式仍在 .NET Framework 4.8 运行，使用真实 CeleTech 和补丁 DLL。

旧 XML 在 .NET Framework 4.8 下触发游戏程序集的 `System.HashCode` 运行时差异，因此 XML 模式明确使用 .NET 10；这是离线驱动环境限制，不是游戏故障。未启动 Unity，没有覆盖完整模组组合与真实存档读写。无参数的回调模式按原有路径读取已安装补丁 DLL；本版未改该 DLL。

可选的结构检查：在本 Mod 根目录运行 `pwsh -NoProfile -File Validation/validate_celetech_comps.ps1`。它默认读本目录 XML，也可通过 `-PatchPath` 指定已部署 XML；它只作辅助检查。

## 更新与退役

- 独立本地 Mod 不会被 Steam 覆盖；更新文件后必须重启游戏。
- CeleTech 或游戏更新后重新运行真实 XML 与回调验证。作者若删除重复组件，会使 Remove 匹配失败；若修复传奇武器卸装 base 回调，当前 Harmony prefix 应退役，避免重复回调。
- `Inherit="False"` 会切断未来父定义新增组件；上游 BaseWeapon 链变化时需重新检查补回列表。
- 本次字段拼写失误属于本补丁，不应向 CeleTech 作者报告为其新故障。原始重复组件缺陷可以另行反馈，但完整游戏复现范围必须如实注明。
