# DMS Arc Spray Fire Mode / DMS 扫射模式切换

**1.0.0 · RimWorld 1.7 · 独立 sidecar · 游戏内浅测待确认**

- packageId: `whitegiver.dms.arcsprayfiremode`
- author: `WhiteGiverMa`
- 依赖：Harmony、Fortified Features Framework（FFF）。无需 CE、VFM 或 Weapon Station；存在它们时按下述规则工作。

## 使用 / Behavior

选中玩家阵营持有 FFF 扫射武器的 Pawn/机兵，或搭载该枪的炮塔，点击 **「环世界原版直射 / DMS 原版扫射」** 按钮即可立即切换，包括连发中途。简中、繁中、英文标签与浮窗齐全；直射复用原版攻击图标，扫射复用自由开火图标，不引入贴图依赖。

- **己方默认直射**：没有选择记录的新枪或旧存档武器，在玩家手中默认走原版命中管线，武器与炮塔面板精度属性正常生效。
- **敌人永远扫射**：按钮只对 `Faction == Faction.OfPlayer` 显示。发射时再次检查 `caster.Faction`；非玩家阵营（敌对/中立/无阵营）一律保留原 DMS 扫射，即使拿到曾被玩家设为直射的枪。有人支架还检查当前 `CompMannable.ManningPawn` 的阵营；非玩家操作员不能直射。
- **选择随枪**：按武器实例存档，而不是按武器类型或当前使用者。掉落、换人、换基站后仍保留；敌人拾取只暂停直射，不清除玩家原选择。重新回到玩家手中恢复原选择。普通武器不受影响。
- **多选**：相同当前模式的原生 `Command_Action` 合并为一组；点击使组内所有枪统一切到另一模式。混合模式显示两个有明确标签的组，各组均可批量切换。过时按钮会复核阵营与当前安装枪，不能修改已被敌人接管或换掉的枪。
- **CE**：检测到 `ceteam.combatextended` 时不注册任何补丁、不提供按钮、不写额外状态。CE 弹道与上游 CE 补丁不受干预。

Player-owned equipped weapons and turrets receive an immediate toggle between **vanilla direct fire** and **original DMS zigzag spray**. Player weapons default to direct fire; enemies, neutral and factionless shooters always spray, including captured guns with a saved Direct choice. Choices follow each gun and survive save/load. Multi-selection groups matching modes and switches the whole group. Combat Extended disables this sidecar completely.

## 范围 / Scope

按实际 `Fortified.Verb_ArcSprayProjectile` 类型（含子类）识别，不硬编码武器 defName。涵盖原版 Pawn/机兵主武器、`Building_TurretGun` 及保留原版调用链的 FFF 炮塔子类、无人武器基站和有人武器支架（通过标准 `gun` 字段）。不替换或改变任何 ThingDef 的 verbClass。

已用本机实际 XML 的七个 verb 配置做离线实例测试：

| 来源 | 枪械 / 炮塔枪 |
|---|---|
| DMS II `3819799478` | `DMS_M214Gatlinggun`, `DMS_GAU19Gatlinggun`, `DMS_GAU4Gatlinggun_Hold`, `DMS_GAU4Gatlinggun`, `DMS_GAU8Gatlinggun` |
| DMS II 守门人 | `DMS_Turret_GateKeeper` 的 `DMS_Gun_GateKeeper` |
| AncientCorps `3469398006` | `DMS_Gsh30Gatlinggun`（鞭笞者） |

还驱动了真实 Weapon Station DLL 的无人/有人实例、FFF `Building_TurretCapacity` 子类。

## 实现与接缝实证 / Design

以本机 **1.7.5000 rev1275** 实际 DLL 反编译 C#、Cecil IL 和真实方法执行为准：

```text
Fortified.Verb_ArcSprayProjectile
  -> Verse.Verb_ArcSpray -> Verse.Verb_Spray -> Verse.Verb
Verse.Verb_Shoot -> Verse.Verb_LaunchProjectile -> Verse.Verb
```

两条分支继承自共同基类 `Verse.Verb`：

1. `Verb_Spray.TryCastShot` prefix 仅对玩家 FFF Verb 的直射选择生效。`AimAngleOverride` 同样门控，直射返回 `null`，枪口回到正常目标朝向。
2. Harmony **Original reverse patch** 读取原版 `Verb_LaunchProjectile.TryCastShot()` IL，以共同的 `Verb` 接收者执行。原版只使用 `Verb` 公共/受保护状态；仅将 launcher 专有的 `Projectile`、`GetForcedMissTarget`、两个调试文字方法接到类型安全静态 helper。`ShotReport.HitReportFor(caster, this, currentTarget)` 中的 `this` 仍是原 FFF Verb，装备、目标、派系与其他针对 `ShotReport` 的补丁可见对象不变。
3. 原版 `ForcedMissRadius > 0.5f`、`canGoWild`、`ChangeDestToMissWild`、`PassCoverChance`、`GetRandomCoverToMissInto`、命中标志、友伤参数、掩体 Def、独特武器伤害属性与发射归属保持原 IL。强制偏移 helper 使用独立的合法 launcher **只取样落点**，调用其真实 `GetForcedMissTarget`，不执行发射、不进存档；缓存每轮预热/Reset 清空。
4. 原版 launcher 的逐发弹药通知改为空栈弹出；发射成功后沿 `Verb_Spray` 原语义仅末发调用 `CompChangeableProjectile.Notify_ProjectileLaunched` 和 `CompApparelReloadable.UsedOnce`，防止把 18 发连发变成扣 18 份弹药。原版真实声明为 `CompApparelVerbOwner.UsedOnce`，不是其 charged 子类重新声明；IL 检查以实际声明类型为准。
5. 不替换 `WarmupComplete`、`PreparePath` 或 `HitCell`。每轮仍由 FFF `PreparePath -> ArcSprayPathUtility.BuildZigzagPath` 建完整三趟路径，直射只不使用它；切回扫射直接用当前 burst index，不会发生空路径/越界。扫射分支完全执行原 `Verb_Spray.TryCastShot -> Fortified.HitCell`。直射按 `currentTarget` 补原有 `sprayEffecterDef`（如有）。
6. 连发数、间隔、音效、枪口焰、每发/每轮燃料均由原 `Verb.TryCastNextBurstShot` / `VerbTick` 状态机负责，未另起状态机。
7. `ThingWithComps.GetGizmos` postfix 接在 Pawn/建筑的共同基类上；真实 Pawn、原版炮塔、Weapon Station 和 FFF 炮塔迭代器均可接入，不为各第三方复制控制入口。`ThingWithComps.ExposeData` postfix 只给匹配武器写一个 `whitegiverDmsArcFireMode` 整数。无自定义 comp/Def/深层类/存档引用；默认 `0`（自动：己方直射/其他扫射）不强制写出，`1` 为直射，`2` 为扫射。缺字段/未知数值回到自动。

启动时严格检查继承链、protected virtual 接缝和签名，验证原单发 IL 中 Projectile/强制偏移/调试/两项弹药通知/ShotReport/野生散射/掩体的实际调用数量。出现新 launcher 私有成员或数量漂移，记录 **`Interface/IL mismatch: refused the entire patch group`** 并回滚全部 forward hooks，保持上游，不静默退化成另一种直射算法。

Original reverse patch uses the vanilla method body, while mods hooking the shared `ShotReport`, equipment, burst or projectile APIs continue to see the real objects. Existing VFM/WeaponStationFireModes controls remain a separate system.

## 基线 / Baseline

核查日期：2026-11-07。运行目录不含第三方 DLL。

| DLL | SHA-256 |
|---|---|
| `Assembly-CSharp.dll` / 1.7.5000 rev1275 | `d55aa8e47fa3c5463ca85b0eccd51f195b4bbcd04f56da3af21c2f6688a81254` |
| FFF 1.7 `Fortified.dll` | `eb7b2a3af12257c9868bf37b8dccbf92e63cc352e9717f7730ac61ab655723b9` |

上游介绍：[FFF](https://steamcommunity.com/sharedfiles/filedetails/?id=3498575851)、[DMS II](https://steamcommunity.com/sharedfiles/filedetails/?id=3819799478)、[AncientCorps](https://steamcommunity.com/sharedfiles/filedetails/?id=3469398006)。

## 构建与离线验证 / Build and validation

```powershell
dotnet build mods/DMSArcSprayFireMode/Source/DMSArcSprayFireMode.csproj -c Release
pwsh -File mods/DMSArcSprayFireMode/Validation/validate.ps1
```

工程 `net10.0`、`Nullable=enable`、`TreatWarningsAsErrors=true`。`GameRoot` / `WorkshopRoot` / `HarmonyPath` 可用 MSBuild 属性覆盖。不自动部署。

2026-11-07：Release 0 警告/0 错误，四个 C# 文件 LSP 无诊断。隔离驱动绑定真实 Assembly-CSharp、Fortified、WeaponStation DLL；**3 项预期 RED，1041 项 GREEN，CE 2 项，真实构造入口 IL 漂移拒用 2 项通过**：

- RED：未应用补丁时，己方仍有扫射角、发射目标是折线格且不调用 ShotReport、没有模式按钮；三个同需求断言真实失败。
- GREEN：18 个分散路径点、三次横向穿越与原始角度；直射 original receiver / currentTarget / ShotReport 真实调用；400 发高/低精度对照为 **400 / 8** 进入目标命中分支（不是游戏中最终击中数量）。实际野生散射、掩体拦截、强制偏移及 LOS 分支执行。
- 七个实际 XML verb 配置、持枪 Pawn、守门人形状炮塔、真实 Weapon Station 与 FFF 子类均可切换。敌对/中立/无阵营武器保存为 Direct 仍不出按钮且走真实扫射；完整敌方 Pawn/炮塔 Gizmo 迭代器也验证无模式按钮。友方支架+敌方操作员额外拒用。
- 原状态机 18 发/34 tick，2 tick 间隔；连发交替切换两方向无异常；弹药/装填各扣 1 次、燃料 18+2、effecter 18、枪口焰 18、音效 36。
- 真实 Scribe 文件三种值往返、新实例读回、缺字段旧档与非法值默认，枪交换状态隔离、原生按钮分组与过期 action 门控。
- QA-only 原版单发副本故意移除关键 getter：真实 Mod 构造入口报一个明确错误、零 forward hooks、零功能按钮。CE 模式同样零 hooks/UI。

**验证边界**：没有启动游戏、没有读写玩家存档或真实设置。仅 QA 副本重绑定 Unity 到 `UnityStubs`，禁用 ModsConfig I/O 和静态纹理初始化；隔离 map/weather/cover/LOS/stat 输入、最终生成/Launch/FX、基础 Thing 存档的无关世界目录和装备 UI 热键，**不替换待测 FFF 路径、单发、ShotReport、原始散射、连发状态机、模式 Scribe**。全图 AI、真实鼠标/绘制、完整存档加载仍待游戏内浅测。

## 安装、浅测、卸载 / Install, shallow check, uninstall

只复制 `About/`、`Assemblies/`、`Languages/` 到独立 Mods 目录；排在 FFF、DMS II、AncientCorps 和 DMSFloggerWeaponTags 后。不得覆盖创意工坊原件。普通重新启动后加载；部署时游戏必须退出，不热补丁、不代启动/关闭游戏。

**一步浅测**：下次正常进游戏，选中现有持 GAU-8/鞭笞者的己方单位或守门人炮塔，**在它正在射击时点一次「环世界原版直射」按钮**，应立即变成「DMS 原版扫射」并横向摆动/散布；再点击可回直射。直射默认可直接使用，无需新开档。敌人没有此按钮。

卸载：正常退出游戏、禁用本 sidecar 即可恢复所有原 DMS 扫射。存档只多一个可忽略的整数键，没有缺类/缺 Def 依赖。停用后保存会丢弃模式选择。

## 维护 / Retirement

- 游戏或 FFF DLL 更新后比较 SHA-256/最后写入时间并重跑离线驱动。核查 `Verb` burst 字段、原版 launcher IL、`Verb_Spray` 的索引与末发消耗、FFF 三趟路径/HitCell，以及 Pawn/炮塔是否仍经过共同 Gizmo/ExposeData 基类。
- 启动日志应出现 `[DMS Arc Fire Mode] Installed player-only ... vanilla shot IL verified.`；若出现拒用日志，此次加载未启用本功能，应先停用并核查，不放宽 IL 锚点掩盖变化。
- FFF/原版若原生提供等价逐枪、玩家专属、可存档的直射/扫射切换，本 sidecar 可退役，避免重复控制。
