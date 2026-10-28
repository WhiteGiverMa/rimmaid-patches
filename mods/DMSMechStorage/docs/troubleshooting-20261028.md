# 封存与唤醒排查记录（2026-10-28）

本文件记录从 1.0.0 到 1.1.4 的真实游戏问题、根因与修正。结论均来自当前安装的 1.7 程序集反编译与用户实测日志；涉及玩家存档的细节已用通用描述替代。

## 症状时间线

| 版本 | 现象 |
|---|---|
| 1.0.0 | 唤醒休眠建筑时报 `The original mech could not be released from its capsule`，封存看起来正常 |
| 1.1.1 / 1.1.2 | 同一句报错保留，但附带数值显示 `spawned=True, destroyed=False, holder=ThingOwner\`1, capsuleHasMech=False, count=0` |
| 1.1.3 | 唤醒成功，但机兵出现在地图左下角；日志出现 `Releasing mech from stale owner Map.` |
| 1.1.4 | 正常落在胶囊格、可正常行动 |

另外两个与唤醒无关、但同轮修掉的问题：

- 对休眠（`SelfShutdown`）中的机兵执行封存时，出现 `TryMakePreToilReservations() returned false for a non-queued job right after StartJob()`。
- 被征召待命的机兵无法封存。

## 根因

### 1. 1.7 里“已生成”的东西本来就挂在 Map 名下

`Thing.SpawnSetup` 会调用 `map.spawnedThings.TryAdd(this, false)`，因此一个已生成的 Thing 的 `holdingOwner` 是 Map 的 `ThingOwner`。1.0.0～1.1.2 把 `holdingOwner != null` 当作“没有释放干净”，而该条件在成功生成后必然为真，导致唤醒 100% 失败。这是本次排查中最大的认知错误。

### 2. `ThingOwner.Contains` 只看链接，不看列表

`ThingOwner.Contains(Thing)` 的实现只有 `item.holdingOwner == this`。由此衍生两个陷阱：

- 链接还在、但列表里已经没有该对象时，`Remove` 会走 `LastIndexOf` 得到 -1 再 `RemoveAt(-1)`，抛 `ArgumentOutOfRangeException`。
- 链接断开、但列表里还留着该对象时，`Remove` 直接返回 false，条目永久残留，`Building_MechCapsule.HasMech`（看 `Count`）永远为真。

因此凡是“从容器里取出”的动作，都必须用 `IndexOf` 判断真实列表，并在需要时先修补链接再移除。

### 3. 把机兵从 Map 的持有列表里摘掉会让它飘到地图角落

1.1.3 的“清理悬空归属”把已生成的机兵从 `map.spawnedThings` 中移除，而它的 `mapIndexOrState` 仍是“已生成”。这种注册不一致会让机兵随后掉到默认坐标 (0,0,0)，也就是地图左下角。修法是：只清理**容器**类型的所有者，Map 永不动。

### 4. 休眠目标被强行拉起

等待过程使用 `Toils_General.WaitWith`，其 `ForceWait` 会为目标重建一个 `Wait` 类作业。目标是 `SelfShutdown` 时，重建出的作业只有落点参数缺失的 `SelfShutdown`，`JobDriver_SelfShutdown.TryMakePreToilReservations` 随即失败并刷出预约警告。传入 `maintainSleep: true` 后，`ForceWait` 会保留目标位置，休眠作业得以正常预约。

### 5. 征召待命被当成战斗

`PawnUtility.IsFighting` 把 `Wait_Combat` 也算作战斗，而征召待命用的正是 `Wait_Combat`。改用 `PawnUtility.IsAttacking`（近战/开火中/已锁定目标）即可放行待命，真正交战时仍然拦截。弹道类机兵在两个 burst 之间可能短暂判定为“未攻击”，但作业每 tick 重验，一旦进入交战状态就会中止。

### 6. 躺卧姿态被一起封存

`Toils_LayDown.SelfShutdown` 会把 `jobs.posture` 设为躺卧，`Pawn.DeSpawn` 只清作业、不重置姿态，而胶囊内容不 tick，于是休眠建筑一直显示躺姿。做法是在 `PawnUtility.GetPosture` 上做一次仅限“玩家所有、DMS 来源、位于 FFF 胶囊内”的显示修正，返回站立；不改健康、阵营或存档字段，旧胶囊读档后自动生效。

### 7. 释放后寻路从旧格起步

`respawningAfterLoad: true` 会跳过 `pather.ResetToCurrentPosition()`，而封存时 `StopDead()` 把 `nextCell` 留在原位置。胶囊被搬动后再唤醒，第一次寻路会从旧格开始。释放成功后显式重置 `pather` 即可。

## 最终释放流程（1.1.4）

1. `CanActivate`：胶囊已生成、内容为非死亡机兵；执行者已生成、玩家阵营、同图、未倒地死亡、能解析出控制器；完整占地检查（仅豁免胶囊自身）；只有需要建立控制关系时才检查带宽。
2. 清理归属：非 Map 的容器所有者按 `IndexOf` 判断后移除，链接断开的只断链；胶囊容器同样按真实列表处理。
3. 若机兵残留“已生成”状态，先 `DeSpawn`；若它仍被登记在 `map.spawnedThings` 却无有效链接，先修链再移除。
4. 在胶囊格 `GenSpawn.Spawn(..., WipeMode.VanishOrMoveAside, respawningAfterLoad: true)` 重新落位，保留电量与平台资源。
5. 校验 `Spawned`、`Map`、`Position`、`Destroyed`、`HasMech`，异常时给出具体数值；成功后重置 `pather`、按需建立控制关系、销毁空胶囊。

失败路径优先原地恢复同一 pawn 与原控制组；地图生成也失败时，把原机兵装回普通 FFF 胶囊放进执行者背包，避免丢 pawn。

## 验证与边界

离线驱动（`Validation/`）用当前安装的 1.7 程序集跑真实容器、关系、带宽、`Blueprint_Install` 与 `DynamicDrawPhaseAt` 逻辑，覆盖：菜单与来源过滤（含 Cinders 正反例）、预约冲突、休眠等待、征召放行与实际攻击拦截、1–5 格占地、同 pawn 往返、失败注入与回滚、无人机/觉醒/指挥机语义、地图注册保持、旧坏档落位修复、pathfollower 重置、姿态仅限未生成对象、悬空链接。

**未由离线驱动覆盖**：游戏窗口的实际像素表现、真实寻路与整局存读档。这些以用户实测为准；本文件所在版本即用户确认通过的版本，发布 DLL 未再重建。

## 维护提示

- 上游 FFF / DMS / Cinders 更新后，先核对本模组六个 Harmony 目标与 `TryAcceptMech`、`ThingOwner`、Overseer 关系、`DrawData` 偏移等契约，再重建并跑驱动。
- 若某版本出现新的 `release failed:` 详细日志，先看里面的 `spawned/pos/registered/holder/capsuleHasMech` 五项，再决定是否属于本模组职责。
- `ThingOwner.Contains` 的语义与 Map 所有权模型是本模组最容易被误改的两处，修改释放逻辑前请先读本文件第 1、2 节。
