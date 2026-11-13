# 给联合轻工作者的反馈草稿

## 发布位置与口径

- 发给 [联合轻工 UFLI 讨论区](https://steamcommunity.com/sharedfiles/filedetails/discussions/3651799915)，或先在[留言区](https://steamcommunity.com/sharedfiles/filedetails/comments/3651799915)发下面的短稿。二选一即可，不必同时刷两处。
- 工坊描述允许反馈 BUG；截至 2026-11-11，在查看的公开介绍中未找到官方源码仓库，因此先反馈而非向其他仓库开 PR。
- 本地完整日志、配置、存档和个人目录不上传。此问题只需要下面的精确警告、版本、接口与独立复现结果。
- 实际游戏已捕获警告；功能失败和修复通过由真实 DLL 隔离驱动复现。未完成无其他 mod 的游戏内点击复现，不能写成“纯净游戏已必现”。
- 不要求关闭警告或改成静默失败；目标是恢复读取与粘贴功能。不要反馈给复乐园或 BWM 维护者，也不要混入其他 BWM 1.7 红字。

## 可直接发的短稿

海豹你好，联合打印机这里遇到一处 BWM 订单剪贴板兼容问题：打开订单窗时会报一次
`[UFRecipes] IWB compat: found ImprovedWorkbenches but could not resolve _copiedBills (version changed?).`

环境是 RimWorld 1.7.5005、联合轻工 10 月 28 日的 1.7 DLL，以及官方 Better Workbench Management 1.6.1.3（不是非官方 1.7 移植版）。核对后似乎不是 `_copiedBills` 改名：`Main.BillCopyPasteHandler` 是字段，但适配里用 `GetProperty` 取；另外 `_copiedBills` 是 `List<Bill_Production>`，不能直接 `as List<Bill>`。

用这两份真实 DLL 做隔离驱动，BWM Copy All 后 UFLI 的 CanPaste=false、Paste=0；仅修第一处仍失败，同时改为字段读取和 `IEnumerable<Bill>` 转新 List 后，真实粘贴可以生成对应克隆订单。游戏内目前只确认黄字，未把完整纯净游戏复现当成已验证。不影响生产，主要是 BWM → 打印机订单转贴。方便的话能检查下这两处吗？谢谢！

## 技术补充

### 接口与最小修改建议

BWM 公开源码可核对：

- [Main.BillCopyPasteHandler](https://github.com/Falconne/ImprovedWorkbenches/blob/f8db7979e9885e0e723a110e56d702b68dbfc341/src/ImprovedWorkbenches/Main.cs)
- [BillCopyPaste._copiedBills](https://github.com/Falconne/ImprovedWorkbenches/blob/f8db7979e9885e0e723a110e56d702b68dbfc341/src/ImprovedWorkbenches/CopyPasteLink/BillCopyPaste.cs)

本机实际 BWM 1.6.1.3 的结构为：

```csharp
internal static Main Instance { get; private set; }
public readonly BillCopyPaste BillCopyPasteHandler = new BillCopyPaste();
// BillCopyPaste 内：
private readonly List<Bill_Production> _copiedBills = new List<Bill_Production>();
```

UFLI `ImprovedWorkbenchesCompat.Resolve()` 获取 handler 时建议用：

```csharp
copyPasteHandler = type?
    .GetField("BillCopyPasteHandler", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
    .GetValue(obj);
```

`GetCopiedBills()` 不直接把两个不同的 `List<T>` 强转，可以改成：

```csharp
return copiedBillsField.GetValue(copyPasteHandler) is IEnumerable<Bill> bills
    ? new List<Bill>(bills)
    : null;
```

`IEnumerable<out T>` 协变，`List<T>` 不协变；保留现有异常处理与依赖缺失逻辑即可。不需要改 `BillStackFacade.PasteSource`、粘贴验证、订单克隆或生产。

### 实际证据与建议游戏内确认步骤

- 实际日志：窗口绘制 `BillStackFacade.PasteSource → CanPaste → ITabMenu_BillListSW.DrawHeader` 时警告一次，不是每帧刷屏。
- 独立进程：原始 BWM `DoCopy(Building_WorkTable)` 写入两个 `Bill_Production`，原 UFLI `CanPaste` false、`Paste` 0；原警告一次。只在内存补 handler 字段后仍是 0，分离出第二处类型问题。
- 修复后：原 UFLI `Paste` 克隆并加入目标 `BillStack`；单张/批量、顺序/数量、混合不支持配方、删除/null、原版与 UFLI 剪贴板优先级通过。没有修改实际工坊 DLL。
- 建议游戏内确认：启动后不要先用打印机自己的复制按钮；在普通工作台用 BWM Copy All 复制打印机支持的订单，再打开联合打印机订单窗并粘贴。此步骤用于复核，不冒充已完成的纯净游戏测试。

### 版本限定

这是官方 BWM 1.6.1.3 的当前接口，不是请求把 BWM 整体移植到 1.7。非官方移植版可能有不同 packageId，未纳入此报告的验证。需要时可提供无个人信息的独立驱动或进一步验证结果；不附带私人存档与完整配置。
