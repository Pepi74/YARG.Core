using NUnit.Framework;
using YARG.Core.Engine;
using YARG.Core.Engine.Guitar;

namespace YARG.Core.UnitTests.Engine;

public class PowerChallengeEngineParametersTests : EngineTester
{
    [Test]
    public void EngineParams_DefaultToVanillaBehavior_WhenNoPowersSpecified()
    {
        var engineParams = new GuitarEngineParameters(
            CreateHitWindowSettings(), 4, 0, 0,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            0.1, 0.1, 0.1, false, true, false, false, true);

        Assert.That(engineParams.StarPowerMultiplier, Is.EqualTo(2));
        Assert.That(engineParams.NotesPerMultiplierIncrease, Is.EqualTo(10));
    }

    [Test]
    public void EngineParams_AcceptOverrides_ForStarPowerNovaPlus()
    {
        var engineParams = new GuitarEngineParameters(
            CreateHitWindowSettings(), 4, 0, 0,
            StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            0.1, 0.1, 0.1, false, true, false, false, true,
            starPowerMultiplier: 6, notesPerMultiplierIncrease: 5);

        Assert.That(engineParams.StarPowerMultiplier, Is.EqualTo(6));
        Assert.That(engineParams.NotesPerMultiplierIncrease, Is.EqualTo(5));
    }

    private static HitWindowSettings CreateHitWindowSettings()
    {
        return new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25);
    }
}