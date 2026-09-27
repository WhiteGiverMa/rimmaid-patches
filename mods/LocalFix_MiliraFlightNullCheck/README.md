# Local Fix - Milira Flight NullCheck

RimWorld **1.6** 本地独立补丁，packageId：`local.meidocho.MiliraFlightNullCheckFix`。

修复 Milira Race 在 `Pawn_FlightTracker.Notify_JobStarted` 上的原生 Harmony 前缀对
`CompFlightControl` 空引用崩溃（NRE）的问题。补丁自注册（无需其他代码调用 `PatchAll`），
且只拦截会崩溃的那一种情况；其余路径完全交还 Milira 自己处理。

## 失败链路（已离线复现）

Milira 1.6（工坊 3256974620）反编译后的前缀：

```csharp
[HarmonyPatch(typeof(Pawn_FlightTracker))]
[HarmonyPatch("Notify_JobStarted")]
public static class MilianPatch_Pawn_FlightTracker_Notify_JobStarted
{
    [HarmonyPrefix]
    public static bool Prefix(Job job, Pawn_FlightTracker __instance, Pawn ___pawn)
    {
        if (/* ___pawn 的 body defName == "Milira_Body" */)
        {
            CompFlightControl compFlightControl = ThingCompUtility.TryGetComp<CompFlightControl>(___pawn);
            if (__instance.CanEverFly && compFlightControl.CanFly && (___pawn.Drafted || ___pawn.mindState.enemyTarget != null))
            {
                // 起飞 + Hediff
                return false;
            }
            // 落地回退
            return false;
        }
        return true;
    }
}
```

- `TryGetComp` 可能是 **null**：Milira body 的 pawn 并不保证带 `CompFlightControl`。
- `if` 中 `__instance.CanEverFly` 为 true 时会直接读 `compFlightControl.CanFly` →
  `NullReferenceException`。`CanEverFly` 为 false 时被短路，不崩。
- 触发条件：Milira body pawn、无飞行控制组件、`CanEverFly == true`，且在 job 开始
  （或 `CompFlightControl.CompTick` / 飞行的 FloatMenu 开关）调用到该方法时。
- 旧版本补丁 DLL 带有 `[HarmonyPatch]` 类，但**没有任何注册**（无 `Mod` 子类、无
  `[StaticConstructorOnStartup]`、无 `PatchAll`），从未生效。诊断驱动 `inspect` 对旧
  DLL 输出：`exactly one [StaticConstructorOnStartup] bootstrap type (found 0)` → RED。

## 这个补丁做了什么

1. **自注册**：`Bootstrap` 带 `[StaticConstructorOnStartup]`，由
   `Verse.StaticConstructorOnStartupUtility.CallAll()` 在启动时执行一次静态构造函数，
   调用 `new Harmony("local.meidocho.MiliraFlightNullCheckFix").PatchAll(...)`。
   `[HarmonyPriority]` 只影响**已注册**补丁之间的顺序；它本身不会注册补丁——所以注册
   必须显式发生。
2. **顺序**：前缀带 `[HarmonyPriority(Priority.HigherThanNormal)]`（500），排在 Milira
   原生前缀（默认 Normal=400）之前；即使 Milira 先注册，执行顺序仍由优先级决定。
3. **最小拦截**：只有「Milira body + `TryGetComp` 为 null + `CanEverFly == true`」这一
   会崩溃的分支被拦截，执行与上游失败分支相同的落地回退
   （`job.flying = false`；飞行中则 `ForceLand()`）并返回 `false`。
   - `CompFlightControl != null` → 返回 `true`，Milira 前缀原样处理（起飞判断不变）。
   - 非 Milira body → 返回 `true`，任何非 Milira 路径不受影响。
   - `CanEverFly == false` → 返回 `true`，上游自己安全地走回退分支。
4. 不复制 Milira 的起飞/Hediff 逻辑，上游更新时只需要复核「崩溃分支是否还在」。

公开文档演示行为与实际 `1.6/Assemblies/LocalFix_MiliraFlightNullCheck.dll` 一致；
源码与发布 DLL 同批构建。

## 安装与兼容

1. 将本目录作为 `Mods/LocalFix_MiliraFlightNullCheck` 安装，确认
   `1.6/Assemblies/LocalFix_MiliraFlightNullCheck.dll` 存在。
2. 需要 Milira Race（`Ancot.MiliraRace`，工坊 3256974620）与 Harmony
   （`brrainz.harmony`，工坊 2009463077）；`About.xml` 已声明依赖与 `loadAfter`。
3. 首次安装后重启游戏。补丁在启动时自行注册，无设置项、无存档结构改动。

本补丁不改 Milira 工坊文件，不改非 Milira pawn 的任何行为，不新增 Def，不写存档。

## 构建与验证

```text
dotnet build Source/LocalFix_MiliraFlight.csproj -c Release
pwsh -File Validation/validate.ps1
```

- `validate.ps1` 默认检查打包的 `1.6/Assemblies/LocalFix_MiliraFlightNullCheck.dll`
  （可用 `-PatchDll` 指向 `Source/bin/...` 刚刚构建的 DLL，`-NoBuild` 跳过驱动构建）。
- 驱动在一次性 .NET Framework 进程中加载**真实的** `Assembly-CSharp.dll`、
  `0Harmony.dll`、`Milira.dll`，用与游戏相同的 `PatchClassProcessor`/静态构造函数路径
  注册补丁；不启动游戏、不写游戏目录。
- 三种模式：
  - `inspect`：`[StaticConstructorOnStartup]` + `[HarmonyPatch]` 恰好各一，判定自注册。
  - `red`：只注册 Milira 前缀，调用真实的 `Pawn_FlightTracker.Notify_JobStarted`，
    断言抛出 NRE 且栈在 `MilianPatch_Pawn_FlightTracker_Notify_JobStarted.Prefix`。
  - `green`：先注册 Milira（最坏注册顺序），再执行本 DLL 的 Bootstrap；断言
    注册恰好一次、优先级 500/400、只补到目标方法、重复执行 cctor 不重复注册、
    端到端调用不抛异常且 `job.flying == false`，另直接调用前缀验证三条分支
    （非 Milira → true；有组件 → true；无组件可飞 → false + 落地）。
- 为离线触达崩溃分支，驱动临时把 `Pawn_FlightTracker.CanEverFly` 的 getter 打桩为
  `true`（该方法依赖游戏内 `DefDatabase`/`StatDefId` 状态）；打桩只作用于驱动进程，
  不影响补丁 DLL。
- 已验证（本机，2026-09-27，PatchDll SHA-256
  `acc008f8c2c4794d8b91c323a05a228f15618181f477bdd12df7217c315957ff`）：
  `inspect`/`red`/`green` 全部 PASS；上游 NRE 复现于真实 Milira 代码；绿色链路含
  端到端调用。发布 DLL 与 `Source/bin` 构建产物哈希一致。

**未验证**：未实际启动 RimWorld、未进入存档、未走完整 mod 列表与真实 Milira 生成链路。
离线驱动绕过 Unity 内容系统（只构造 pawn/ThingDef/Job 外壳），不构成游戏内通过。
浅测建议：用带 Milira 的 mod 列表启动一次，检查开发者日志无装配/补丁错误；正常游玩
中崩溃本身只在「无飞行组件的 Milira body」出现，不易特意构造。

## 更新监测与退役

- 本补丁是独立本地 Mod，不会被 Steam 更新覆盖。发布时除更新
  `1.6/Assemblies/LocalFix_MiliraFlightNullCheck.dll` 外，还需要同步根目录
  `checksums.sha256`（本次发布仅更新了 DLL，checksum 由仓库维护者同步）。
- 上游：Milira Race 工坊 3256974620；本次核对 `1.6/Assemblies/Milira.dll`
  SHA-256 `371891b8bbe05b68fff7d6dcaed74a183d39ed6fd5d24c36a0bd9eaf4e4bd0e1`，
  Harmony 2.4.1（工坊 2009463077）。Milira 的 About.xml 没有语义版本号。
- 退役信号（任一成立即可停用）：
  1. Milira 自行给 `compFlightControl` 加了 null 检查（可反编译上述类核对）；
     此时 `validate.ps1` 的 `red` 模式会因「不再抛 NRE」而 FAIL——这本身就是上游已修复
     的信号。
  2. 目标方法/类改名（如 `Pawn_FlightTracker.Notify_JobStarted`、
     `Milira.CompFlightControl`、body defName `Milira_Body`）：`green` 模式会在注册或
     端到端阶段 FAIL，需要先核对签名再决定是否更新补丁。
  3. Milira 更新后重新运行 `pwsh -File Validation/validate.ps1`；全 PASS 表示本补丁
     在当次版本上仍然成立。不要只看构建通过。

## English summary

Local, self-registering RimWorld 1.6 Harmony sidecar for Milira Race
(Workshop 3256974620). Milira's own prefix on `Pawn_FlightTracker.Notify_JobStarted`
dereferences `CompFlightControl.CanFly` without checking `TryGetComp` for null and
throws an NRE for Milira-body pawns without a flight-control comp. The previous local
DLL never registered itself (no `Mod` class, no `StaticConstructorOnStartup`, no
`PatchAll`), so it was dead code. This patch adds a
`[StaticConstructorOnStartup]` bootstrap with its own Harmony ID, keeps
`HigherThanNormal` priority so its prefix runs before Milira's, and intercepts only the
crashing case (Milira body, comp == null, `CanEverFly == true`) with the same landing
fallback Milira uses; every other call is returned to Milira untouched. Validation:
`Validation/validate.ps1` loads the real `Assembly-CSharp.dll`, `0Harmony.dll` and
`Milira.dll` in a throwaway .NET Framework process and runs `inspect` (self-registration),
`red` (upstream NRE reproduced) and `green` (exactly-once registration, priority order,
crash-free end-to-end call). No game launch or deployment is performed here.
