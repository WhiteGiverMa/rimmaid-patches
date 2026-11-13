# 联合轻工 BWM 订单剪贴板修复 / UFLI BWM Clipboard Fix

- **packageId**：`whitegiver.ufli.iwbclipboardfix`
- **版本**：1.0.0，RimWorld 1.7 / .NET 10
- **依赖**：Harmony、[联合轻工 UFLI](https://steamcommunity.com/sharedfiles/filedetails/?id=3651799915)（`kindseal.ufli`）、[Better Workbench Management](https://steamcommunity.com/sharedfiles/filedetails/?id=935982361)（`falconne.bwm`）
- **类型**：独立 Harmony sidecar。不修改工坊文件、原 DLL、订单数据格式或存档。

## 修复的问题

打印机订单窗口检查粘贴来源时，UFLI 的 `ImprovedWorkbenchesCompat` 会输出：

```text
[UFRecipes] IWB compat: found ImprovedWorkbenches but could not resolve _copiedBills (version changed?).
```

当前已核对的两处原因：

1. UFLI 用 `GetProperty("BillCopyPasteHandler")` 读取 BWM 的 `Main.BillCopyPasteHandler`，但实际成员是 `public readonly` **字段**，不是属性。
2. BWM 的 `_copiedBills` 为 `List<Bill_Production>`。它不能通过 `as List<Bill>` 转换；仅修正第一处后，仍会静默返回 null。

后果是 BWM 独立剪贴板里的订单不能导入联合打印机。警告在每个进程中只解析一次，单纯不再打印警告并不能证明功能修复。

## 补丁行为与边界

仅对 `UFRecipes.ImprovedWorkbenchesCompat.GetCopiedBills()` 安装一个条件式 prefix：

- 从实际已加载的 UFLI/BWM 模组查找程序集，不加载第二份 DLL。
- 使用 `Main.Instance` 属性与两个正确的实例字段获取当前列表。
- 通过 `IEnumerable<Bill>` 将生产订单复制为新的 `List<Bill>`，保留顺序和元素引用，不修改 BWM 自己的剪贴板。
- 每次读取当前 Main、handler 和列表，不缓存实例或订单快照。
- 接口不符时不安装 hook；运行时读取不可用时交还 UFLI 原方法，保留其日志与失败语义。不全局屏蔽警告。

以下原规则保持不变：

1. 来源优先级：UFLI 自身剪贴板 → 原版 `BillUtility.Clipboard` → BWM 剪贴板。
2. `CanPaste` 按任一有效项判断，`Paste` 逐项过滤不兼容、null、已删除订单，克隆并归属目标工作台。
3. 不增加 BWM 链接订单功能，不把其“全部配方兼容才可粘贴”的规则套到 UFLI 上。
4. 不修改配方注入、生产、产物投递或其他 UFLI sidecar；不处理独立的 WaitWith 警告。

## 安装与浅测

将此目录放到 `RimWorld/Mods/UFLIIwbClipboardFix`，在 Harmony、UFLI、BWM **三者之后**启用。需要正常重启游戏；运行中的进程不会热加载本补丁。

成功注册日志：

```text
[UFLI IWB Clipboard Fix] Patched UFRecipes.ImprovedWorkbenchesCompat.GetCopiedBills; BWM field/list conversion repaired.
```

正常游玩时，用 BWM 复制一张打印机支持的订单，再在打印机订单窗点一次粘贴；应出现对应的新订单。若 UFLI 已有自己的复制内容，它仍优先使用自己的剪贴板，属于本补丁刻意保留的上游规则。

## 构建与真实 DLL 回归

```powershell
dotnet build Source/UFLIIwbClipboardFix.csproj -c Release
# 将 Source/bin/Release/net10.0/UFLIIwbClipboardFix.dll 复制到 1.7/Assemblies 后：
pwsh -File Validation/validate.ps1 -Qa '<outside-game QA directory>'
```

编译属性 `GameRoot` / `HarmonyPath` 可覆盖默认本机路径。QA 还可通过 `RIMWORLD_ROOT`、`HARMONY_DIRECTORY`、`UFLI_DLL`、`IWB_DLL` 环境变量选择对应运行依赖；编译引用用 `GameRoot` / `HarmonyPath` / `UFLIPath` MSBuild 属性指定。

驱动使用真实游戏、UFRecipes、BWM 与补丁 DLL。只在 QA 目录重绑 Unity 引用；为防读取或覆盖用户配置，仅禁用 QA 副本 `ModsConfig` 的初始化。目标复制、读取、克隆和粘贴方法不改写。引擎音频与日志接入点在驱动进程隔离。

2026-11-11 离线结果：生产与 QA Release 构建均 0 警告/错误，3 个 C# 源文件 LSP 无诊断；**12 RED / 36 GREEN / 2 依赖缺失检查通过**。

- RED：真实 `BWM.CopyAll` 捕获两张订单，原 UFLI 的 `CanPaste=false`、`Paste=0`，精确警告一次；在 QA 内存单独补字段读取仍返回零，复现第二处错误。
- GREEN：真实单张/整组复制与 `BillStackFacade.Paste` 新增订单、克隆/顺序/数量/归属、两级来源优先权、清空/删除/混合配方/null、RemoveBill、实例替换、失败回退和恢复均通过。
- 依赖缺失：Mod 构造器幂等，不注册兼容 hook。

**离线驱动不是游戏内 UI 测试。** 原生窗口点击、完整模组启动、自然生产与存档往返未由本驱动验证，不据此宣称游戏浅测通过。

## 基线、更新监测与退役

验证基线：

| 组件 | 版本/构建 | SHA-256 |
|---|---|---|
| RimWorld `Assembly-CSharp.dll` | 1.7.5005 rev1082 / 1.7.9810.32479 | `82387f78262461e53bb06a67c3945912b876d1fdb07235122bbb6e59d073f125` |
| UFLI `1.7/Assemblies/UFRecipes.dll` | 1.0.0.0，2026-10-28 构建 | `439713cfc6986565a2c18ea15805d4dbbccbb8f173d08206c6fb9bdf8bcda57a` |
| 官方 BWM `Assemblies/ImprovedWorkbenches.dll` | 1.6.1.3 | `0913320230337b8482c3903454a3e1417bafb6a7aa80a3703d936736cba827d4` |

官方 BWM 本身只声明到 1.6。本补丁只验证上述组合的剪贴板接口，不代表修复 BWM 的其他 1.7 兼容问题；不自动替换成非官方移植版。

- Steam 更新后比较两个上游 DLL 的 `LastWriteTime` 和 `Get-FileHash -Algorithm SHA256`。变化时先停用本补丁并复核，不把“构建成功”当作接口没变。
- UFLI 同时修正字段读取和集合转换，且不带此补丁时 BWM → 打印机真实转贴通过后，删除本补丁。
- BWM 包名、程序集名、成员种类/类型变化时，补丁跳过并保留上游行为；不扩大反射匹配或猜测新接口。
- 本补丁没有存档字段，可在正常退出游戏后停用；不会回滚已经粘贴出的订单。

## 上游反馈

可直接发送的中文短稿及技术版见 [docs/upstream-report.zh-CN.md](docs/upstream-report.zh-CN.md)。归属方是联合轻工作者，不是 BWM 或复乐园；尚未代用户公开发帖。

## English summary

Restores the UFLI fallback that imports copied bills from BWM 1.6.1.3: read the actual handler field, then copy the covariant `IEnumerable<Bill>` into a `List<Bill>`. One conditional Harmony prefix preserves UFLI's clipboard precedence, filtering and cloning. No upstream binaries or save formats are changed. Revalidate both DLLs on updates; remove after upstream includes both corrections. Real-DLL offline regression passes; native game UI QA remains separate.
