# 失能机关：机兵封存 / DMS Mech Storage

RimWorld 1.7 / Biotech。选中一名机械师殖民者，右键己方 DMS 机兵，选择 **「封存某机兵」**。机械师走近后工作 300 tick，将原机兵装入 Fortified Features Framework 的休眠机械体建筑。

版本：1.1.4。packageId：`whitegiver.dms.mechstorage`。

## 功能与边界

- 使用原 FFF 休眠建筑，保留原 pawn 身份、姓名、装备、物品栏和健康状态，不重新生成机兵。外观由 FFF 的机兵绘制逻辑提供；卸载后的物品也保留机兵姓名。
- 装备中的主武器会按古代军团休眠建筑的方式画在机兵脚边；武器仍是同一件物品，不会被丢出或复制。衣物与其他装备同样原样保留。
- 封存后解除原机械师控制关系并立即刷新带宽/控制列表。不要求由原机械师操作，也不要求封存者有空余带宽。
- **被征召待命也可以封存**；只有正在攻击（`IsAttacking`，含近战、开火中或已锁定目标）才会拦下。目标是休眠/自停机的机兵时，等待过程保留其睡眠状态，不会打断它。
- 可通过原版「卸载／重新安装」搬运，再由机械师右键休眠建筑，使用 FFF 原有的「控制」任务启用。
- 普通受控机兵重新启用时需要足够带宽，并归启用者控制。部署无人机、已觉醒机兵和独立指挥机不强行建立 Overseer 关系。
- **指挥机封存成功后解除所有下属连接；唤醒后需要重新连接下属。** 不伪造死亡，不删除其内部控制器。封存失败时不会解除下属关系。
- 部署无人机保留原平台引用；平台销毁会影响其无人机，这是 FFF 原有机制。本模组不退款、解除平台关系或改变无人机归属。
- 机械师须可行动；目标须存活、已部署、同地图、玩家所有。原地完整占地必须可行走，不能覆盖其他建筑或会被删除的有价值对象。空间不足时先把机兵移到开阔处。
- 储存中的机兵不 ticking，沿用 FFF 容器的行为；其显示姿态固定为站立，避免把睡眠/倒地的姿势带进休眠建筑。**休眠建筑仍可受伤；按 FFF 原规则被摧毁时会失去内部机兵。** 这不是无敌保险箱。

### DMS 范围

以种族 ThingDef 的 `modContentPack.PackageIdPlayerFacing` 和 `RaceProps.IsMechanoid` 判断，不按名字前缀或 FFF 组件误收其他模组。

白名单：

| 系列 | packageId |
|---|---|
| DMS 2 核心 | `aoba.deadmanswitch.core` |
| 古代军团 | `aoba.deadmanswitch.ancientcorps` |
| 合成人形 | `aoba.deadmanswitch.synthetic` |
| 远征 | `aoba.deadmanswitch.expedition` |
| Taktikmagier | `aoba.deadmanswitch.taktikmagier` |
| MobileDragoon | `aoba.deadmanswitch.mobiledragoon` |
| Motorized | `aoba.deadmanswitch.motorized` |
| 军团 | `srxf.deadmanswitch.legion` |
| Tri-Tech | `dms.tritech` |
| 第十四战斗群 | `dms.xivbattlegroup` |

原版、非白名单模组，以及并非机械体的车辆、驾驶员、装备不受影响。这里只界定本功能范围，不保证所有分支自身与 1.7 兼容，也不为它们迁移程序集。

### Cinders of the Embergarden

该模组本体不属于 DMS，因此不整体放行，只按它自己的 DMS 兼容层识别：包为 `BreadMo.Cinders`，且该种族带 `Fortified.MechWeaponExtension` 并实现 `Fortified.IWeaponUsable`。当前命中「墓碑」（`Cinder_Mech_Krepost`）与「仿制墓碑」（`Cinder_Mech_Tombstone`）；「被侵蚀的外骨骼」「战争打击者」「战争清扫者」及各炮塔步行机只有该模组自带的载具/内建炮塔系统，没有 DMS 重装备标记，不提供封存。

## 安装

依赖 Harmony、Biotech、Fortified Features Framework（3498575851）及 DMS 2（3819799478）。加载在它们之后。

将本目录的 `About/`、`Assemblies/`、`Defs/`、`Languages/` 放到单独的 `Mods/DMSMechStorage/`。不要把整个仓库或 `Source/bin` 复制到 Mods。不修改工坊文件。正在运行的游戏不会热加载，须正常重启后使用。

启动日志标记：`[DMS Mech Storage] Mechanitor storage orders and scoped FFF capsule restoration enabled.`

## 实现与失败处理

- 原生 `FloatMenuOptionProvider`，独立 `DMSMS_StoreMech` JobDef/JobDriver。沿用原版预约、接近和 `WaitWith`（期间让目标等待，并保留睡眠状态）机制。
- 封存直接复用 DMS 自身技能所用的 `MechCapsuleUtility.DeactivateMech`；仍保留 capsule 引用并检查转移结果，失败可回滚。
- 释放时按 1.7 的真实所有权模型处理：`Thing.SpawnSetup` 会把已生成对象挂到 `map.spawnedThings`，因此 `holdingOwner != null` 不是错误状态；清理只针对容器所有者，Map 永不触碰。`ThingOwner.Contains` 只比较链接、不查列表，故一律用 `IndexOf` 判断真实列表：在列表里才移除（缺链接先补回），只是悬空引用则仅断链。机兵若残留“已生成”状态或仍登记在 `map.spawnedThings` 却无有效链接，先修复再在胶囊格重新生成，随后逐项校验 `Spawned/Map/Position/Destroyed/HasMech`，失败时输出具体数值与容器内容。
- 封存/唤醒全过程的问题记录见 [排查记录](docs/troubleshooting-20261028.md)。
- 入舱失败优先原地恢复同一机兵及原控制组；地图恢复也失败时，把装着原机兵的普通 FFF 胶囊放入机械师物品栏。失败激活保留原机兵供重试，不复制 pawn。
- 注册六个 Harmony 目标，均带 DMS/玩家胶囊或持有者守卫，其余对象执行原逻辑：`Building_MechCapsule.CanActivate(Thing)`、`ActivateMech(Thing)`、`DynamicDrawPhaseAt`（脚边武器）、`MinifiedThing.LabelNoCount`、`PawnUtility.GetPosture`、`GenSpawn.Spawn`（仅在从 DMS 胶囊释放原机兵时保留恢复语义）。
- 重新部署使用 `respawningAfterLoad: true`，保留真实电池余时和机兵平台资源，不触发「首次生成」补满/重建逻辑。释放时只豁免当前胶囊自身的建筑占位，其余占地仍检查。

## 验证状态（2026-10-28）

**用户已确认游戏内验证正常**：封存、征召待命封存、休眠机兵封存、脚边武器、唤醒落点与正常行动。离线驱动覆盖于此前的排查轮次，详见 [排查记录](docs/troubleshooting-20261028.md)。

严格构建、C# 诊断及离线真实程序集驱动通过；游戏窗口像素表现与整局存读档未由离线驱动覆盖。

验证基线：RimWorld `1.7.4990 rev1069`，当前安装的 FFF 1.7 DLL。

| 文件 | SHA-256 |
|---|---|
| 发布 `DMSMechStorage.dll` | `417fb0ca96eb63c682c4076cb577b382d944f421e31abcd64055a16509555bff` |
| FFF `Fortified.dll` | `0a6be73cd22cf4559e0afd461a5dcc48e8c39aad684d3f388b9d8f7ddd6e21a3` |
| 游戏 `Assembly-CSharp.dll` | `4e61d57205944c113631234bc55136a1a7c1679667b26008bee5425bcffa1463` |

运行 `pwsh -File Validation/validate.ps1`，可通过 `-GameRoot`、`-WorkshopRoot`、`-QaRoot`、`-ModPath` 指定位置。发布构建：`dotnet build Source/DMSMechStorage.csproj -c Release`，不会自动部署。

驱动已覆盖：十个 DMS 来源包、Cinders 正反例、原版/其他模组反例；原生 provider 发现、中文菜单、失效点击、资格/寻路入口；真实预约冲突及最后 toil 重验；征召待命放行与实际攻击拦截；睡眠目标在 `WaitWith` 下保留可预约的休息格；FFF 1–5 格占地、非锚点阻挡；同 pawn 姓名/伤病/装备/物品栏往返；带宽和控制缓存；入舱/生成/移出容器后抛错的故障注入与回滚；无关系 tracker 无人机、电池余时、平台资源、觉醒和指挥机控制语义；睡眠/倒地胶囊的显示姿态与 `BodyAngle`/`GetBodyPos`；脚边武器的偏移/角度/确定性散布与不触发条件；真实 minify 和 `Blueprint_Install.MakeSolidThing` 的同对象转移及名称。

### 离线替身边界

QA-only 副本仅重绑定 FFF 的 Unity 引用，并清除游戏 `ModsConfig`、`ModLister`、`BaseContent` 初始化，阻断真实配置/内容扫描；不改安装中的程序集。Defs/地图为内存 fixture，胶囊名称/尺寸取自当前 FFF XML，不运行完整游戏 Def 加载。

保留真实入舱、ThingOwner 所有权、GenSpawn 的占地/wipe/容器移出逻辑、关系列表、relation worker、控制组和带宽刷新、FFF 相关 `PostSpawnSetup`、minify/install 方法。显式替换地图生成/销毁的引擎注册、图形/音频/翻译查询、统计数值来源、能力计算、寻路、AI 调度和界面通知；Job 路径到达/延迟由驱动手动推进。因此**不把这些结果冒充 GUI、真实寻路、整局 Scribe 读档或任意第三方回调环境的验证**。

最小游戏浅测：正常重启后，选中机械师，右键一名原本没有自封存技能的 DMS 机兵，点「封存」，观察它成为同名休眠建筑且带宽下降。卸载/搬运/重装、完整读档可在正常游玩时顺手确认，不要求为此制造复杂场景。

## 更新、停用与限制

FFF 或游戏更新后先核对上述六个 Harmony 目标、`TryAcceptMech`/胶囊容器契约、Overseer 关系/指挥机机制、`DrawData` 偏移来源及原生 float-menu API，再重建并运行驱动。工坊 DLL 的修改时间和哈希可作为更新提示，不能只看程序集版本（FFF 固定为 1.0.0.0）。Cinders 更新后要重核其 `1.7/DMS` 兼容层是否仍只覆盖墓碑与仿制墓碑。

本模组没有自有休眠建筑或额外持久化数据，但有自定义作业。停用前结束/取消封存作业，并先唤醒封存中的无人机、觉醒机兵和指挥机，避免停用后回到 FFF 原始恢复语义。不要删除 FFF/DMS 依赖后直接读含有其内容的存档。

上游原生提供等价的机械师右键全系列封存、保持独立控制语义和安全释放后，本模组可停用；不是见到原来的局部自封存技能就算已替代。

第三方 `DeSpawn`/`SpawnSetup`、控制通知或 inventory 守卫任意抛错时不能承诺绝对事务性；极端回调失败可能留下已部署但未完成连接的机兵。离线未遇到这一类实际失败，不作全模组兼容保证。
