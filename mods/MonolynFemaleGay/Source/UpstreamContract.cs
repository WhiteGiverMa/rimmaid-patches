using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.MonolynFemaleGay;

internal sealed class UpstreamContract
{
    internal readonly Type ModType;
    internal readonly MethodInfo ApplyMethod, DrawMethod, GenerateMethod;
    internal readonly Action Apply;
    internal readonly XenotypeDef Xenotype;
    internal readonly GeneDef AbstinenceGene, SterileGene;
    internal readonly ThingDef Race;
    private readonly FieldInfo abstinenceFlag;

    private UpstreamContract(Type modType)
    {
        if (!typeof(Mod).IsAssignableFrom(modType))
            throw new InvalidOperationException($"{modType.FullName} no longer derives from {typeof(Mod).FullName}.");
        ModType = modType;
        ApplyMethod = RequireMethod(modType, "ApplyGenderPatch", true, typeof(void));
        DrawMethod = RequireMethod(modType, "DrawFirstTabSettings", false, typeof(void), typeof(Listing_Standard));
        GenerateMethod = RequireMethod(typeof(PawnGenerator), "GeneratePawn", true, typeof(Pawn), typeof(PawnGenerationRequest));
        Apply = ApplyMethod.CreateDelegate<Action>();

        Assembly assembly = modType.Assembly;
        abstinenceFlag = RequireField(RequireType(assembly, "ASEL.MonolynModSettings"), "Abstinence", typeof(bool), writable: true);
        Xenotype = RequireDef<XenotypeDef>(assembly, "ASEL.MNXenotypeDefOf", "MonolynXenotype");
        AbstinenceGene = RequireDef<GeneDef>(assembly, "ASEL.MNGeneDefOf", "HereditaryAbstinence");
        SterileGene = RequireDef<GeneDef>(assembly, "ASEL.MNGeneDefOf", "Sterile");
        Race = RequireDef<ThingDef>(assembly, "ASEL.MNThingDefOf", "MNRace");
        if (Xenotype.genes == null)
            throw new InvalidOperationException("ASEL.MNXenotypeDefOf.MonolynXenotype.genes was not initialized.");
    }

    internal bool Abstinence
    {
        get => (bool)abstinenceFlag.GetValue(null)!;
        set => abstinenceFlag.SetValue(null, value);
    }

    internal static UpstreamContract Bind() => new(AccessTools.TypeByName("ASEL.Monolyn")
        ?? throw new InvalidOperationException("Missing upstream type ASEL.Monolyn."));

    internal static MethodInfo RequireMethod(Type type, string name, bool isStatic, Type returns, params Type[] args)
    {
        MethodInfo? method = type.GetMethod(name,
            BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
            binder: null, types: args, modifiers: null);
        if (method == null || method.IsStatic != isStatic || method.ReturnType != returns || method.IsGenericMethod || method.IsAbstract
            || !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(args))
            throw new InvalidOperationException($"Unexpected signature for {type.FullName}.{name}.");
        return method;
    }

    internal static FieldInfo RequireField(Type type, string name, Type fieldType, bool writable = false)
    {
        FieldInfo? field = type.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static);
        if (field == null || field.FieldType != fieldType || (writable && (field.IsInitOnly || field.IsLiteral)))
            throw new InvalidOperationException($"Expected {(writable ? "writable " : "")}static {fieldType.Name} field {type.FullName}.{name}.");
        return field;
    }

    private static Type RequireType(Assembly assembly, string name) => assembly.GetType(name)
        ?? throw new InvalidOperationException($"Missing upstream type {name}.");

    private static T RequireDef<T>(Assembly assembly, string owner, string name) where T : Def
        => RequireField(RequireType(assembly, owner), name, typeof(T)).GetValue(null) is T def && def.defName == name
            ? def : throw new InvalidOperationException($"{owner}.{name} did not resolve to the expected Def.");
}
