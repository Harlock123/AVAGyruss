using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAGyruss.Engine;
using AVAGyruss.Views;

namespace AVAGyruss.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GYRUSS_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avagyruss-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GYRUSS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAGyruss-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static int StageOf(StageKind kind, string planet) =>
        Enumerable.Range(1, 40).First(s => GameEngine.Describe(s) is var d && d.Kind == kind && d.Planet == planet);

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 71250, 9);
        g.HighScores.Insert("AVA", 52800, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Warping_to_neptune_with_the_formation_in()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.StageTime > 5 && g.Ship.Alive && g.Fighters.Count(f => !f.Dead && f.State == EnemyState.Holding) >= 4
                                       && g.Fighters.Count(f => !f.Dead && f.State == EnemyState.Entering && f.T > 0.8) >= 4));
        Shot(w, c, "play");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Warp && g.PhaseTime > 0.8));
        Shot(w, c, "warp");
    }

    [AvaloniaFact]
    public void Satellites_and_a_meteor()
    {
        var (w, c, g) = Open();
        g.Go(6);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Satellites.Count == 3 && g.Satellites[0].Age > 1.1));
        Shot(w, c, "satellites");
        Assert.True(PlayUntil(g, c, g => g.Ship.Double && g.Meteors.Any(m => m.R > 140 && m.R < 220) && g.Ship.Alive && g.Shots.Count > 0));
        Shot(w, c, "meteor");
    }

    [AvaloniaFact]
    public void Arriving_at_saturn_and_earth()
    {
        var (w, c, g) = Open();
        g.Go(StageOf(StageKind.Planet, "SATURN"));
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.StageTime > 6 && g.Ship.Alive && g.Fighters.Any(f => !f.Dead && f.State == EnemyState.Diving)));
        Shot(w, c, "saturn");
        g.Go(StageOf(StageKind.Planet, "EARTH"));
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.StageTime > 5 && g.Ship.Alive));
        Shot(w, c, "earth");
    }

    [AvaloniaFact]
    public void A_chance_stage_and_a_ship_lost()
    {
        var (w, c, g) = Open();
        g.Go(4);
        Assert.True(PlayUntil(g, c, g => g.ChanceHits >= 6 && g.Fighters.Count(f => !f.Dead && f.R > 60) >= 6));
        Shot(w, c, "chance");
        g.Go(5);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.StageTime > 4));
        var (x, y) = g.Ship.Pos;
        g.PutBullet(x, y, 0, 0);
        g.Idle(20);
        c.Advance(0.3);
        Shot(w, c, "lost");
    }
}
