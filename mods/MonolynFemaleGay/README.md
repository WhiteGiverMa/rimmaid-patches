# 墨诺灵全员女性同性恋 / Monolyn All Female & Gay

独立 RimWorld 1.7 扩展，版本 `1.0.0`，包 ID `whitegiver.monolyn.femalegay`。需要 Harmony、Biotech 和墨诺灵（`ASEL.MonolynRace`）；排在墨诺灵之后，如有「更好的洗礼」也排在它之后。不修改上游文件。

**模组设置 → 墨诺灵 → 常规**，或 **模组设置 → 墨诺灵：全员女性与同性恋**，均可操作同一个开关，默认开启。

| 状态 | 性别与性取向 | 生育 |
| --- | --- | --- |
| 本补丁开启 | 女性、同性恋；取消遗传性禁欲的恋爱与亲热惩罚 | 绝育，代谢 +1 |
| 原模组的「遗传性禁欲」开启 | 无性别、无性恋，原版行为 | 生育力为零，代谢 +1 |
| 两个开关都关闭 | 普通性别与性取向 | 绝育，代谢 +1 |

首次加载时如果两个开关均开启，本补丁优先；之后手动开启其中一个会关闭另一个。开局选人前已更新墨诺灵异种，新生成的明确墨诺灵请求会在取名与分配背景前指定女性（不覆盖显式指定的性别）；基因负责后续确定性别与性取向，也适用于既有小人和洗礼。关闭后会恢复记录过的原性别、移除由基因强制的同性恋特性，并解除原性取向的压制。已经在女性模式下生成的小人没有可恢复的先前男性性别；不会重摇性别、名字、背景或恢复已清除的胡子与男性体型。

**游戏内最小浅测：** 启用补丁后新建墨诺灵开局，在选人页点开一个墨诺灵的小人信息，确认标记为女性、身上有「同性恋」特性和「墨诺灵姊妹之契」基因。若要卸载，先关闭本开关，读入受影响的存档并另存，使存档先移除本补丁的基因引用，再停用补丁。

## English

This RimWorld 1.7 sidecar adds a single shared switch, on by default, in **Monolyn → General** and **Monolyn: All Female & Gay**. It requires Harmony, Biotech and Monolyn Race, and loads after Monolyn and the optional Monolyn Same Pawn Baptism mod.

- **Sidecar on:** Monolyn are female and gay. The hereditary abstinence romance penalty is gone; sterility and +1 metabolism remain.
- **Stock abstinence on:** Monolyn are genderless and asexual as in the original mod, with zero fertility and +1 metabolism.
- **Both off:** Ordinary gender and orientation; still sterile, with +1 metabolism.

The sidecar wins a first-launch tie, but subsequently the switch you explicitly turn on wins. The template is updated before starting pawn selection, and explicit generation genders are respected by the request hook; the gene still applies its effect when added. Turning the mode off restores recorded gender and removes only gene-sourced traits. It does not reroll names, backstories or gender at birth, or restore trimmed beards and normalized body types.

**Quick smoke test:** Start a new Monolyn game and inspect one Monolyn on the starting-pawn screen for female gender, Gay, and Monolyn sisterhood. Before uninstalling, turn the feature off, load each affected save and save again under a new name to remove references to the custom gene.

## 构建与验证 / Build and verification

From this mod directory (PowerShell, .NET 10 SDK):

```powershell
dotnet build Source/MonolynFemaleGay.csproj -c Release -warnaserror
./Validation/validate.ps1
```

路径可在脚本中用 `-GameRoot`、`-WorkshopRoot`、`-HarmonyPath`、`-AselPath` 覆盖；直接构建则用对应的 `-p:` MSBuild 参数。构建只将发布 DLL 复制到本补丁的 `1.7/Assemblies/`。

**离线结果 / Offline result:** Release 0 warnings, 0 errors; **122 checks passed**. RED verifies both untouched upstream modes and the unchanged stock generation request. GREEN checks actual Harmony registration, package and localized Def data, startup and three-mode transitions, both settings entry points with isolated writes, Scribe round trips, native gene and trait add/remove behavior, starting-pawn correction, both baptism gene-ingestion patterns, generation request filtering and fail-closed upstream contract drift.

验证使用真实 1.7 `Assembly-CSharp.dll`（内存中绕开平台启动）、真实 `0Harmony.dll`、`ASEL.dll` 和发布目录中本补丁 DLL。使用最小 pawn/Def fixtures，隔离 UI、渲染和存储边界；未启动游戏。洗礼验证覆盖 `SetXenotype` 和按模板逐项加基因的入口，不是完整洗礼仪式；生成验证拦截真实 Harmony 入口的请求，不会运行整套人物生成或验收画面。

## 维护基线 / Maintenance baseline

- RimWorld `1.7.5014 rev1195`, `Assembly-CSharp.dll` SHA-256 `11D7C9FAF52BE22933DA840D906B1AF7B48F7EA574A886C045B1FB12BD366124`.
- [Monolyn Race, Workshop 3742031864](https://steamcommunity.com/sharedfiles/filedetails/?id=3742031864), `1.7/Assemblies/ASEL.dll`, last modified **2026-11-23 19:36:42 +08:00**, SHA-256 `B0F7354D7C5D9745C25AEA804C08A1840A9D0F9AC27F54C45B43766A058998B5`.
- Release DLL SHA-256 `C9ED0DDA6A9DF3A9DAAD15B2A0A32EF9E9ED1FD01BC7212CB0FF2E60A2667BB4`.

上游更新时间或哈希变动后重跑验证，并检查 `ASEL.Monolyn.ApplyGenderPatch()`、`DrawFirstTabSettings(Listing_Standard)`、`Abstinence` 及各个 DefOf，尤其注意三态切换顺序和洗礼是否仍读取 `MonolynXenotype.genes`。找不到契约时，游戏日志会输出 `[whitegiver.monolyn.femalegay] Cannot install Monolyn integration`，补丁不会启用。若原模组提供等价三态选项或不再使用该异种基因模板，应先按上述步骤清理存档，再评估退役。

After upstream updates, rerun validation and check the same methods, DefOfs, transition order and baptism template readers. A contract mismatch logs the prefix above and leaves the integration disabled. Retire the sidecar if upstream implements the same three modes or moves away from the patched template; remove the custom gene from saves first.
