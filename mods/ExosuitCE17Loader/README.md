# Exosuit / MobileDragoon：1.7 CE 独立加载补丁

恢复 Exosuit Framework 在 1.7 加载列表中排除的 `1.6/CE` 内容，供 MobileDragoon 已加载的 CE 补丁使用。不改工坊原件，不删除装备、弹药或 NPC 生成逻辑，不复制 MobileDragoon 内容。

## 安装与范围

把本目录复制到本地 `Mods/ExosuitCE17Loader`，再将自己安装的 Exosuit Framework（工坊 3352894993）`1.6/CE/` 全部内容原样复制到本补丁 `1.7/CE/`。本次原件共 16 文件：1 DLL、2 定义 XML、13 语言 XML。第三方内容只在本机部署，不随本仓库分发；未附第三方原件时，本目录只是安装元数据，不能单独修复问题。

加载顺序：Harmony、Combat Extended、Exosuit Framework、MobileDragoon、本补丁。仅用于 1.7；1.6 空加载块和嵌套 CE 目录避免自动加载旧版重复内容。不要将 `CE` 内容直接摊到 `1.7/`。

本次验证基线：游戏 1.7.4981.136；Exosuit 1.7 主 DLL SHA256 `7e2ab0953a702ff4280e67ba1df45d53a5652966c037e070e4e46541225a02e4`；原始 Exosuit_CE.dll SHA256 `e7b167309929bb9510369c688a0ad9a08c7798948c800584e79343be05ef2e9e`；活动 CE DLL SHA256 `8da219cfd3d12b157d809035a9384d7e1e512a6a65ecd430480874f39e32aaa5`。部署目录的 `upstream-files.sha256` 记录全部原件校验值。

## 验证与已知边界

真实游戏/Exosuit 1.7/本地 CE 依赖的进程外驱动：83 类型加载，620 方法 JIT 成功、4 项跳过、0 失败；真实 Harmony 3 PatchAll 成功、13 目标登记。仅 QA 临时副本重定向 UnityEngine 引用以模拟游戏自带的 UnityStubs 绑定；部署 DLL 是未修改原件。引擎内容目录和 Mod/静态初始化发现机制已只读复核。

尚未完成游戏内验收，不能据此声称肩炮开火、背包供弹、NPC 生成和读档全部正常。首次正常启动后快速开始一次，检查 `Exosuit.CE` 缺类型、MF AmmoBackpack 与 DMS TurretPack 缺定义报错是否消失；应能看到 Exosuit CE 设置分类。供弹和肩炮在正常游玩时继续观察。独立模组身份可能使用新的设置文件，原框架的全局弹药预设不会自动迁移。

## 更新、退役与撤回

上游 Exosuit **实际恢复**自己的 CE 程序集及定义加载后，在下一次启动前停用本补丁，避免重复程序集/定义；仅 CE 或框架宣布更新，不等于已验证可退役。核对 Exosuit 的 LoadFolders、CE 内容校验值及新日志。Exosuit、MobileDragoon、CE 或游戏更新后都需要重新检查接口和引用；不得盲目沿用旧副本。

Steam 不覆盖本地 Mods，但也不会自动更新这份副本。可用 PowerShell `Get-FileHash -Algorithm SHA256` 对照本地清单检测上游变化。切到其他游戏大版本前停用并复核。撤回仅需在游戏关闭时禁用 `whitegiver.exosuit.ce17loader`，原工坊文件不需恢复；加载含相关装备的存档前恢复兼容内容，不清理存档中的物品。
