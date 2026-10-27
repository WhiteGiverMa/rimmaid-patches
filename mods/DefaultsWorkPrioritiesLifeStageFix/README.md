# Defaults 自定义儿童阶段兼容修复

独立 RimWorld **1.7 / Waylith** 兼容 Mod，依赖 Harmony 与 **1trickPwnyta's Defaults**（工坊 `3285178686`）。

`packageId`: `whitegiver.defaults.workprioritieslifestagefix`，版本 `1.0.0`。

## 修复行为

只修复 Defaults 正式版对原版 `HumanlikeChild` 的两处硬编码假设：

1. `Defaults.WorkPriorities.Patch_Pawn.Postfix(Pawn)` 使用 `lifeStageAges.First(...)`，种族没有 `HumanlikeChild` 时抛出 `Sequence contains no matching element`。
2. `Defaults.Policies.Patch_Pawn_AgeTracker.Postfix(...)` 使用 `LifeStageMinAge(HumanlikeChild)`，缺失时由原版记录红字并返回 `0`，既污染日志，也可能把无儿童阶段的种族误判为 0 岁。

两处都改为按 `developmentalStage.Child()` 查找真正的儿童阶段：自定义儿童阶段使用其真实 `minAge`；没有儿童阶段时，对应年龄比较安全地为 false。原版儿童阶段行为保持不变。

不修改 Defaults 或其他工坊 DLL，不修改原版全局 `LifeStageMinAge`，不跳过整个 Postfix，不吞异常，不修改存档，也不返还修复前已经损失的材料或未成功生成的产物。

## 失败链与实现

工作优先级补丁挂在 `Pawn.Notify_DisabledWorkTypesChanged`。生物生日解锁工作类型时，原版的静态 `tmpEnabledWorkTypes` 会记录新增项；Defaults 抛错后，该列表可能错过末尾清理，后续的阵营变化或死亡通知便可能继续触发同一异常。无人机装配在 `SetFaction` 后才执行地图 `Spawn`，所以该异常会直接阻断成品落地。

本补丁通过软引用定位两个 Defaults Postfix，先完成整组签名与调用数预检，再各替换一个调用点：

- `First<LifeStageAge>` → 按儿童发展阶段返回对应条目；无儿童阶段时返回仅供原 `minAge` 读取的 `-1` 哨兵，在非负生物年龄域内与上游 nullable 缺值比较等价为 false。
- `LifeStageMinAge(LifeStageDef)` → 返回真实儿童阶段年龄；无儿童阶段时返回 `NaN`，与原 nullable 比较一样恒为 false，同时避免缺项红字与 0 岁误判。

其余 IL、标签、异常块、工作优先级写入、Pawn 类型判断和策略分配保持原样。任一目标缺失、签名变化或调用数不为一时整组不应用；应用过程中失败只撤回本补丁拥有的补丁。

作者已在 Experimental／1.7 分支的 [`00e16b1`](https://github.com/1trickPwnyta/DefaultsMod/commit/00e16b11e0c31fc265700303866eaef93bd02258) 同时修复这两处；本 sidecar 将相同语义补到正式版，不引入其他分支差异。

## 构建与安装

```powershell
dotnet build .\Source\DefaultsWorkPrioritiesLifeStageFix.csproj -c Release
```

可通过 `-p:GameRoot=... -p:HarmonyPath=...` 指定本机 1.7 游戏根目录与 Harmony 3 路径；不需要 Defaults 编译期引用。构建不会自动部署。

关闭游戏后，将本目录的 **About/** 与 **Assemblies/** 复制到游戏 `Mods/DefaultsWorkPrioritiesLifeStageFix/`，不要复制 Source/Validation/bin/obj。在模组列表中排在 Harmony、Defaults 之后并启用，正常启动生效。卸载时关闭游戏并禁用此独立补丁即可，无新增存档数据。

成功日志：

```text
[Defaults Life Stage Fix] Patched Defaults work-priority and policy life-stage lookups (two call sites).
```

出现 `patch group not applied` 或 `Could not apply` 视为**未生效**。Defaults 不在进程中时会明确记录 skipped。

## 回归验证

```powershell
pwsh -File .\Validation\validate.ps1
```

可指定 `-GameRoot`、`-HarmonyPath`、`-DefaultsPath` 和 `-PatchPath`；默认使用本目录发布 DLL。2026-10-26 验证结果：

- Release 构建零警告、零错误；生产与验证 C# 文件 LSP 诊断均为空。
- RED：实际 Defaults 工作优先级 Postfix 在无儿童阶段种族上复现 `Sequence contains no matching element`；原版儿童阶段基线正常。
- RED：实际 Defaults 策略 Postfix 在自定义儿童阶段上复现原版缺项红字，并漏掉 3 岁策略分配。
- GREEN：通过真实 Mod 构造函数注册 Harmony 3 双点补丁；每个目标只改一个调用，其余指令、标签与异常块保持，0／2 个调用点的漂移均被拒绝。
- GREEN：无儿童阶段、10 岁自定义儿童种族、3 岁自定义儿童种族、3 岁原版儿童种族均通过；无儿童阶段不会扩展工作类型或误判 0 岁，3 岁时保留全部工作类型与策略分配。
- GREEN：缺少 Defaults 时整组跳过；发布程序集没有 Defaults 或旧 Unity 编译依赖。
- PASS：额外制造第二个目标转译失败，确认第一个已应用补丁会撤回，同时保留其他 Harmony owner 的补丁。

**离线接缝边界**：两个 Defaults Postfix、`First`、`LifeStageMinAge` 与实际 IL 转译均执行真实代码。QA 只替换禁用工作类型集合、工作优先级写入、Pawn 类型判定、策略写入和日志采集，避免依赖完整存档与 Mod 设置。`Validation/bin/.../bound/` 中的临时副本把旧 Unity 引用绑定到 UnityStubs，并禁用游戏副本的 `ModsConfig` 初始化，避免接触用户配置；工坊原件与部署补丁不作这些测试改写。

当前状态为 **game-qa-pending**。游戏内最小验收是读档完成一台米莉拉运载无人机，确认单位真正落地且没有新的同类红字；生日策略可随后在自然生日时观察，不为测试修改年龄或存档。离线验证不替代真实地图生产、完整生日流程或存读档。

## 上游更新与退役

核验基线：2026-10-26，RimWorld `1.7.4985 rev1269`，Defaults 正式版 `1.9.4b`，实际加载 `1.6/Assemblies/Defaults.dll` SHA-256：

```text
3558323585E2DD9972F1EF89E2A736614CE37D3B0BFC0AD5FDC60EC9F6D5D88F
```

上游 DLL 哈希或最后修改时间变化是复核信号，不是自动退役证据。正式版同时并入作者 `00e16b1` 的两处修复后可停用本补丁。若只修其中一处、改变方法签名或改变调用结构，本补丁会整组拒绝应用；此时应复核并更新 sidecar，而不是放宽校验或静默应用一半。
