namespace AVAGyruss.Engine;

/// <summary>What a stage is: a warp on the way to a planet, the planet itself, or a chance stage.</summary>
public enum StageKind { Warp, Planet, Chance }

public sealed class Meteor
{
    public double A, R, Spin;
    public bool Dead;
}

public sealed partial class GameEngine
{
    public const double TurnRate = 3.4, ShotSpeed = 1000, FireDelay = 0.14, BulletSpeed = 270;
    public const int MaxShots = 4, GroupSize = 8;
    public const double StartSeconds = 2.4, DyingSeconds = 2.2, WarpSeconds = 2.2, LingerSeconds = 26;
    public const double EntrySeconds = 2.8, DiveSeconds = 1.9, HitRadius = 17;
    public const int GroupBonus = 1000, PerfectBonus = 10000;
    public const int FirstExtraLife = 30000, ExtraLifeEvery = 70000;

    /// <summary>The way to Earth: the planets passed, and how many warps to each.</summary>
    public static readonly (string Planet, int Warps)[] Route =
        [("NEPTUNE", 2), ("URANUS", 2), ("SATURN", 3), ("JUPITER", 3), ("MARS", 3), ("EARTH", 3)];

    public int Stage { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    public List<Bolt> Shots { get; } = [];
    public List<Bolt> Bullets { get; } = [];
    public List<Fighter> Fighters { get; } = [];
    public List<Satellite> Satellites { get; } = [];
    public List<Meteor> Meteors { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double StageTime { get; private set; }
    /// <summary>The formation's slow turn.</summary>
    public double FormationA { get; private set; }
    /// <summary>Fighters hit in a chance stage, out of how many.</summary>
    public int ChanceHits { get; private set; }
    /// <summary>For tests: nothing comes unless a test puts it there.</summary>
    internal bool Calm { get; set; }

    public (StageKind Kind, string Planet, int WarpsLeft, int Lap) Info => Describe(Stage);
    public double Pace => Math.Min(1.7, 1 + 0.04 * (Stage - 1));
    public int Groups => Info.Kind == StageKind.Warp ? 4 : 5;
    public int Total => Groups * GroupSize;

    private int _nextExtra;
    private double _fireWait, _diveIn, _meteorIn, _lingerFrom = -1;
    private bool _satellitesDone;
    private readonly int[] _groupLeft = new int[8];

    /// <summary>What the nth stage is: count along the route, each planet taking its warps, then itself, then a chance stage.</summary>
    public static (StageKind Kind, string Planet, int WarpsLeft, int Lap) Describe(int stage)
    {
        int n = stage - 1, lap = 1;
        while (true)
        {
            foreach (var (planet, warps) in Route)
            {
                if (n < warps) return (StageKind.Warp, planet, warps - n, lap);
                n -= warps;
                if (n == 0) return (StageKind.Planet, planet, 0, lap);
                n--;
                if (n == 0) return (StageKind.Chance, planet, 0, lap);
                n--;
            }
            lap++;
        }
    }

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Stage = 1;
        _nextExtra = FirstExtraLife;
        Ship.Double = false;
        LayOut();
        SetPhase(Phase.StageStart);
    }

    private void BeginStage(int stage)
    {
        Stage = stage;
        LayOut();
        SetPhase(Phase.StageStart);
        var (kind, planet, warps, _) = Info;
        switch (kind)
        {
            case StageKind.Warp: Say($"{warps} {(warps == 1 ? "WARP" : "WARPS")} TO {planet}", null, StartSeconds); break;
            case StageKind.Planet: Say(planet, "YOU HAVE ARRIVED", StartSeconds); break;
            default: Say("CHANCE STAGE", "SHOOT ALL FORTY FOR A BONUS", StartSeconds); break;
        }
        Play(kind == StageKind.Chance ? Sfx.Chance : Sfx.StageStart);
    }

    private void LayOut()
    {
        Fighters.Clear();
        Shots.Clear();
        Bullets.Clear();
        Satellites.Clear();
        Meteors.Clear();
        Effects.Clear();
        StageTime = 0;
        ChanceHits = 0;
        _diveIn = 4;
        _meteorIn = 6;
        _lingerFrom = -1;
        _satellitesDone = false;
        Array.Fill(_groupLeft, GroupSize);
        Ship.Alive = true;
        Ship.Guard = 0;
        if (Calm) return;
        // Every group of eight flies in on the same path, one after another.
        var rng = new Random(Stage * 41 + 9);
        bool chance = Info.Kind == StageKind.Chance;
        for (int gi = 0; gi < Groups; gi++)
        {
            var path = chance ? FlightPath.PassThrough : gi % 2 == 0 ? FlightPath.SpiralIn : FlightPath.LoopOut;
            double a0 = rng.NextDouble() * Math.PI * 2;
            int spin = rng.Next(2) == 0 ? -1 : 1;
            int kind = (gi + Stage) % 4;
            for (int k = 0; k < GroupSize; k++)
                Fighters.Add(new Fighter
                {
                    Path = path, A0 = a0, Spin = spin, Group = gi, Slot = gi * GroupSize + k, Kind = kind,
                    T = -(1.0 + gi * 3.3 + k * 0.17), R = -1, FireIn = 2 + rng.NextDouble() * 4,
                });
        }
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        FormationA += 0.15 * dt;
        switch (Phase)
        {
            case Phase.StageStart:
                SteerShip(c, dt);
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                StageTime += dt;
                SteerShip(c, dt);
                Fire(c, dt);
                MoveFighters(dt);
                Extras(dt);
                MoveBolts(dt);
                Hits();
                if (Phase == Phase.Play && !Calm && Fighters.Count > 0 && Fighters.All(f => f.Dead)) StageDone();
                break;
            case Phase.Dying:
                MoveFighters(dt);
                MoveBolts(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Ship.Alive = true;
                    Ship.Guard = 2;
                    Bullets.Clear();
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Warp:
                SteerShip(c, dt);
                if (PhaseTime > WarpSeconds) BeginStage(Stage + 1);
                break;
        }
        Shots.RemoveAll(s => s.Dead);
        Bullets.RemoveAll(b => b.Dead);
        Satellites.RemoveAll(s => s.Dead);
        Meteors.RemoveAll(m => m.Dead);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    /// <summary>The ship sweeps round the rim towards the way the stick points, the short way round, and stops when let go.</summary>
    private void SteerShip(Controls c, double dt)
    {
        var s = Ship;
        s.Guard = Math.Max(0, s.Guard - dt);
        if (!s.Alive || (c.Dx == 0 && c.Dy == 0)) return;
        double want = Math.Atan2(c.Dy, c.Dx);
        double turn = Field.Turn(s.A, want);
        s.A += Math.Clamp(turn, -TurnRate * dt, TurnRate * dt);
    }

    private void Fire(Controls c, double dt)
    {
        _fireWait -= dt;
        if (!c.Fire || _fireWait > 0 || !Ship.Alive || Shots.Count >= MaxShots * (Ship.Double ? 2 : 1)) return;
        _fireWait = FireDelay;
        var (x, y) = Ship.Pos;
        double dx = Field.CX - x, dy = Field.CY - y, d = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / d, uy = dy / d;
        foreach (double side in Ship.Double ? new[] { -8.0, 8.0 } : new[] { 0.0 })
            Shots.Add(new Bolt { X = x + ux * 18 - uy * side, Y = y + uy * 18 + ux * side, Vx = ux * ShotSpeed, Vy = uy * ShotSpeed });
        Play(Sfx.Fire);
    }

    // ───────────────────────────── the fighters ─────────────────────────────

    /// <summary>Where a fighter's place in the formation is: rings near the centre, turning slowly and breathing in and out.</summary>
    public (double A, double R) SlotOf(Fighter f)
    {
        int ring = f.Slot % 3;
        double a = FormationA + f.Slot * 2 * Math.PI / 40 + ring * 0.21;
        double r = 72 + ring * 30 + Math.Sin(StageTime * 1.4 + ring) * 9;
        return (a, r);
    }

    /// <summary>Where a fighter is, a fraction u of the way along its group's path in.</summary>
    private static (double A, double R) Along(Fighter f, double u)
    {
        double e = u * u * (3 - 2 * u);
        return f.Path switch
        {
            // From beyond the rim, spiralling in towards the middle.
            FlightPath.SpiralIn => (f.A0 + f.Spin * u * 2.8, Field.Ring + 90 - (Field.Ring + 90 - 110) * e),
            // Out of the depths in a great loop, and back in.
            FlightPath.LoopOut => (f.A0 + f.Spin * u * 3.6, 30 + Math.Sin(u * Math.PI) * Field.Ring * 0.82 + 70 * u),
            // A chance stage: out from the centre and away past the rim.
            _ => (f.A0 + f.Spin * u * 3.0, 10 + (Field.Ring + 120) * u),
        };
    }

    private void MoveFighters(double dt)
    {
        double pace = Pace;
        bool chance = Info.Kind == StageKind.Chance;
        foreach (var f in Fighters)
        {
            if (f.Dead) continue;
            f.T += dt * pace;
            switch (f.State)
            {
                case EnemyState.Entering:
                {
                    if (f.T < 0) continue;
                    double u = Math.Min(1, f.T / EntrySeconds);
                    (f.A, f.R) = Along(f, u);
                    if (u >= 1)
                    {
                        if (chance) { f.Dead = true; continue; }
                        f.State = EnemyState.Holding;
                    }
                    break;
                }
                case EnemyState.Holding:
                case EnemyState.Returning:
                {
                    var (sa, sr) = SlotOf(f);
                    double k = Math.Min(1, 3 * dt);
                    f.A += Field.Turn(f.A, sa) * k;
                    f.R += (sr - f.R) * k;
                    if (f.State == EnemyState.Returning && Math.Abs(f.R - sr) < 4) f.State = EnemyState.Holding;
                    // The formation fires out now and then.
                    f.FireIn -= dt;
                    if (f.FireIn <= 0 && Phase == Phase.Play && f.State == EnemyState.Holding)
                    {
                        f.FireIn = (3 + _rng.NextDouble() * 5) / pace;
                        if (_rng.NextDouble() < 0.5) Shoot(f);
                    }
                    break;
                }
                case EnemyState.Diving:
                {
                    // Out from the formation in a sweeping curve towards where the ship was, firing as it comes.
                    double u = f.T / DiveSeconds;
                    double before = u - dt * pace / DiveSeconds;
                    f.A = f.DiveA0 + Field.Turn(f.DiveA0, f.DiveTo) * Math.Min(1, u * 1.2) + Math.Sin(u * Math.PI * 2) * 0.25 * f.Spin;
                    f.R = f.DiveR0 + (Field.Ring + 70 - f.DiveR0) * Math.Pow(Math.Min(1, u), 1.4);
                    foreach (double at in new[] { 0.35, 0.62 })
                        if (before < at && u >= at && Phase == Phase.Play) Shoot(f);
                    if (u >= 1)
                    {
                        // Gone past the rim: back to its place from the depths.
                        f.State = EnemyState.Returning;
                        f.R = 0;
                    }
                    break;
                }
                case EnemyState.Leaving:
                    f.R += (60 + f.R * 1.2) * dt;
                    if (f.R > Field.Ring + 120) f.Dead = true;
                    break;
            }
        }
        if (Phase != Phase.Play || chance || Calm) return;
        // Divers, now and then, once the first group is in.
        bool settled = Fighters.Any(f => f.State == EnemyState.Holding);
        _diveIn -= dt;
        if (settled && _diveIn <= 0 && Fighters.Count(f => f.State == EnemyState.Diving && !f.Dead) < 3)
        {
            _diveIn = Math.Max(0.7, 2.2 - 0.07 * Stage) * (0.6 + _rng.NextDouble() * 0.8);
            var holders = Fighters.Where(f => !f.Dead && f.State == EnemyState.Holding).ToList();
            if (holders.Count > 0)
            {
                var d = holders[_rng.Next(holders.Count)];
                d.State = EnemyState.Diving;
                d.T = 0;
                d.DiveA0 = d.A;
                d.DiveR0 = d.R;
                d.DiveTo = Ship.A + (_rng.NextDouble() - 0.5) * 0.8;
                Play(Sfx.Dive);
            }
        }
        // Once the last group is in, those left linger a while and then fly off.
        if (_lingerFrom < 0 && Fighters.All(f => f.Dead || f.State != EnemyState.Entering)) _lingerFrom = StageTime;
        if (_lingerFrom >= 0 && StageTime - _lingerFrom > LingerSeconds)
            foreach (var f in Fighters.Where(f => !f.Dead && f.State is EnemyState.Holding or EnemyState.Returning)) f.State = EnemyState.Leaving;
    }

    /// <summary>A bullet from a fighter, aimed at where the ship is.</summary>
    private void Shoot(Fighter f)
    {
        if (Calm || !Ship.Alive) return;
        var (x, y) = Field.At(f.A, f.R);
        var (tx, ty) = Ship.Pos;
        double dx = tx - x, dy = ty - y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double speed = BulletSpeed * Pace;
        Bullets.Add(new Bolt { X = x, Y = y, Vx = dx / d * speed, Vy = dy / d * speed });
        Play(Sfx.EnemyFire);
    }

    // ───────────────────────────── satellites and meteors ─────────────────────────────

    private void Extras(double dt)
    {
        if (Calm || Info.Kind == StageKind.Chance) return;
        // Satellites gather at the centre on every other stage; the bright one in the middle gives the double shot.
        if (!_satellitesDone && Stage % 2 == 0 && StageTime > 9)
        {
            _satellitesDone = true;
            double a = _rng.NextDouble() * Math.PI * 2;
            for (int k = -1; k <= 1; k++) Satellites.Add(new Satellite { A = a + k * 0.45, R = 4, Power = k == 0 });
            Play(Sfx.Satellite);
        }
        foreach (var s in Satellites)
        {
            s.Age += dt;
            s.A += 0.9 * dt;
            s.R = s.Age < 1 ? 4 + 70 * s.Age : s.Age < 9 ? 74 : 74 - (s.Age - 9) * 90;
            if (s.R <= 0) s.Dead = true;
        }
        // Meteors rush out of the depths from the third stage: they can't be shot.
        if (Stage < 3) return;
        _meteorIn -= dt;
        if (_meteorIn <= 0)
        {
            _meteorIn = Math.Max(3, 7 - 0.2 * Stage) * (0.7 + _rng.NextDouble() * 0.6);
            Meteors.Add(new Meteor { A = Ship.A + (_rng.NextDouble() - 0.5) * 0.9, R = 6, Spin = _rng.NextDouble() * 6 });
            Play(Sfx.Meteor);
        }
        foreach (var m in Meteors)
        {
            m.R += (50 + m.R * 1.5) * Pace * dt;
            m.Spin += dt * 3;
            if (m.R > Field.Ring + 140) m.Dead = true;
        }
    }

    private void MoveBolts(double dt)
    {
        foreach (var s in Shots)
        {
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Age += dt;
            double dx = s.X - Field.CX, dy = s.Y - Field.CY;
            if (dx * dx + dy * dy < 22 * 22 || s.Age > 1) s.Dead = true;
        }
        foreach (var b in Bullets)
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            b.Age += dt;
            if (b.X < -40 || b.X > Field.Width + 40 || b.Y < Field.Top - 40 || b.Y > Field.Height + 40) b.Dead = true;
        }
    }

    // ───────────────────────────── hits ─────────────────────────────

    private void Hits()
    {
        bool chance = Info.Kind == StageKind.Chance;
        foreach (var s in Shots)
        {
            foreach (var f in Fighters)
            {
                if (f.Dead || f.R < 0) continue;
                var (x, y) = Field.At(f.A, f.R);
                double r = HitRadius * Field.Scale(f.R) + 3;
                if ((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) > r * r) continue;
                s.Dead = true;
                Down(f, chance);
                break;
            }
            if (s.Dead) continue;
            foreach (var sat in Satellites)
            {
                var (x, y) = Field.At(sat.A, sat.R);
                if ((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) > 14 * 14) continue;
                s.Dead = sat.Dead = true;
                Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = sat.Power ? 0xFF7AF0FF : 0xFFFFA040, Size = 0.6 });
                AddScore(sat.Power ? 300 : 100, x, y);
                if (sat.Power)
                {
                    Ship.Double = true;
                    Effects.Add(new Effect { Kind = EffectKind.Word, X = Field.CX, Y = Field.CY - 60, Text = "DOUBLE SHOT!", Colour = 0xFF7AF0FF, Life = 1.5 });
                    Play(Sfx.Power);
                }
                else Play(Sfx.Hit);
                break;
            }
            if (s.Dead) continue;
            // Meteors can't be hurt: they just soak up shots.
            foreach (var m in Meteors)
            {
                var (x, y) = Field.At(m.A, m.R);
                double r = 22 * Field.Scale(m.R);
                if ((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) < r * r) { s.Dead = true; break; }
            }
        }
        // What can hit the ship.
        var ship = Ship;
        if (!ship.Alive || ship.Guard > 0) return;
        var (sx, sy) = ship.Pos;
        foreach (var b in Bullets)
            if ((b.X - sx) * (b.X - sx) + (b.Y - sy) * (b.Y - sy) < 15 * 15) { b.Dead = true; Lose("SHOT DOWN"); return; }
        foreach (var f in Fighters)
        {
            // Only a diver comes at the ship; the squadrons sweeping in from behind fly over it.
            if (f.Dead || f.State != EnemyState.Diving || f.R < Field.Ring - 40) continue;
            var (x, y) = Field.At(f.A, f.R);
            if ((x - sx) * (x - sx) + (y - sy) * (y - sy) < 24 * 24) { Down(f, chance); Lose("RAMMED"); return; }
        }
        foreach (var m in Meteors)
        {
            var (x, y) = Field.At(m.A, m.R);
            double r = 22 * Field.Scale(m.R) + 10;
            if ((x - sx) * (x - sx) + (y - sy) * (y - sy) < r * r) { m.Dead = true; Lose("HIT BY A METEOR"); return; }
        }
    }

    private void Down(Fighter f, bool chance)
    {
        f.Dead = true;
        var (x, y) = Field.At(f.A, f.R);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = FighterColour(f.Kind), Size = Field.Scale(f.R) });
        int points = chance ? 100 : f.State switch { EnemyState.Entering => 100, EnemyState.Diving => 150, _ => 50 };
        AddScore(points, x, y);
        Play(Sfx.Hit);
        if (chance) ChanceHits++;
        // A whole group shot down earns its bonus.
        if (--_groupLeft[f.Group] == 0 && !chance)
        {
            int bonus = GroupBonus * (Info.Kind == StageKind.Planet ? 2 : 1);
            AddScore(bonus, x, y - 30, 0xFFFFE14A);
            Play(Sfx.Group);
        }
    }

    public static uint FighterColour(int kind) => kind switch { 0 => 0xFFFF5A5A, 1 => 0xFF5AD8FF, 2 => 0xFFFFD84A, _ => 0xFFB070FF };

    private void Lose(string cause)
    {
        Cause = cause;
        Lives--;
        Ship.Alive = false;
        Ship.Double = false;
        var (x, y) = Ship.Pos;
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = 0xFFF2F2F2, Size = 1.6, Life = 1.2 });
        Shots.Clear();
        // Divers go back to their places.
        foreach (var f in Fighters.Where(f => f.State == EnemyState.Diving)) { f.State = EnemyState.Returning; f.R = 0; }
        Play(Sfx.Explode);
        Music(false);
        SetPhase(Phase.Dying);
        Say(cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "SHIP" : "SHIPS")} LEFT" : null, DyingSeconds);
    }

    private void StageDone()
    {
        if (Info.Kind == StageKind.Chance)
        {
            int bonus = ChanceHits == Total ? PerfectBonus : ChanceHits * 100;
            AddScore(bonus, Field.CX, Field.CY, 0xFFFFE14A);
            Say(ChanceHits == Total ? "PERFECT!" : $"HIT {ChanceHits} OF {Total}", $"BONUS {bonus}", WarpSeconds);
            Play(ChanceHits == Total ? Sfx.Perfect : Sfx.Warp);
        }
        else Play(Sfx.Warp);
        Music(false);
        Bullets.Clear();
        SetPhase(Phase.Warp);
    }

    private void AddScore(int points, double x, double y, uint colour = 0xFFF2F2F2)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Colour = colour });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a stage.</summary>
    internal void Go(int stage = 1)
    {
        StartGame();
        BeginStage(stage);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Fighter PutFighter(double a, double r, EnemyState state = EnemyState.Holding, int group = 0)
    {
        var f = new Fighter { A = a, R = r, State = state, Group = group, Slot = Fighters.Count, FireIn = 99, T = 0 };
        Fighters.Add(f);
        return f;
    }

    internal Bolt PutBullet(double x, double y, double vx, double vy)
    {
        var b = new Bolt { X = x, Y = y, Vx = vx, Vy = vy };
        Bullets.Add(b);
        return b;
    }

    internal Meteor PutMeteor(double a, double r)
    {
        var m = new Meteor { A = a, R = r };
        Meteors.Add(m);
        return m;
    }

    internal Satellite PutSatellite(double a, double r, bool power)
    {
        var s = new Satellite { A = a, R = r, Power = power, Age = 2 };
        Satellites.Add(s);
        return s;
    }

    internal void Dive(Fighter f)
    {
        f.State = EnemyState.Diving;
        f.T = 0;
        f.DiveA0 = f.A;
        f.DiveR0 = f.R;
        f.DiveTo = Ship.A;
    }

    internal void SetGroupLeft(int group, int left) => _groupLeft[group] = left;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
}
