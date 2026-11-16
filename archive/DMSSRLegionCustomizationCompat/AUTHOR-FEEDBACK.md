# 给《死手开关：合成体》作者的兼容反馈 / Feedback for the Synthriada author

> 这份反馈只讨论“机兵参数调节”窗口对《死手开关：军团》机兵的兼容性。
> This report is only about the mech customization window's compatibility with DMS Legion.
> 不包含性能、寻路、属性计算或平衡性话题。
> It contains no performance, pathing, stat-calculation, or balance topics.

---

## 中文版（可直接发送）

**主题：`MechCustomizationUtility.IsSupportedPawn` 未接受 `DMSL_Mech_*`，军团机兵无法打开“机兵参数调节”**

你好，

我在 RimWorld 1.7 下同时使用：

- The Dead Man's Switch - Synthriada（`Cephalopod.DeadManSwitch.Synthriada`，Workshop `3835512735`）
- The Dead Man's Switch - Legion（`Srxf.DeadManSwitch.Legion`，Workshop `3666117000`）

遇到一个只影响军团机兵的自定义窗口问题，想反馈给你。

### 现象

对 `DMSL_Mech_leopard`（豹）、`DMSL_Mech_AgriculturalFrame`（农业框架）、`DMSL_Mech_ZBZ59D`、`DMSL_Mech_Terra`（尘世）等军团机兵打开“改装”时，只出现 Fortified 的普通 `Window_MechModification`；没有你做的五条参数滑条、上限刻度、预览上限和协议模拟面板。相同 Mod 列表下，`DMS_Mech_*`（例如 `DMS_Mech_Krepost`）和 `Cinder_Mech_*` 的窗口是完整的。

### 根因

在 1.7 的 `DMSSR.dll` 中，`DMSSR.UI.MechCustomizationUtility.IsSupportedPawn(Pawn)`（反编译约 4815–4822 行）是：

```csharp
private static bool IsSupportedPawn(Pawn pawn)
{
    string text = pawn?.def?.defName ?? string.Empty;
    if (!text.StartsWith("DMS_Mech_") && !text.Contains("DMS_Mech"))
        return text.StartsWith("Cinder_Mech_");
    return true;
}
```

军团 20 台机兵的 defName 全部是 `DMSL_Mech_*`：

- 不满足 `StartsWith("DMS_Mech_")`；
- 也不包含连续子串 `"DMS_Mech"`（实际是 `DMSL_Mech`，中间多一个 `L`）；
- 不满足 `StartsWith("Cinder_Mech_")`。

因此 `IsSupportedPawn` 返回 `false`，`CreateWindowFor` 返回 `null`，Fortified 的窗口工厂链回退到普通 `Window_MechModification`。这不是参数上限或身体结构的问题：我用真实 `DMSSR.dll` + `Fortified.dll` + `DMS_Legion.dll` 做了离线验证，`GetOrCreateCustomizationComp` 和 `MechCustomizationCapUtility.GetParamCaps` 在军团身体上本来就能正常工作。

### 建议的最小修改

**1）让 `IsSupportedPawn` 接受 `DMSL_Mech_`：**

```csharp
private static bool IsSupportedPawn(Pawn pawn)
{
    string text = pawn?.def?.defName ?? string.Empty;
    if (text.StartsWith("DMS_Mech_")
        || text.Contains("DMS_Mech")
        || text.StartsWith("DMSL_Mech_"))
    {
        return true;
    }

    return text.StartsWith("Cinder_Mech_");
}
```

**2）让“微型电子学基础完成时给已生成机兵补 `DMS_MechCustomization`”的扫描也覆盖 `DMSL_Mech_`：**

`DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs()`（反编译约 37234–37278 行）目前只判断 `text.StartsWith("DMS_Mech_")`。建议改成：

```csharp
if (!string.IsNullOrEmpty(text)
    && (text.StartsWith("DMS_Mech_") || text.StartsWith("DMSL_Mech_")))
{
    // existing HasHediff / MakeHediff / AddHediff logic
}
```

这样已经生成的军团机兵在 MicroelectronicsBasics 完成时会得到与 `DMS_Mech_*` 相同的初始 hediff，而不是等到第一次打开窗口才创建。

**3）（可选，更通用的设计）** 如果你不想继续维护硬编码前缀，可以考虑增加一个公开的 `DefModExtension`（例如 `DefModExt_MechCustomizationSupported`），让 `IsSupportedPawn` 同时检查 `pawn.def.HasModExtension<DefModExt_MechCustomizationSupported>()`。这样军团（以及其他机兵 Mod）可以在自己的 XML 里显式声明支持，而不需要你再改 C#。上面的前缀修改仍然是最小、最直接的修复。

### 军团 1.7 的 20 个 defName

```
DMSL_Mech_AgriculturalFrame
DMSL_Mech_ArmoredGrenadiers
DMSL_Mech_Arsonist
DMSL_Mech_Commander
DMSL_Mech_Commandos
DMSL_Mech_DesignatedMarksman
DMSL_Mech_DoctorFrame
DMSL_Mech_DrillingBarge
DMSL_Mech_Engineer
DMSL_Mech_Guardian
DMSL_Mech_IronFace
DMSL_Mech_leopard
DMSL_Mech_panzer
DMSL_Mech_Scout
DMSL_Mech_Shaman
DMSL_Mech_SpecialPolice
DMSL_Mech_STGeorge
DMSL_Mech_SupportFrame
DMSL_Mech_Terra
DMSL_Mech_ZBZ59D
```

另有 `DMSL_Drone_CommandingBird`（指挥鸟无人机）。我**不建议**把它纳入参数调节：你的上游同样没有把 `DMS_Drone_*` 纳入 `IsSupportedPawn`，而且无人机没有可调的底盘参数。我的兼容补丁也刻意只匹配 `DMSL_Mech_`。

### 身体结构 / 上限

我用真实 XML 解析了军团使用的 8 种 BodyDef（`DMS_Bipedal`、`DMS_Humanoid_TypeA/B`、`DMSL_MechanicsCavalryman_TypeA`、`DMSL_Body_Terra`、`DMS_KillDozer`、`DMS_LandCrab`、`DMS_Chop`），并在离线夹具里打开了 20/20 台机兵的 `Window_MechCustomization`。`GetParamCaps` 能正确读取 `MechanicalLeg` 的 `MovingLimbCore`、`DMS_Battery` 的 `BloodPumpingSource`、`ArtificialBrain` 的 `ConsciousnessSource` 等标签；例如在豹身上安装 `DMS_SyntheticTendon`（`MechanicalLeg`）、`DMS_ReinforcedFrame`（`DMS_HeavyChassis`）、`DMS_NuclearBattery`（`DMS_Battery`）后，上限为 supply 1.75 / compute 1.30 / electro 1.30 / drive 1.55 / subsystem 1.30，`SetParams` 与 Scribe 往返都正常。

### 一个需要你判断的设计问题：`DMSL_Mech_Terra`

`DMSL_Mech_Terra` 使用 `DMSL_Body_Terra`，这是一个为“能穿大部分人类装备”而设计的普通人类形身体（`Torso` / `Arm` / `Leg` / `Head` / `Eye` 等），不是 `DMS_Synthroid` 那种湿件合成体（`DMS_SynthicArm` / `DMS_SynthicLeg` / `SightSensor` / `HearingSensor`）。

我**没有**把 `DMSL_Body_Terra` 加进 `SyntheticBionicUtility.SupportedBodyDefNames`，因为：

- 合成体植入配方和别名表是按 `DMS_Synthroid` 的部件结构写的；
- Terra 的普通四肢被替换成合成体 added-part 后，可能影响它的装备/贴图/部件效率假设；
- 军团 XML 没有要求 Terra 走湿件合成体路线。

Terra 现在可以正常打开五参数面板、正常算上限，只是不会出现“合成体植入”子面板。如果你认为 Terra 在设定上应该算湿件合成体，请告诉我；那是你上游白名单的一行设计决定，我不希望在兼容补丁里替你静默决定。

### 复现环境

- RimWorld 1.7
- Synthriada `3835512735/1.7/Assemblies/DMSSR.dll`
- Legion `3666117000/1.7/Assemblies/DMS_Legion.dll`
- Fortified `3498575851/1.7/Assemblies/Fortified.dll`

如果你希望，我可以提供离线验证日志（RED：20/20 军团机兵 `IsSupportedPawn=false`、`CreateWindowFor=null`；GREEN：打上上述前缀补丁后 20/20 返回 `Window_MechCustomization`，参数上限、Scribe、协议队列均通过）。

谢谢！

---

## English version (ready to send)

**Subject: `MechCustomizationUtility.IsSupportedPawn` rejects `DMSL_Mech_*`, so Legion mechs cannot open the customization window**

Hello,

I am running RimWorld 1.7 with:

- The Dead Man's Switch - Synthriada (`Cephalopod.DeadManSwitch.Synthriada`, Workshop `3835512735`)
- The Dead Man's Switch - Legion (`Srxf.DeadManSwitch.Legion`, Workshop `3666117000`)

I hit a customization-window issue that only affects Legion mechs.

### Symptom

Opening the modification window for `DMSL_Mech_leopard`, `DMSL_Mech_AgriculturalFrame`, `DMSL_Mech_ZBZ59D`, `DMSL_Mech_Terra`, etc. shows only Fortified's plain `Window_MechModification`. Your five-parameter panel — the supply/computation/electro/drive/subsystem sliders, cap ticks, preview caps, and the protocol-simulation panel — is missing. With the same mod list, `DMS_Mech_*` (e.g. `DMS_Mech_Krepost`) and `Cinder_Mech_*` show the full window.

### Root cause

In the 1.7 `DMSSR.dll`, `DMSSR.UI.MechCustomizationUtility.IsSupportedPawn(Pawn)` (decompiled around lines 4815–4822) is:

```csharp
private static bool IsSupportedPawn(Pawn pawn)
{
    string text = pawn?.def?.defName ?? string.Empty;
    if (!text.StartsWith("DMS_Mech_") && !text.Contains("DMS_Mech"))
        return text.StartsWith("Cinder_Mech_");
    return true;
}
```

All 20 Legion mechs are named `DMSL_Mech_*`:

- `StartsWith("DMS_Mech_")` is false;
- `Contains("DMS_Mech")` is also false — the actual string is `DMSL_Mech`, which does not contain the contiguous substring `DMS_Mech`;
- `StartsWith("Cinder_Mech_")` is false.

So `IsSupportedPawn` returns `false`, `CreateWindowFor` returns `null`, and the Fortified factory chain falls back to the plain `Window_MechModification`. This is not a cap or body problem: I validated offline against the real `DMSSR.dll`, `Fortified.dll`, and `DMS_Legion.dll`; `GetOrCreateCustomizationComp` and `MechCustomizationCapUtility.GetParamCaps` already work on Legion bodies before any patch.

### Suggested minimal fix

**1) Accept `DMSL_Mech_` in `IsSupportedPawn`:**

```csharp
private static bool IsSupportedPawn(Pawn pawn)
{
    string text = pawn?.def?.defName ?? string.Empty;
    if (text.StartsWith("DMS_Mech_")
        || text.Contains("DMS_Mech")
        || text.StartsWith("DMSL_Mech_"))
    {
        return true;
    }

    return text.StartsWith("Cinder_Mech_");
}
```

**2) Include `DMSL_Mech_` in the MicroelectronicsBasics completion scan:**

`DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs()` (decompiled around lines 37234–37278) currently only tests `text.StartsWith("DMS_Mech_")`. Suggested:

```csharp
if (!string.IsNullOrEmpty(text)
    && (text.StartsWith("DMS_Mech_") || text.StartsWith("DMSL_Mech_")))
{
    // existing HasHediff / MakeHediff / AddHediff logic
}
```

This gives already-spawned Legion mechs the same initial hediff as `DMS_Mech_*` when MicroelectronicsBasics finishes, instead of waiting for the first window open.

**3) (Optional, more general)** If you would rather not keep extending hardcoded prefixes, consider a public `DefModExtension` such as `DefModExt_MechCustomizationSupported`, checked by `IsSupportedPawn` via `pawn.def.HasModExtension<DefModExt_MechCustomizationSupported>()`. Legion (and other mech mods) could then opt in from XML. The prefix change above is still the smallest direct fix.

### The 20 Legion 1.7 defNames

```
DMSL_Mech_AgriculturalFrame
DMSL_Mech_ArmoredGrenadiers
DMSL_Mech_Arsonist
DMSL_Mech_Commander
DMSL_Mech_Commandos
DMSL_Mech_DesignatedMarksman
DMSL_Mech_DoctorFrame
DMSL_Mech_DrillingBarge
DMSL_Mech_Engineer
DMSL_Mech_Guardian
DMSL_Mech_IronFace
DMSL_Mech_leopard
DMSL_Mech_panzer
DMSL_Mech_Scout
DMSL_Mech_Shaman
DMSL_Mech_SpecialPolice
DMSL_Mech_STGeorge
DMSL_Mech_SupportFrame
DMSL_Mech_Terra
DMSL_Mech_ZBZ59D
```

There is also `DMSL_Drone_CommandingBird`. I would **not** include it: upstream also excludes its own `DMS_Drone_*` family, and a drone has no chassis parameters to tune. My compatibility patch intentionally matches only `DMSL_Mech_`.

### Bodies and caps

I parsed the real XML for the 8 BodyDefs used by Legion (`DMS_Bipedal`, `DMS_Humanoid_TypeA/B`, `DMSL_MechanicsCavalryman_TypeA`, `DMSL_Body_Terra`, `DMS_KillDozer`, `DMS_LandCrab`, `DMS_Chop`) and opened `Window_MechCustomization` for 20/20 Legion mechs in an offline fixture. `GetParamCaps` reads the expected tags: `MovingLimbCore` on `MechanicalLeg`, `BloodPumpingSource` on `DMS_Battery`, `ConsciousnessSource` on `ArtificialBrain`, etc. With `DMS_SyntheticTendon` on a `MechanicalLeg`, `DMS_ReinforcedFrame` on `DMS_HeavyChassis`, and `DMS_NuclearBattery` on `DMS_Battery`, the leopard caps are supply 1.75 / compute 1.30 / electro 1.30 / drive 1.55 / subsystem 1.30; `SetParams` and a Scribe round-trip are exact.

### One design question for you: `DMSL_Mech_Terra`

`DMSL_Mech_Terra` uses `DMSL_Body_Terra`, a humanlike body (`Torso` / `Arm` / `Leg` / `Head` / `Eye`, etc.) built so Terra can wear most human apparel. It is not a wetware synthroid like `DMS_Synthroid` (`DMS_SynthicArm`, `DMS_SynthicLeg`, `SightSensor`, `HearingSensor`).

I deliberately did **not** add `DMSL_Body_Terra` to `SyntheticBionicUtility.SupportedBodyDefNames` because:

- the synthetic install recipes and alias table are written for the `DMS_Synthroid` part structure;
- replacing Terra's vanilla limbs with synthetic added-part hediffs could affect its apparel/graphic/part-efficiency assumptions;
- no Legion XML asks for Terra to use the wetware synthetic path.

Terra currently opens the five-parameter panel and gets correct caps; it simply has no synthetic-install sub-panel. If you consider Terra a wetware synthetic body by design, please say so — that is a one-line upstream whitelist decision, and I do not want the compatibility patch to make it silently.

### Reproduction environment

- RimWorld 1.7
- Synthriada `3835512735/1.7/Assemblies/DMSSR.dll`
- Legion `3666117000/1.7/Assemblies/DMS_Legion.dll`
- Fortified `3498575851/1.7/Assemblies/Fortified.dll`

I can provide the offline validation log if useful (RED: 20/20 Legion mechs `IsSupportedPawn=false`, `CreateWindowFor=null`; GREEN: after the prefix fix, 20/20 return `Window_MechCustomization`, with caps, Scribe, and protocol queue all passing).

Thank you!
