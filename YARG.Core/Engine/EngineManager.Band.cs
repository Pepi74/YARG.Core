using System;
using System.Collections.Generic;
using YARG.Core.Logging;

namespace YARG.Core.Engine
{
    public partial class EngineManager
    {
        private const int NUMBER_OF_STAR_SCORE_THRESHOLDS = 6;
        public int   Score { get; set; }
        public int   Combo { get; set; }
        public float Stars { get; private set; }

        private int _currentStarIndex;

        public int[] StarScoreThresholds = new int[NUMBER_OF_STAR_SCORE_THRESHOLDS];
        public int   BandMultiplier => Math.Max(_starpowerCount * 2, 1);

        private int          _activeCodaCount;

        public delegate void CodaStartDelegate(CodaSection codaSection);
        public delegate void CodaEndDelegate(CodaSection codaSection);

        public event CodaStartDelegate? OnCodaStart;
        public event CodaEndDelegate? OnCodaEnd;

        public int TotalCodaBonus
        {
            get
            {
                var totalBonus = 0;
                foreach (var engine in Engines)
                {
                    totalBonus += engine.BaseEngine.CurrentCodaBonus;
                }

                return totalBonus;
            }
        }

        public bool CodaSuccess
        {
            get
            {
                foreach (var engine in Engines)
                {
                    if (!engine.BaseEngine.CodaSuccess)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private void CodaStartHandler(CodaSection coda)
        {
            if (_activeCodaCount == 0)
            {
                OnCodaStart?.Invoke(coda);
            }

            _activeCodaCount++;
        }

        private void CodaEndHandler(CodaSection coda)
        {
            var success = CodaSuccess;
            _activeCodaCount--;
            if (_activeCodaCount == 0)
            {
                OnCodaEnd?.Invoke(coda);

                foreach (var engine in Engines)
                {
                    engine.BaseEngine.AwardCodaBonus(success);
                }
            }
        }

        private void UpdateBandMultiplier()
        {
            foreach (var engine in Engines)
            {
                engine.BaseEngine.UpdateBandMultiplier(BandMultiplier);
            }
        }

        public static int[] GetStarScoreCutoffs(List<int[]> starScoreCutoffsList)
        {
            int thresholdCount = starScoreCutoffsList.Count > 0 ? starScoreCutoffsList[0].Length : NUMBER_OF_STAR_SCORE_THRESHOLDS;

#if UNITY_EDITOR || YARG_TEST_BUILD || YARG_NIGHTLY_BUILD
            foreach (var playerCutoffsList in starScoreCutoffsList)
            {
                YargLogger.AssertFormat(
                    playerCutoffsList.Length == thresholdCount,
                    "Expected player star score cutoffs to contain {0} thresholds, got {1}.",
                    thresholdCount,
                    playerCutoffsList.Length);
            }
#endif

            int[] bandStarScoreCutoffs = new int[thresholdCount];
            for (int i = 0; i < thresholdCount; i++)
            {
                int totalStarCutoff = 0;
                foreach (var playerCutoffsList in starScoreCutoffsList)
                {
                    totalStarCutoff += playerCutoffsList[i];
                }

                bandStarScoreCutoffs[i] = (int) Math.Floor(totalStarCutoff *
                    (1 + .265f * (starScoreCutoffsList.Count - 1)));
            }

            return bandStarScoreCutoffs;
        }

        public void UpdateStars()
        {
            // Update which star we're on
            while (_currentStarIndex < StarScoreThresholds.Length &&
                Score > StarScoreThresholds[_currentStarIndex])
            {
                _currentStarIndex++;
            }

            // Calculate current star progress
            float progress = 0f;
            if (_currentStarIndex < StarScoreThresholds.Length)
            {
                int previousPoints = _currentStarIndex > 0 ? StarScoreThresholds[_currentStarIndex - 1] : 0;
                int nextPoints = StarScoreThresholds[_currentStarIndex];
                progress = YargMath.InverseLerpF(previousPoints, nextPoints, Score);
            }

            // Speed Freak's and Streak Guardian's bonus stars aren't part of Score/StarScoreThresholds, so they're summed separately here, uncapped. Unity applies the Power Challenge / All-Powerful cap afterward.
            int speedFreakBonus = 0;
            int streakGuardianBonus = 0;
            foreach (var engine in Engines)
            {
                speedFreakBonus += engine.BaseEngine.BaseStats.SpeedFreakBonusStars;
                streakGuardianBonus += engine.BaseEngine.BaseStats.StreakGuardianBonusStars;
            }

            Stars = _currentStarIndex + progress + speedFreakBonus + streakGuardianBonus;
        }
    }
}