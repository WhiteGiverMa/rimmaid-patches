# DMS Flogger Weapon Tags / 鞭笞者装备修复

RimWorld **1.7**，版本 **1.0.0**，packageId `whitegiver.dms.floggerweapontags`。
纯 XML sidecar，无 DLL、Harmony 或运行时扫描，不修改工坊原件。

## 问题与修复范围

AncientCorps 的 `DMS_Gsh30Gatlinggun`（Gsh30-6 "Flogger"／鞭笞者机炮）原本只带 `DMS_VehicleWeapon`、`H_Gatling30mm`。骇蜥、棱堡等机兵开启武器标签筛选后，这两个标签与它们的白名单没有交集，即使体型足够也不能装备。

本补丁只向这把武器追加两个缺失的标签，各自存在时不重复添加：

- `DMS_MechWeaponHeavy`：接通鬣蜥、Gecko、Gravekeeper 等接受 Heavy 的机体。
- `V_Gatling30mm`：兼容接受这类 30mm 转轮炮标签的机体，包括骇蜥、棱堡。

保留原有两个标签、`HeavyAutomatroid` 的 **2.5** 数值，以及武器的射击、伤害、配方和造价。不加入 `DMS_MechWeaponUltraHeavy`，不修改其他武器、机体白名单或全局装备限制。

标签是“任一匹配”，不是要求两个同时命中。机体启用的科技／武器类别限制仍有效。**白名单路线不会再强制检查体型**；保留 2.5 数值不代表小型第三方机体在主动允许这些标签后仍会被体型挡住。

## 依赖与安装

- [The Dead Man's Switch / DMS2](https://steamcommunity.com/sharedfiles/filedetails/?id=3819799478)
- [The Dead Man's Switch - AncientCorps](https://steamcommunity.com/sharedfiles/filedetails/?id=3469398006)
- 上游所需的 Fortified Features Framework 与 Biotech 仍须正常加载。

把本目录复制到游戏 `Mods/DMSFloggerWeaponTags/`，启用并排在 DMS2、AncientCorps 之后；若使用 CE，也须排在 CE 之后。默认根目录布局，不需要 `LoadFolders.xml`。

不要求 CE；在已有 V30 或 Heavy 标签的 XML 上只补缺失的一项，不覆盖 CE 或其他模组的现有标签。未验证完整 CE 战斗环境。

## 验证与最小浅测

2026-11-02，针对 RimWorld `1.7.4995 rev309`、DMS2 `2.0.5`、AncientCorps `2.0.0`：

- **离线验证通过**：使用游戏原生 `DirectXmlToObject.ObjectFromXml<PatchOperation>` / `Apply` 执行实际发布的 `FloggerWeaponTags.xml`，再调用当前 FFF 的真实装备判定，不手工模拟补标签。骇蜥、棱堡、鬣蜥、Gecko 均从拒绝变为通过。
- 原始标签保留、两项各追加一次、已有任一／两项与重复应用的幂等性、其他武器和机体定义不变、2.5 数值保留均通过。单独接受 Heavy 或 V30 均可用，完全无交集、科技／武器类别不匹配仍拒绝。
- 缺少目标武器或直接标签列表时，原生执行器报告 XPath 失败而不修改其他定义，符合更新后需重核的约定。验证只在隔离副本执行，未改安装程序集或工坊文件。
- **用户游戏内验证通过**：2026-11-02 用户确认部署版本可用；运行 XML 与离线验证版本一致。完整 CE 环境仍未验证。

更新后复核时，选中现有骇蜥或棱堡，右键地上的鞭笞者并选择装备。无需重造机兵、武器或修改存档。

## 维护与退役

- AncientCorps 更新后，重新核对 `DMS_Gsh30Gatlinggun` 的直接 `weaponTags` 列表和 FFF 的标签筛选规则。
- 上游原生补齐 **Heavy 与 V30** 两项时可停用本补丁；只有一项时仍补另一项。存在的标签不会由本补丁重复添加。
- 目标 defName 被改名、移除或 `weaponTags` 结构改变时，XPath 将失配，游戏日志会报告补丁失败；应先停用核对，不要盲目重打旧补丁。
- 可比较工坊目标 XML 的修改时间／哈希监测更新，但文件时间变化本身不代表补丁失效。
- 本补丁没有新增 Def 或持久化字段。移除后重启即恢复上游装备规则，不迁移或改写存档；已装备武器的后续处理遵循上游。

## 可给作者的反馈

非 CE 的 `DMS_Gsh30Gatlinggun` 只有 `DMS_VehicleWeapon`、`H_Gatling30mm`，与骇蜥／棱堡的 `UsableWeaponTags` 无交集。开启 `EnableWeaponFilter` 后体型检查不兜底，因此 2.5 的鞭笞者会被拒绝，而带匹配标签的 3.0 武器可用。独立 XML 补丁追加 `DMS_MechWeaponHeavy` 与 `V_Gatling30mm`，保留原标签及 2.5 数值。当前框架的离线判定可复现此差异；完整游戏测试状态见上节。
