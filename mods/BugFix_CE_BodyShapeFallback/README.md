# BugFix: CE 红字合集

**日期**: 2026-07-03（**2026-09-27 修订 Bug #1 触发范围**）
**补丁模组**: `BugFix_CE_BodyShapeFallback`
**影响模组**: Combat Extended (ceteam.combatextended)

本模组合并了三个 CE 相关的红字修复，均为 CE 自身的误报/边界情况处理不足。

---

## Bug #1: CE returning BodyType Undefined

**触发**: Dead Man's Switch Legion 等缺少 CE bodyShape 的 mod
**场景**: 鼠标悬停显示射击精度提示时

### 堆栈

```
CE returning BodyType Undefined for pawn <illustrative pawn>
  at CombatExtended.CE_Utility:GetCollisionBodyFactors(Pawn)
  ...
  (鼠标悬停提示时触发)
```

### 根因

CE 的 `GetCollisionBodyFactors` 通过 `RacePropertiesExtensionCE.bodyShape` 获取碰撞体型，但这条分支只在 Pawn 非站立或已经倒地时才执行（站立且未倒地的 Pawn 根本不读 bodyShape）。DMS Legion 15 个中型机械体未定义 bodyShape → CE fallback 到 Invalid 并打 ErrorOnce。不影响游戏功能，但刷屏红字。

原 Prefix 只要发现 bodyShape 缺失就无条件返回 `(BodySize*0.4, BodySize*1.25)` 并跳过原方法，因此有两个副作用：

- **车辆回调被绕过**：CE 原方法第一步会询问 `Compatibility.Patches.GetCollisionBodyFactors`（VehiclesCompat 注册的 `VehiclePawn → (1, def.fillPercent)`）。Prefix 抢先返回后，缺 bodyShape（或未定义 `RacePropertiesExtensionCE`）的车辆拿到的是 BodySize 近似值，命中盒/枪口/压制几何随之改变。
- **站立 Pawn 被误伤**：站立未倒地的 Pawn 在 CE 里不会进入 bodyShape 分支，本应保留 `BoundsInjector.ForPawn` 的基线系数，却被替换成 BodySize 近似值。

### 修复

- **Harmony Prefix（收窄后）**：仅当以下三条同时成立时，才返回 BodySize fallback 并跳过原方法——
  1. Pawn 非站立或已倒地（CE 唯一会读取 bodyShape 的情形）；
  2. `def` 上没有已定义的 `bodyShape`；
  3. 没有已注册的兼容回调（如 VehiclesCompat）会接管它的系数。
  站立 Pawn、已定义 bodyShape 的 Pawn、以及回调接管的 Pawn 一律交回 CE 原方法，行为与未装本补丁一致。
- Prefix 通过动态反射调用 `CombatExtended.Compatibility.Patches.GetCollisionBodyFactors` 判定回调归属，不对 Vehicle Framework 程序集产生编译或运行时硬依赖；老 CE 若没有该 API 则退回纯 bodyShape 判定。
- **XML Patch**：为 DMS Legion 15 个机械体补充 `<bodyShape>Humanoid</bodyShape>`（不变）。

---

## Bug #2: penAmount or armorAmount are zero for Blunt

**触发**: CE 近战格挡伤害计算
**场景**: 战斗中用武器格挡时

### 堆栈

```
penAmount or armorAmount are zero for Blunt on <illustrative weapon>
  at CombatExtended.ArmorUtilityCE:TryPenetrateArmor(...)
  at CombatExtended.ArmorUtilityCE:ApplyParryDamage(...)
  at CombatExtended.Verb_MeleeAttackCE:DoParry(...)
```

### 根因

`TryPenetrateArmor` 对武器做格挡伤害计算时，`penAmount`（穿甲值）和 `armorAmount`（ToughnessRating）同时为零。CE 的报错检查只看 `ArmorRating_Sharp/Blunt/Heat` 三维——武器没有这些护甲统计，自然全是零。**行为正确**（零穿甲对零护甲 = 无伤害），红字是误报。

### 修复

- **Harmony Prefix**: 当 `penAmount==0 && armorAmount==0` 时直接返回（不格挡，无伤害），跳过原方法的错误日志

---

## 源码与构建

- `Source/BodyShapeFallbackPatch.cs` - Bug #1
- `Source/ParryDamageFix.cs` - Bug #2
- `Patches/DMSL_Mech_BodyShape.xml` - DMS 特定修复
- `Source/BugFix_CE_BodyShapeFallback.csproj` - net48 构建项目（引用本机 RimWorld 与 Harmony，可用 `-p:GameRoot=` / `-p:HarmonyDll=` 覆盖路径）
- `Validation/validate.ps1` + `Validation/Harness` - net48 回归驱动（不启动游戏）

```bash
dotnet build Source/BugFix_CE_BodyShapeFallback.csproj -c Release   # -> Source/bin/BugFix_CE_BodyShapeFallback.dll
pwsh -File Validation/validate.ps1 -PatchDll Assemblies/BugFix_CE_BodyShapeFallback.dll
```

构建不会自动部署；验证通过后把 DLL 复制到本目录 `Assemblies/`，并在提交时同步根目录 `checksums.sha256`。

## 验证证据（2026-09-27）

回归驱动加载真实 CE 程序集与指定的补丁 DLL，用 CE 自己的 `RegisterCollisionBodyFactorCallback` 注册车辆替身回调，在无游戏进程的情况下对比修复前后行为：

- 修复前 DLL（SHA-256 `005370f0…7eb2`，6144 B）：**2 项失败**——非站立车辆替身的回调系数被换成 `(0.6, 1.875)`；站立 Pawn 被误拦。
- 修复后 DLL（SHA-256 `60c8e1b4bb223dfa751597a7a03e7f459a85745d2574248975d7ca81280d506a`，8704 B）：**全部通过**——车辆保持 `(1, fillPercent)`；站立 Pawn 保持 CE 基线系数；非站立/倒地且缺 bodyShape 的 Pawn 仍走 BodySize fallback 且不再打印 `CE returning BodyType Undefined`；已定义 bodyShape 的倒地数学不变。
- 两个 DLL 的 Release 构建均为 0 warnings / 0 errors。
- 对照 CE：活动本地镜像 `Mods/CombatExtendedLocal/Assemblies/CombatExtended.dll`（SHA-256 `0249e0f0…274d`，AssemblyVersion 15.6.3.1）；工坊 CE 含同一回调 API。
- 该驱动不启动游戏，不能替代游戏内浅测；浅测关注点：DMS 缺 bodyShape 机械体不再刷红字、车辆命中表现恢复原状。

## 依赖与退役信号

- 依赖：Harmony；Combat Extended 的 `CE_Utility.GetCollisionBodyFactors`、`Compatibility.Patches.GetCollisionBodyFactors`、`RacePropertiesExtensionCE.bodyShape`。
- 上游更新后：先跑 `Validation/validate.ps1`；若日志出现回调绑定警告或前缀安装失败，说明 CE 方法签名变了，需要复核。
- 退役条件：CE 自身在 bodyShape 缺失时不再打 `CE returning BodyType Undefined`（或提供等价保护）时，删除 Bug #1 的 Harmony 部分。

## 向上游报告

### CE 侧
- GitHub: https://github.com/CombatExtended-Continued/CombatExtended
- Bug #1: 建议 `GetCollisionBodyFactors` 在 bodyShape 为 null 时跳过 ErrorOnce 并保留基线系数（Invalid 形状的数学本来就是 1:1 恒等）
- Bug #2: 建议 `TryPenetrateArmor` 对非护甲物品（武器）跳过护甲三维检查

### DMS 侧（Bug #1）
- QQ 群: 1040461987（AOBA）
- DMS Legion CE patch 漏了 15 个中型机械体的 `bodyShape`
