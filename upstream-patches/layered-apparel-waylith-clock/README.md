# Layered Apparel：Waylith 时间 API 修复

2026-10-13，适用 RimWorld 1.7.4976 rev1043。原 LayeredApparel.dll SHA256：`799A6473019C4991A3674905BD2653D3266FA806E1E4E9E1CCACB3865ED9A48A`。

UnityStubs 缺少 Time.unscaledTimeAsDouble。普通 Harmony transpiler 在读取原 IL 时先因缺失成员失败，无法运行替换步骤。改用离线 Mono.Cecil 重写，只将 double getter MemberRef 名改为 get_realtimeSinceStartupAsDouble，保留程序集引用、签名及其他方法 IL。

三处：CompLayeredApparel.UpdateVacuumStateCore（0.25秒节流）、Dialog_ApparelManagement.UpdateStatus（提示到期）、Apply后台回调（提示截止时间+3秒）。后两处必须用同一时钟。原API为帧采样时间，替换为单调实时时间，采样粒度略有区别；不改真空判断、衣物数据或存档。ExosuitPatch无此调用，未修改。

`dotnet run --project Repair.csproj -c Release -- patch <原DLL> <输出DLL>`。依赖本机已有Mono.Cecil 0.11.6，参考路径可按机器调整。工具要求恰好3处调用，写后逐方法对照，只有目标成员名变化。不要让输入输出同一路径。

验证：原DLL用测试专用UnityEngine→UnityStubs绑定后，真实UpdateVacuumStateCore JIT复现MissingMethodException；修正版3处真实方法均JIT成功，实际替代getter调用成功。qa-bind只为标准dotnet测试主机复刻引擎重定向，部署DLL不改程序集引用。最小驱动未初始化引擎，不代表完整GUI/真空外观已实测。

修后SHA256：`C93ECA2E9E9B96C0F81397CF319CC5D6B6C8910AD084F9E30AFCC8B6BB479397`。旧meidocho.layeredapparel.waylithtimefix保留历史、不启用。

Steam更新工坊3632480044可能覆盖DLL；hash变化后重新核对三处调用及引擎API，不盲目重打。作者消除该调用或引擎实现原getter后退役。恢复时关闭游戏后复制保存的原DLL，不启用失败sidecar。

浅测：正常启动读档，确认殖民者栏与管理窗口正常且无get_unscaledTimeAsDouble红字。真空环境切换需另行观察。
