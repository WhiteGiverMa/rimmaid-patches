# 带宽转接基站 / Mechanitor Bandwidth Relay

RimWorld 1.7，依赖 **Biotech** 和 **Harmony**。模组只引用 DLC 原有的「频带节点」贴图，没有附带 DLC 素材，也不修改原建筑。

## 用法

1. 完成「基础机械科技」研究后，在「生物技术」建造栏建造「带宽转接基站」。占地 2×2，消耗 200 钢和 4 个零部件，建造等级 4，通电时耗电 300 W。
2. 选中基站，点「设置带宽转接」；也可以选中一名机械师，右键基站进入同一窗口。选供给方、接收方，输入整数目标带宽或点「填入可用量」，再点「确认」。检查面板显示转接方向和实际量 / 目标量。
3. 基站只划拨供给方自己的空余带宽，不借出收到的带宽，不改变监督关系或控制组。多座基站共用同一个空余池，按基站的固定顺序分配。供给方的机兵和孕育账单优先，空余变动时实际划拨量自动调整。

**同图配对，离图继续转接。** 新建或更换配对时，两名机械师都必须在基站所在地图。连好后，两人在不同地图或远行队中也能继续共享带宽；不必回图就能调整目标带宽或解除原配对。基站必须保持存在、供电并属于玩家，双方仍须是合格的己方机械师；停电或暂时失去资格会暂停而不清空配置。旧版保存的连接直接沿用。

若收回的带宽已经被接收方用掉，原版会暂停超额孕育或让多余机兵失去控制；带宽及时恢复时会重新取得仍在组内的机兵，已野化的机兵不自动重接。通过窗口主动修改或解除且会造成超编时会先要求确认。转接不改变原版机兵指挥距离与跨图控制规则。

安装时只复制 `About/`、`Assemblies/`、`Defs/`、`Languages/` 到同一个新模组目录，在游戏内启用。已有存档可新增基站；不要把移除玩法模组当作安全的原位更新。要移除，先在游戏里解除并拆掉基站，处理接收方超编，另存备份后再停用。

## 构建与验证

```powershell
dotnet build Source/MechanitorBandwidthRelay.csproj -c Release
# 检查构建结果，显式复制发布 DLL 到本 Mod 的 Assemblies；构建不会写游戏目录。
dotnet run --project Validation/Validation.csproj -c Release
```

自定义安装位置可传 `-p:GameRoot=...`、`-p:HarmonyPath=...`，验证运行时分别用 `RIMWORLD_ROOT`、`RIMWORLD_HARMONY`（Harmony 所在目录）。验证直接加载 `Assemblies/MechanitorBandwidthRelay.dll`，可在 `--` 后额外传入另一个成品目录。

基线：`1.7.5013 rev802`，Waylith/.NET 10。1.0.0 的 DLL 在换图断言上失败，1.1.0 通过 81 项离线检查。驱动使用真实 `Assembly-CSharp.dll` 的隔离副本，保留原版带宽/控制与孕育结算、远行队容器成员关系、供电信号、基站弃图回调和 Scribe 引用解析；不运行配置/工坊发现、渲染、机械师实际旅行或完整游戏读档。实机浅测：已有连接时，把任一方派到另一张图，确认基站仍显示原转接量。未启动游戏，未读取或修改玩家配置或存档。

维护时核对 `Pawn_MechanitorTracker.TotalBandwidth`、`Notify_BandwidthChanged` 及地图读档顺序。转接作用于机械师控制容量和原版带宽条，不改 `MechBandwidth` 原始面板属性；只读原始属性的第三方 UI 不会显示转接。游戏或 Harmony 升级后先运行验证再替换 DLL。

## English quick start

Research **Basic mechtech**, then build a **bandwidth relay** under **Biotech**. Select the relay and use **Configure bandwidth**, or right-click it with a mechanitor selected. Choose the lender, receiver and requested amount. The relay uses the vanilla Biotech band-node artwork and requires Biotech and Harmony.

Only spare capacity is lent. Both mechanitors must be on the relay's map to create or change the pairing; once linked, they can be on different maps or in caravans. The relay still needs power and both mechanitors must remain eligible. Mech ownership, control groups, and command range are unchanged. If the receiver becomes over capacity, the game applies its normal bandwidth-loss behavior.
