using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Guitar;
using YARG.Core.Engine.Guitar.Engines;

namespace YARG.Core.UnitTests.Engine;

public class PowerChallengeEngineBehaviorTests : EngineTester
{
    [Test]
    public void SpeedFreakPlus_RaisesMultiplierFasterThanDefault()
    {
        var defaultEngine = CreateTestEngine(notesPerMultiplierIncrease: 10);
        var speedFreakEngine = CreateTestEngine(notesPerMultiplierIncrease: 5);

        defaultEngine.EngineStats.Combo = 12;
        speedFreakEngine.EngineStats.Combo = 12;

        defaultEngine.RunUpdateMultiplier();
        speedFreakEngine.RunUpdateMultiplier();

        Assert.That(defaultEngine.EngineStats.ScoreMultiplier, Is.EqualTo(2));   // 12/10 + 1
        Assert.That(speedFreakEngine.EngineStats.ScoreMultiplier, Is.EqualTo(3)); // 12/5 + 1
    }

    [Test]
    public void StarPowerNovaPlus_MultipliesByConfiguredAmount_InsteadOfDefaultTwo()
    {
        var novaEngine = CreateTestEngine(starPowerMultiplier: 6);

        novaEngine.EngineStats.Combo = 9; // multiplier antes de SP: 9/10 + 1 = 1
        novaEngine.EngineStats.IsStarPowerActive = true;
        novaEngine.RunUpdateMultiplier();

        Assert.That(novaEngine.EngineStats.ScoreMultiplier, Is.EqualTo(6)); // 1 * 6
    }

    [Test]
    public void StarPowerAmplifierPlus_FillsBarCompletely_OnPhraseCompletion()
    {
        var engine = CreateTestEngine(starPowerPhraseGainPercent: 100);

        engine.RunAwardStarPower();

        Assert.That(engine.EngineStats.StarPowerTickAmount, Is.EqualTo(engine.TicksPerFullSpBar));
    }

    [Test]
    public void StarPowerGeneratorPlus_GrantsTenPercent_EveryTenNoteStreak()
    {
        var engine = CreateTestEngine(starPowerGeneratorStreakPercent: 10);

        for (int i = 0; i < 10; i++)
        {
            engine.RunIncrementCombo();
        }

        var expectedTicks = (uint) (engine.TicksPerFullSpBar * 0.10);
        Assert.That(engine.EngineStats.StarPowerTickAmount, Is.EqualTo(expectedTicks));
    }

    [Test]
    public void MultiplierExtenderPlus_RaisesTheMultiplierCap()
    {
        var extenderEngine = CreateTestEngine(maxMultiplierBonus: 2); // tope de guitarra 4 -> 6

        extenderEngine.EngineStats.Combo = 100; // muy por encima de cualquier umbral, debería topar

        extenderEngine.RunUpdateMultiplier();

        Assert.That(extenderEngine.EngineStats.ScoreMultiplier, Is.EqualTo(6));
    }

    private static TestFiveFretGuitarEngine CreateTestEngine(int maxMultiplierBonus = 0, int starPowerMultiplier = 2,
        int notesPerMultiplierIncrease = 10, int starPowerPhraseGainPercent = 25, int starPowerGeneratorStreakPercent = 0)
    {
        var engineParams = new GuitarEngineParameters(
            CreateHitWindowSettings(), 4 + maxMultiplierBonus, 0, 0,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            0.1, 0.1, 0.1, false, true, false, false, true,
            starPowerMultiplier, notesPerMultiplierIncrease, starPowerPhraseGainPercent, starPowerGeneratorStreakPercent);

        var notes = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, [], new(), new());
        return new TestFiveFretGuitarEngine(notes, CreateSyncTrack(), engineParams);
    }

    private sealed class TestFiveFretGuitarEngine(
        InstrumentDifficulty<GuitarNote> chart,
        SyncTrack syncTrack,
        GuitarEngineParameters engineParameters)
        : YargFiveFretGuitarEngine(chart, syncTrack, engineParameters, false)
    {
        public void RunUpdateMultiplier() => UpdateMultiplier();
        public void RunAwardStarPower() => AwardStarPower(null!);
        public void RunIncrementCombo() => IncrementCombo();
    }

    private static SyncTrack CreateSyncTrack()
    {
        var syncTrack = new SyncTrack(480);
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));
        return syncTrack;
    }

    private static HitWindowSettings CreateHitWindowSettings()
    {
        return new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
    }
}