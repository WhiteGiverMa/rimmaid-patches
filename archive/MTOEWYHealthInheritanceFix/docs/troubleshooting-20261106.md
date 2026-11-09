# 健康继承补丁排错记录（2026-11-06）

本文只记录通用诊断与维护信息，不包含玩家名称、存档内容、本机用户名或私人路径。

## 1. 旧导出仍然缺仿生体/植入体

这是数据缺失，不是恢复端筛选问题。旧版上游采集端只写入 `psylinkLevel` 与 `mechlink`，没有写入通用 Hediff 快照，因此旧 `.aidier` 中不存在可供本补丁恢复的仿生体或植入体数据。

处理方式：保持目标新档不变，启用本补丁后打开旧源档，重新执行上游导出，再回到目标档导入新记录。不要手工伪造健康 XML；部位索引、标签、等级和 def 必须来自实际源 pawn。

## 2. 启动出现 target type/signature mismatch

错误前缀为：

```text
[MTOEWY Health Inheritance Fix] Target signature mismatch or patch failure; no patch retained
```

检查 RimWorld 是否为受支持的 1.7、Harmony 是否启用，以及上游模组是否更新。补丁要求同时找到：

- `IdealTransferNeural.Capture(Pawn)`
- `IdealTransferNeural.ExposeData()`
- `IdealTransferCodec.Project(XmlDocument)`
- `IdealTransferNeural.Restore(Pawn)`

查找或挂载任一失败时会撤销本补丁的全部 Harmony 所有者项。不要只注释某个报错或保留部分挂载；先核对新上游方法体，再更新并重跑红绿驱动。

## 3. 导入时逐条跳过 Hediff

可能警告：

- `Skipping missing HediffDef`：目标环境没有源记录所需的 HediffDef。确认对应内容模组已启用且 defName 未变化。
- `body part ... is unavailable or missing`：目标身体结构不匹配，或普通植入体的目标部位/祖先部位缺失。该条会跳过，其他条目继续恢复。

成对部位恢复会优先使用经过 def 与标签核对的原始索引，再按自定义标签回退，最后寻找相同部位 def 且没有同种 Hediff 的部位。不要为“让索引命中”而删除标签校验，否则身体布局变化时可能把左右器官叠加到同侧。

## 4. XML 被 Parse 拒绝

上游 `IdealTransferCodec.Parse` 限制文档大小、节点数、深度及 XML Attribute。本补丁的 `<hediffs><li>…</li></hediffs>` 必须使用八个纯标量字段，全部通过 `Scribe_Values.Look(..., forceSave: true)` 写入；不得添加 `Class` 属性或深层 Hediff 对象。

`Project` 前置会临时分离 `hediffs`，后置只保留 `li` 及以下字段：

```text
defName bodyDefName partIndex partDefName partCustomLabel severity level visible
```

若新增字段，必须同步修改条目的 `ExposeData`、投影白名单、恢复逻辑和离线结构断言；只允许 `IsNull=True` 的上游既有空节点规则不能当成新增类型属性的许可。

## 5. 离线验证环境边界

验证驱动加载真实游戏、Harmony 和上游程序集。临时 `bind` 副本只做两类适配：

1. 将 Unity 程序集引用重绑到游戏提供的 `UnityStubs`，并跳过配置/美术静态初始化，避免读取真实配置或初始化渲染。
2. 将正常游戏中由 mod 加载器提供的 `GenTypes.AllActiveAssemblies` 枚举接到游戏、上游与补丁程序集，保证真实 Scribe 加载器能解析完整类型名。

此外只隔离渲染缓存、健康状态转换和启灵能力授予等会话副作用。被测上游 Capture/Codec/Wrap/Unwrap/Load/Restore 与本补丁四个后置/前后置方法不使用替代实现。QA 目录必须位于游戏和上游工坊目录之外。

常见失败：

- `UnityEngine.CoreModule` 缺失：先执行最新驱动的 `bind`，不要使用旧 QA 副本。
- Scribe 报 `Could not find class`：确认类型程序集枚举边界已安装，不要把回退类型产生的红字视为通过。
- `Prefs.LogVerbose` 空引用：驱动应使用内存中的空 `PrefsData`，不要读取或写入真实 Prefs。
- RED 未丢失健康状态：可能意外提前挂载了补丁；RED/GREEN 应是独立进程。
- GREEN 左右器官重叠：检查目标镜像夹具是否确实改变索引，以及回退是否按标签与占用情况匹配。

## 6. 验证结论的正确解读

真实 DLL 离线红绿通过证明：采集、标量持久化、上游投影、哈希包装、Parse、载入和恢复链路在构造夹具中成立。它不证明完整游戏 UI、旧记录逆向恢复、所有第三方 Hediff 的自定义 comps 或所有身体模组组合均已测试。

完成离线验证后仍需一次正常游戏内浅测：从含代表性仿生体和植入体的旧源档重新导出，在目标档导入，检查部位、左右位置、等级、严重程度与日志。上游若原生实现等效通用健康快照并可独立通过同类测试，本补丁可退役。
