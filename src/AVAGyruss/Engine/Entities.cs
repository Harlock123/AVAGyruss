namespace AVAGyruss.Engine;

/// <summary>The stick (the ship sweeps round the rim towards the way it points) and the fire button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// Looking down the tunnel of space: the ship circles the rim at <see cref="Ring"/>, the far distance is
/// the centre. Things near the centre are small and far off; they grow as they come out towards the rim.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 64;
    public const double CX = Width / 2, CY = (Top + Height) / 2 + 6;
    /// <summary>The ship's circle.</summary>
    public const double Ring = 310;

    /// <summary>A point at an angle and a distance out from the centre.</summary>
    public static (double X, double Y) At(double a, double r) => (CX + Math.Cos(a) * r, CY + Math.Sin(a) * r);

    /// <summary>How big something looks at a distance out: tiny at the centre, full size at the rim.</summary>
    public static double Scale(double r) => 0.2 + 0.8 * Math.Clamp(r / Ring, 0, 1.3);

    /// <summary>The turn from one angle to another, between −π and π.</summary>
    public static double Turn(double from, double to)
    {
        double d = (to - from) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        if (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }
}

public sealed class Ship
{
    public double A = Math.PI / 2;
    public bool Alive = true;
    /// <summary>The double shot, from the satellite.</summary>
    public bool Double;
    public double Guard;
    public (double X, double Y) Pos => Field.At(A, Field.Ring);
}

/// <summary>A shot, an enemy's bullet or a meteor: flies in a straight line on the screen.</summary>
public sealed class Bolt
{
    public double X, Y, Vx, Vy, Age;
    public bool Dead;
}

public enum EnemyState { Entering, Holding, Diving, Returning, Leaving }

public enum FlightPath { SpiralIn, LoopOut, PassThrough }

/// <summary>A fighter: flies in on its group's path, holds its place in the formation, dives at the ship.</summary>
public sealed class Fighter
{
    public double A, R;
    public EnemyState State;
    public FlightPath Path;
    /// <summary>The path's start angle and which way it turns; the time since it set off (negative: not yet).</summary>
    public double A0;
    public int Spin;
    public double T;
    public int Group, Slot;
    public double FireIn;
    /// <summary>A dive's start and the angle it aims at.</summary>
    public double DiveA0, DiveR0, DiveTo;
    public int Kind;
    public bool Dead;
}

/// <summary>One of the satellites that sometimes gather at the centre: the bright one in the middle gives the double shot.</summary>
public sealed class Satellite
{
    public double A, R, Age;
    public bool Power, Dead;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
