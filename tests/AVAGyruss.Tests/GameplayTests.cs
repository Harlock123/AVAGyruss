using AVAGyruss.Engine;

namespace AVAGyruss.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avagyruss-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play on a stage with nothing in space unless a test puts it there.</summary>
    private static GameEngine Game(int stage = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(stage);
        return g;
    }

    /// <summary>A fighter sitting in its place in the formation.</summary>
    private static Fighter Holder(GameEngine g, int group = 0)
    {
        var f = g.PutFighter(0, 0, EnemyState.Holding, group);
        (f.A, f.R) = g.SlotOf(f);
        return f;
    }

    private static readonly Controls Fire = new(Fire: true);

    // ───────────────────────────── the route ─────────────────────────────

    [Fact]
    public void The_route_warps_from_planet_to_planet_with_a_chance_stage_after_each_and_goes_round_again()
    {
        Assert.Equal((StageKind.Warp, "NEPTUNE", 2, 1), GameEngine.Describe(1));
        Assert.Equal((StageKind.Warp, "NEPTUNE", 1, 1), GameEngine.Describe(2));
        Assert.Equal((StageKind.Planet, "NEPTUNE", 0, 1), GameEngine.Describe(3));
        Assert.Equal((StageKind.Chance, "NEPTUNE", 0, 1), GameEngine.Describe(4));
        Assert.Equal((StageKind.Warp, "URANUS", 2, 1), GameEngine.Describe(5));
        int lap = GameEngine.Route.Sum(r => r.Warps + 2);
        Assert.Equal((StageKind.Chance, "EARTH", 0, 1), GameEngine.Describe(lap));
        Assert.Equal((StageKind.Warp, "NEPTUNE", 2, 2), GameEngine.Describe(lap + 1));
    }

    [Fact]
    public void A_stage_starts_by_saying_where_the_ship_is_going()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        Assert.Equal("2 WARPS TO NEPTUNE", g.Message);
        Assert.Equal(g.Total, g.Fighters.Count);
        Assert.Equal(32, g.Total);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void The_ship_sweeps_round_the_rim_towards_the_way_the_stick_points_and_stops_when_let_go()
    {
        var g = Game();
        Assert.Equal(Math.PI / 2, g.Ship.A, 3);
        g.Run(10, new Controls(Dx: 1));
        Assert.InRange(g.Ship.A, 0.1, Math.PI / 2 - 0.3);
        g.Run(60, new Controls(Dx: 1));
        Assert.Equal(0, Field.Turn(g.Ship.A, 0), 2);
        double a = g.Ship.A;
        g.Idle(30);
        Assert.Equal(a, g.Ship.A, 6);
        g.Run(90, new Controls(Dy: -1));
        Assert.Equal(0, Field.Turn(g.Ship.A, -Math.PI / 2), 2);
    }

    [Fact]
    public void Shots_fly_into_the_middle_and_only_a_few_at_a_time()
    {
        var g = Game();
        g.Run(1, Fire);
        var s = Assert.Single(g.Shots);
        var (sx, sy) = (s.X, s.Y);
        g.Idle(5);
        Assert.True(Math.Abs(s.X - Field.CX) + Math.Abs(s.Y - Field.CY) < Math.Abs(sx - Field.CX) + Math.Abs(sy - Field.CY));
        int most = 0;
        for (int t = 0; t < 120; t++) { g.Run(1, Fire); most = Math.Max(most, g.Shots.Count); }
        Assert.InRange(most, 2, GameEngine.MaxShots);
        g.Idle(70);
        Assert.Empty(g.Shots);
    }

    // ───────────────────────────── shooting things ─────────────────────────────

    [Fact]
    public void A_fighter_in_the_formation_is_shot_down_for_50()
    {
        var g = Game();
        var f = Holder(g);
        g.Ship.A = f.A;
        g.Run(40, Fire);
        Assert.True(f.Dead);
        Assert.Equal(50, g.Score);
    }

    [Fact]
    public void A_diver_is_worth_150()
    {
        var g = Game();
        var f = g.PutFighter(g.Ship.A, 160);
        g.Dive(f);
        g.Run(40, Fire);
        Assert.True(f.Dead);
        Assert.Equal(150, g.Score);
    }

    [Fact]
    public void Shooting_down_the_last_of_a_group_earns_the_group_bonus()
    {
        var g = Game();
        var f = Holder(g, group: 2);
        g.SetGroupLeft(2, 1);
        g.Ship.A = f.A;
        g.Run(40, Fire);
        Assert.True(f.Dead);
        Assert.Equal(50 + GameEngine.GroupBonus, g.Score);
    }

    [Fact]
    public void The_bright_satellite_gives_a_double_shot_until_the_ship_is_lost()
    {
        var g = Game();
        var sat = g.PutSatellite(1.0, 74, power: true);
        g.Ship.A = 1.0;
        g.Run(40, Fire);
        Assert.True(sat.Dead);
        Assert.True(g.Ship.Double);
        Assert.Equal(300, g.Score);
        g.Idle(70);
        g.Run(1, Fire);
        Assert.Equal(2, g.Shots.Count);
        var (x, y) = g.Ship.Pos;
        g.PutBullet(x, y, 0, 0);
        g.Idle(1);
        Assert.False(g.Ship.Double);
    }

    [Fact]
    public void A_plain_satellite_is_worth_100_and_gives_nothing_else()
    {
        var g = Game();
        var sat = g.PutSatellite(2.0, 74, power: false);
        g.Ship.A = 2.0;
        g.Run(40, Fire);
        Assert.True(sat.Dead);
        Assert.False(g.Ship.Double);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void Meteors_cannot_be_shot_they_soak_up_the_shots()
    {
        var g = Game();
        var m = g.PutMeteor(0.5, 160);
        g.Ship.A = 0.5;
        g.Run(60, Fire);
        Assert.False(m.Dead);
        Assert.Contains(m, g.Meteors);
        Assert.Equal(0, g.Score);
        Assert.All(g.Shots, s => Assert.True(Math.Abs(s.X - Field.CX) + Math.Abs(s.Y - Field.CY) > 100));
    }

    // ───────────────────────────── losing a ship ─────────────────────────────

    [Fact]
    public void A_bullet_shoots_the_ship_down()
    {
        var g = Game();
        int lives = g.Lives;
        var (x, y) = g.Ship.Pos;
        g.PutBullet(x - 60, y, 300, 0);
        g.Idle(20);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOT DOWN", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
    }

    [Fact]
    public void A_meteor_reaching_the_ship_destroys_it()
    {
        var g = Game();
        g.PutMeteor(g.Ship.A, Field.Ring);
        g.Idle(1);
        Assert.Equal("HIT BY A METEOR", g.Cause);
    }

    [Fact]
    public void A_diver_rams_the_ship_but_a_fighter_sweeping_past_does_not()
    {
        var g = Game();
        var past = g.PutFighter(g.Ship.A, Field.Ring, EnemyState.Holding);
        g.Idle(1);
        Assert.True(g.Ship.Alive);
        past.Dead = true;
        var diver = g.PutFighter(g.Ship.A, Field.Ring);
        g.Dive(diver);
        g.Idle(1);
        Assert.Equal("RAMMED", g.Cause);
        Assert.True(diver.Dead);
    }

    [Fact]
    public void A_new_ship_comes_back_shielded_for_a_moment()
    {
        var g = Game();
        var (x, y) = g.Ship.Pos;
        g.PutBullet(x, y, 0, 0);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(g.Ship.Alive);
        Assert.True(g.Ship.Guard > 0);
        g.PutBullet(x, y, 0, 0);
        g.Idle(1);
        Assert.True(g.Ship.Alive);
        g.Idle(150);
        g.PutBullet(x, y, 0, 0);
        g.Idle(1);
        Assert.False(g.Ship.Alive);
    }

    [Fact]
    public void Losing_the_last_ship_ends_the_game()
    {
        var g = Game();
        g.SetLives(1);
        var (x, y) = g.Ship.Pos;
        g.PutBullet(x, y, 0, 0);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Extra_ships_come_at_30000_and_every_70000_after()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(29990);
        var f = Holder(g);
        g.Ship.A = f.A;
        g.Run(40, Fire);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the stages ─────────────────────────────

    [Fact]
    public void Clearing_the_fighters_warps_on_to_the_next_stage()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        foreach (var f in g.Fighters) f.Dead = true;
        g.Idle(1);
        Assert.Equal(Phase.Warp, g.Phase);
        g.Idle((int)(60 * GameEngine.WarpSeconds) + 5);
        Assert.Equal(2, g.Stage);
        Assert.Equal("1 WARP TO NEPTUNE", g.Message);
        Assert.Equal(g.Total, g.Fighters.Count(f => !f.Dead));
    }

    [Fact]
    public void Fighters_hold_back_dive_at_the_ship_and_shoot()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        bool dived = false, shot = false;
        for (int t = 0; t < 60 * 20; t++)
        {
            g.Idle(1);
            dived |= g.Fighters.Any(f => f.State == EnemyState.Diving);
            shot |= g.Bullets.Count > 0;
        }
        Assert.True(dived);
        Assert.True(shot);
        Assert.Contains(g.Fighters, f => !f.Dead && f.State == EnemyState.Holding);
    }

    [Fact]
    public void Satellites_gather_on_even_stages_and_meteors_come_from_the_third()
    {
        static (int Satellites, int Powers, bool Meteors) Wait(int stage, double seconds)
        {
            var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(stage);
            g.SetLives(99);
            int sats = 0, powers = 0;
            bool meteors = false;
            for (int t = 0; t < 60 * 120 && g.StageTime < seconds; t++)
            {
                g.Idle(1);
                sats = Math.Max(sats, g.Satellites.Count);
                powers = Math.Max(powers, g.Satellites.Count(s => s.Power));
                meteors |= g.Meteors.Count > 0;
            }
            return (sats, powers, meteors);
        }
        Assert.Equal((0, 0, false), Wait(1, 12));
        Assert.Equal((3, 1), (Wait(2, 9.5).Satellites, Wait(2, 9.5).Powers));
        Assert.Equal(0, Wait(5, 12).Satellites);
        Assert.True(Wait(3, 12).Meteors);
    }

    [Fact]
    public void A_chance_stage_flies_by_without_firing_and_counts_the_hits()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(4);
        Assert.Equal(StageKind.Chance, g.Info.Kind);
        Assert.Equal(40, g.Total);
        for (int t = 0; t < 60 * 60 && g.Phase == Phase.Play; t++)
        {
            g.Idle(1);
            Assert.Empty(g.Bullets);
            Assert.DoesNotContain(g.Fighters, f => f.State == EnemyState.Diving);
        }
        Assert.Equal(Phase.Warp, g.Phase);
        Assert.Equal("HIT 0 OF 40", g.Message);
        Assert.True(g.Ship.Alive);
    }

    [Fact]
    public void The_computer_pilot_scores_on_a_chance_stage()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(4);
        for (int t = 0; t < 60 * 60 && g.Phase == Phase.Play; t++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.ChanceHits > 10, $"{g.ChanceHits} hits");
        Assert.Equal(g.ChanceHits == 40 ? "PERFECT!" : $"HIT {g.ChanceHits} OF 40", g.Message);
        Assert.Equal(g.ChanceHits * 100 + (g.ChanceHits == 40 ? GameEngine.PerfectBonus : g.ChanceHits * 100), g.Score);
    }

    [Fact]
    public void The_computer_pilot_gets_through_the_first_stages()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        for (int t = 0; t < 60 * 90 && g.State == GameState.Playing && g.Stage < 4; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Stage >= 4, $"stage {g.Stage}");
        Assert.True(g.Score > 5000);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, Math.Round(g.Ship.A, 6), g.Stage);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        var (x, y) = g.Ship.Pos;
        g.PutBullet(x, y, 0, 0);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "GYR") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("GYR", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
