# 机械师带宽条 / Mechanitor Bandwidth Bar

生物技术（Biotech）机械师带宽 Gizmo 的显示替换。仅改绘制，不改带宽数值、机械师机制与存档。

## 解决的问题

原版 `RimWorld.MechanitorBandwidthGizmo.GizmoOnGUI` 用「一格 = 1 带宽」画方块：

1. 从 2 行开始，行数 `num2` 一直自增，直到 `num2 * 每行列数 >= 带宽总数`；
2. 格子高度 = `可用高度 / 行数`，格子再 `ContractedBy(2f)`；
3. 带宽一多，格子被压到 4px 以下，`ContractedBy(2f)` 之后出现零尺寸或负尺寸矩形。

截图中的 `134 / 175` 会落到 8 行 × 31 列、每格约 4px，所以方块重叠、溢出、画糊。带宽上千时更糟。

## 方案

改为**容量进度条**：满量程 `max(已用, 总容量)`，按比例映射，任何带宽数量都不会退化。

```
┌ Bandwidth ──────────────── 134 / 175 ┐
│ ████████████████████████┆░░░░░░░░░░░ │  黄 = 已用，白线 = 总容量
│ 余量 41                      孕育 3  │
└──────────────────────────────────────┘
```

- 黄色实心：`UsedBandwidthFromSubjects`
- 黄色条纹：`UsedBandwidthFromGestation`（孕育中的机械，仍计入已用）
- 红色：超过总容量的部分，白色竖线标记总容量位置
- 刻度线：按「每格至少 8px」自动选择 1 / 2 / 5 / 10 / 20 … 的步长；小带宽时每格仍是 1 带宽，接近原版的格子观感
- 头部数字超编时变红；底部显示余量（或超编量）与孕育占用
- Tooltip 保留原版内容：机械种类与各自带宽合计

Gizmo 尺寸保持原版 `136 × 75`，不影响 Gizmo 网格排布。

## 实现

`Source/MechanitorBandwidthBarMod.cs`

- Harmony **Prefix** 挂在 `MechanitorBandwidthGizmo.GizmoOnGUI`，返回 `false` 跳过原版绘制；
- 通过 `AccessTools.FieldRefAccess` 读取私有 `tracker` 字段；
- `tracker` 为空、patch 未安装或 `GizmoOnGUI` 解析失败时返回 `true`，回退原版绘制；
- 绘制只用 `Widgets.DrawBoxSolid` / `Widgets.Label` / `Widgets.DrawWindowBackground`，不新建纹理。

`Source/BandwidthLayout.cs` 是纯布局数学（不引用 Unity/Verse），负责比例、余量、超编、刻度步长，可离线驱动。

## 构建

```text
dotnet build mods/MechanitorBandwidthBar/Source/MechanitorBandwidthBar.csproj -c Release
```

默认引用本机 `A:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll`、`UnityStubs.dll` 和工坊 Harmony（`2009463077\1.7\Assemblies\0Harmony.dll`），可用 `-p:GameRoot=` / `-p:HarmonyPath=` 覆盖。

发布 DLL 位于 `Assemblies/MechanitorBandwidthBar.dll`。

## 离线验证

```text
dotnet run --project mods/MechanitorBandwidthBar/Validation/Validation.csproj -c Release
dotnet run --project mods/MechanitorBandwidthBar/Validation.Harness/Harness.csproj -c Release
```

- `Validation`：布局数学，覆盖 3/5、134/175、200/175 超编、0/0、负数、孕育 clamp、百万级带宽、零宽度。
- `Validation.Harness`：加载真实 `Assembly-CSharp.dll` + Harmony，断言 Prefix 已注册、`tracker` 为空时回退原版。

两者都不绘制真实 UI；实际观感需要在游戏里选中机械师确认。

## 兼容与退役

- 只 patch `GizmoOnGUI`，不改 `GetWidth`、`Visible` 或其他 Gizmo 行为。
- 若其他 mod 也 prefix-skip 同一方法，生效顺序由 Harmony 加载顺序决定。
- 上游改动以下任一项时重新核对：`MechanitorBandwidthGizmo.GizmoOnGUI` 签名、私有 `tracker` 字段、`Pawn_MechanitorTracker` 的 `UsedBandwidth` / `TotalBandwidth` / `UsedBandwidthFromGestation`。
- 上游若自行修复大带宽渲染，可直接停用本 mod。
