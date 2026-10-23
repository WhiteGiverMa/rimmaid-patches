# 武器基站：CE 特殊武器后坐力兼容

RimWorld 1.7 独立 sidecar，package ID：`whitegiver.weaponstation.cerecoilfix`。
需要 Harmony、Combat Extended 与 Weapon Station（工坊 `3807251471`），排在三者之后。

## 修复行为

- 侵蚀葫芦等保留原版 `Verb_Shoot` / `Verse.VerbProperties` 的特殊武器，装入 CE 武器基站后不再因 CE 后坐力动画触发 `InvalidCastException`。
- 仅这些不含 CE 后坐力数据的武器在基站上的动画偏移置零；标准 CE 枪械继续调用原 CE 动画函数。
- 不修改武器定义、特殊弹道、伤害、射击、换弹、弹药或存档，不关闭全局后坐力动画。
- 不改工坊 DLL，可与 `WeaponStationPickTarget` 一起启用，两个补丁分别只处理绘制和安装按钮。

## 根因与实现

Weapon Station 的 `WeaponStation_CE.Building_WeaponStationCE.DrawAt(Vector3, bool)` 在已装武器、后坐力动画开启、且射击后的后坐力恢复窗口内调用 `CE_Utility.Recoil`。后者把非 CE projectile verb 的 `verbProps` 强转为 `CombatExtended.VerbPropertiesCE`。

Cinders 的 `Cinder_ErosionGourd` 即使加载了 CE XML，也仍使用原版射击类型和特殊触手弹丸。不能为消除绘制报错而直接把它改成 CE 弹道。

本补丁只将目标 `DrawAt` 内的一次 `Recoil` 调用替换为同签名包装器：有 `VerbPropertiesCE` 时委托原函数，其余情况返回零偏移并继续后续绘制。没有全局 prefix 或吞异常的 finalizer。原指令的 opcode、跳转标签和异常块均保留，其余绘制不改。

启动时显式核对方法参数类型与唯一调用点。上游签名变化、调用点数量不再为一，或其他 transpiler 改变该调用时，会清楚报错并拒绝应用，不静默误改。CE 武器站类型未加载时安全跳过。

## 构建与安装

```text
dotnet build Source/WeaponStationCERecoilFix.csproj -c Release
```

可用 MSBuild 属性 `GameRoot`、`HarmonyPath` 指向自己的 RimWorld 1.7 与 Harmony 1.7 版本。引用均不复制到输出，构建不自动部署。只把本目录的 `About`、`Assemblies` 复制到 `RimWorld/Mods/WeaponStationCERecoilFix` 并启用，正常重启游戏后生效。不要复制 `Source`、`Validation` 或它们的 `bin/obj`。

本补丁不添加持久化数据，停用只会恢复上游原来的绘制行为，无需改存档。

## 回归验证

2026-10-20：Release 构建零警告、零错误，两个 C# 文件 LSP 无诊断。进程外加载真实活动 CE、Weapon Station 与 Harmony 3，验证：

- 原 CE 函数对原版 `Verb_Shoot` / `VerbProperties` 复现同一类型转换异常。
- 实际 Mod 构造函数成功注册到真实 `DrawAt`，恰好替换一处调用，不修改全局 CE 函数。
- 原版、空、近战输入返回零动画偏移；指令身份、opcode、跳转标签与异常块保持；零个或两个调用点会拒绝应用。
- `Verb_ShootCE` 及「原版 verb + CE properties」两条有效路径，共 48 组角度/时间对照的偏移与角度精确相同，其中 24 组为非零动画。
- 未加载 CE 武器站时，实际 Mod 构造函数安全跳过。

2026-10-20：使用者确认游戏内验证正常。本次故障的实机浅测已通过；上述离线驱动仍只用于其列明的类型、接线和动画回归边界，不代表所有武器组合均已实测。

验证驱动位于 `Validation/`。需要 .NET 10，额外的 Mono.Cecil 仅用于验证，不进入发布 DLL。

```text
dotnet build Validation/Validation.csproj -c Release
dotnet Validation/bin/Release/net10.0/Validation.dll bind
dotnet Validation/bin/Release/net10.0/Validation.dll red
dotnet Validation/bin/Release/net10.0/Validation.dll green
dotnet Validation/bin/Release/net10.0/Validation.dll absent
```

运行时路径可通过 `RIMWORLD_ROOT`、`HARMONY_DIRECTORY`、`WEAPONSTATION_DIRECTORY`、`CE_ASSEMBLY` 覆盖；`RECOIL_FIX_ASSEMBLY` 可指定待检的部署 DLL，默认检查同目录构建产物。编译期另传前述 MSBuild 属性。

`bind` 只在验证输出的 `bound/` 内创建临时副本，把旧 Unity 引用改为 `UnityStubs`，模拟 Waylith 的绑定，不改安装原件，也不改方法体。驱动执行真实后坐力函数，但不初始化完整渲染器，因此**不是游戏内视觉与战斗验收**。完整离线 `DrawAt` 驱动受 Waylith 着色器初始化限制，不据此宣称实测通过。

重启读原档后，让一座装侵蚀葫芦的基站向空地开一枪，确认不再出现该类型转换红字且武器正常显示；普通 CE 枪械动画可在后续正常游玩中观察。如果此前为临时规避而关闭了 CE「显示后坐力动画」，请先重新打开，否则不会覆盖这条修复路径。

## 上游更新与退役

核验基线：

- Weapon Station `WeaponStation_CE.dll`：`c68d66aaa7d674b6251fc56297a16360c662df70fb2faead12a391e3a296d802`。
- CE `CombatExtended.dll`：`8da219cfd3d12b157d809035a9384d7e1e512a6a65ecd430480874f39e32aaa5`。

独立 mod 不会被 Steam 的工坊更新覆盖，但 CE、Weapon Station 或游戏更新都可能改变调用契约。可用 `Get-FileHash -Algorithm SHA256 <DLL路径>` 对照以上基线，变化后重新跑驱动，检查 `DrawAt` 唯一调用与 `Recoil` 的六个参数类型。

成功接线日志：`[Weapon Station CE Recoil Fix] Patched Building_WeaponStationCE.DrawAt (one recoil call).`。出现 `patch not applied` 或 `Could not apply` 时不要当作已修复。

如果 Weapon Station 原生跳过非 CE 后坐力、或 CE 自己支持这些类型，停用本补丁后验证葫芦开火不再报错即可退役；不要仅因上游发布更新就假定问题已修。
