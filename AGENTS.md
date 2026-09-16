# Rimmaid 补丁仓库约定

## 范围

- `mods/` 下的每个目录都是一个可独立加载的 RimWorld mod。绝不在目录之间合并 package ID 或加载顺序元数据。
- `upstream-patches/` 只存放 diff 和重建笔记；不要把完整的第三方仓库收录进来。
- `archive/` 不建议安装。`tools/` 可能是输出嘈杂的诊断工具，而不是玩法修复。

## 隐私

- 绝不提交存档、游戏日志、`ModsConfig.xml`、`Prefs.xml`、`.agents/`、`.omo/`，以及本地 STATUS/handoff 文件。
- 公开文档必须使用通用的 pawn/物品示例，不要使用私有存档中的名字或 ID。

## 构建与部署

- mod 带有可部署的发布 DLL 时要保留它；忽略 PDB、`bin/` 和 `obj/` 输出。
- 既有的 package ID 是持久的兼容性标识符，不要随意改名。
- 许多旧 csproj 文件里包含原作者的绝对 RimWorld 路径。除非有意把项目改造成可移植的，否则保留原行为；绝不要假设这些路径在别的机器上也存在。

## 补丁维护

- 为每个补丁记录触发它的依赖项，以及上游更新后的退役信号。
- 依赖更新后，重新构建之前先验证 XML 补丁目标和 Harmony 方法签名。
- 优先使用独立的 sidecar mod。对创意工坊 mod 的直接修改只应作为恢复说明放在 `upstream-patches/workshop-xml-edits/` 中。
