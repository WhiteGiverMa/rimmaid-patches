# WRMegaCorp QuestPart_CSC 读档修复

独立 RimWorld **1.7 / Waylith** 兼容 Mod，依赖 Harmony 与 Wolfein Race MegaCorp Edition（工坊 `3687841204`）。

`packageId`: `whitegiver.fix_wrmegacorp.questpartcsc`，版本 `1.0.0`。

## 修复行为

修复读取含 `WRMegaCorp.QuestPart_CSC` 的存档时的红字：

```text
SaveableFromNode exception: System.MissingMethodException:
Constructor on type 'WRMegaCorp.QuestPart_CSC' not found.
...
Some quest parts were null after loading.
```

- 让 1.7 在反序列化 `QuestPart_CSC` 时走它原本的五参数构造函数，part 不再丢失。
- 补上原 Mod 未保存的 `pos` 字段；旧存档没有 `pos` 时给地图上的随机可站立落点，避免接受飞船坠毁任务后残骸掉到 `(0,0,0)`。
- 不修改工坊 DLL、Def 或存档；其他 QuestPart 类型和其他存档字段不受影响。

## 根因

RimWorld 1.7 的 `Quest.ExposeData` 用：

```csharp
Scribe_Collections.Look(ref parts, "parts", LookMode.Deep);
```

加载 quest parts，**不传 `ctorArgs`**。`ScribeExtractor.SaveableFromNode<QuestPart>` 随后的：

```csharp
Activator.CreateInstance(type, bindingAttr, null, ctorArgs, null, null);
```

因此要求每个 part 类型有无参构造函数。而 WRMegaCorp `2.30`（`1.6/Assemblies/WRMegaCorp.dll`）的 `QuestPart_CSC` 只有一个构造函数：

```csharp
public QuestPart_CSC(string signalAccept, Map map, int shipChunkCount, IntVec3 pos, Quest quest)
```

`Activator.CreateInstance` 找不到匹配构造，抛 `MissingMethodException`；`SaveableFromNode` 捕获后返回 `null`，`Quest.ExposeData` 再把 null 从 `parts` 中移除。报错节点正是：

```xml
<li Class="WRMegaCorp.QuestPart_CSC">
  <shipChunkCount>3</shipChunkCount>
  <signalTag>Quest237.Accept</signalTag>
  <map>Map_0</map>
</li>
```

`QuestPart_CSC.ExposeData` 只保存 `shipChunkCount`、`signalTag`、`map`，没有保存 `pos`。所以即使 part 恢复，旧存档的位置也永远会变成默认 `(0,0,0)`。

## 实现

Harmony Prefix 挂在实际的 `ScribeExtractor.CreateInstance(Type, object[])` 上：

- 仅当目标类型是 `WRMegaCorp.QuestPart_CSC` 且 `ctorArgs` 为空时，直接调用它原本的五参数构造函数创建实例，并跳过原 `Activator.CreateInstance`。
- 其他类型、其他 `ctorArgs`（例如 1.6 路径传入的 quest）原样放行。
- 创建成功后，Mod 自己的 `ExposeData` 仍按原逻辑读取 `shipChunkCount` / `signalTag` / `map`。

`QuestPart_CSC.ExposeData` 的 Postfix：

- 追加 `Scribe_Values.Look(ref pos, "pos")`，新存档会保存 `pos`。
- `LoadingVars` 且 `pos == IntVec3.Zero`、`map != null` 时，用 `DropCellFinder.RandomDropSpot(map)` 选一个落点。
- 读档时输出一条恢复日志，便于确认 part 真的回来了：

```text
[Fix_WRMegaCorp_QuestPartCSC] Restored QuestPart_CSC: shipChunkCount=3, signalTag=Quest237.Accept, pos=(...), map=Map_0
```

## 构建与安装

```powershell
dotnet build .\Source\Fix_WRMegaCorp_QuestPartCSC.csproj -c Release
```

可通过 `-p:GameRoot=... -p:WorkshopRoot=...` 指定本机路径。构建不会自动部署。

关闭游戏后，把本目录的 **About/** 与 **Assemblies/** 复制到游戏 `Mods/Fix_WRMegaCorp_QuestPartCSC/`，在模组列表中排在 Harmony、Wolfein Race MegaCorp Edition 之后并启用。

成功日志：

```text
[Fix_WRMegaCorp_QuestPartCSC] Patches applied.
[Fix_WRMegaCorp_QuestPartCSC] Created QuestPart_CSC with its real five-arg constructor.
[Fix_WRMegaCorp_QuestPartCSC] Restored QuestPart_CSC: shipChunkCount=3, signalTag=Quest237.Accept, pos=(...), map=Map_0
```

出现 `Failed to apply patch` 视为未生效。

## 验证

- 2026-11-04 离线构建：0 警告、0 错误。
- 2026-11-04 `Validation/` 驱动真实 `QuestPart_CSC` 与 `ScribeExtractor.CreateInstance`：空 `ctorArgs` 复现 `MissingMethodException`；补丁 Prefix 用真实五参数构造函数创建实例；其他类型与非空 `ctorArgs` 原样放行。
- 2026-11-04 隔离存档实测（真实 1.7 客户端，`-savedatafolder` 指向临时配置与存档，无关存档未改动）：
  - `[Fix_WRMegaCorp_QuestPartCSC] Patches applied.`
  - `[Fix_WRMegaCorp_QuestPartCSC] Created QuestPart_CSC with its real five-arg constructor.`
  - `[Fix_WRMegaCorp_QuestPartCSC] Loaded QuestPart_CSC: shipChunkCount=3, signalTag=<signal>, pos=(...)`
  - `[Fix_WRMegaCorp_QuestPartCSC] Placed QuestPart_CSC at (...) on the player home map.`
  - 同次加载不再出现 `SaveableFromNode exception` / `Some quest parts were null after loading.`
- 后续游戏内确认：正常存档读档一次，确认日志与任务面板无异常后可提升验证等级。

## 上游更新与退役

核验基线：2026-11-04，RimWorld `1.7.4995 rev309`，WRMegaCorp `2.30`（`1.6/Assemblies/WRMegaCorp.dll`，工坊 `3687841204`，SHA-256 `b320f73a8244b5608f6c0545956eebcaf719e8845b7a5989baf9799e65df1c96`）。上游给 `QuestPart_CSC` 补上无参构造、或改成 1.7 的存档协议后，本补丁可退役。工坊 DLL 更新后复核 `QuestPart_CSC` 的构造签名与 `ExposeData` 字段。
