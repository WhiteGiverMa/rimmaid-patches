using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DMSSRUnifiedFix.Validation.StasisPodCorpse;

internal static class CarryAudit
{
    internal static void Run(string installedPath, string boundPath, string dmssrPath)
    {
        using AssemblyDefinition installed = AssemblyDefinition.ReadAssembly(installedPath);
        using AssemblyDefinition bound = AssemblyDefinition.ReadAssembly(boundPath);
        using AssemblyDefinition dmssr = AssemblyDefinition.ReadAssembly(dmssrPath);
        var boundMethods = Types(bound.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .GroupBy(m => m.FullName).ToDictionary(g => g.Key, g => new Queue<MethodDefinition>(g));
        int compared = 0;
        foreach (TypeDefinition type in Types(installed.MainModule.Types))
        foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
        {
            if (method.Name == ".cctor" && type.FullName is "Verse.ModsConfig" or "Verse.Prefs") continue;
            MethodDefinition candidate = boundMethods[method.FullName].Dequeue();
            if (Fingerprint(method) != Fingerprint(candidate)) throw new InvalidOperationException("Changed QA method: " + method.FullName);
            compared++;
        }
        Console.WriteLine($"AUDIT PASS {compared} real game method bodies unchanged (only ModsConfig/Prefs .cctor excluded).");
        int calls = 0;
        foreach (TypeDefinition type in Types(dmssr.MainModule.Types))
        foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
        {
            if (!method.Body.Instructions.Any(i => i.Operand is MethodReference { Name: "StartCarryThing", DeclaringType.FullName: "Verse.AI.Toils_Haul" })) continue;
            TypeDefinition driver = type.DeclaringType ?? type;
            bool countWrite = method.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference { Name: "count", DeclaringType.FullName: "Verse.AI.Job" });
            Console.WriteLine($"AUDIT CARRY {driver.FullName} / {method.Name}: count write in iterator={countWrite}");
            calls++;
        }
        if (calls != 5) throw new InvalidOperationException("StartCarryThing audit changed: " + calls);
        Console.WriteLine("AUDIT PASS all 5 DMSSR StartCarryThing drivers enumerated.");
    }

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition child in Types(type.NestedTypes)) yield return child;
        }
    }

    private static string Fingerprint(MethodDefinition method)
    {
        var indices = method.Body.Instructions.Select((value,index)=>(value,index)).ToDictionary(pair=>pair.value,pair=>pair.index);
        string Operand(object? value) => value switch
        {
            Instruction target => "@" + indices[target],
            Instruction[] targets => string.Join(",",targets.Select(target=>indices[target])),
            _ => value?.ToString() ?? "",
        };
        return string.Join("\n",method.Body.Instructions.Select(i=>i.OpCode+" "+Operand(i.Operand)))
            + string.Join("\n",method.Body.Variables.Select(v=>v.VariableType.FullName))
            + string.Join("\n",method.Body.ExceptionHandlers.Select(h=>$"{h.HandlerType}:{Operand(h.TryStart)}:{Operand(h.TryEnd)}:{Operand(h.HandlerStart)}:{Operand(h.HandlerEnd)}:{h.CatchType}"));
    }
}
