# 蝉鸣音量 / Cicada Volume

RimWorld **1.7** 独立小 mod，`whitegiver.ambience.cicadavolume`，版本 1.0.0。无需 Harmony 或其他前置。

## 使用

在「选项 → Mod 设置 → 蝉鸣音量」拖动滑条：**0–100%**，步长 1%，默认 100%。0% 静音，100% 保持原版音量；提供恢复默认按钮。拖动立即生效，关闭设置窗口自动保存，适用于所有存档。支持简体中文、繁体中文和英文。

首次安装：将本目录的 `About`、`Assemblies`、`Patches`、`Languages` 复制到游戏 `Mods/CicadaVolumeControl/`，在模组列表启用并重启。不要复制 `Source`、`Validation` 或构建输出。可用于已有存档，停用即可恢复原版声音，不向存档添加数据。

## 调整范围

原版没有独立命名为“蝉鸣”的音效，而是三段环境虫鸣录音。本 mod 用同一个滑条调整：

| SoundDef | 使用场景 |
|---|---|
| `Ambient_NightInsects_Standard` | 温带、寒带、干旱等地图的夜间虫鸣 |
| `Ambient_NightInsects_Jungle` | 丛林及发光森林的夜间虫鸣 |
| `Ambient_DayInsects_Jungle` | 丛林及发光森林的白天虫鸣 |

**不能从混合录音中只分离某一种昆虫。** 同一录音里的其他声音也会一起变小。使用其他 mod 新增的独立 SoundDef 不在此范围；沿用这三个 SoundDef 的替换录音仍会生效。

不改其他 SoundDef、音乐、风雨雷声或总环境音设置。蝉鸣仍受原版昼夜、温度、镜头距离、音乐淡出、总音量与环境音量控制，因此不会把原本不该出现的虫鸣强行播放出来。

## 实现与维护

XML 只向这三个 SoundDef 的 `Main` 子声音追加一个 `Constant` 音量映射。自定义 `SoundParamSource` 每帧读取当前设置，倍率与原版音量因子相乘。没有全局补丁、逐 tick 扫描或重建声音，也不修改游戏/工坊原件。静音再恢复不累计缩小基础音量。

核验基线：本机 **1.7.4985 rev1269 / Waylith / .NET 10** 的实际 DLL 与 `Data/Core/Defs/SoundDefs/World_Sustainers_Ambiences.xml`，未用 RimSage 的 1.6 索引替代 1.7 接口。

游戏更新后复核这三个 def 的 `subSounds/li[name="Main"]/paramMappings`、`SoundParamSource.ValueFor(Sample)` 和 `Sample.Update → ApplyMappedParameters`。其他声音 mod 若整体替换这些定义或删除映射，需要调整加载顺序或重新核验。可检查上述 XML 与 `Assembly-CSharp.dll` 的修改时间/hash 发现版本变动。原版若提供同等独立调节，可停用本 mod；它是音量偏好功能，不因某次 bug 修复自动退役。

## 构建与验证

```text
dotnet build Source/CicadaVolumeControl.csproj -c Release -p:GameRoot="你的 RimWorld 1.7 目录"
dotnet build Validation/Validation.csproj -c Release -p:GameRoot="你的 RimWorld 1.7 目录"
dotnet Validation/bin/Release/net10.0/Validation.dll --help
dotnet Validation/bin/Release/net10.0/Validation.dll bind "游戏目录" "本 mod 目录" "本 mod DLL" "临时 QA 目录"
dotnet Validation/bin/Release/net10.0/Validation.dll run "游戏目录" "本 mod 目录" "本 mod DLL" "同一个临时 QA 目录"
```

需要 .NET 10 SDK。构建不会自动部署；发布时只将生成的 `CicadaVolumeControl.dll` 放进 `Assemblies/`。Validation 使用真实游戏程序集，仅在临时 QA 副本隔离配置初始化，不修改安装原件或真实配置。

已验证：Release 构建零警告错误、四个 C# 文件 LSP 无诊断；真实 1.7 `PatchOperationAdd` 与新版 XML 解析器绑定三个目标，原始节点及所有非目标 SoundDef 保持不变；同一 Sample 的 100/50/0/25/100% 即时变化、静音恢复、重复设置不累乘、原版环境音倍率保留；真实 Mod 构造与 Scribe 读写、缺省值及越界钳制。帮助与错误参数路径通过 PTY 验证。

离线验证只使用两个实际映射（原环境音量 + 新倍率），跳过地图/天气/音乐条件，并以无音频设备的 AudioSource 外壳观察真实音量/mute 属性写入。没有启动游戏，**设置页渲染、实际录音听感和全模组加载仍待游戏内确认**，不把进程外驱动视为游戏实测。

游戏内最后一项浅测：在听得到虫鸣时打开本设置，把滑条拖到 0%，再恢复 100%，确认虫鸣消失并恢复。原版本身的淡入/淡出和场景条件继续保留。

## English

An independent RimWorld 1.7 mod with one live **0–100%** slider for vanilla cicada/insect ambience. Default 100%; 0% mutes it. Find **Cicada Volume** in Mod Settings. Changes save when the window closes. No Harmony dependency. Install only `About`, `Assemblies`, `Patches` and `Languages`.

Targets the three insect SoundDefs above, including jungle day/night recordings. It cannot separate insect species within a mixed recording. Other sounds and the game's master/ambience controls are unchanged. A constant parameter mapping reads the setting every frame; no sound restart is needed. In-game listening and UI appearance still require a game check.
