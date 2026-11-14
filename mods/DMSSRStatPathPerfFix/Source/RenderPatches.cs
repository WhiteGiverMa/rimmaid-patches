using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR.Content;
using DMSSR.Mech;
using DMSSR.Patches;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    internal static class RenderPatches
    {
        private sealed class Extension<T>(T? value) where T : DefModExtension
        {
            internal readonly T? Value = value;
        }

        // Cache the extension OBJECT, not its fields: live scale/bank values remain live.
        // Negative entries use a holder because ConcurrentDictionary cannot store null values.
        private static readonly ConcurrentDictionary<ThingDef, Extension<ModExtension_WingedMech>> Winged = new();
        private static readonly ConcurrentDictionary<ThingDef, Extension<ModExtension_SyntheticPresentation>> Synthetic = new();
        internal static bool Installed { get; private set; }
        private static int wingedBuilds;
        private static int syntheticBuilds;
        internal static int WingedBuilds => System.Threading.Volatile.Read(ref wingedBuilds);
        internal static int SyntheticBuilds => System.Threading.Volatile.Read(ref syntheticBuilds);
        [ThreadStatic] private static ThingDef? lastWingedDef;
        [ThreadStatic] private static ModExtension_WingedMech? lastWinged;
        [ThreadStatic] private static ThingDef? lastSyntheticDef;
        [ThreadStatic] private static ModExtension_SyntheticPresentation? lastSynthetic;

        internal static void Apply(Harmony harmony)
        {
            foreach (Type type in new[] { typeof(Patch_EmbryteWeaver_UnfinishedDraw), typeof(Patch_EmbryteWeaver_UnfinishedPrint) })
                harmony.Patch(AccessTools.Method(type, "Prefix"),
                    transpiler: new HarmonyMethod(typeof(RenderPatches), nameof(UnfinishedGate)));
            Type hydra = typeof(ModExtension_WingedMech).Assembly.GetType("DMSSR.Mech.HydraFlightRendering", true)!;
            harmony.Patch(AccessTools.Method(hydra, "TryGetTransform"),
                transpiler: new HarmonyMethod(typeof(RenderPatches), nameof(CacheExtensionCalls)));
            harmony.Patch(AccessTools.Method(typeof(Patch_SyntheticPresentationScale), "Postfix"),
                transpiler: new HarmonyMethod(typeof(RenderPatches), nameof(CacheExtensionCalls)));
            Installed = true;
        }

        private static IEnumerable<CodeInstruction> UnfinishedGate(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> body = new(instructions);
            Label unfinished = generator.DefineLabel();
            body[0].labels.Add(unfinished);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Isinst, typeof(UnfinishedThing));
            yield return new CodeInstruction(OpCodes.Brtrue, unfinished);
            yield return new CodeInstruction(OpCodes.Ldc_I4_1);
            yield return new CodeInstruction(OpCodes.Ret);
            foreach (CodeInstruction instruction in body) yield return instruction;
        }

        private static IEnumerable<CodeInstruction> CacheExtensionCalls(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Def)
                    && method.Name == nameof(Def.GetModExtension) && method.IsGenericMethod)
                {
                    Type extension = method.GetGenericArguments()[0];
                    string? name = extension == typeof(ModExtension_WingedMech) ? nameof(GetWinged)
                        : extension == typeof(ModExtension_SyntheticPresentation) ? nameof(GetSynthetic) : null;
                    if (name != null)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(RenderPatches), name);
                        count++;
                    }
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one DMSSR render extension lookup, found " + count);
        }

        internal static ModExtension_WingedMech? GetWinged(Def def)
        {
            if (def.modExtensions == null || def.modExtensions.Count == 0) return null;
            if (def is not ThingDef thing) return def.GetModExtension<ModExtension_WingedMech>();
            if (ReferenceEquals(lastWingedDef, thing)) return lastWinged;
            ModExtension_WingedMech? value = Winged.GetOrAdd(thing, static key =>
            {
                System.Threading.Interlocked.Increment(ref wingedBuilds);
                return new Extension<ModExtension_WingedMech>(key.GetModExtension<ModExtension_WingedMech>());
            }).Value;
            lastWingedDef = thing;
            lastWinged = value;
            return value;
        }

        internal static ModExtension_SyntheticPresentation? GetSynthetic(Def def)
        {
            if (def.modExtensions == null || def.modExtensions.Count == 0) return null;
            if (def is not ThingDef thing) return def.GetModExtension<ModExtension_SyntheticPresentation>();
            if (ReferenceEquals(lastSyntheticDef, thing)) return lastSynthetic;
            ModExtension_SyntheticPresentation? value = Synthetic.GetOrAdd(thing, static key =>
            {
                System.Threading.Interlocked.Increment(ref syntheticBuilds);
                return new Extension<ModExtension_SyntheticPresentation>(key.GetModExtension<ModExtension_SyntheticPresentation>());
            }).Value;
            lastSyntheticDef = thing;
            lastSynthetic = value;
            return value;
        }
    }
}
