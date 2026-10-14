# 两个指定定义的描述空白修复

适用于 RimWorld 1.7，依赖 Harmony。放在 Harmony、Kiiro Race 和 Oberonia aurea 后。

仅在 `Def.ConfigErrors()` 校验前，对 `Techprint_Kiiro_PrototypeGun` 和 `OA_RK_New_Windbreaker_G` 的 `description` 调用 `Trim()`。不跳过校验，不改正文中的换行、装备数值、科技印记数量或存档，不修改工坊原件。通过 Mod 构造函数注册，早于加载期配置检查，不依赖较晚的 StaticConstructorOnStartup。

风衣的 1.6 原始 XML 在 description 标签后紧跟换行。科技印记是运行时生成的定义，游戏生成器最后拼接科研 description；原始科研 XML 干净，具体运行时空白写入者未确认，因此此修复是精准的输出规范化，不声称修复某个已确证的引擎缺陷。

构建：`dotnet build Source/TargetedDescriptionWhitespaceFix.csproj -c Release`。项目引用本机 1.7 游戏及 Harmony DLL；其他环境需要调整 HintPath。仅部署 About 与编译 DLL 到 Assemblies。

游戏运行时不能热生效。正常重启后检查新日志，这两条首尾空白红字应消失；原有日志不清除。未在游戏内验收前不宣称全面兼容。

## 时效与退役

Steam 更新不会覆盖独立补丁。游戏或 Harmony 更新后，重新核验 `Def.ConfigErrors()` 方法与构建/注册验证；切回 1.6 禁用此 net10 构建。上游修好两个描述后可禁用本补丁，正常启动确认无同名空白告警即可退役。补丁不会自动扩展到其他定义。
