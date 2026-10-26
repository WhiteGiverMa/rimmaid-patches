# 钢筋混凝土重型桥梁 / Reinforced Concrete Heavy Bridge

独立的 RimWorld **1.7** 模组，`whitegiver.reinforcedconcrete.heavybridge`，版本 1.1.0。

## 功能

同时完成原版「重型桥梁」（`HeavyBridges`）与防御工事的「混凝土」（`FT_Concrete`）研究后，在「结构」中建造「钢筋混凝土重型桥梁」。默认每格 **14 个钢筋混凝土**（`FT_ReinforcedConcreteBag`），不额外消耗钢铁。

桥梁定义直接继承 Odyssey 的 `HeavyBridge`，不复制或分发游戏贴图，只改名称、说明、材料造价和研究前置。原版重型承载、爆炸免疫、不燃、建造工作量 2500、地基/地板分层、洪水与太空桥梁规则、绘制路径、涂色和拆除规则均保留。钢筋混凝土作为固定消耗材料而非可选 stuff，不会把材料的三倍建造工时或颜色修正乘到桥梁上。手动建造原版钢铁桥不受影响。

飞船行为同样保持原版：重型桥梁不是「飞船结构」地基，不会随飞船起飞。本模组不增加飞船规则或航行限制。

## 依赖与安装

必须启用 **Harmony**（`brrainz.harmony`）、**Odyssey**（`Ludeon.RimWorld.Odyssey`）与 **[防御工事 - 工业](https://steamcommunity.com/sharedfiles/filedetails/?id=2561619583)**（`Aoba.Fortress.Industrial`），本模组排在三者之后。1.1.0 使用 Harmony 实现自动补桥选型；防御工事本身的 Fortified Features Framework 等前置照其要求安装，不额外打包这些前置的程序集。

仅将本目录的 `About`、`Assemblies`、`Defs`、`Languages` 复制到 `RimWorld/Mods/ReinforcedConcreteHeavyBridge/`，在模组列表启用并重启。不要将整个补丁仓库或 `Source`、`Validation`、构建输出复制进 Mods。

## Mod 设置

「选项 → Mod 设置 → 钢筋混凝土重型桥梁」：

* 每格钢筋混凝土用量：整数 **1–10,000**，默认 **14**；可恢复默认。
* 修改立即更新建造需求、造价及地形财富缓存，游戏中同步重算地图财富并使房间统计失效；已有蓝图、工地和桥梁也读取新值，关闭设置页保存。
* 已交给工地的材料不会被删除或替换。调低造价后，完工时按原版流程消耗工地容器；建议在工地开工前调整。
* 拆除返还沿用原版按**当前造价**计算的规则，而不是追溯每格建造时的历史用量。
* 设置是全局偏好，适用于所有存档；支持简体中文、繁体中文和英文。

### 自动补桥选型

设置页可在以下两个选项间即时切换，关闭窗口自动保存：

* **自动放上原版重型桥梁（钢铁）**：默认，自动选用原版钢铁重型桥梁。
* **自动放上钢筋混凝土重型桥梁**：在水面等需要重型桥支撑的格子上放建筑时，自动补本模组桥的蓝图，材料预览同步使用设置中的钢筋混凝土用量。

自动补桥由 **[Replace Stuff - Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3526354009)** 提供，并非原版功能。它是可选集成，没有启用时此开关无效果，本模组的手动造桥、造价和卸载转换仍可用。

只影响之后自动补出的重型桥梁；已有蓝图、工地、建成桥、普通木桥与其他桥类型不变，不因切换选项而在已有桥工地上叠第二张桥蓝图。手动选择桥梁的入口不变。未完成「混凝土」研究时仍沿用可用的原版桥，不绕过研究；钢筋混凝土库存不足时仍可规划钢筋混凝土桥，不会偷偷切回钢铁。鼠标费用行的图标与数量跟随选型，Replace Stuff 原本固定的「桥梁」文字不改。

实现只为 Replace Stuff 的 `PlaceBridges.GetNeededBridge(BuildableDef, IntVec3, Map, ThingDef)` 注册一个 Harmony postfix。鼠标材料预览与实际补桥共用此入口，不另写一套自动造桥系统，也不修改其放置许可逻辑。

## 卸载前转换

1. **先备份并载入要处理的存档**，打开本模组设置。
2. 点击「将所有钢筋混凝土重型桥梁转化为原版重型桥梁以便卸载模组」，确认后转换。
3. 已建桥梁原位变成原版 `HeavyBridge`，不拆桥、不退款或额外收费；保留桥上建筑、地板、天然底层与涂色。完成后的桥梁按原版钢铁桥规则拆除返还材料。
4. 未完成的本模组桥梁蓝图/工地会被**取消**，按原版取消流程全额返还工地已收到的材料，工作进度不保留，也不会新建钢铁桥工地。
5. **另存为一个新存档，再停用本模组并重启游戏**。继续使用转换后的存档；其他旧存档必须分别转换。

按钮只作用于**当前游戏所有已加载地图**，包括非当前选中的地图；不自动写存档，不修改磁盘上的其他存档，也不支持替其他模组清理其私自封存于 `Find.Maps` 之外的地图。主菜单未载入游戏时只提示，不进行转换。转换前的备份是撤销方式。

角色可能保存曾用过的地形攻击来源，转换也会把其中的自定义桥引用换成原版桥，保留攻击 tracker 和冷却。收集包括世界角色、地图与世界容器、尸体、运输船和飞船里的角色；并非只检查当前站在桥上的人。异常时会明确提示不要卸载，不会把失败当作成功。

不要跳过转换直接卸载：原版读取缺失 TerrainDef 时可能回退成土壤，而不是恢复水面；未清理的蓝图/工地也会留下缺失 ThingDef。

## 工坊预览图

`About/Preview.png` 为 **1024×1024** PNG，可直接用于工坊上传。封面是原创几何示意图，游戏内仍复用原版贴图。Windows 自带 System.Drawing 程序化绘制，无需安装绘图库；固定随机种子，重复运行输出一致：

```text
pwsh -NoProfile -File tools/draw-preview.ps1
pwsh -NoProfile -File tools/draw-preview.ps1 -OutputPath "自定义输出路径.png"
```

## 构建、核验与维护

基线为本机 **1.7.4985 rev1269 / Waylith / .NET 10** 的实际 XML 和程序集，不使用 RimSage 的旧 1.6 索引代替当前接口。

```text
dotnet build Source/ReinforcedConcreteHeavyBridge.csproj -c Release -p:GameRoot="RimWorld 1.7 目录"
dotnet build Validation/Validation.csproj -c Release -p:GameRoot="RimWorld 1.7 目录"
dotnet Validation/bin/Release/net10.0/Validation.dll --help
dotnet Validation/bin/Release/net10.0/Validation.dll bind "游戏目录" "本 mod 目录" "发布 DLL" "独立临时 QA 目录"
dotnet Validation/bin/Release/net10.0/Validation.dll run "游戏目录" "本 mod 目录" "发布 DLL" "同一个 QA 目录"
dotnet Validation/bin/Release/net10.0/Validation.dll absent "游戏目录" "本 mod 目录" "发布 DLL" "同一个 QA 目录"
pwsh -NoProfile -File tools/package.ps1 -OutputDirectory "新的打包输出目录"
```

构建不自动部署。发布 DLL 放入 `Assemblies`，不分发游戏或第三方程序集。`GameRoot` / `HarmonyPath` 可通过 MSBuild 参数覆盖；Validation 的 `HARMONY_DIRECTORY` 与 `REPLACE_STUFF_ASSEMBLY` 环境变量可覆盖两项可选本机查找路径。`bind` 为已安装的 Replace Stuff 制作仅重定向 Unity 程序集引用的临时 QA 副本，`run` 加载它，`absent` 不加载它验证缺席路径。

Validation 仅在临时副本隔离配置/绘图初始化，并以测试外壳隔离地图预约/UI清理与物品真正落地；真实转换、`Destroy(Cancel)`、返料计算/容器转移、地形与角色来源 Scribe 方法保持原样。自动补桥测试使用真实 Replace Stuff 选型/补桥方法与真实 Harmony 3 注册，仅将最终 `GenConstruct.PlaceBlueprintForBuild` 接到捕获入口，观察其请求的蓝图 Def，不冒充完整地图生成。

已验证的基础功能：Release 构建零警告错误，C# 文件 LSP 干净；真实 XML 继承/新版解析器/交叉引用、两项研究4种完成状态；14个钢筋混凝土且无额外钢铁、工时与贴图引用不变；设置37的真实保存读回、缺省/上下限、即时造价与地形财富缓存更新（先建财富缓存，再改造价，与真实市场价比较）；两地图的裸桥/地板下桥/临时层/涂色保留，地形网格、寻路事件和房间统计失效通知；角色来源引用/冷却保留、重复转换幂等及异常层次预检拒绝；真实蓝图/Frame取消，只退已交付的9个材料、不碰其他蓝图；移除自定义定义及生成工地定义后，真实地形网格和角色地形来源保存读回通过；真实角色收集器覆盖多层地图容器内的死者、世界角色与未生成运输船载荷。封面实际执行、1024/256尺寸查看、重复输出哈希一致。

1.1.0 自动补桥定点验证：真实 Mod 构造注册唯一 postfix，反复载入设置不重复注册；新设置两选项真实 Scribe 保存读回，旧设置缺键默认钢铁；真实 Replace Stuff 发现两座重型桥，四研究组合×两选项、库存反向选择与零库存、普通木桥/其他桥/null/越界/干地/已有桥均按边界返回；实际 `EnsureBridge.PlaceBridgeIfNeeded` 请求的桥蓝图与预览选型一致，已有钢铁/钢筋混凝土蓝图和Frame都不叠加；实际安装 DLL 的 IL 证明鼠标预览与补桥共用唯一选型调用。Replace Stuff 缺席时没有自动补桥 patch，基础功能回归通过。

**仍待游戏浅测**：设置页实际渲染与点击、鼠标预览实际绘制、殖民者完整建造作业、返料真正落地、完整游戏存档在停用模组后重新载入。这些无法从进程外自动驱动，不以离线驱动通过冒充游戏验证。自动补桥浅测：设置里选择钢筋混凝土后，在需要重型桥的水面放一格墙，检查桥蓝图材料是钢筋混凝土；切回钢铁，在另一格比较。首次真实卸载仍请使用备份副本。

更新后复核 `HeavyBridge` 的 XML 继承名、`FT_ReinforcedConcreteBag` / `FT_Concrete`、`TerrainGrid` 的地基分层/存档格式及 `Frame.Destroy(Cancel)` 的返料路径；Replace Stuff 更新后核对 `GetNeededBridge` 四参数签名及预览/`EnsureBridge` 共用调用，原版缺类时只跳过可选集成，签名漂移则报未应用。不要更改本模组 defName/packageId，否则旧存档可能丢失对应定义。若原版或工业防御工事提供等价可调钢筋混凝土重型桥梁，可先转换存档再停用本模组。

## English

Research **Heavy bridges** and **Concrete** to build a vanilla heavy bridge for **14 reinforced concrete per cell** by default. Requires Odyssey and Fortifications - Industrial. Vanilla textures, heavy support, blast/fire resistance and construction work are inherited unchanged. Configure the integer material cost in Mod Settings; changes are live and save when closing the window.

Version 1.1.0 additionally requires Harmony and optionally integrates with Replace Stuff - Continued. Choose steel or reinforced concrete for automatically added heavy bridges; steel is the default, before Concrete research the vanilla result is retained, and existing plans/frames and other bridge types are preserved. Both mouse material costs and new bridge plans use the same selection hook. Without Replace Stuff, only this automatic-placement option is inactive; manual bridge construction remains available.

Before removal, back up and load each save, use the confirmed conversion button, and save under a new name. Completed bridges across all loaded maps become vanilla steel heavy bridges without replacing floors or natural terrain. Unfinished custom bridge plans are cancelled and delivered frame materials refunded by vanilla cancellation; construction progress is not retained. The button does not edit other save files or save automatically. Disable this mod only after saving the converted game. The original geometric Workshop illustration is reproducible with `tools/draw-preview.ps1`.
