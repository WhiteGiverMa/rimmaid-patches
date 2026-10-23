using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace WhiteGiver.WolfeinBlackScienceMeshCloneFix;

public sealed class MeshCloneFix : Mod
{
    public MeshCloneFix(ModContentPack content) : base(content)
    {
        Apply();
        Log.Message("[BlackScience Mesh Clone Fix] Independent Owl meshes enabled.");
    }

    public static void Apply()
    {
        var target = AccessTools.Method("BlackScience.Patch_OwlGreatswordEquipmentAiming:CreateAxisFlippedMesh")
            ?? throw new MissingMethodException("BlackScience.Patch_OwlGreatswordEquipmentAiming.CreateAxisFlippedMesh");
        new Harmony("whitegiver.wolfeinblackscience.meshclonefix").Patch(target,
            transpiler: new HarmonyMethod(typeof(MeshCloneFix), nameof(ReplaceClone)));
    }

    public static IEnumerable<CodeInstruction> ReplaceClone(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method
                && method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate"
                && method.IsGenericMethod && method.GetGenericArguments().Length == 1
                && method.GetGenericArguments()[0] == typeof(Mesh) && method.GetParameters().Length == 1)
            {
                instruction.operand = AccessTools.Method(typeof(MeshCloneFix), nameof(CopyPlane));
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one Owl mesh clone call, found {replaced}; recheck BlackScience version.");
    }

    public static Mesh CopyPlane(Mesh source)
    {
        // ponytail: only Owl's single-submesh planes; re-audit if upstream changes the factory input.
        return new Mesh
        {
            vertices = source.vertices,
            normals = source.normals,
            uv = source.uv,
            triangles = source.triangles
        };
    }
}
