# Fortified CE：Waylith 独立加载模组

> 已按维护决定归档，仅保留历史加载方案，不建议安装。此处归档不代表已核验上游修复，也不会修改本机已安装模组或启用配置。

只用于本机 RimWorld 1.7.4976 rev1043、FFF 1.7 和当前 CE 本地镜像。FFF 的 1.7 LoadFolders 排除了 1.6/CE，但 Citadel/DMS 仍引用 FortifiedCE 类型，导致12个FTC炮塔 thingClass为空、阅读策略初始化失败及烟幕/APS组件缺失。

本模组只携带工坊 FFF 现有的 `FortifiedCE.dll` 原件，不重建、不改XML定义、不吞异常，不修改工坊目录或存档。程序集作者 AOBA。原件来源：工坊3498575851的1.6/CE/Assemblies；SHA256 `34F058A12EB7293FC4ECA74CE55C9E11A15A2D749E73757DFEC5F5ECA8181B73`。第三方原件仅部署在本机Mods，不纳入公开补丁仓库；需要重装时从自己的FFF副本复制，核实hash/接口后再启用。

加载顺序：Harmony、Combat Extended、FFF、本模组。仅声明1.7，切回1.6时应禁用本模组，因为FFF自身会加载FortifiedCE。不要同时加载两份同名程序集。

验证：旧DLL引用的CE/Fortified类型成员检查通过；隔离驱动加载实际DLL测试副本（仅为标准dotnet主机复刻UnityEngine→UnityStubs重定向），41类型加载、174普通方法JIT通过；实际Harmony3 PatchAll通过，9个目标确实注册。部署DLL是未经改写的原件。驱动不是完整游戏，未验证真实炮塔开火、烟幕拦截、APS或读档。

游戏浅测：重启后开发者快速开始，确认进入地图，并检查日志不再出现FortifiedCE类型缺失、FTC null thingClass和ReadingPolicyDatabase异常。实战功能另需观察，不能只靠开局通过宣称全兼容。

时效性：FFF上游恢复兼容FortifiedCE加载后退役本模组，避免程序集重复。任何FFF/CE更新都须重新核对成员、Harmony注册与新日志；不能按旧hash直接重打。撤回：关闭游戏后从ModsConfig移除 `meidocho.fortifiedce.waylithloader`，保留目录不影响未启用状态。
