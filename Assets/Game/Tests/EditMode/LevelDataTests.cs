using System;
using NUnit.Framework;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Tests.EditMode
{
    public class LevelDataTests
    {
        [Test]
        public void ShippedCatalogIsValidAndEverySolutionIsVerified()
        {
            var levels = TestLevels.LoadShipped();
            Assert.GreaterOrEqual(levels.Count, 1);
            var report = LevelValidator.ValidateCatalog(levels, runSolutions: true);
            Assert.IsTrue(report.Ok, report.ToString());
        }

        [TestCaseSource(typeof(TestLevels), nameof(TestLevels.ShippedIds))]
        public void ShippedSolutionEarnsExpectedStars(string id)
        {
            var level = TestLevels.Shipped(id);
            var run = SolutionRunner.Run(level, level.Solution.Actions);
            Assert.IsTrue(run.Completed, id + ": " + run.Failure + " " + run.Error);
            Assert.AreEqual(level.Solution.ExpectedStars, run.Stars);
        }

        [Test]
        public void ShippedLevelsAreNumberedContiguously()
        {
            var catalog = new LevelCatalog(TestLevels.LoadShipped());
            foreach (int world in catalog.Worlds())
            {
                var list = catalog.InWorld(world);
                for (int i = 0; i < list.Count; i++) Assert.AreEqual(i + 1, list[i].Number, list[i].Id);
            }
        }

        [Test]
        public void ValidLevelPasses()
        {
            var report = LevelValidator.Validate(TestLevels.Simple());
            Assert.IsTrue(report.Ok, report.ToString());
        }

        [Test]
        public void DetectsDuplicateIds()
        {
            var a = TestLevels.Simple();
            var b = TestLevels.Simple();
            b.Number = 2;
            var report = LevelValidator.ValidateCatalog(new[] { a, b }, runSolutions: false);
            StringAssert.Contains("duplicate level id", report.ToString());
        }

        [Test]
        public void DetectsBrokenReferencesAndRules()
        {
            var l = TestLevels.Simple();
            l.Targets[0].CenterSource = "nope";
            l.Planets[0].Style = "plaid";
            l.Stars.ThreeStarMoves = 5;
            l.Hints[0].Focus = "ghost";
            l.Sources[0].Regions.Clear();
            l.Solution.Actions.Clear();
            var text = LevelValidator.Validate(l).ToString();
            StringAssert.Contains("unknown centre", text);
            StringAssert.Contains("unknown style", text);
            StringAssert.Contains("star criteria", text);
            StringAssert.Contains("unknown object", text);
            StringAssert.Contains("placement region", text);
            StringAssert.Contains("missing solution", text);
        }

        [Test]
        public void DetectsTunnellingRisk()
        {
            var l = TestLevels.Simple();
            l.Constants.MaxSpeed = 100;
            StringAssert.Contains("maxSpeed * timeStep", LevelValidator.Validate(l).ToString());
        }

        [Test]
        public void DetectsLevelSolvedWithoutMoving()
        {
            var l = TestLevels.Simple();
            l.Targets[0].Position = new Vec2(-2.5, 0); // straight in the planet's path
            var report = new ValidationReport();
            LevelValidator.VerifySolution(l, report);
            StringAssert.Contains("without moving", report.ToString());
        }

        [Test]
        public void DetectsWrongSolution()
        {
            var l = TestLevels.Simple();
            l.Solution.Actions[0] = new SolutionAction { Type = SolutionActionType.Place, Beacon = "b1", Position = new Vec2(3, 1) };
            var report = new ValidationReport();
            LevelValidator.VerifySolution(l, report);
            StringAssert.Contains("does not complete", report.ToString());
        }

        [Test]
        public void ParserReportsBadData()
        {
            Assert.Throws<FormatException>(() => LevelJson.Parse("{\"id\":\"x\",\"planets\":[{\"pos\":[1]}]}"));
            Assert.Throws<FormatException>(() => LevelJson.Parse("{\"id\":\"x\",\"sources\":[{\"kind\":\"wormhole\"}]}"));
        }

        [Test]
        public void ParserReadsAllSections()
        {
            const string json = @"{
              ""id"": ""t"", ""world"": 2, ""number"": 3, ""name"": ""N"", ""objective"": ""O"", ""maxMoves"": 4, ""turnDuration"": 2,
              ""constants"": { ""gravity"": 2 },
              ""planets"": [ { ""id"": ""p"", ""pos"": [1, 2], ""vel"": [3, 4], ""style"": ""ice"" } ],
              ""sources"": [ { ""id"": ""s"", ""kind"": ""star"", ""pos"": [0, 0], ""strength"": 6 },
                             { ""id"": ""b"", ""kind"": ""beacon"", ""mobility"": ""limited-use"", ""uses"": 2, ""pos"": [1, 1], ""regions"": [ { ""circle"": [1, 1, 2] } ] } ],
              ""hazards"": [ { ""id"": ""h"", ""pos"": [3, 3] } ],
              ""targets"": [ { ""id"": ""r"", ""kind"": ""orbit-ring"", ""center"": ""s"", ""radius"": 2.5, ""order"": 1 } ],
              ""stars"": { ""twoStarMoves"": 3, ""threeStarMoves"": 2, ""threeStarRequiresNoHint"": false },
              ""hints"": [ { ""text"": ""h1"", ""focus"": ""b"", ""area"": [1, 1, 1] } ],
              ""solution"": { ""actions"": [ { ""type"": ""place"", ""beacon"": ""b"", ""pos"": [2, 1] }, { ""type"": ""launch"" } ], ""expectedStars"": 3 }
            }";
            var l = LevelJson.Parse(json);
            Assert.AreEqual(2, l.World);
            Assert.AreEqual(2.0, l.Constants.Gravity);
            Assert.AreEqual(new Vec2(3, 4), l.Planets[0].Velocity);
            Assert.AreEqual(SourceKind.Star, l.Sources[0].Kind);
            Assert.AreEqual(BeaconMobility.LimitedUse, l.Sources[1].Mobility);
            Assert.AreEqual(2, l.Sources[1].Uses);
            Assert.AreEqual(RegionShape.Circle, l.Sources[1].Regions[0].Shape);
            Assert.AreEqual(TargetKind.OrbitRing, l.Targets[0].Kind);
            Assert.AreEqual("s", l.Targets[0].CenterSource);
            Assert.IsFalse(l.Stars.ThreeStarRequiresNoHint);
            Assert.IsTrue(l.Hints[0].HasArea);
            Assert.AreEqual(2, l.Solution.Actions.Count);
        }
    }
}
