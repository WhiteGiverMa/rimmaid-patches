# 沃芬黑科 1.7 公共网格污染修复

独立 sidecar，仅支持 RimWorld 1.7 / Waylith。需要 Harmony 与 Wolfein Black Science Expand，排在二者之后。无需修改工坊 DLL 或存档，正常重启游戏生效。

## 根因与修复

`OwlGreatswordAxisFlip` / `OwlGreatswordAxisFlipReversed` 是网格名，不是补丁类。BlackScience 的 `Patch_OwlGreatswordEquipmentAiming` 静态初始化调用 `CreateAxisFlippedMesh`，对 `Object.Instantiate<Mesh>(MeshPool.plane10/plane10Flip)` 返回值交换 UV 并改名。

当前 Waylith 的 `InstantiateObject` 对没有克隆工厂的运行期对象返回原对象。这让模组直接改写公共平面；`Building_Door.DrawMovers` 恰好使用这两个平面，其他引用同一网格的实时绘制也受影响。这不是全局 Quaternion 轴向变化，也不是巨剑角度补偿需要移除。

本补丁在 `Mod` 构造阶段，只替换上述私有工厂中的一个 `Instantiate<Mesh>` 调用为 `new Mesh` 并复制顶点、法线、UV、三角形。当前 UnityStubs 的 getter/setter 提供数组副本，因此不共享数组。原方法继续处理 UV 和名字，巨剑角度、后坐力、材质、绘制逻辑全部保留。不全局修改 Instantiate，不改门绘制，不增加事后静态 readonly 字段替换。

补丁必须在原类型静态初始化前注册。游戏的 `LoadedModManager.LoadAllActiveMods` 先创建 Mod 实例，`PlayDataLoader` 后续才执行 `StaticConstructorOnStartupUtility.CallAll`；因此用 Mod 构造而不是排在原模组之后的静态初始化来注册。

## 验证与边界

2026-10-14：Release 构建 0 警告 / 0 错误，C# LSP 无诊断。真实 BlackScience DLL（仅在临时 QA 副本重定向旧 Unity 程序集引用，部署原件不变）、真实游戏 MeshPool/UnityStubs/Harmony 3 驱动：

* 修前两个原静态字段均与公共网格同引用，公共 UV 被转置、名字被覆盖。
* 修后真实 Harmony 注册及原静态初始化成功，两个巨剑副本独立，公共 UV/名字保持不变，巨剑转置 UV、顶点、法线、三角形一致。
* 原 `ResolveHeldDrawTransform` 的 12 个角度及分支边界验证通过。
* 真实 `UnityWorld.InstantiateObject` 的无工厂路径确认返回原网格。

未启动游戏，未做门动画/实际巨剑贴图的视觉验收。正常重启后读档，查看一扇原先异常的门是否恢复；正常游玩时观察巨剑朝向。本补丁不处理该模组缺 Vulkan shader variant / material 为 null 的独立问题。

## 构建、退役、回退

`dotnet build Source/WolfeinBlackScienceMeshCloneFix.csproj -c Release`。可覆盖 `GameRoot` 和 `HarmonyPath`，构建不自动部署。安装包仅需 About 与 Assemblies。

核验的上游 BlackScience.dll SHA256：`E5D9DD125D74183ECC48A79F03B1518CEE21A4AF13E59EE098CD53FBE20A2B50`，工坊 ID 3717539116，2026-09-06 版本。游戏 Assembly-CSharp SHA256：`3D4F1DDFE71BD8760CB488255E3BDEAC9366F537DC1EF813CC6F8D09824E2DA9`。

Steam 不会覆盖独立补丁，但上游或引擎更新后应重新核验 DLL hash、工厂签名、单一克隆调用与输入仍为单子网格平面。匹配不是恰好一次会明确报错，不能把启动日志视为全面兼容证明。作者改为真正复制网格，或引擎补齐 Mesh 克隆后，禁用本补丁并复核门与巨剑，即可退役。回退只需禁用独立补丁并重启，不必改存档。

## 给作者的反馈草稿

你好，核查了 1.7 的门贴图反馈：Waylith 对运行期 Mesh 调用 Object.Instantiate 时，在没有 clone factory 的路径会返回原对象。Owl 的 CreateAxisFlippedMesh 因此会直接交换 MeshPool.plane10 / plane10Flip 的 UV，门也使用这两个网格。进程外使用实际程序集已复现别名与 UV 污染；改成 new Mesh 并复制 vertices/normals/uv/triangles 后，共享平面不再被改动，原巨剑 UV 与角度逻辑可以保留。游戏内视觉仍待确认。希望这个定位能帮到 1.7 适配。
