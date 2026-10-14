# Layered Apparel：Waylith 时间接口兼容补丁

适用：RimWorld 1.7.4976 / Layered Apparel 工坊 3632480044。

失败链：殖民者栏请求头像，Layered Apparel 的 `UpdateVacuumStateCore` 被 JIT 编译，调用当前 `UnityStubs.dll` 缺失的 `UnityEngine.Time.unscaledTimeAsDouble`，引发 `MissingMethodException`。即使当前不在真空地图，JIT 解析缺失成员仍可能先失败。

独立 Harmony transpiler 只替换该方法中的时间 getter 为现有的 `realtimeSinceStartupAsDouble`，保留 double 精度及 0.25 秒刷新阈值。该值是单调实时时钟，不是游戏 tick 时间；没有修改真空判断、衣物、外观数据或存档结构。原 API 是帧采样时间，替换为调用时实时采样，对此 UI 节流用途可接受。

构建：`dotnet build Source/LayeredApparelWaylithTimeFix.csproj -c Release`，可用 `GameRoot` / `HarmonyPath` 覆盖参考路径。发布 DLL 放入 `Assemblies/`。加载顺序：Harmony、Layered Apparel、本补丁。仅声明 1.7。

退役：上游 Layered Apparel 不再调用缺失属性，或 Waylith 实现该属性后可禁用。升级后需重新检查目标方法和实际 API；未匹配调用时 transpiler 保持原代码。Steam 更新不会覆盖独立补丁。不要把本补丁当作 Layered Apparel 全面兼容 1.7 的保证。

游戏内验证：重启后读取原档，查看殖民者栏是否正常显示且不再出现此 MissingMethodException。真实真空/室内切换的外观刷新需在对应地图另行观察。
> 已被 `upstream-patches/layered-apparel-waylith-clock` 的离线修复取代，仅保留历史，请勿启用。真实 Harmony 注册会在读取缺失 API 时失败；下面的早期验证记录不代表修复有效。保留 DLL 是原历史产物，不以本次重新构建替换。
