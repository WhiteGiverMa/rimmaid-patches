# CE Overpenetration

Combat Extended 子 mod — 子弹穿过敌人继续飞行。

## 功能

### 1. 过穿透 (Overpenetration)

所有无终端载荷的普通锐伤子弹在穿透 Pawn 的护甲后都会自动判断是否继续飞行，不需要逐弹种配置。伤害和穿深由 CE 根据穿透后的实际速度继续计算。

**行为模型：**
- 仅处理 `BulletCE + BallisticsTrajectoryWorker/LerpedTrajectoryWorker + Sharp` 的 Pawn 命中。
- 完全偏转、护盾吸收、锐伤转钝伤、overhead 弹丸和带终端载荷的弹丸不会过穿。
- 终端载荷是会在命中点消耗或分解弹头的效果：爆炸半径、爆炸组件和破片组件均在首次碰撞正常结算。
- 仅对当前命中目标追加伤害的效果不是终端载荷；例如 AP-I 的燃烧附伤会对每个被穿透 Pawn 独立结算。
- 以 CE 护甲后伤害比重建剩余穿深，再扣除 `BodyPartSharpArmor × BodySize^(1/3)` 的身体穿越预算。若不足以保留命中时速度的 20%，弹头会留在体内；只有剩余穿深足以穿越身体时才继续飞行。
- 穿透后原弹丸从命中点继续正常 Tick，不传送，因此 CE/VEF 护盾和 BlockerRegistry 仍能拦截。
- 后续穿深、伤害、空气阻力和重力由 CE 原生速度模型接管。
- 命中历史和链式穿透计数会随存档保存。

## 日志设置

在「选项 → Mod 设置 → CE 过穿透」勾选或取消「记录过穿透详情」。默认关闭，且不受游戏的开发者模式影响；开启时每次成功过穿会打印弹种、目标、剩余速度、穿深和连锁次数。切换即时生效，关闭设置窗口时保存为全局 Mod 设置，不写入存档。初始化信息同样只在开启详情时打印。真正的过穿失败和 CE 兼容性错误始终保留，以免隐藏故障。

游戏运行时旧程序集不会热加载；替换 DLL 后需重新启动游戏。之前已经写入的 `Player.log` 内容不会自动清除。

## 职责拆分

- 破墙 AI / LoS / job 熔断兼容修复已拆到 `CEBreachingFix`。
- 瞄准时间、weapon handling、aiming accuracy 数值调谐已拆到 `CEEliteCombatTweaks`。

## 不修改 CE 源码

纯 Harmony patch + `ConditionalWeakTable`，完全独立于 CE 源码。

## 依赖

- RimWorld 1.6
- Combat Extended
- Harmony

## 构建

```bash
dotnet build Source/CEOverpenetration/CEOverpenetration.csproj -c Release
pwsh -File Validation/validate.ps1
```

DLL 输出到 `Source/CEOverpenetration/bin/CEOverpenetration.dll`，**不会自动部署**。验证通过后手动更新本目录 `Assemblies/CEOverpenetration.dll`，并同步 `checksums.sha256`；本地游戏 Mod 需要另外替换其 `Assemblies/` 中的 DLL 与 `Languages/` 目录。验证驱动使用真实游戏程序集检查设置默认值、即时切换和 Scribe 存取，不替代 Unity 设置窗口或游戏内弹道浅测。

本 Mod 是独立本地补丁，不会被 Steam 覆盖；Combat Extended 更新后检查 `BulletCE.Impact`、`ArmorUtilityCE.GetAfterArmorDamage`、`ProjectileCE.Impact` 等 Harmony 目标签名与弹丸状态模型，若上游提供等价过穿功能则停用本补丁。上游工坊页面：https://steamcommunity.com/sharedfiles/filedetails/?id=2890901044 。
