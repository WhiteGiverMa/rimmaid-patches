# 给作者的反馈草稿

仅为草稿，尚未发布。以下不包含私人存档、人物信息或本机路径。

---

你好，反馈一个自动技能的空引用问题，辛苦啦。

环境：RimWorld 1.7.4990 rev1069（Waylith），Auto Ability，工坊 ID 3578731212。自动战斗中出现：

```text
Exception in Verse.AI.ThinkNode_Tagger TryIssueJobPackage: System.NullReferenceException
   at AutoAbility.AutoAbility_Utils.MyValidate(Ability ability_item, LocalTargetInfo thing,
        AutoCastManager autoCastManager, AutoCastSetting tmp) in AutoAbility_Utils.cs:line 137
   at AutoAbility.AutoAbility_Utils.AutoTryGiveJob(Pawn pawn, Boolean checkDrafted, Boolean checkDaily,
        Boolean checkReaction) in AutoAbility_Utils.cs:line 469
   at AutoAbility.JobGiver_AutoFightWithAbility.TryGiveJob(Pawn pawn) in AutoAbility_Patches.cs:line 77
```

反编译当前 DLL 后，line 137 对应“避免对友军使用范围技能”的末尾：

```csharp
if (tmp.autoPreventAlly && ability_item.def.HasAreaOfEffect)
{
    foreach (LocalTargetInfo item in ability_item.GetAffectedTargets(thing))
    {
        LocalTargetInfo current2 = item;
        if (current2.Pawn.IsColonist || current2.Pawn.IsColonyMech || current2.Pawn.IsColonyAnimal)
        {
            return false;
        }
    }
}
```

问题在于 `current2.Pawn` 可能为 `null`：范围技能在 `canUseAoeToGetTargets` 关闭时，`GetAffectedTargets` 会直接返回原目标；如果目标是物品、尸体、格子或其他非 Pawn 对象，`Pawn` 就是 `null`。`current2.Pawn.IsColonist` 没有空检查，便会抛空引用。

我已经用真实的 `MyValidate` 和真实 `GetAffectedTargets` 在离线环境复现了这条异常；尚未确认其他玩家环境的触发频率，也没有定位到具体是哪个范围技能最先触发。红字不会让游戏崩溃，但该技能在这次验证中会被跳过。

最小修复方向是给三处读取加空检查，例如：

```csharp
if (current2.Pawn != null &&
    (current2.Pawn.IsColonist || current2.Pawn.IsColonyMech || current2.Pawn.IsColonyAnimal))
```

或者只把非 Pawn 目标从“友军检查”里排除。谢谢维护这个 mod！
