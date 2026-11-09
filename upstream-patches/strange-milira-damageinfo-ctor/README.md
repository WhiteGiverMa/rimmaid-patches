# strangeMiliraWeapon 1.7 DamageInfo 构造函数兼容补丁

## 对象

- 上游：**瓦莱丽的米莉拉扩展包**（`Valeries.miliraexpansion`，Steam 工坊 `3422615030`，作者「米莉拉瓦莱丽」）
- 被修改文件：`1.6/Assemblies/strangeMiliraWeapon.dll`（1.7 游戏因 `LoadFolders.xml` 无 `v1.7` 条目而回退加载 1.6 目录）
- 未修改：`1.5/Assemblies/strangeMiliraWeapon.dll`、任何 XML、任何其他工坊文件

## 症状

启动时红字：

```
Error in static constructor of GAU8TailSound.HarmonyInit: System.TypeInitializationException
 ---> HarmonyLib.HarmonyException: Patching exception in method virtual System.Boolean strangeMiliraWeapon.Verb_Shoot_BurnSelf::TryCastShot()
 ---> System.MissingMethodException: Method not found: 'Void Verse.DamageInfo..ctor(Verse.DamageDef, Single, Single, Single, Verse.Thing, Verse.BodyPartRecord, Verse.ThingDef, SourceCategory, Verse.Thing, Boolean, Boolean, RimWorld.QualityCategory, Boolean)'.
```

触发方：**The Dead Man's Switch - More weapon sounds**（`senilia.deadmanswitch.moreweaponsounds`，工坊 `3600522698`）的 `GAU8TailSound.HarmonyInit` 用 `Patch_AllVerb_TryCastShot.TargetMethods()` 扫描全游戏所有 `Verb` 子类的 `TryCastShot`，为其中 184 个打音效补丁；扫到 `strangeMiliraWeapon.Verb_Shoot_BurnSelf.TryCastShot()` 时，Harmony 读取 IL 解析方法引用失败，整个 `PatchAll()` 中断。

## 根因

`strangeMiliraWeapon.dll` 是 1.6 编译的，`Verb_Shoot_BurnSelf.TryCastShot()` 的 IL 硬编码调用 **13 参数**的 `Verse.DamageInfo..ctor`：

```csharp
new DamageInfo(DamageDefOf.Burn, Rand.Range(1f, 2f), 20f, -1f, caster, part,
    EquipmentSource.def, SourceCategory.ThingOrUnknown, null, true, true,
    QualityCategory.Normal, true)
```

1.7 的 `DamageInfo` 构造函数改为 **14 参数**，末尾新增 `bool preventCascade = false`，13 参数重载已删除。可选参数只是 C# 编译期语法糖，IL 方法签名仍然是 14 参数，因此 1.6 DLL 的方法引用在 1.7 运行时解析不到。

影响：
- 音效 mod 的两个 Harmony 补丁全部未打上（`[GAU8TailSound] Harmony patches applied.` 不出现）。
- 「能量增强型轻轨枪」（`Milira_HomemadeHandRailGun`）与「能量增强型磁轨步枪」（`Milira_HomemadeMagneticRailRifle`）在 1.7 下开火时 JIT `TryCastShot` 会抛 `MissingMethodException`，武器实际不可用。

## 修复

只改一条指令，不碰任何逻辑：

1. 在 `newobj DamageInfo::.ctor(13 params)` 之前插入 `ldc.i4.0`（即 `preventCascade = false`，与 1.7 默认值一致）。
2. 把该 `newobj` 的方法引用替换为 14 参数版本（参数类型从原引用逐一复制，末尾追加 `System.Boolean`）。

其余所有方法、字段、元数据保持逐条不变（工具会对比全部方法 IL 并断言只有这一处变化）。

## 验证

- **PATCH PASS**：目标方法仅新增 1 条 `ldc.i4.0`，`DamageInfo..ctor` 由 13 参数变为 14 参数，其他方法 IL 逐条一致。
- **RED**：原 DLL 在真实 1.7 `Assembly-CSharp.dll` 下 `RuntimeHelpers.PrepareMethod(TryCastShot)` → `MissingMethodException`，与游戏日志完全一致。
- **GREEN**：补丁 DLL 同一验证 → JIT PASS。
- 反编译确认：`new DamageInfo(..., true, false)`，末尾为 `preventCascade = false`。

## 部署记录

- 首次：2026-11-09 19:08 直接改工坊 DLL；19:14 按用户要求改为**本地副本方案**，工坊文件已还原。
- 工坊文件：**保持原版**，`3422615030/1.6/Assemblies/strangeMiliraWeapon.dll` SHA256 `9c4e39101d216d91253ed9e3f6d79156035ef1664fdcda2a55670cd8a3400c9b`（未修改）。
- 本地副本：`A:/SteamLibrary/steamapps/common/RimWorld/Mods/ValeriesMiliraExpansion`，完整复制工坊 `3422615030`，保留 packageId `Valeries.miliraexpansion`。
- 本地副本 DLL：`1.6/Assemblies/strangeMiliraWeapon.dll`，SHA256 `48dca1a0001c3b5089038348f8880f7597cdcce95a29fae0dea0adbc8b0ab412`（补丁版）。
- 原版备份：`A:/RimWorldBackups/strange-milira-damageinfo-20261109-190803/strangeMiliraWeapon.dll.orig`。
- 配置：`ModsConfig.xml` 条目保持 `valeries.miliraexpansion`，无需修改。

## 加载与切换

`ModLister.RebuildModList` 先扫本地 `Mods/`、后扫工坊；同 packageId 时本地版占用原 id，工坊版自动加 `_steam` 后缀。因此：

- 加载本地补丁版：配置写 `valeries.miliraexpansion`（当前状态）。
- 切回工坊原版：配置写 `valeries.miliraexpansion_steam`。**注意**：工坊版是未打补丁的 1.6 DLL，1.7 下会重现 GAU8TailSound 红字与两把武器开火失败。

## 恢复

删除本地 `Mods/ValeriesMiliraExpansion` 后，工坊版自动回到原 id。回退到 1.6 游戏前，应停用本地副本或还原其 DLL，因为 14 参数构造函数在 1.6 不存在。

## 时效性

- 本补丁只对 **1.7 游戏 + 1.6 目录 DLL** 的组合成立。
- 本地副本是静态复制，Steam 更新工坊 mod **不会**覆盖本地副本，但也不会自动获得上游修复。
- 上游若发布 1.7 目录/DLL：把配置切回 `valeries.miliraexpansion_steam` 验证上游版，确认后删除本地副本。
- 判断本地补丁是否还在：`sha256sum Mods/ValeriesMiliraExpansion/1.6/Assemblies/strangeMiliraWeapon.dll` 应为 `48dca1a0…`。

## 工具

本目录 `Program.cs` + `strange-milira-damageinfo-ctor.csproj`（net10.0 + Mono.Cecil 0.11.6）：

```powershell
dotnet build -c Release
dotnet bin/Release/net10.0/strange-milira-damageinfo-ctor.dll inspect <dll>          # 打印目标方法 IL
dotnet bin/Release/net10.0/strange-milira-damageinfo-ctor.dll patch <in> <out>      # 打补丁并断言只有一处变化
dotnet bin/Release/net10.0/strange-milira-damageinfo-ctor.dll verify <dll>          # 用真实 Assembly-CSharp JIT 验证
```

## 上游反馈建议

症状与修复链路可复现，且 `RED` 已用真实 1.7 `Assembly-CSharp.dll` 证实。可以给作者留言：`Verb_Shoot_BurnSelf.TryCastShot` 的 `DamageInfo` 构造调用需补第 14 个参数 `preventCascade`（1.7 默认 `false`），或直接提供 1.7 版 DLL。无法保证作者会更新。
