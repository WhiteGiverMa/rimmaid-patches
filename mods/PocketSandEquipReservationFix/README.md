# Pocket Sand 地面装备预留修复

独立 sidecar，面向 RimWorld 1.7 与原版 [Pocket Sand](https://steamcommunity.com/sharedfiles/filedetails/?id=2226330302)。保留背包切枪功能，只为地面装备补上原版预约机制；不改工坊 DLL、不改存档。

## 修复什么

Pocket Sand 用 XML 把通用 `Equip` 的驱动替换为 `PocketSand.JobDriver_Equip`，但它的 `TryMakePreToilReservations(bool)` 直接返回 `true`，没有真正 Reserve。装备者正在走向武器时，普通自动搬运或 Pick Up And Haul 仍可预约并拿走武器。

普通殖民者和使用 Fortified Features Framework 通用 `Equip` 接口的 DMS 机兵都可能受影响。米莉安的独立 `Milira_EquipMilian` 和其他有自己预约实现的装备任务不属于补丁目标。

本补丁只对 Pocket Sand 自己声明的 `TryMakePreToilReservations(bool)` 加一个 Harmony postfix：

- 原方法返回失败时不改变结果。
- 地面武器通过原版 `pawn.Reserve(target, job, 1, -1, null, errorOnFailed)` 预约，预约失败也会让装备任务失败。
- 武器在衣架中时按原版规则预约衣架，不预约未生成的武器；这只补预约，不新增 Pocket Sand 本身缺失的衣架取物功能。
- 普通背包内切枪不创建地图预约。
- 原版玩家强制命令抢占语义不变；后发的玩家强制指令仍可抢占，不会禁止玩家主动重新分配武器。

重量检查、换装延迟、换下武器放背包、音效与实际装备 toils 完全保留。取消/结束任务沿用原版按 job 释放预约，不增加存档数据或常驻扫描。

## 安装与卸载

1. 安装这个完整目录到游戏 `Mods/PocketSandEquipReservationFix`。
2. 保留原版 Pocket Sand，在它和 Harmony 之后启用本补丁。
3. 正常重启游戏。已经运行的实例不会热加载新 DLL。

packageId：`whitegiver.pocketsand.equipreservationfix`。目标框架为 `.NET 10`。只支持本 README 核验的原版 Pocket Sand，不声明支持不同 packageId 的非官方移植。

卸载时先结束正在进行的装备任务，再停用并正常重启。补丁没有独立 JobDef、ThingDef 或持久组件，不需要编辑存档。

## 构建与验证

```powershell
dotnet build Source/PocketSandEquipReservationFix.csproj -c Release
```

项目使用 `GameRoot` / `HarmonyPath` 指定本机引用，第三方依赖不随补丁分发。构建不会自动部署游戏。

基线：游戏 `1.7.5000 rev1275`；原 Pocket Sand `1.6/Assemblies/PocketSand.dll` SHA256：

```text
4f0c8f663cb206f7ec81c282d5b689d9c244d734ef35ecc494f41cca73ec41cc
```

2026-11-10：Release 构建零警告/错误，已改 C# 诊断干净。真实 Pocket Sand DLL + 游戏预约/作业方法 + Harmony 的离线驱动 **36 项检查通过**，包括：

- 修复前原方法返回成功但预约表为空，另一名同阵营 pawn 可以预约目标；修复后不可预约。
- 原版搬运驱动在同一 fixture 中确实建立预约，排除假的冲突模型。
- 排队与正式开始的重复调用不新增重复预约；原版按 job 清理后可再次搬运。
- 堆叠目标使用整堆预约；背包不产生地图预约；衣架预约源建筑。
- 非强制装备保留失败；强制装备能通过真实原版作业终止/清理抢占当前或排队中的普通搬运。
- 保留上游 false 结果与无目标边界；Harmony 实际绑定且重复注册不叠加。

复跑（仅离线临时目录，需已安装的 Mono.Cecil 0.11.6 供隔离脚本使用）：

```powershell
pwsh -NoProfile -File Validation/run.ps1
```

隔离仅在临时游戏 DLL 副本中屏蔽配置/模组目录、日志和预约调试材质的静态初始化；Pocket Sand DLL、预约与作业终止/清理方法没有替换。当前搬运被抢占后，只有“寻找下一份无关工作”的边界在驱动中跳过。离线驱动未打开真实存档或连接运行中的游戏。2026-11-10用户确认启用本补丁后读档成功；地面装备不被自动搬运抢走的玩法浅测尚未单独确认，不能把加载通过写成完整游戏 QA。

最小游戏浅测：正常重启后，在现成地面武器上给征召者下达一次装备命令，观察他走过去正常拿到武器；无需反复卸载模组或新建复杂场景。背包内切枪在正常游玩时顺手确认。

## 更新监测与退役

- 独立补丁不会被 Steam 更新覆盖，但上游方法签名、目标容器语义或游戏预约机制改变后需重新核验。
- 原版 Pocket Sand 若已在地面装备时调用 `Reserve`，本补丁不再必要，应停用后对照；不要仅凭新增“1.7”标签判断已修。
- 目标类型/声明方法缺失或签名不匹配时打印警告并跳过，不扫描其他装备驱动或放宽到全局预约。
- 可在 PowerShell 检查上游 DLL 的 `LastWriteTime` 与 SHA256；与上面基线不同就先复核目标实现，再决定保留或退役。

```powershell
$dll = Join-Path $WorkshopRoot '2226330302/1.6/Assemblies/PocketSand.dll'
Get-Item -LiteralPath $dll | Select-Object LastWriteTime, Length
Get-FileHash -LiteralPath $dll -Algorithm SHA256
```

## 上游反馈

2026-11-10 检查原工坊当前公开评论和更新记录，未找到这一预留缺口的明确报告或已修说明。未发现可确认的公开源码仓库。本补丁不代替上游修复，不自动发帖。

可直接发给作者的简短说明：

> Hi! I found a possible ground-equip reservation issue in Pocket Sand. `JobDriver_Equip.TryMakePreToilReservations(bool)` currently returns true without reserving the weapon. Since Pocket Sand also replaces vanilla's Equip driver, an automatic hauler can reserve and take the target while the wearer walks toward it. Reserving spawned ground targets through vanilla `pawn.Reserve`, while leaving inventory swaps untouched, appears to address the missing reservation. The player reported the symptom in a modded game; a clean in-game reproduction has not been confirmed. Thanks for maintaining the mod!

## English summary

Restores vanilla reservations for Pocket Sand ground-equip jobs with one postfix on `PocketSand.JobDriver_Equip.TryMakePreToilReservations(bool)`. Ground targets reserve the whole stack for one pawn, outfit-stand targets reserve their source, and inventory swaps remain unchanged. This does not add outfit-stand extraction support or modify equip toils, mass checks, delays, save data, PUAH or global reservation logic. Requires the original Pocket Sand and Harmony; enable after both and restart normally. Offline checks and in-game QA are recorded separately. Retire the patch when upstream implements the missing reservations or changes the target contract.
