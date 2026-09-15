# 构建、打包与验证

[English](BUILDING.md)

本仓库包含 **17 个 `.csproj` 项目**，目标框架为 .NET Framework 4.8，在原 Windows 环境用 .NET SDK 10.0.401 验证构建。默认输出到各项目被 Git 忽略的 `bin/`，不会覆盖随仓库保存的 `Assemblies/` 原件，也不会默认部署到游戏。

## 准备引用

- 需要兼容的 .NET SDK、.NET Framework 4.8 引用程序集，以及项目声明的 NuGet 包。
- 游戏、Harmony、CE 和其他模组 DLL 由使用者自己提供，本仓库不打包这些第三方依赖。
- CEOverpenetration、CEEliteCombatTweaks、CEBreachingFix 可通过 `-p:CombatExtendedDll=外部DLL路径` 指定 CE；不再要求一个不存在的相邻 CombatExtended 目录。
- LocalFix_MiliraFlightNullCheck 可用 `-p:HarmonyDll=外部DLL路径` 指定 Harmony；默认值已修正为实际存在的 `Current/Assemblies/0Harmony.dll`。
- 部分项目支持 `GameRoot` / `WorkshopRoot`，其他旧项目仍有写死的 `HintPath`。**本次合仓没有把所有旧项目改造成跨机器通用构建。** 换机器时检查各项目实际使用哪些属性，其余绝对引用需要在本地调整。
- `Directory.Build.props.example` 是可选的本机配置示例。复制为被忽略的 `Directory.Build.props` 后，仅对使用相应属性的项目生效；项目内无条件赋值可能覆盖这些默认值。命令行 `-p:` 的优先级更高。

## 构建示例

```text
dotnet build mods/CEBreachingFix/Source/CEBreachingFix/CEBreachingFix.csproj -c Release -p:CombatExtendedDll=D:/ReferenceMods/CombatExtended.dll
```

保留 DLL 的 SHA-256 见 `checksums.sha256`。仅为验证编译，不要把 `bin/` 新输出覆盖到这些原件或游戏目录。

## 显式部署

BossgroupConcurrentSummons、DMSWorkTypeCacheFix、KeyzAllowUtilitiesFinishOffFix 保留旧部署目标，但必须传入 `-p:DeployLocal=true` 才会执行。开启前务必指定正确 `DeployRoot`；其他项目没有自动部署目标。

以下示例只写入显式指定的沙箱，**不是游戏目录**：

```text
dotnet build mods/BossgroupConcurrentSummons/1.6/Source/BossgroupConcurrentSummons.csproj -c Release -p:DeployLocal=true -p:DeployRoot=D:/PatchSandbox/BossgroupConcurrentSummons
```

发布新版时，再有意地更新对应 Mod 的 DLL、源码、说明和校验和。安装时复制单个完整 Mod 目录，不复制整个 monorepo，不盲目启用全部补丁。

## 验证范围

| 项目组 | 数量 | 构建结果 |
|---|---:|---|
| 模组与 WVC 恢复项目 | 14 | Release 通过 |
| CeleTech 回调驱动项目 | 1 | 构建通过，不能据此认定驱动运行通过 |
| ConduitRoomStatsFix、GizmoDiag 恢复项目 | 2 | Release 通过 |

两个更早的 Mod 只有零散 `.cs` 源文件、没有 `.csproj`，不计入这 17 个；纯 XML Mod 不需要 C# 编译。

WVC、ConduitRoomStatsFix、GizmoDiag 的源码由旧 DLL 恢复整理而来，能编译不等于与原始 DLL 字节或行为完全一致。CeleTech 驱动还保留原机的运行时查找路径，不能仅运行旧路径就声称验证了新克隆的 DLL。

在暂存完成的 Git 目录或新克隆中执行：

```text
pwsh -File tools/verify-repository.ps1
```

该检查验证包 ID、XML、私密/生成物排除规则和原始 DLL 校验和，不写入游戏。LSP 因工作区路径限制无法执行，使用实际编译、XML 和归档完整性检查替代。本次迁移没有启动游戏，不宣称通过游戏内验证。
