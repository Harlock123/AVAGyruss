using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAGyruss.Engine;
using Effect = AVAGyruss.Engine.Effect;

namespace AVAGyruss.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAGyruss/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA2B8, Cyan = 0xFF7AF0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static Point At(double a, double r) { var (x, y) = Field.At(a, r); return new Point(x, y); }

    /// <summary>Points of a shape given as (forward, across) pairs, placed at a point, turned to face a way, and scaled.</summary>
    private static Point[] Shape(Point at, double facing, double scale, params (double F, double S)[] pts)
    {
        double c = Math.Cos(facing), s = Math.Sin(facing);
        return pts.Select(p => new Point(at.X + (p.F * c - p.S * s) * scale, at.Y + (p.F * s + p.S * c) * scale)).ToArray();
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02020A), new Rect(0, 0, Field.Width, Field.Height));
        DrawStars(dc);
        DrawPlanet(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            // The ship's circle, faintly.
            dc.DrawEllipse(null, Pen(0x182E6AC8, 2), new Point(Field.CX, Field.CY), Field.Ring, Field.Ring);
            foreach (var m in g.Meteors) DrawMeteor(dc, m);
            foreach (var s in g.Satellites) DrawSatellite(dc, s);
            foreach (var f in g.Fighters.Where(f => !f.Dead && f.R >= 0).OrderBy(f => f.R)) DrawFighter(dc, f);
            foreach (var s in g.Shots) dc.DrawLine(Pen(Yellow, 3), new Point(s.X, s.Y), new Point(s.X - s.Vx * 0.016, s.Y - s.Vy * 0.016));
            foreach (var b in g.Bullets)
            {
                dc.DrawEllipse(Brush(0x80FF4A3A), null, new Point(b.X, b.Y), 6, 6);
                dc.DrawEllipse(Brush(0xFFFFE0C0), null, new Point(b.X, b.Y), 2.6, 2.6);
            }
            if (g.Ship.Alive && (g.Ship.Guard <= 0 || (int)(_clock * 12) % 2 == 0)) DrawShip(dc, g.Ship.Pos, g.Ship.A, 1, g.Ship.Double);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    // ───────────────────────────── space ─────────────────────────────

    /// <summary>Stars streaming out from the centre as the ship flies on: faster, and drawn out into streaks, in a warp.</summary>
    private void DrawStars(DrawingContext dc)
    {
        var g = Engine;
        bool warp = g.Phase == Phase.Warp && g.State is GameState.Playing or GameState.Attract;
        double speed = warp ? 6 : 1;
        for (int i = 0; i < 160; i++)
        {
            uint h = Hash(i, 1, 9);
            double a = (h % 6283) / 1000.0;
            double r0 = (h >> 13) % 500;
            double r = (r0 + _clock * (40 + (h >> 22) % 60) * speed) % 560 + 8;
            var p = At(a, r);
            byte alpha = (byte)Math.Clamp(r * 0.6, 30, 255);
            uint colour = (uint)alpha << 24 | ((h >> 5) % 3 == 0 ? 0x00FFE0C0u : 0x00D0E0FFu);
            if (warp) dc.DrawLine(Pen(colour, 1.6), At(a, r * 0.8), p);
            else dc.FillRectangle(Brush(colour), new Rect(p.X, p.Y, 1 + r / 300, 1 + r / 300));
        }
    }

    /// <summary>The planet ahead: a speck on the warps, close and huge when it's reached.</summary>
    private void DrawPlanet(DrawingContext dc)
    {
        var g = Engine;
        if (g.State is not (GameState.Playing or GameState.Attract or GameState.Paused)) return;
        var (kind, planet, warps, _) = g.Info;
        if (kind == StageKind.Chance) return;
        double size = kind == StageKind.Planet ? 150 : 14 + (4 - Math.Min(3, warps)) * 10;
        var c = new Point(Field.CX + (kind == StageKind.Planet ? 0 : 160), Field.CY - (kind == StageKind.Planet ? 0 : 150));
        var (body, band) = planet switch
        {
            "NEPTUNE" => (0xFF2A4AC8u, 0xFF4A7AF0u), "URANUS" => (0xFF5AC8D0u, 0xFF8AE8E8u), "SATURN" => (0xFFD0B070u, 0xFFE8D090u),
            "JUPITER" => (0xFFC88A50u, 0xFFE8C090u), "MARS" => (0xFFC04A2Au, 0xFFE07A4Au), _ => (0xFF2A6AD0u, 0xFF3AB04Au),
        };
        uint fade = kind == StageKind.Planet ? 0xFF000000 : 0xC0000000;
        uint F(uint col) => (col & 0x00FFFFFF) | fade;
        if (planet == "SATURN")
            dc.DrawEllipse(null, Pen(F(0xFFE8D8A0), size * 0.08), c, size * 1.7, size * 0.42);
        dc.DrawEllipse(Brush(F(body)), null, c, size, size);
        var clip = dc.PushGeometryClip(new EllipseGeometry(new Rect(c.X - size, c.Y - size, size * 2, size * 2)));
        // Bands, or for Earth, land.
        if (planet == "EARTH")
        {
            for (int k = 0; k < 5; k++)
            {
                uint h = Hash(k, 3, 7);
                dc.DrawEllipse(Brush(F(band)), null, new Point(c.X + ((int)(h % 100) - 50) / 100.0 * size, c.Y + ((int)((h >> 8) % 100) - 50) / 100.0 * size), size * 0.3, size * 0.2);
            }
            dc.DrawEllipse(Brush(0x40FFFFFF), null, new Point(c.X - size * 0.3, c.Y - size * 0.3), size * 0.5, size * 0.2);
        }
        else
            for (int k = -2; k <= 2; k++)
                dc.FillRectangle(Brush(F(band) & 0x90FFFFFF), new Rect(c.X - size, c.Y + k * size * 0.32 - size * 0.06, size * 2, size * 0.12));
        dc.DrawEllipse(Brush(0x50000000), null, new Point(c.X + size * 0.25, c.Y + size * 0.2), size * 0.85, size * 0.85);
        clip.Dispose();
        if (planet == "SATURN")
            dc.DrawLine(Pen(F(0xFFE8D8A0), size * 0.08), new Point(c.X - size * 1.7, c.Y), new Point(c.X + size * 1.7, c.Y));
    }

    // ───────────────────────────── ships ─────────────────────────────

    /// <summary>The player's ship, nose to the centre.</summary>
    private void DrawShip(DrawingContext dc, (double X, double Y) pos, double a, double scale, bool twin)
    {
        var p = new Point(pos.X, pos.Y);
        double facing = a + Math.PI;
        var hull = Shape(p, facing, scale, (20, 0), (2, -7), (-10, -16), (-6, -4), (-12, 0), (-6, 4), (-10, 16), (2, 7));
        Poly(dc, 0xFFD8E4F8, 0xFF2A3A6A, 1.5, hull);
        Poly(dc, 0xFF3A7AE0, null, 0, Shape(p, facing, scale, (10, 0), (0, -4), (-4, 0), (0, 4)));
        foreach (var side in twin ? new[] { -12.0, 12.0 } : new[] { 0.0 })
        {
            var gun = Shape(p, facing, scale, (twin ? 12 : 22, side), (twin ? 4 : 14, side));
            dc.DrawLine(Pen(Yellow, 2.5 * scale), gun[0], gun[1]);
        }
        double flame = 4 + Math.Sin(_clock * 40) * 2;
        Poly(dc, 0xC0FFA040, null, 0, Shape(p, facing, scale, (-12, -3), (-12 - flame, 0), (-12, 3)));
    }

    private void DrawFighter(DrawingContext dc, Fighter f)
    {
        var p = At(f.A, f.R);
        double s = Field.Scale(f.R) * 1.7;
        uint body = GameEngine.FighterColour(f.Kind);
        // They face outwards, towards the ship, as they come.
        double facing = f.A;
        double flap = Math.Sin(_clock * 18 + f.Slot) * 4;
        var left = Shape(p, facing, s, (2, -3), (-6, -15 - flap), (-10, -8));
        var right = Shape(p, facing, s, (2, 3), (-6, 15 + flap), (-10, 8));
        Poly(dc, (body & 0x00FFFFFF) | 0xA0000000, null, 0, left);
        Poly(dc, (body & 0x00FFFFFF) | 0xA0000000, null, 0, right);
        Poly(dc, body, 0xFF101010, 1, Shape(p, facing, s, (12, 0), (2, -5), (-10, -3), (-10, 3), (2, 5)));
        var eye = Shape(p, facing, s, (6, -2.5), (6, 2.5));
        dc.DrawEllipse(Brush(White), null, eye[0], 2 * s, 2 * s);
        dc.DrawEllipse(Brush(White), null, eye[1], 2 * s, 2 * s);
    }

    private void DrawSatellite(DrawingContext dc, Satellite sat)
    {
        var p = At(sat.A, sat.R);
        double s = Field.Scale(sat.R) * 2.6;
        uint colour = sat.Power ? Cyan : 0xFFFFA040;
        if (sat.Power) dc.DrawEllipse(Brush((uint)(80 + 60 * Math.Sin(_clock * 10)) << 24 | 0x007AF0FF), null, p, 14 * s, 14 * s);
        dc.DrawEllipse(Brush(colour), Pen(0xFF202020, 1), p, 7 * s, 7 * s);
        for (int k = 0; k < 4; k++)
        {
            double a = k * Math.PI / 2 + _clock * 2;
            dc.DrawLine(Pen(colour, 1.5), new Point(p.X + Math.Cos(a) * 7 * s, p.Y + Math.Sin(a) * 7 * s), new Point(p.X + Math.Cos(a) * 13 * s, p.Y + Math.Sin(a) * 13 * s));
        }
    }

    private static void DrawMeteor(DrawingContext dc, Meteor m)
    {
        var p = At(m.A, m.R);
        double s = Field.Scale(m.R) * 22;
        var pts = Enumerable.Range(0, 9).Select(k =>
        {
            double a = k * Math.PI * 2 / 9 + m.Spin, r = s * (0.75 + 0.25 * ((k * 37) % 5) / 4.0);
            return new Point(p.X + Math.Cos(a) * r, p.Y + Math.Sin(a) * r);
        }).ToArray();
        Poly(dc, 0xFF7A6A5A, 0xFF3A2A1A, 1.5, pts);
        dc.DrawEllipse(Brush(0xFF5A4A3A), null, new Point(p.X + s * 0.2, p.Y - s * 0.15), s * 0.25, s * 0.2);
    }

    private static void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 40 * t) * e.Size;
                dc.DrawEllipse(null, Pen((uint)(255 * (1 - t)) << 24 | (e.Colour & 0x00FFFFFF), 3), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = r * (0.6 + Hash(k, 2, 3) % 10 * 0.06);
                    dc.FillRectangle(Brush((uint)(255 * (1 - t)) << 24 | 0x00FFF0C0), new Rect(c.X + Math.Cos(a) * d - 1.5, c.Y + Math.Sin(a) * d - 1.5, 3, 3));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 14 - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 40, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF04040E), new Rect(0, 0, Field.Width, Field.Top));
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, White);
        Text(dc, "HI", new Point(170, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(170, 44), 28, Dim);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (!inGame) return;
        var (kind, planet, warps, lap) = g.Info;
        string where = kind switch { StageKind.Warp => $"{warps} {(warps == 1 ? "WARP" : "WARPS")} TO {planet}", StageKind.Planet => planet, _ => "CHANCE STAGE" };
        Text(dc, $"STAGE {g.Stage}" + (lap > 1 ? $"  LAP {lap}" : ""), new Point(Field.Width / 2, 16), 22, Dim, center: true);
        Text(dc, where, new Point(Field.Width / 2, 44), 30, Yellow, center: true);
        if (g.Ship.Double) Text(dc, "DOUBLE", new Point(760, 30), 24, Cyan);
        Text(dc, "SHIPS", new Point(880, 16), 22, Dim);
        for (int k = 0; k < Math.Min(5, g.Lives - (g.Ship.Alive ? 1 : 0)); k++)
            DrawShip(dc, (890 + k * 26, 46), Math.PI / 2, 0.55, false);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2;
        Text(dc, msg, new Point(cx, Field.CY - 30), 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, Field.CY + 20), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x9002020A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        // A ship circling the title.
        double a = _clock * 0.8;
        DrawShip(dc, Field.At(a, Field.Ring), a, 1.2, false);
        Text(dc, "AVA GYRUSS", new Point(cx, 170), 116, 0xFF7AB8FF, center: true);
        Text(dc, "AFTER THE 1983 ARCADE CLASSIC", new Point(cx, 236), 28, Dim, center: true);
        string[] lines =
        [
            "FLY ROUND THE RIM AND SHOOT INTO THE DEPTHS",
            "WARP FROM PLANET TO PLANET ALL THE WAY TO EARTH",
            "SHOOT THE BRIGHT SATELLITE FOR A DOUBLE SHOT",
            "METEORS CAN'T BE SHOT - GET OUT OF THEIR WAY",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 300 + i * 34), 27, White, center: true);
        (string Name, string Points)[] table = [("FIGHTER", "50-150"), ("WHOLE GROUP", "1000"), ("SATELLITE", "100-300"), ("PERFECT CHANCE", "10000")];
        for (int i = 0; i < table.Length; i++)
        {
            double x = 170 + i * 228;
            Text(dc, table[i].Name, new Point(x, 470), 22, Dim, center: true);
            Text(dc, table[i].Points, new Point(x, 496), 26, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 590), 46, Yellow, center: true);
        Text(dc, "ARROWS: ROUND THE RIM (THE SHIP HEADS FOR THE WAY YOU POINT)    SPACE: FIRE", new Point(cx, 656), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD002020A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP PILOTS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   STAGE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD002020A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
