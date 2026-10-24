using System.Collections.Generic;
using NUnit.Framework;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Progression;
using PocketOrbits.Core.Saving;
using PocketOrbits.Core.Utilities;

namespace PocketOrbits.Tests.EditMode
{
    public class StarEvaluatorTests
    {
        static readonly StarCriteria Criteria = new StarCriteria { TwoStarMoves = 3, ThreeStarMoves = 2, ThreeStarRequiresNoHint = true };

        [TestCase(false, 1, false, 0)]
        [TestCase(true, 9, false, 1)]
        [TestCase(true, 3, false, 2)]
        [TestCase(true, 2, false, 3)]
        [TestCase(true, 1, true, 2)]
        [TestCase(true, 0, false, 3)]
        public void Evaluate(bool completed, int moves, bool hint, int expected)
        {
            Assert.AreEqual(expected, StarEvaluator.Evaluate(Criteria, completed, moves, hint));
        }

        [Test]
        public void HintAllowedWhenCriteriaSaysSo()
        {
            var c = new StarCriteria { TwoStarMoves = 2, ThreeStarMoves = 1, ThreeStarRequiresNoHint = false };
            Assert.AreEqual(3, StarEvaluator.Evaluate(c, true, 1, true));
        }
    }

    public class SaveTests
    {
        [Test]
        public void RoundTripPreservesEverything()
        {
            var d = new SaveData();
            d.Get("w1-01").Completed = true;
            d.Get("w1-01").BestStars = 2;
            d.Get("w1-01").BestMoves = 3;
            d.Settings.MusicVolume = 0.25f;
            d.Settings.HighContrast = true;
            d.Settings.TrajectoryPreview = false;
            d.Settings.Quality = 2;
            d.LastPlayedLevel = "w1-01";
            var back = SaveSerializer.FromJson(SaveSerializer.ToJson(d), out var status);
            Assert.AreEqual(LoadStatus.Loaded, status);
            Assert.IsTrue(back.IsCompleted("w1-01"));
            Assert.AreEqual(2, back.StarsFor("w1-01"));
            Assert.AreEqual(3, back.Get("w1-01").BestMoves);
            Assert.AreEqual(0.25f, back.Settings.MusicVolume, 1e-6);
            Assert.IsTrue(back.Settings.HighContrast);
            Assert.IsFalse(back.Settings.TrajectoryPreview);
            Assert.AreEqual(2, back.Settings.Quality);
            Assert.AreEqual("w1-01", back.LastPlayedLevel);
        }

        [TestCase("{not json")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"version\":1,\"levels\":{\"x\":5}}")]
        public void CorruptDataFallsBackToDefaults(string json)
        {
            var d = SaveSerializer.FromJson(json, out var status);
            Assert.AreEqual(LoadStatus.CorruptReset, status);
            Assert.AreEqual(0, d.Levels.Count);
        }

        [Test]
        public void MissingSaveIsNotAnError()
        {
            SaveSerializer.FromJson(null, out var status);
            Assert.AreEqual(LoadStatus.NoSaveFound, status);
        }

        [Test]
        public void NewerVersionIsNotMisread()
        {
            SaveSerializer.FromJson("{\"version\":99}", out var status);
            Assert.AreEqual(LoadStatus.NewerVersionReset, status);
        }

        [Test]
        public void OutOfRangeValuesAreSanitised()
        {
            var d = SaveSerializer.FromJson("{\"version\":1,\"levels\":{\"a\":{\"bestStars\":7}},\"settings\":{\"musicVolume\":4,\"textScale\":9,\"quality\":-3}}", out _);
            Assert.AreEqual(3, d.StarsFor("a"));
            Assert.IsTrue(d.IsCompleted("a"));
            Assert.AreEqual(1f, d.Settings.MusicVolume);
            Assert.AreEqual(1.6f, d.Settings.TextScale);
            Assert.AreEqual(0, d.Settings.Quality);
        }

        [Test]
        public void CorruptFileIsBackedUp()
        {
            var storage = new MemorySaveStorage { Contents = "garbage{" };
            var service = new SaveService(storage);
            service.Load();
            Assert.AreEqual(LoadStatus.CorruptReset, service.LastLoadStatus);
            Assert.AreEqual("garbage{", storage.BackupContents);
        }
    }

    public class ProgressionTests
    {
        static LevelCatalog Catalog()
        {
            var levels = new List<LevelDefinition>();
            for (int i = 1; i <= 3; i++)
            {
                var l = TestLevels.Simple();
                l.Id = "w1-0" + i;
                l.Number = i;
                levels.Add(l);
            }
            var w2 = TestLevels.Simple();
            w2.Id = "w2-01";
            w2.World = 2;
            levels.Add(w2);
            return new LevelCatalog(levels);
        }

        [Test]
        public void OnlyFirstLevelUnlockedInitially()
        {
            var pm = new ProgressionManager(Catalog(), new SaveService(new MemorySaveStorage()));
            Assert.IsTrue(pm.IsUnlocked("w1-01"));
            Assert.IsFalse(pm.IsUnlocked("w1-02"));
            Assert.IsFalse(pm.IsWorldUnlocked(2));
        }

        [Test]
        public void CompletionUnlocksNextLevelWithAnyStars()
        {
            var catalog = Catalog();
            var pm = new ProgressionManager(catalog, new SaveService(new MemorySaveStorage()));
            var r = pm.RecordCompletion(catalog.Get("w1-01"), moves: 9, hintUsed: true);
            Assert.AreEqual(1, r.Stars);
            Assert.IsTrue(r.NextLevelNewlyUnlocked);
            Assert.IsTrue(pm.IsUnlocked("w1-02"));
        }

        [Test]
        public void StarsNeverDecrease()
        {
            var catalog = Catalog();
            var pm = new ProgressionManager(catalog, new SaveService(new MemorySaveStorage()));
            pm.RecordCompletion(catalog.Get("w1-01"), 1, false);
            var r = pm.RecordCompletion(catalog.Get("w1-01"), 5, true);
            Assert.AreEqual(1, r.Stars);
            Assert.AreEqual(3, pm.Data.StarsFor("w1-01"));
            Assert.IsFalse(r.NewBest);
        }

        [Test]
        public void FinishingWorldUnlocksNextWorld()
        {
            var catalog = Catalog();
            var pm = new ProgressionManager(catalog, new SaveService(new MemorySaveStorage()));
            pm.RecordCompletion(catalog.Get("w1-01"), 1, false);
            pm.RecordCompletion(catalog.Get("w1-02"), 1, false);
            var r = pm.RecordCompletion(catalog.Get("w1-03"), 1, false);
            Assert.IsTrue(r.WorldNewlyUnlocked);
            Assert.IsTrue(pm.IsWorldUnlocked(2));
        }

        [Test]
        public void ProgressSurvivesRestart()
        {
            var catalog = Catalog();
            var storage = new MemorySaveStorage();
            var pm = new ProgressionManager(catalog, new SaveService(storage));
            pm.RecordCompletion(catalog.Get("w1-01"), 2, false);
            Assert.Greater(storage.Writes, 0, "completion saves immediately");

            // Simulated app restart: brand new services reading the same storage.
            var service = new SaveService(storage);
            service.Load();
            var pm2 = new ProgressionManager(catalog, service);
            Assert.AreEqual(LoadStatus.Loaded, service.LastLoadStatus);
            Assert.AreEqual(2, pm2.Data.StarsFor("w1-01"));
            Assert.IsTrue(pm2.IsUnlocked("w1-02"));
        }

        [Test]
        public void SuggestedLevelIsFirstUnfinished()
        {
            var catalog = Catalog();
            var pm = new ProgressionManager(catalog, new SaveService(new MemorySaveStorage()));
            Assert.AreEqual("w1-01", pm.SuggestedLevel().Id);
            pm.RecordCompletion(catalog.Get("w1-01"), 1, false);
            Assert.AreEqual("w1-02", pm.SuggestedLevel().Id);
        }
    }

    public class JsonTests
    {
        [Test]
        public void ParsesNestedValuesAndComments()
        {
            var o = (Dictionary<string, object>)MiniJson.Parse("{ // comment\n \"a\": [1, -2.5e1, true, null, \"x\\\"y\"], \"b\": {} }");
            var a = (List<object>)o["a"];
            Assert.AreEqual(1.0, a[0]);
            Assert.AreEqual(-25.0, a[1]);
            Assert.AreEqual(true, a[2]);
            Assert.IsNull(a[3]);
            Assert.AreEqual("x\"y", a[4]);
        }

        [TestCase("{\"a\":1,}")]
        [TestCase("{\"a\" 1}")]
        [TestCase("[1 2]")]
        [TestCase("\"open")]
        public void RejectsMalformedJson(string text)
        {
            Assert.Throws<MiniJson.JsonException>(() => MiniJson.Parse(text));
        }
    }
}
