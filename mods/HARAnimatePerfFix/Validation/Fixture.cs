using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HARAnimate.Animation;
using HARAnimate.Examples;
using Verse;

namespace HARAnimatePerfFix.Validation;

/// <summary>
/// Minimal offline RimWorld 1.7 fixture: real <see cref="Pawn" />, real <see cref="ThingDef" />, real
/// HARAnimate animator stacks. No game loop, no defs, no map, no Unity scene.
/// </summary>
internal sealed class Fixture
{
    internal const string Race = "ValidationRace";
    internal const string OtherRace = "OtherValidationRace";
    internal const string PreExistingRace = "PreExistingRace";

    internal const string EarLabel = "Ear";
    internal const string RightEarLabel = "RightEar";
    internal const string TailLabel = "Tail";
    internal const string WingLabel = "Wing";

    internal ThingDef RaceDef = null!;
    internal ThingDef OtherRaceDef = null!;

    internal Pawn[] Pawns = Array.Empty<Pawn>();
    internal PawnDrawParms[] South = Array.Empty<PawnDrawParms>();
    internal PawnDrawParms[] East = Array.Empty<PawnDrawParms>();

    internal Pawn OtherPawn = null!;
    internal PawnDrawParms OtherSouth;
    internal PawnDrawParms OtherEast;

    internal ThingDef PreExistingRaceDef = null!;
    internal Pawn PreExistingPawn = null!;
    internal PawnDrawParms PreExistingSouth;

    internal AnimatorStack EarStack = null!;
    internal AnimatorStack RightEarStack = null!;
    internal AnimatorStack TailStack = null!;
    internal AnimatorStack WingStack = null!;

    internal static Fixture Create(int pawnCount)
    {
        Fixture fixture = new()
        {
            RaceDef = new ThingDef
            {
                defName = Race,
                category = ThingCategory.Pawn,
                thingClass = typeof(Pawn),
            },
            OtherRaceDef = new ThingDef
            {
                defName = OtherRace,
                category = ThingCategory.Pawn,
                thingClass = typeof(Pawn),
            },
            PreExistingRaceDef = new ThingDef
            {
                defName = PreExistingRace,
                category = ThingCategory.Pawn,
                thingClass = typeof(Pawn),
            },
        };

        fixture.Pawns = new Pawn[pawnCount];
        fixture.South = new PawnDrawParms[pawnCount];
        fixture.East = new PawnDrawParms[pawnCount];

        for (int i = 0; i < pawnCount; i++)
        {
            Pawn pawn = new() { def = fixture.RaceDef, thingIDNumber = 90001 + i };
            fixture.Pawns[i] = pawn;

            PawnDrawParms south = PawnDrawParms.Default;
            south.pawn = pawn;
            south.facing = Rot4.South;
            fixture.South[i] = south;

            PawnDrawParms east = PawnDrawParms.Default;
            east.pawn = pawn;
            east.facing = Rot4.East;
            fixture.East[i] = east;
        }

        fixture.OtherPawn = new Pawn { def = fixture.OtherRaceDef, thingIDNumber = 90001 + pawnCount };

        PawnDrawParms otherSouth = PawnDrawParms.Default;
        otherSouth.pawn = fixture.OtherPawn;
        otherSouth.facing = Rot4.South;
        fixture.OtherSouth = otherSouth;

        PawnDrawParms otherEast = PawnDrawParms.Default;
        otherEast.pawn = fixture.OtherPawn;
        otherEast.facing = Rot4.East;
        fixture.OtherEast = otherEast;

        fixture.PreExistingPawn = new Pawn { def = fixture.PreExistingRaceDef, thingIDNumber = 91000 };

        PawnDrawParms preExistingSouth = PawnDrawParms.Default;
        preExistingSouth.pawn = fixture.PreExistingPawn;
        preExistingSouth.facing = Rot4.South;
        fixture.PreExistingSouth = preExistingSouth;

        fixture.EarStack = BuildEarStack(rightFlip: false);
        fixture.RightEarStack = BuildEarStack(rightFlip: true);
        fixture.TailStack = BuildTailStack();
        fixture.WingStack = BuildWingStack();
        return fixture;
    }

    /// <summary>Registers the four stacks exactly the way HARAnimate's HarmonyInit does.</summary>
    internal void RegisterStacks()
    {
        AnimatorSystem.Register($"{Race}:{EarLabel}", EarStack);
        AnimatorSystem.Register($"{Race}:{RightEarLabel}", RightEarStack);
        AnimatorSystem.Register($"{Race}:{TailLabel}", TailStack);
        AnimatorSystem.Register($"{OtherRace}:{WingLabel}", WingStack);
    }

    internal static AnimatorStack BuildEarStack(bool rightFlip)
    {
        AnimatorStack stack = new() { rightFlip = rightFlip };

        // Two animators so stack composition (the '+' operator) is exercised too.
        stack.Animators.Add(new EarBreathingAnimator
        {
            velocity = 0.7f,
            offsetSizeZ = 0.2f,
            angleSize = 3f,
            scaleSizeZ = 0.1f,
        });

        stack.Animators.Add(new EarFlickAnimator
        {
            windowWidth = 128,
            duration = 60,
            cycles = 2,
            probability = 0.6f,
            offsetSizeZ = 0.05f,
            angleSize = 1.5f,
            scaleSizeZ = 0.02f,
        });

        return stack;
    }

    internal static AnimatorStack BuildTailStack()
    {
        AnimatorStack stack = new() { rightFlip = false };
        stack.Animators.Add(new TailFlickAnimator
        {
            windowWidth = 96,
            duration = 40,
            cycles = 3,
            probability = 0.5f,
            offsetSizeZ = 0.3f,
            angleSize = 2f,
            scaleSizeZ = 0.05f,
        });
        stack.Animators.Add(new TailBreathingAnimator
        {
            velocity = 0.4f,
            offsetSizeZ = 0.1f,
            angleSize = 1f,
            scaleSizeZ = 0.03f,
        });
        return stack;
    }

    internal static AnimatorStack BuildWingStack()
    {
        AnimatorStack stack = new() { rightFlip = true };
        stack.Animators.Add(new EarBreathingAnimator
        {
            velocity = 1.3f,
            offsetSizeX = 0.4f,
            offsetSizeZ = 0.4f,
            angleSize = 4f,
            scaleSizeZ = 0.2f,
        });
        return stack;
    }
}

/// <summary>
/// Bit-exact, Unity-type-free view of <see cref="BodyAddonTransform" /> (24 bytes: Vector3 Offset,
/// float Angle, Vector2 Scale). Used so that comparisons are exact float-bit comparisons rather than
/// formatted-string comparisons, and so the validator never depends on Unity vector helpers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RawTransform : IEquatable<RawTransform>
{
    public float OffsetX;
    public float OffsetY;
    public float OffsetZ;
    public float Angle;
    public float ScaleX;
    public float ScaleY;

    internal static void AssertLayout()
    {
        int size = Marshal.SizeOf<BodyAddonTransform>();
        if (size != 24 || Marshal.SizeOf<RawTransform>() != 24)
        {
            throw new InvalidOperationException(
                $"BodyAddonTransform is {size} bytes, RawTransform is {Marshal.SizeOf<RawTransform>()} bytes; the raw view is stale.");
        }
    }

    internal static RawTransform From(BodyAddonTransform value)
    {
        return Unsafe.As<BodyAddonTransform, RawTransform>(ref value);
    }

    public bool Equals(RawTransform other)
    {
        return OffsetX.Equals(other.OffsetX)
               && OffsetY.Equals(other.OffsetY)
               && OffsetZ.Equals(other.OffsetZ)
               && Angle.Equals(other.Angle)
               && ScaleX.Equals(other.ScaleX)
               && ScaleY.Equals(other.ScaleY);
    }

    public override bool Equals(object? obj)
    {
        return obj is RawTransform other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(OffsetX, OffsetY, OffsetZ, Angle, ScaleX, ScaleY);
    }

    public override string ToString()
    {
        return $"offset({OffsetX:R},{OffsetY:R},{OffsetZ:R}) angle({Angle:R}) scale({ScaleX:R},{ScaleY:R})";
    }
}
