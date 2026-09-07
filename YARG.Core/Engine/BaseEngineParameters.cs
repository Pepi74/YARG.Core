using System.Globalization;
using System.IO;
using System.Linq;
using YARG.Core.Extensions;
using YARG.Core.IO;

namespace YARG.Core.Engine
{
    public abstract class BaseEngineParameters
    {
        public readonly HitWindowSettings HitWindow;

        public readonly int MaxMultiplier;

        /// <summary>
        /// The instrument's max multiplier WITHOUT any power-based bonus (e.g. Multiplier Extender).
        /// Used when calculating the reference "perfect FC" score so that star thresholds stay a fixed
        /// bar regardless of active powers.
        /// </summary>
        public readonly int BaseMaxMultiplier;

        public readonly double StarPowerWhammyBuffer;

        public readonly double SustainDropLeniency;

        public readonly float[] StarMultiplierThresholds;

        public readonly float[] SoloBonusStarMultiplierThresholds;

        public readonly bool EnableLanes;

        public readonly int StarPowerMultiplier;

        public readonly int NotesPerMultiplierIncrease;

        public readonly int StarPowerPhraseGainPercent;

        public readonly int StarPowerGeneratorStreakPercent;
        public readonly int BaseMultiplierOffset;
        public readonly int SpeedFreakBonusThreshold;
        public readonly double SpeedFreakBonusSongLength;

        public double SongSpeed;

        protected BaseEngineParameters(HitWindowSettings hitWindow, int maxMultiplier, double spWhammyBuffer,
            double sustainDropLeniency, float[] starMultiplierThresholds, float[] soloBonusStarMultiplierThresholds, bool enableLanes, int starPowerMultiplier = 2, int notesPerMultiplierIncrease = 10, int starPowerPhraseGainPercent = 25, int starPowerGeneratorStreakPercent = 0, int? baseMaxMultiplier = null, int baseMultiplierOffset = 1, int speedFreakBonusThreshold = 0, double speedFreakBonusSongLength = 0)
        {
            HitWindow = hitWindow;
            StarPowerWhammyBuffer = spWhammyBuffer;
            SustainDropLeniency = sustainDropLeniency;
            MaxMultiplier = maxMultiplier;
            BaseMaxMultiplier = baseMaxMultiplier ?? maxMultiplier;
            StarMultiplierThresholds = starMultiplierThresholds;
            SoloBonusStarMultiplierThresholds = soloBonusStarMultiplierThresholds;
            EnableLanes = enableLanes;
            StarPowerMultiplier = starPowerMultiplier;
            NotesPerMultiplierIncrease = notesPerMultiplierIncrease;
            StarPowerPhraseGainPercent = starPowerPhraseGainPercent;
            StarPowerGeneratorStreakPercent = starPowerGeneratorStreakPercent;
            BaseMultiplierOffset = baseMultiplierOffset;
            SpeedFreakBonusThreshold = speedFreakBonusThreshold;
            SpeedFreakBonusSongLength = speedFreakBonusSongLength;
        }

        protected BaseEngineParameters(ref FixedArrayStream stream, int version)
        {
            HitWindow = new HitWindowSettings(ref stream, version);
            MaxMultiplier = stream.Read<int>(Endianness.Little);
            StarPowerWhammyBuffer = stream.Read<double>(Endianness.Little);

            // Version 7 but DATA_MIN was increased so no need to version check
            SustainDropLeniency = stream.Read<double>(Endianness.Little);

            // Read star multiplier thresholds
            int count = stream.Read<int>(Endianness.Little);
            StarMultiplierThresholds = new float[count];
            for (int i = 0; i < StarMultiplierThresholds.Length; i++)
            {
                // The way BaseScore is calculated changed in version 12, so the thresholds are stored differently (multiplied by the max multiplier) for older versions
                int factor = version >= 13 ? 1 : MaxMultiplier;
                StarMultiplierThresholds[i] = stream.Read<float>(Endianness.Little) / factor;
            }

            if (version >= 13)
            {
                // Read solo star multiplier thresholds
                int starMultCount = stream.Read<int>(Endianness.Little);
                SoloBonusStarMultiplierThresholds = new float[starMultCount];
                for (int i = 0; i < SoloBonusStarMultiplierThresholds.Length; i++)
                {
                    SoloBonusStarMultiplierThresholds[i] = stream.Read<float>(Endianness.Little);
                }
            }
            else
            {
                // Before version 12, solo bonuses had no effect on scoring.
                SoloBonusStarMultiplierThresholds = new float[6];
            }

            SongSpeed = stream.Read<double>(Endianness.Little);

            // Version 10 and higher
            if (version >= 10)
            {
                EnableLanes = stream.ReadBoolean();
            }

            // Intentionally not deserialized yet. A replay recorded with powers active will simply fall back to default values on playback.
            StarPowerMultiplier = 2;
            NotesPerMultiplierIncrease = 10;
            StarPowerPhraseGainPercent = 25;
            StarPowerGeneratorStreakPercent = 0;
            BaseMultiplierOffset = 1;
            SpeedFreakBonusThreshold = 0;
            BaseMaxMultiplier = MaxMultiplier;
        }

        public virtual void Serialize(BinaryWriter writer)
        {
            HitWindow.Serialize(writer);
            writer.Write(MaxMultiplier);
            writer.Write(StarPowerWhammyBuffer);

            writer.Write(SustainDropLeniency);

            // Write star multiplier thresholds
            writer.Write(StarMultiplierThresholds.Length);
            foreach (var f in StarMultiplierThresholds)
            {
                writer.Write(f);
            }

            // Write solo multiplier star thresholds
            writer.Write(SoloBonusStarMultiplierThresholds.Length);
            foreach (var f in SoloBonusStarMultiplierThresholds)
            {
                writer.Write(f);
            }

            writer.Write(SongSpeed);
            writer.Write(EnableLanes);
        }

        public override string ToString()
        {
            var thresholds = string.Join(", ",
                StarMultiplierThresholds.Select(i => i.ToString(CultureInfo.InvariantCulture)));

            return
                $"Hit window: ({HitWindow.MinWindow}, {HitWindow.MaxWindow})\n" +
                $"Hit window dynamic: {HitWindow.IsDynamic}\n" +
                $"Max multiplier: {MaxMultiplier}\n" +
                $"Star thresholds: {thresholds}";
        }
    }
}