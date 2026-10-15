namespace AVAGyruss.Engine;

/// <summary>
/// The computer at the stick, for the demonstration (and the tests). It holds the trigger down, sweeps
/// round the rim to line up on what it wants to shoot (the power satellite first, then divers, then
/// whatever is nearest round the rim), and slips aside from bullets and meteors coming its way.
/// </summary>
public sealed class ShipPilot
{
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var s = g.Ship;
        if (!s.Alive || g.Phase is not (Phase.Play or Phase.StageStart)) return default;
        var (sx, sy) = s.Pos;

        // A bullet that will pass close: slip aside along the rim, away from its line.
        foreach (var b in g.Bullets)
        {
            double rx = b.X - sx, ry = b.Y - sy, v2 = b.Vx * b.Vx + b.Vy * b.Vy;
            double t = Math.Clamp(-(rx * b.Vx + ry * b.Vy) / Math.Max(1, v2), 0, 0.9);
            double mx = rx + b.Vx * t, my = ry + b.Vy * t;
            if (mx * mx + my * my > 30 * 30) continue;
            // Which way along the rim takes the ship off the bullet's line.
            double tx = -Math.Sin(s.A), ty = Math.Cos(s.A);
            int way = mx * tx + my * ty > 0 ? -1 : 1;
            Doing = "dodge";
            return Move(s.A, way, true);
        }
        foreach (var m in g.Meteors)
        {
            double turn = Field.Turn(m.A, s.A);
            if (m.R > Field.Ring * 0.35 && Math.Abs(turn) < 0.4) { Doing = "meteor"; return Move(s.A, turn >= 0 ? 1 : -1, true); }
        }

        // Something to shoot.
        double best = double.MaxValue, want = s.A;
        foreach (var sat in g.Satellites)
        {
            double cost = Math.Abs(Field.Turn(s.A, sat.A)) - (sat.Power ? 3 : 0);
            if (cost < best) { best = cost; want = sat.A; }
        }
        foreach (var f in g.Fighters)
        {
            if (f.Dead || f.R < 0) continue;
            // A diver coming close: get off its line if it's nearly on top, otherwise shoot it.
            if (f.State == EnemyState.Diving && f.R > Field.Ring - 70 && Math.Abs(Field.Turn(s.A, f.A)) < 0.3)
            {
                Doing = "duck the diver";
                return Move(s.A, Field.Turn(f.A, s.A) >= 0 ? 1 : -1, true);
            }
            double cost = Math.Abs(Field.Turn(s.A, f.A)) - (f.State == EnemyState.Diving ? 1.2 : 0) - f.R / 900;
            if (cost < best) { best = cost; want = f.A; }
        }
        double err = Field.Turn(s.A, want);
        Doing = "aim";
        return Math.Abs(err) < 0.03 ? new Controls(0, 0, true) : Move(s.A, Math.Sign(err), true);
    }

    /// <summary>Round the rim one way: the stick pointing a quarter turn on from the ship, in that direction.</summary>
    private static Controls Move(double a, int way, bool fire)
    {
        double toward = a + way * Math.PI / 2;
        int octant = ((int)Math.Round(toward / (Math.PI / 4)) % 8 + 8) % 8;
        (int X, int Y) d = octant switch { 0 => (1, 0), 1 => (1, 1), 2 => (0, 1), 3 => (-1, 1), 4 => (-1, 0), 5 => (-1, -1), 6 => (0, -1), _ => (1, -1) };
        return new Controls(d.X, d.Y, fire);
    }
}
