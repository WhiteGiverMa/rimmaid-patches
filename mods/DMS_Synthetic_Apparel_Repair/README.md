# DMS Synthetic Apparel Repair：Legion 维修调用兼容

2026-10-13：当前 Legion `JobDriver_MechRepairMech.<MakeNewToils>b__3_2(int delta)` 实际调用 `MechRepairUtility.RepairTick(Pawn)`，旧补丁只寻找双参数重载，导致匹配失败并安全停用全部装备维修事务。

修复：优先匹配实际单参数调用，插入无delta的事务守卫，按一次维修/一次扣费处理；Legion对Milian目标继续使用Milira费率但每次仅处理1次。不把回调delta视为维修次数。旧双参数调用路径保留，供1.6旧DLL使用；不是按游戏版本猜测而是按实际调用签名分流。安全停用机制保留，不放宽匹配窗口、不吞异常。

验证：旧部署DLL在实际Legion IL/Harmony3驱动下安全闸门false、单参数守卫0，复现问题；新DLL驱动中实际注册成功、单参数守卫恰好1、安全闸门true。测试副本仅为标准dotnet复刻引擎Unity程序集重定向，发布DLL不改引用。Release 0 warnings/errors，LSP无错误。尚未游戏内验收耐久恢复、能量扣除或旧1.6双参数运行路径。

发布SHA256：`2E256B2F9D50FBCE7080CF2D9C829FD15ADAA59C826EFEBA855ED78F85F39093`。Legion/原版维修回调升级后必须重新核对签名、实际IL和守卫注册。若上游原生支持所需装备维修则评估退役，勿只隐藏安全警告。

浅测：正常重启读档，确认无Legion transaction changed红字，让已有Legion维修者维修一名受损装备机兵，观察装备耐久上升及正常能量变化。
