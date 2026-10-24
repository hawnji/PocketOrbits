using PocketOrbits.Core.Levels;

namespace PocketOrbits.Core.Progression
{
    /// <summary>Deterministic one-to-three star rating for a completed attempt.</summary>
    public static class StarEvaluator
    {
        public static int Evaluate(StarCriteria criteria, bool completed, int moves, bool hintUsed)
        {
            if (!completed) return 0;
            int stars = 1;
            if (moves <= criteria.TwoStarMoves) stars = 2;
            if (stars == 2 && moves <= criteria.ThreeStarMoves && !(criteria.ThreeStarRequiresNoHint && hintUsed)) stars = 3;
            return stars;
        }

        /// <summary>Human readable criteria for the level card / completion screen.</summary>
        public static string[] Describe(StarCriteria c)
        {
            return new[]
            {
                "Complete the objective",
                "Use " + c.TwoStarMoves + (c.TwoStarMoves == 1 ? " move" : " moves") + " or fewer",
                "Use " + c.ThreeStarMoves + (c.ThreeStarMoves == 1 ? " move" : " moves") + " or fewer" + (c.ThreeStarRequiresNoHint ? " without a hint" : ""),
            };
        }
    }
}
