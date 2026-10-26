# 米莉安：地面选择武器 / Milian Weapon Interactions

独立 RimWorld **1.7** 本地补丁，版本 **1.0.0**，packageId `whitegiver.milian.weaponinteractions`。依赖 Harmony 与 Milira Race，不需要 Fortified Features Framework，也不修改第三方程序集或 Def。

## 两种交互

1. **米莉安配置界面**：左键武器格，关闭配置窗口并进入地图武器选择；点地上武器，由这名米莉安自己过去装备。右键同一武器格，仍打开原武器列表，包括上游的取消换装选项。
2. **直接右键装备**：单独选中一名玩家控制的米莉安，右键地面武器，选择「装备……」，由米莉安自己执行换装。征召与未征召均支持；不为普通殖民者、原版机械族或其他机体添加此入口。

选择时按 Esc 或右键仅退出目标选择，不修改已有装备或取消已经下达的任务。取消已下达的换装可使用原列表中的取消选项，或按原版方式打断作业。

## 保留的规则

- 只选择同地图上已落地的单件武器，不从其他角色装备栏、容器、另一地图或武器堆叠中隔空取枪。
- 沿用 Milira `CompTargetableWeapon.Available(Thing, Pawn)` 的近战/远程限制，保留 Milian Modification 自适应机械臂的兼容规则，不另外维护武器白名单。
- 下达 Milira 自己的 `Milira_EquipMilian` 作业，TargetA 为米莉安，TargetB 为武器；不瞬间传送、不改换装时间，保留战术枪带等专用换装联动。
- 地图可达、预约和基本装备资格继续检查；显式装备指令按原版习惯解除目标武器的禁止状态。
- 原有「殖民者右键米莉安，再选择武器」的代装路径、配置表绘制和原武器列表均保留。

## 安装与卸载

只复制本目录的 `About`、`Assemblies`、`Languages` 到 `RimWorld/Mods/MilianWeaponInteractions`，启用并排在 Harmony、Ancot Library 与 Milira Race 之后。启用 Milian Modification 时也排在其后。正常重启游戏才会加载；不支持向正在运行的游戏热注入。

本补丁不添加存档数据、武器、装备组件或自定义作业定义。停用并重启仅恢复上游交互；正在进行的换装是上游自己的任务，不需要清理自定义存档内容。不要为测试本补丁中途卸载 Milira 本体。

本地 sidecar 补丁，源码与发布 DLL 收录于本仓库，未发布到创意工坊。上游作品仍受作者自己的条款约束；包中不附 Milira、Ancot Library、Milian Modification、Harmony 或游戏程序集。

## 构建与验证

```text
dotnet build Source/MilianWeaponInteractions.csproj -c Release
```

`GameRoot`、`HarmonyPath` 可通过 MSBuild 属性覆盖。产物在 `Source/bin/Release/net10.0`，构建不自动部署；安装 DLL 放入 `Assemblies`。

2026-10-26：Source/Validation 的 Release 构建均为零警告、零错误，4 个 C# 文件诊断干净。生产程序集只引用游戏、UnityStubs 与 Harmony 3，不硬引用 Milira、Ancot Library、Milian Modification 或 FFF。生产代码仅对 `PawnColumnWorker_EquipmentMilian.DoCell` 注册一个定点 transpiler；菜单 provider 由原版自动发现，缺依赖或接缝漂移时不提供新入口。

真实安装 DLL 的进程外驱动已验证：

- 近战/远程过滤、自适应机械臂允许相反类别，以及移除配件模组后的限制恢复。
- 原始 XML 解析出的 `Milira_EquipMilian` driver、TargetA=机体/TargetB=武器；两种新入口均捕获具体机体和枪，不受其他菜单 provider 后续切换影响。
- 只注册一个目标、重复 Mod 构造不重复打补丁；唯一按钮接缝替换、原菜单及原绘制调用保留、零/多个匹配拒绝。
- 失控/其他机体、错误类别、堆叠、迷雾、不可达、已有预约、目标被移走时不下达任务。
- 左键关闭真实主表窗口，启动真实 Targeter；不要求米莉安预先被选中，目标参数只接受地面武器；停止选择不修改已捕获任务。右键仍通过原分支，原列表的筛选及取消项保留。
- 真实 `JobDriver_EquipMilian.EquipMilian` 把携带的新枪放入装备槽、旧枪放入战术枪带，容器无重复物品。
- Milira 不存在时，独立进程中的实际 Mod 构造不注册任何 Harmony 补丁。

验证只改独立 QA 副本：旧 Unity 引用绑定至 UnityStubs、隔离配置/渲染内容初始化与最终落物位置。IMGUI 命中、迷雾/可达环境、机体控制状态和任务真正启动等由 QA 外壳驱动；枪带验证隔离装备通知与渲染刷新，原 Milira 换装方法、原资格函数、Job 构造、原容器转移、主表移除和 Targeter 初始化保持真实。**没有验证游戏内完整窗口绘制、OS 鼠标命中、寻路/Toil、实际材质刷新或存档读回**，不把离线通过当作游戏浅测完成。

```text
dotnet build Validation/Validation.csproj -c Release
dotnet Validation/bin/Release/net10.0/Validation.dll --help
dotnet Validation/bin/Release/net10.0/Validation.dll bind GAME_ROOT WORKSHOP_ROOT QA_ROOT
dotnet Validation/bin/Release/net10.0/Validation.dll run GAME_ROOT WORKSHOP_ROOT Assemblies/MilianWeaponInteractions.dll QA_ROOT
dotnet Validation/bin/Release/net10.0/Validation.dll absent GAME_ROOT WORKSHOP_ROOT Assemblies/MilianWeaponInteractions.dll QA_ROOT
pwsh -NoProfile -File tools/package.ps1 -OutputDirectory NEW_OUTPUT_DIRECTORY
```

`WORKSHOP_ROOT` 指 Steam 的 `workshop/content/294100`；离线绑定只写 `QA_ROOT/bound`，拒绝把 QA 输出放在游戏/工坊内部。`contract` 模式可单独核对上游资格函数与真实 XML 任务契约。包只包含 About/Assemblies/Languages/README，不包含源码、验证驱动、第三方 DLL、日志或配置。

发布 DLL SHA256：`819cf7a7e48b7dc52373a1f9666c1eed9b3256cbc16c666457ce164a993bec2f`。

最小浅测：正常重启并启用补丁后，左键配置表的一个武器格，再点地上兼容武器；右键武器格应仍是原列表。随后单选该米莉安，右键另一把兼容武器，下达装备指令，确认它亲自拿取。无需新建或改写存档。

地图选取使用原版简式 Targeter：点击无效位置不会下单，并结束本次选取；再次左键武器格可重新选。Esc/右键取消只结束选取，不打断原任务。

## 上游更新与退役

更新 Milira、Ancot Library 或游戏后，核对武器列 `DoCell(Rect, Pawn, PawnTable)`、`GetEquipmentFloatMenu(Pawn)`、`CompTargetableWeapon.Available(Thing, Pawn)`、米莉安身份判断以及 `Milira_EquipMilian` 的 TargetA/TargetB 契约。

还需核对 `DoCell` 中唯一 `Widgets.ButtonInvisible(Rect, true)` 的参数压栈序列。应用日志若报告缺少签名或未应用，不应当作已经获得两个入口；不要通过忽略错误继续使用半套交互。如果上游原生提供这两种便捷交互，可停用本补丁。

## English

Left-click a Milian weapon cell to pick a ground weapon; right-click keeps the original list and cancellation entry. A single selected, player-controlled Milian can also equip a ground weapon through its context menu, drafted or undrafted. Both paths reuse Milira's eligibility check and its own equip job, retaining adaptive-arm and tactical-sling integration. No FFF dependency, third-party binaries, Def edits or new save data. Requires a normal game restart. Full in-game interaction QA remains separate from offline validation.
