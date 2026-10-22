using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Engine.Guitar;
using YARG.Core.Engine.Guitar.Engines;
using YARG.Core.Engine.Keys.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

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

    // Streak Guardian shield consumption. A badly-timed strum near a note produces two
    // engine events, an overstrum and that note's miss, from one player mistake, which
    // must cost one shield, not two. The two run on independent clocks:
    //   overstrum at strumTime + strum leniency   (StrumLeniencyTimer expiry)
    //   miss      at note.Time + backEnd          (hit window expiry)
    // so either can land first, and a fix handling only one ordering leaves the other
    // double-charging. Timings below are the Default preset: a flat 140 ms window
    // (70 ms either side) and 50 ms of strum leniency, or 25 ms when no note is in
    // its window.

    private const double SG_LEAD_NOTE_TIME = 0.5;      // hit, to get combo off 0 (Overstrum() no-ops at NoteIndex 0)
    private const double SG_TARGET_NOTE_TIME = 1.0;    // missed; window [0.93, 1.07]
    private const double SG_SECOND_TARGET_TIME = 1.05; // second missed note for the overlap test; window [0.98, 1.12]

    // Never reached by any scenario here. It exists so NoteIndex is never past the
    // end of the chart, because Overstrum() cancels itself outright once it is
    // ("past last note and no active sustains"). Without it the miss-first ordering
    // produces no overstrum at all and its test passes vacuously.
    private const double SG_TRAILING_NOTE_TIME = 2.0;

    [Test]
    public void StreakGuardianPlus_StrumInsideWindow_OverstrumFirst_ConsumesOneShield()
    {
        var (engine, target, _) = CreateStreakGuardianEngine();

        // Strum at 0.95, inside the target's window, still holding Green against a
        // Red note: the overstrum lands at 1.00, the miss after it at 1.07.
        RunWithInputs(engine, endTime: 1.5, LeadNoteThenStrumAt(0.95));

        using (Assert.EnterMultipleScope())
        {
            AssertOverstrummedAndMissed(engine, target);
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.EqualTo(1),
                "one mistake (overstrum first, then the miss) consumed both shields instead of one");
        }
    }

    [Test]
    public void StreakGuardianPlus_StrumInsideWindow_MissFirst_ConsumesOneShield()
    {
        var (engine, target, _) = CreateStreakGuardianEngine();

        // Strum at 1.04: still inside the window, but late enough that the leniency
        // outlives it. The miss lands first at 1.07, the overstrum after it at 1.09.
        RunWithInputs(engine, endTime: 1.5, LeadNoteThenStrumAt(1.04));

        using (Assert.EnterMultipleScope())
        {
            AssertOverstrummedAndMissed(engine, target);
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.EqualTo(1),
                "one mistake (miss first, then the overstrum) consumed both shields instead of one");
        }
    }

    [Test]
    public void StreakGuardianPlus_StrumOutsideWindow_ConsumesTwoShields()
    {
        var (engine, target, _) = CreateStreakGuardianEngine();

        // Strum at 0.85. No note is in its window yet, so the small leniency applies
        // and the overstrum lands at 0.875, before the target's window even opens
        // at 0.93. Strumming out of time and then missing the note are two separate
        // mistakes, so both shields should go. Guards a fix that coalesces too eagerly.
        RunWithInputs(engine, endTime: 1.5, LeadNoteThenStrumAt(0.85));

        using (Assert.EnterMultipleScope())
        {
            AssertOverstrummedAndMissed(engine, target);
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.Zero,
                "two unrelated mistakes were wrongly coalesced into one shield");
        }
    }

    [Test]
    public void StreakGuardianPlus_OneStrumSpanningTwoMissedNotes_ShieldsEachMissSeparately()
    {
        var (engine, target, secondTarget) = CreateStreakGuardianEngine(withSecondTargetNote: true);

        // The notes at 1.00 and 1.05 have overlapping windows and a single strum at
        // 1.00 sits inside both. Events: overstrum (1.05), miss (1.07), miss (1.12).
        // That strum may only ever pair with ONE miss, making this two logical
        // mistakes, exactly the two shields available, so the combo survives.
        // Without that one-shot rule all three events collapse into a single shield.
        RunWithInputs(engine, endTime: 1.6, LeadNoteThenStrumAt(1.00));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.Overstrums, Is.EqualTo(1),
                "scenario did not produce exactly one overstrum");
            Assert.That(target.WasMissed && secondTarget!.WasMissed, Is.True,
                "scenario did not miss both target notes");
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1),
                "a third break event got through unshielded and reset the combo");
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.Zero,
                "both shields should have been spent, one per missed note");
        }
    }

    [Test]
    public void StreakGuardianPlus_StrumThenCorrectFretWithinLeniency_SpendsNoShield()
    {
        var (engine, target, _) = CreateStreakGuardianEngine();

        // Strum at 0.96 on the wrong fret, then land Red at 0.98, still inside both
        // the strum leniency (ends 1.01) and the note's window. This is a legitimate
        // late hit, not a mistake: no overstrum, no miss, nothing to shield. Guards a
        // fix that starts charging shields on successful late hits.
        var inputs = new List<GameInput>
        {
            GameInput.Create(SG_LEAD_NOTE_TIME, GuitarAction.GreenFret, true),
            GameInput.Create(SG_LEAD_NOTE_TIME, GuitarAction.StrumDown, true),
            GameInput.Create(0.96, GuitarAction.StrumUp, true),
            GameInput.Create(0.98, GuitarAction.GreenFret, false),
            GameInput.Create(0.98, GuitarAction.RedFret, true),
        };

        RunWithInputs(engine, endTime: 1.5, inputs);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.WasHit, Is.True, "the late-but-legal hit did not land");
            Assert.That(engine.EngineStats.Overstrums, Is.Zero, "a legitimate late hit was counted as an overstrum");
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.EqualTo(2), "a shield was spent on a clean hit");
        }
    }

    // The same rule under Precision, whose window is dynamic: it shrinks with note
    // density instead of sitting at a flat 140 ms. The pairing carries no constant of
    // its own, so it should follow any preset; these pin that down. Both strum times
    // sit far enough inside/outside to hold for any window this config can produce
    // (40 to 120 ms), so neither test has to predict the dynamic curve's output.

    // Precision preset's five-fret guitar values, from EnginePreset.Defaults.cs.
    private static HitWindowSettings PrecisionHitWindow() =>
        new(0.12, 0.04, 1.0, true, 0.93, 1.0, 1.5, 0.160, 0.080);

    [Test]
    public void StreakGuardianPlus_PrecisionPreset_StrumInsideWindow_ConsumesOneShield()
    {
        var (engine, target, _) = CreateStreakGuardianEngine(
            hitWindow: PrecisionHitWindow(), strumLeniency: 0.04, strumLeniencySmall: 0.02);

        // 15 ms before the note: inside the window even at its narrowest, 20 ms a side.
        RunWithInputs(engine, endTime: 1.5, LeadNoteThenStrumAt(SG_TARGET_NOTE_TIME - 0.015));

        using (Assert.EnterMultipleScope())
        {
            AssertOverstrummedAndMissed(engine, target);
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.EqualTo(1),
                "one mistake consumed both shields instead of one under a dynamic hit window");
        }
    }

    [Test]
    public void StreakGuardianPlus_PrecisionPreset_StrumOutsideWindow_ConsumesTwoShields()
    {
        var (engine, target, _) = CreateStreakGuardianEngine(
            hitWindow: PrecisionHitWindow(), strumLeniency: 0.04, strumLeniencySmall: 0.02);

        // 200 ms before the note: outside the window even at its widest, 60 ms a side.
        RunWithInputs(engine, endTime: 1.5, LeadNoteThenStrumAt(SG_TARGET_NOTE_TIME - 0.2));

        using (Assert.EnterMultipleScope())
        {
            AssertOverstrummedAndMissed(engine, target);
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.Zero,
                "two unrelated mistakes were wrongly coalesced under a dynamic hit window");
        }
    }

    // The same rule on drums: the spurious input is an overhit, but the behaviour must
    // be identical. Drums differ only in plumbing, since Overhit() is raised inline as the
    // pad is struck, so CurrentTime already is the input time, whereas guitar has to
    // remember its strum time because its overstrum arrives 25-50 ms late.

    [Test]
    public void StreakGuardianPlus_Drums_OverhitInsideWindow_ConsumesOneShield()
    {
        var (engine, target) = CreateStreakGuardianDrumsEngine();

        // Green pad at 0.95, inside the Yellow target's window: the overhit lands at
        // 0.95, the miss after it at 1.07. One mistake, so one shield.
        RunWithInputs(engine, endTime: 1.5, LeadPadThenWrongPadAt(0.95));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1), "scenario did not produce exactly one overhit");
            Assert.That(target.WasMissed, Is.True, "scenario did not miss the target note");
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.EqualTo(1),
                "one mistake (overhit then miss) consumed both shields instead of one");
        }
    }

    [Test]
    public void StreakGuardianPlus_Drums_OverhitOutsideWindow_ConsumesTwoShields()
    {
        var (engine, target) = CreateStreakGuardianDrumsEngine();

        // Green pad at 0.85, before the target's window opens at 0.93. Two separate
        // mistakes, so two shields. The drums counterpart of the guitar guard.
        RunWithInputs(engine, endTime: 1.5, LeadPadThenWrongPadAt(0.85));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1), "scenario did not produce exactly one overhit");
            Assert.That(target.WasMissed, Is.True, "scenario did not miss the target note");
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.Zero,
                "two unrelated mistakes were wrongly coalesced into one shield");
        }
    }

    // Red pad on the lead note, then the Green pad (which matches nothing) at the
    // given time. Velocity 1.0 clears the engine's 0.35 velocity threshold.
    private static List<GameInput> LeadPadThenWrongPadAt(double overhitTime) => new()
    {
        GameInput.Create(SG_LEAD_NOTE_TIME, DrumsAction.RedDrum, 1.0f),
        GameInput.Create(overhitTime, DrumsAction.GreenDrum, 1.0f),
    };

    private static (YargDrumsEngine Engine, DrumNote Target) CreateStreakGuardianDrumsEngine()
    {
        var leadNote = CreateDrumNote(FourLaneDrumPad.RedDrum, SG_LEAD_NOTE_TIME);
        var targetNote = CreateDrumNote(FourLaneDrumPad.YellowDrum, SG_TARGET_NOTE_TIME);
        // Same reason as the guitar chart: Overhit() cancels itself once NoteIndex is
        // past the last note, which would void the scenario entirely.
        var trailingNote = CreateDrumNote(FourLaneDrumPad.BlueDrum, SG_TRAILING_NOTE_TIME);

        var noteList = new List<DrumNote> { leadNote, targetNote, trailingNote };
        for (int i = 1; i < noteList.Count; i++)
        {
            noteList[i].PreviousNote = noteList[i - 1];
            noteList[i - 1].NextNote = noteList[i];
        }

        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
        var engineParams = new DrumsEngineParameters(hitWindow, 4,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            DrumsEngineParameters.DrumMode.ProFourLane,
            noStarPowerOverlap: false, enableLanes: true,
            streakGuardianMaxShields: 2);

        var diff = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert,
            noteList, new(), new());

        var syncTrack = new SyncTrack(SG_RESOLUTION);
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));

        return (new YargDrumsEngine(diff, syncTrack, engineParams, false, false), targetNote);
    }

    private static DrumNote CreateDrumNote(FourLaneDrumPad pad, double time) =>
        new(pad, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, time, SgTickOf(time));

    [Test]
    public void StreakGuardianPlus_ShieldedOverstrum_StillBreaksTheStarPowerPhrase([Values(0, 2)] int shields)
    {
        // A star power phrase over 1.5 / 2.0 / 2.5, entered by hitting its first note.
        var lead = CreateGuitarNote(FiveFretGuitarFret.Green, 0.5);
        var spStart = CreateGuitarNote(FiveFretGuitarFret.Red, 1.5, NoteFlags.StarPower | NoteFlags.StarPowerStart);
        var spMid = CreateGuitarNote(FiveFretGuitarFret.Yellow, 2.0, NoteFlags.StarPower);
        var spEnd = CreateGuitarNote(FiveFretGuitarFret.Blue, 2.5, NoteFlags.StarPower | NoteFlags.StarPowerEnd);

        var engine = CreateEngineFor([lead, spStart, spMid, spEnd], shields);

        var inputs = new List<GameInput>
        {
            GameInput.Create(0.5, GuitarAction.GreenFret, true),
            GameInput.Create(0.5, GuitarAction.StrumDown, true),
            GameInput.Create(1.5, GuitarAction.GreenFret, false),
            GameInput.Create(1.5, GuitarAction.RedFret, true),
            GameInput.Create(1.5, GuitarAction.StrumUp, true),
            // Overstrum in the gap at 1.75, where no note is close enough to be missed.
            // That isolates the overstrum: it is the only thing that can strip the phrase.
            GameInput.Create(1.75, GuitarAction.StrumDown, true),
        };

        // Stops at 2.05, before spMid's window closes at 2.07. Past that the note is
        // missed and MissNote's own unconditional strip would mask the result.
        RunWithInputs(engine, endTime: 2.05, inputs);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(spStart.WasHit, Is.True, "scenario never entered the star power phrase");
            Assert.That(engine.EngineStats.Overstrums, Is.EqualTo(1), "scenario did not produce exactly one overstrum");
            Assert.That(spMid.WasMissed, Is.False, "scenario missed a note, which strips the phrase on its own");

            // The shield protects the streak, not the phrase, so this holds whether or not
            // shields were held, matching quickplay, drums, keys and every miss path.
            Assert.That(spMid.IsStarPower, Is.False, "a shielded overstrum kept its star power phrase alive");
            Assert.That(spEnd.IsStarPower, Is.False, "a shielded overstrum kept its star power phrase alive");
        }
    }

    private static YargFiveFretGuitarEngine CreateEngineFor(List<GuitarNote> notes, int shields)
    {
        for (int i = 1; i < notes.Count; i++)
        {
            notes[i].PreviousNote = notes[i - 1];
            notes[i - 1].NextNote = notes[i];
        }

        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
        var engineParams = new GuitarEngineParameters(hitWindow, 4, 0, 0,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            hopoLeniency: 0.08, strumLeniency: 0.05, strumLeniencySmall: 0.025,
            infiniteFrontEnd: false, antiGhosting: true, soloTaps: false,
            noStarPowerOverlap: false, enableLanes: true,
            streakGuardianMaxShields: shields);

        var diff = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert,
            notes, new(), new());

        var syncTrack = new SyncTrack(SG_RESOLUTION);
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));

        return new YargFiveFretGuitarEngine(diff, syncTrack, engineParams, isBot: false);
    }

    [Test]
    public void StreakGuardianPlus_TwoSeparateMistakes_CostTwoShieldsAndKeepTheCombo()
    {
        var lead = CreateGuitarNote(FiveFretGuitarFret.Green, 0.5);
        var note1 = CreateGuitarNote(FiveFretGuitarFret.Red, 1.00);
        var note2 = CreateGuitarNote(FiveFretGuitarFret.Yellow, 1.05);
        var trailing = CreateGuitarNote(FiveFretGuitarFret.Orange, 3.0);

        var engine = CreateEngineFor([lead, note1, note2, trailing], shields: 2);

        // Strums at 0.95 and 1.05, each inside its own note's window, interleaving as
        // overstrum(1.00) miss(1.07) overstrum(1.10) miss(1.12). Two mistakes, so both
        // shields go but the combo lives. The counterpart of the over-forgiving guards:
        // pairing must not become so eager that two mistakes cost only one shield.
        var inputs = new List<GameInput>
        {
            GameInput.Create(0.5, GuitarAction.GreenFret, true),
            GameInput.Create(0.5, GuitarAction.StrumDown, true),
            GameInput.Create(0.95, GuitarAction.StrumUp, true),
            GameInput.Create(1.05, GuitarAction.StrumDown, true),
        };

        RunWithInputs(engine, endTime: 2.0, inputs);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.Overstrums, Is.EqualTo(2), "scenario did not produce two overstrums");
            Assert.That(note1.WasMissed && note2.WasMissed, Is.True, "scenario did not miss both notes");
            Assert.That(engine.EngineStats.StreakGuardianShields, Is.Zero, "two mistakes should spend both shields");
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1), "two mistakes with two shields broke the combo");
        }
    }

    // Crowd Hyper. The song starts with the meter full, and hits made while it stays there fill an
    // overflow bar instead of being clamped away. All five bonus stars cost the happiness of a share
    // of the chart's notes hit at a full meter, so the bar scales with the chart.

    private const float HAPPINESS_PER_HIT = EngineManager.HAPPINESS_PER_NOTE_HIT;

    // Per-star threshold for the BaseStats tests, which set it directly.
    private const float CROWD_HYPER_OVERFLOW_PER_STAR = 0.5f;

    // Share of the chart handed to the engine, which derives the per-star threshold from it.
    private const float CROWD_HYPER_CHART_FRACTION = 0.9f;

    [Test]
    public void CrowdHyperPlus_AwardsOneStarPerOverflowBar()
    {
        var stats = new GuitarStats { CrowdHyperOverflowPerStar = CROWD_HYPER_OVERFLOW_PER_STAR };

        using (Assert.EnterMultipleScope())
        {
            stats.CrowdHyperOverflow = 0.49f;
            Assert.That(stats.CrowdHyperBonusStars, Is.Zero);

            stats.CrowdHyperOverflow = 0.5f;
            Assert.That(stats.CrowdHyperBonusStars, Is.EqualTo(1));

            stats.CrowdHyperOverflow = 2.5f;
            Assert.That(stats.CrowdHyperBonusStars, Is.EqualTo(5));

            stats.CrowdHyperOverflow = 20f;
            Assert.That(stats.CrowdHyperBonusStars, Is.EqualTo(5), "bonus stars went past the cap of five");
        }
    }

    [Test]
    public void CrowdHyperPlus_AwardsNothingWhenThePowerIsInactive()
    {
        // PerStar is 0 for every player without the power, which must not divide by zero
        // or hand out stars for overflow that could never have accumulated.
        var stats = new GuitarStats { CrowdHyperOverflowPerStar = 0f, CrowdHyperOverflow = 20f };

        Assert.That(stats.CrowdHyperBonusStars, Is.Zero);
    }

    [Test]
    public void CrowdHyperPlus_StartsTheSongWithTheCrowdMaxed()
    {
        var (_, _, withPower) = CreateCrowdHyperSetup(CROWD_HYPER_CHART_FRACTION);
        var (_, _, without) = CreateCrowdHyperSetup(0f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withPower.Happiness, Is.EqualTo(1f), "the power did not start the crowd maxed");
            Assert.That(without.Happiness, Is.EqualTo(RockMeterPreset.Normal.StartingHappiness),
                "a player without the power no longer starts at the preset's happiness");
        }
    }

    [Test]
    public void CrowdHyperPlus_AccumulatesOverflowWhileTheMeterIsMaxed()
    {
        var (_, engine, _) = CreateCrowdHyperSetup(CROWD_HYPER_CHART_FRACTION);

        RunWithInputs(engine, endTime: 1.5, HitGreenNotesAt(CrowdHyperNoteTimes));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(CrowdHyperNoteTimes.Length),
                "scenario did not hit every note");
            Assert.That(engine.EngineStats.CrowdHyperOverflow,
                Is.EqualTo(CrowdHyperNoteTimes.Length * HAPPINESS_PER_HIT).Within(1e-5f));
        }
    }

    [Test]
    public void CrowdHyperPlus_StopsAccumulatingOnceTheMeterDropsBelowMax()
    {
        var (_, engine, container) = CreateCrowdHyperSetup(CROWD_HYPER_CHART_FRACTION, missSecondNote: true);

        // The second note is Red while Green is held, so it is missed without an overstrum.
        // One miss costs four hits' worth of happiness, so the three hits that follow only
        // climb back partway and none of them may spill.
        RunWithInputs(engine, endTime: 1.5, HitGreenNotesAt(new[] { 0.5, 0.8, 0.9, 1.0 }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(container.Happiness, Is.LessThan(1f), "the meter never actually dropped");
            Assert.That(engine.EngineStats.CrowdHyperOverflow, Is.EqualTo(HAPPINESS_PER_HIT).Within(1e-5f),
                "overflow kept accumulating after the meter fell below the top");
        }
    }

    [Test]
    public void CrowdHyperPlus_OverflowRateIgnoresThePlayersRockMeterPreset()
    {
        // Casual doubles HitRecoveryMultiplier. The player's meter refills twice as fast,
        // but the power must ask the same of everyone, so the overflow rate stays put.
        var (_, engine, _) = CreateCrowdHyperSetup(CROWD_HYPER_CHART_FRACTION, meterPreset: RockMeterPreset.Casual);

        RunWithInputs(engine, endTime: 1.5, HitGreenNotesAt(CrowdHyperNoteTimes));

        Assert.That(engine.EngineStats.CrowdHyperOverflow,
            Is.EqualTo(CrowdHyperNoteTimes.Length * HAPPINESS_PER_HIT).Within(1e-5f),
            "the overflow rate followed the player's rock meter preset");
    }

    [Test]
    public void CrowdHyperPlus_BonusStarsReachTheBandStarCount()
    {
        // The HUD reads the band's star count, which sums bonus stars separately from the
        // per-engine count. Measured as a difference so score-based stars don't factor in.
        var (manager, engine, _) = CreateCrowdHyperSetup(CROWD_HYPER_CHART_FRACTION);

        manager.UpdateStars();
        float before = manager.Stars;

        engine.EngineStats.CrowdHyperOverflow = 2 * engine.EngineStats.CrowdHyperOverflowPerStar;
        manager.UpdateStars();

        Assert.That(manager.Stars - before, Is.EqualTo(2f).Within(1e-4f),
            "Crowd Hyper bonus stars never made it into the band's star count");
    }

    [Test]
    public void CrowdHyperPlus_ThresholdScalesWithTheChartsNoteCount()
    {
        // Same fraction, twice the notes: each bonus star must cost twice the overflow, so the power
        // asks the same share of any chart instead of a fixed amount that short charts can't reach.
        float shortChart = CreateGuitarEngineWithNotes(200, CROWD_HYPER_CHART_FRACTION).EngineStats.CrowdHyperOverflowPerStar;
        float longChart = CreateGuitarEngineWithNotes(400, CROWD_HYPER_CHART_FRACTION).EngineStats.CrowdHyperOverflowPerStar;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shortChart, Is.GreaterThan(0f), "the threshold was never computed");
            Assert.That(longChart, Is.EqualTo(shortChart * 2).Within(1e-6f),
                "doubling the chart did not double what each bonus star costs");
        }
    }

    private static YargFiveFretGuitarEngine CreateGuitarEngineWithNotes(int noteCount, float chartFraction)
    {
        var notes = new List<GuitarNote>();
        for (int i = 0; i < noteCount; i++)
        {
            notes.Add(CreateGuitarNote(FiveFretGuitarFret.Green, 0.5 + i * 0.25));
        }

        for (int i = 1; i < notes.Count; i++)
        {
            notes[i].PreviousNote = notes[i - 1];
            notes[i - 1].NextNote = notes[i];
        }

        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
        var engineParams = new GuitarEngineParameters(hitWindow, 4, 0, 0,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            hopoLeniency: 0.08, strumLeniency: 0.05, strumLeniencySmall: 0.025,
            infiniteFrontEnd: false, antiGhosting: true, soloTaps: false,
            noStarPowerOverlap: false, enableLanes: true,
            crowdHyperChartFraction: chartFraction);

        var diff = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert, notes, new(), new());
        var syncTrack = new SyncTrack(SG_RESOLUTION);
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));

        return new YargFiveFretGuitarEngine(diff, syncTrack, engineParams, isBot: false);
    }

    [Test]
    public void CrowdHyperPlus_PerfectRunWithoutStarPower_EarnsAllFiveStars([Values("guitar", "drums", "keys")] string instrument)
    {
        // A clean run on a real chart must fill the bar on every instrument, counting each chord note on drums
        // and keys. Star Power stays off because the bot fires it whenever it can, and sustained phrase notes
        // fill the bar even with phrase gain at zero.
        var chart = GetChart();
        var manager = new EngineManager();

        BaseEngine engine;
        double endTime;
        switch (instrument)
        {
            case "guitar":
            {
                var notes = chart.FiveFretGuitar.GetDifficulty(Difficulty.Expert);
                var p = EnginePreset.Default.FiveFretGuitar.Create(StarMultiplierThresholds, SoloBonusStarMultiplierThresholds, false,
                    starPowerPhraseGainPercent: 0, crowdHyperChartFraction: CROWD_HYPER_CHART_FRACTION);
                var e = new YargFiveFretGuitarEngine(notes, chart.SyncTrack, p, isBot: true);
                e.AllowStarPower(false);
                manager.Register(e, notes, chart, RockMeterPreset.Normal);
                engine = e; endTime = notes.GetEndTime();
                break;
            }
            case "drums":
            {
                var notes = chart.ProDrums.GetDifficulty(Difficulty.Expert);
                var p = EnginePreset.Default.Drums.Create(StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
                    DrumsEngineParameters.DrumMode.ProFourLane,
                    starPowerPhraseGainPercent: 0, crowdHyperChartFraction: CROWD_HYPER_CHART_FRACTION);
                var e = new YargDrumsEngine(notes, chart.SyncTrack, p, true, false);
                e.AllowStarPower(false);
                manager.Register(e, notes, chart, RockMeterPreset.Normal);
                engine = e; endTime = notes.GetEndTime();
                break;
            }
            default:
            {
                var notes = chart.Keys.GetDifficulty(Difficulty.Expert);
                var p = EnginePreset.Default.ProKeys.Create(StarMultiplierThresholds, SoloBonusStarMultiplierThresholds, false,
                    starPowerPhraseGainPercent: 0, crowdHyperChartFraction: CROWD_HYPER_CHART_FRACTION);
                var e = new YargFiveLaneKeysEngine(notes, chart.SyncTrack, p, isBot: true);
                e.AllowStarPower(false);
                manager.Register(e, notes, chart, RockMeterPreset.Normal);
                engine = e; endTime = notes.GetEndTime();
                break;
            }
        }

        manager.InitializeHappiness(true);

        bool spEverActive = false;
        for (double t = 0; t <= endTime + 1; t += 0.01)
        {
            engine.Update(t);
            spEverActive |= engine.BaseStats.IsStarPowerActive;
        }

        var s = engine.BaseStats;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spEverActive, Is.False, "Star Power activated, so the run doesn't measure the base rate");
            Assert.That(s.NotesHit, Is.EqualTo(s.TotalNotes), "the bot did not hit every note");
            Assert.That(s.CrowdHyperOverflow / HAPPINESS_PER_HIT, Is.EqualTo(s.TotalNotes).Within(0.5),
                "some hit notes never reached the overflow bar");
            Assert.That(s.CrowdHyperBonusStars, Is.EqualTo(5),
                "a perfect run without Star Power did not earn all five bonus stars");
        }
    }

    private static readonly double[] CrowdHyperNoteTimes = { 0.5, 0.6, 0.7, 0.8, 0.9 };

    // Holds Green for the whole run and strums on each note time, alternating strum
    // direction so every strum registers as its own press.
    private static List<GameInput> HitGreenNotesAt(double[] times)
    {
        var inputs = new List<GameInput> { GameInput.Create(times[0] - 0.05, GuitarAction.GreenFret, true) };
        for (int i = 0; i < times.Length; i++)
        {
            var strum = i % 2 == 0 ? GuitarAction.StrumDown : GuitarAction.StrumUp;
            inputs.Add(GameInput.Create(times[i], strum, true));
        }

        return inputs;
    }

    private static (EngineManager Manager, YargFiveFretGuitarEngine Engine, EngineManager.EngineContainer Container)
        CreateCrowdHyperSetup(float chartFraction, RockMeterPreset? meterPreset = null, bool missSecondNote = false)
    {
        var notes = new List<GuitarNote>();
        foreach (var time in CrowdHyperNoteTimes)
        {
            // The second note becomes Red when the scenario wants it missed: Green stays
            // held throughout, so it cannot be hit and no strum is aimed at it.
            bool makeUnhittable = missSecondNote && notes.Count == 1;
            notes.Add(CreateGuitarNote(makeUnhittable ? FiveFretGuitarFret.Red : FiveFretGuitarFret.Green, time));
        }

        for (int i = 1; i < notes.Count; i++)
        {
            notes[i].PreviousNote = notes[i - 1];
            notes[i - 1].NextNote = notes[i];
        }

        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
        var engineParams = new GuitarEngineParameters(hitWindow, 4, 0, 0,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            hopoLeniency: 0.08, strumLeniency: 0.05, strumLeniencySmall: 0.025,
            infiniteFrontEnd: false, antiGhosting: true, soloTaps: false,
            noStarPowerOverlap: false, enableLanes: true,
            crowdHyperChartFraction: chartFraction);

        var diff = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert,
            notes, new(), new());

        var chart = new SongChart(SG_RESOLUTION);
        chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));

        var engine = new YargFiveFretGuitarEngine(diff, chart.SyncTrack, engineParams, isBot: false);

        var manager = new EngineManager();
        var container = manager.Register(engine, diff, chart, meterPreset ?? RockMeterPreset.Normal);
        manager.InitializeHappiness(false);

        return (manager, engine, container);
    }

    // Guards against a scenario passing vacuously: the overstrum and the miss whose
    // interaction is under test must both actually have happened.
    private static void AssertOverstrummedAndMissed(YargFiveFretGuitarEngine engine, GuitarNote target)
    {
        Assert.That(engine.EngineStats.Overstrums, Is.EqualTo(1),
            "scenario did not produce exactly one overstrum");
        Assert.That(target.WasMissed, Is.True, "scenario did not miss the target note");
    }

    private const int SG_RESOLUTION = 480;

    // 120 bpm at 480 ticks/quarter -> 960 ticks per second.
    private static uint SgTickOf(double time) => (uint) (time * 960);

    // Hits the lead note, then strums once more at the given time. Green stays held
    // throughout, so that second strum lands on a note it can't match. It uses
    // StrumUp (the lead note used StrumDown) so it registers as its own button press
    // rather than a repeat of one already held down.
    private static List<GameInput> LeadNoteThenStrumAt(double strumTime) => new()
    {
        GameInput.Create(SG_LEAD_NOTE_TIME, GuitarAction.GreenFret, true),
        GameInput.Create(SG_LEAD_NOTE_TIME, GuitarAction.StrumDown, true),
        GameInput.Create(strumTime, GuitarAction.StrumUp, true),
    };

    private static void RunWithInputs(BaseEngine engine, double endTime, List<GameInput> inputs)
    {
        int next = 0;
        for (double time = 0; time <= endTime; time += 0.005)
        {
            while (next < inputs.Count && inputs[next].Time <= time)
            {
                var input = inputs[next];
                engine.QueueInput(ref input);
                next++;
            }

            engine.Update(time);
        }
    }

    private static (YargFiveFretGuitarEngine Engine, GuitarNote Target, GuitarNote? SecondTarget)
        CreateStreakGuardianEngine(bool withSecondTargetNote = false, HitWindowSettings? hitWindow = null,
            double strumLeniency = 0.05, double strumLeniencySmall = 0.025)
    {
        var leadNote = CreateGuitarNote(FiveFretGuitarFret.Green, SG_LEAD_NOTE_TIME);
        var targetNote = CreateGuitarNote(FiveFretGuitarFret.Red, SG_TARGET_NOTE_TIME);

        var noteList = new List<GuitarNote> { leadNote, targetNote };

        GuitarNote? secondTargetNote = null;
        if (withSecondTargetNote)
        {
            secondTargetNote = CreateGuitarNote(FiveFretGuitarFret.Yellow, SG_SECOND_TARGET_TIME);
            noteList.Add(secondTargetNote);
        }

        noteList.Add(CreateGuitarNote(FiveFretGuitarFret.Orange, SG_TRAILING_NOTE_TIME));

        for (int i = 1; i < noteList.Count; i++)
        {
            noteList[i].PreviousNote = noteList[i - 1];
            noteList[i - 1].NextNote = noteList[i];
        }

        // Defaults to a fixed 140 ms window and 50 ms strum leniency, matching the
        // Default engine preset. Default (and Casual) use a non-dynamic window, so
        // this is the real shape of the window for most players, and the widest one,
        // which gives the coalescing the most room to over-forgive.
        hitWindow ??= new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
        var engineParams = new GuitarEngineParameters(hitWindow, 4, 0, 0,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            hopoLeniency: 0.08, strumLeniency: strumLeniency, strumLeniencySmall: strumLeniencySmall,
            infiniteFrontEnd: false, antiGhosting: true, soloTaps: false,
            noStarPowerOverlap: false, enableLanes: true,
            streakGuardianMaxShields: 2);

        var diff = new InstrumentDifficulty<GuitarNote>(Instrument.FiveFretGuitar, Difficulty.Expert,
            noteList, new(), new());

        var syncTrack = new SyncTrack(SG_RESOLUTION);
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));

        return (new YargFiveFretGuitarEngine(diff, syncTrack, engineParams, isBot: false), targetNote, secondTargetNote);
    }

    private static GuitarNote CreateGuitarNote(FiveFretGuitarFret fret, double time,
        NoteFlags flags = NoteFlags.None) =>
        new(fret, GuitarNoteType.Strum, GuitarNoteFlags.None, flags, time, 0, SgTickOf(time), 0);

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
