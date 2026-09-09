using System.IO;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.Engine.Guitar
{
    public class GuitarStats : BaseStats
    {
        /// <summary>
        /// Number of overstrums which have occurred.
        /// </summary>
        public int Overstrums;

        /// <summary>
        /// Number of hammer-ons/pull-offs which have been strummed.
        /// </summary>
        public int HoposStrummed;

        /// <summary>
        /// Number of ghost inputs the player has made.
        /// </summary>
        public int GhostInputs;

        /// <summary>
        /// True if the player is currently allowed to hammer-on/pull-off without strumming. Normally mirrors <see cref="BaseStats.Combo"/> being non-zero, but Streak Guardian can decouple the two: a shield-saved miss clears this even though Combo is preserved, while a shield-saved overstrum leaves it untouched.
        /// </summary>
        public bool CanHopo = true;
        public override bool IsFullCombo => base.IsFullCombo && Overstrums == 0;

        public GuitarStats()
        {
        }

        public GuitarStats(GuitarStats stats) : base(stats)
        {
            Overstrums = stats.Overstrums;
            HoposStrummed = stats.HoposStrummed;
            GhostInputs = stats.GhostInputs;
            CanHopo = stats.CanHopo;
        }

        public GuitarStats(ref FixedArrayStream stream, int version)
            : base(ref stream, version)
        {
            Overstrums = stream.Read<int>(Endianness.Little);
            HoposStrummed = stream.Read<int>(Endianness.Little);
            GhostInputs = stream.Read<int>(Endianness.Little);
        }

        public override void Reset()
        {
            base.Reset();
            Overstrums = 0;
            HoposStrummed = 0;
            GhostInputs = 0;
            CanHopo = true;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Overstrums);
            writer.Write(HoposStrummed);
            writer.Write(GhostInputs);
        }

        public override ReplayStats ConstructReplayStats(string name, bool isReplayPlayer)
        {
            return new GuitarReplayStats(name, isReplayPlayer, this);
        }
    }
}