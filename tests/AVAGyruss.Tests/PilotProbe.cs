using AVAGyruss.Engine;

namespace AVAGyruss.Tests;

/// <summary>How the computer pilot gets on, stage by stage (set GYRUSS_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_flies()
    {
        if (Environment.GetEnvironmentVariable("GYRUSS_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "gyruss-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var stages = new List<string>();
            var last = g.Phase;
            int stage = 1, t = 0;
            double start = 0;
            for (; t < 60 * 600 && g.Stage <= 9; t++)
            {
                if (g.Phase == Phase.Warp && last != Phase.Warp && g.Info.Kind == StageKind.Chance) stages.Add($"chance {g.ChanceHits}/{g.Total}");
                g.Tick(Environment.GetEnvironmentVariable("GYRUSS_PROBE") == "lazy" ? new Controls(Fire: t % 20 < 10) : g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"S{g.Stage} {g.Cause}"] = deaths.GetValueOrDefault($"S{g.Stage} {g.Cause}") + 1;
                if (g.Stage != stage) { stages.Add($"S{stage}:{(t - start) / 60:0}s"); stage = g.Stage; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: stage {g.Stage} after {t / 60:0}s score {g.Score} | {string.Join(" ", stages)}");
            output.WriteLine("   " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}
