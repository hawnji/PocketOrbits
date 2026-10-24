using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Saving;

namespace PocketOrbits.Core.Progression
{
    public struct CompletionResult
    {
        public int Stars;
        public int PreviousBestStars;
        public bool FirstCompletion;
        public bool NewBest;
        public LevelDefinition NextLevel;
        public bool NextLevelNewlyUnlocked;
        public bool WorldNewlyUnlocked;
    }

    /// <summary>
    /// Linear unlocks: the first level is always open; each level unlocks when the previous one is completed
    /// (any star count). A world is unlocked when its first level is unlocked. Stars never decrease.
    /// </summary>
    public sealed class ProgressionManager
    {
        readonly LevelCatalog _catalog;
        readonly SaveService _save;

        public ProgressionManager(LevelCatalog catalog, SaveService save)
        {
            _catalog = catalog;
            _save = save;
        }

        public SaveData Data => _save.Data;

        public bool IsUnlocked(string levelId)
        {
            int i = _catalog.IndexOf(levelId);
            if (i < 0) return false;
            if (i == 0) return true;
            return Data.IsCompleted(_catalog.Levels[i - 1].Id) || Data.IsCompleted(levelId);
        }

        public bool IsWorldUnlocked(int world)
        {
            var levels = _catalog.InWorld(world);
            return levels.Count > 0 && IsUnlocked(levels[0].Id);
        }

        public int StarsInWorld(int world)
        {
            int n = 0;
            foreach (var l in _catalog.InWorld(world)) n += Data.StarsFor(l.Id);
            return n;
        }

        public int TotalStars
        {
            get
            {
                int n = 0;
                foreach (var l in _catalog.Levels) n += Data.StarsFor(l.Id);
                return n;
            }
        }

        public void RecordAttempt(string levelId)
        {
            Data.Get(levelId).Attempts++;
            Data.LastPlayedLevel = levelId;
        }

        /// <summary>Records a completed level and saves immediately.</summary>
        public CompletionResult RecordCompletion(LevelDefinition level, int moves, bool hintUsed)
        {
            var next = _catalog.Next(level.Id);
            bool nextWasUnlocked = next != null && IsUnlocked(next.Id);
            bool nextWorldWasUnlocked = next != null && IsWorldUnlocked(next.World);

            var record = Data.Get(level.Id);
            int stars = StarEvaluator.Evaluate(level.Stars, true, moves, hintUsed);
            var result = new CompletionResult
            {
                Stars = stars,
                PreviousBestStars = record.BestStars,
                FirstCompletion = !record.Completed,
                NewBest = stars > record.BestStars,
                NextLevel = next,
            };
            record.Completed = true;
            if (stars > record.BestStars) record.BestStars = stars;
            if (moves < record.BestMoves) record.BestMoves = moves;

            result.NextLevelNewlyUnlocked = next != null && !nextWasUnlocked && IsUnlocked(next.Id);
            result.WorldNewlyUnlocked = next != null && next.World != level.World && !nextWorldWasUnlocked && IsWorldUnlocked(next.World);
            _save.Save();
            return result;
        }

        /// <summary>The level the "Play" button should open: first unlocked level without a completion, else the last played.</summary>
        public LevelDefinition SuggestedLevel()
        {
            foreach (var l in _catalog.Levels)
                if (IsUnlocked(l.Id) && !Data.IsCompleted(l.Id)) return l;
            var last = Data.LastPlayedLevel != null ? _catalog.Get(Data.LastPlayedLevel) : null;
            return last ?? (_catalog.Levels.Count > 0 ? _catalog.Levels[0] : null);
        }
    }
}
