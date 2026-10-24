using System.Collections.Generic;
using NUnit.Framework;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Objectives;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Tests.EditMode
{
    public class PuzzleSessionTests
    {
        static PuzzleSession NewSession(LevelDefinition l = null) => new PuzzleSession(l ?? TestLevels.Simple());

        static void Place(PuzzleSession s, string beacon, Vec2 pos)
        {
            Assert.AreEqual(SelectResult.Selected, s.Select(s.State.FindSource(beacon)));
            Assert.IsTrue(s.Drag(pos).Valid);
            Assert.IsTrue(s.Confirm());
        }

        [Test]
        public void StartsReadyForInput()
        {
            Assert.AreEqual(GameState.ReadyForInput, NewSession().Current);
        }

        [Test]
        public void SolvingCompletesWithThreeStars()
        {
            var s = NewSession();
            Place(s, "b1", TestLevels.SimpleSolution);
            Assert.AreEqual(1, s.Moves);
            Assert.IsTrue(s.Launch());
            s.RunToEnd();
            Assert.AreEqual(GameState.LevelCompleted, s.Current);
            Assert.AreEqual(3, s.Stars);
        }

        [Test]
        public void LaunchWithoutMoveFailsWithReason()
        {
            var s = NewSession();
            s.Launch();
            s.RunToEnd();
            Assert.AreEqual(GameState.LevelFailed, s.Current);
            Assert.AreEqual(FailureReason.Escaped, s.LastOutcome.Reason);
        }

        [Test]
        public void CannotSelectBeaconWhileSimulating()
        {
            var s = NewSession();
            s.Launch();
            Assert.AreEqual(SelectResult.InvalidState, s.Select(0));
            Assert.IsFalse(s.Launch());
        }

        [Test]
        public void DragClampsIntoPlacementRegion()
        {
            var s = NewSession();
            s.Select(0);
            var r = s.Drag(new Vec2(-10, 0));
            Assert.IsTrue(r.Clamped);
            Assert.AreEqual(-1.5, r.Position.X, 1e-12);
            Assert.AreEqual(GameState.PreviewingMove, s.Current);
        }

        [Test]
        public void OverlappingPlacementIsRejected()
        {
            var l = TestLevels.Simple();
            l.Hazards.Add(new HazardDefinition { Id = "rock", Position = new Vec2(0, 0), Radius = 0.4 });
            var s = NewSession(l);
            s.Select(0);
            var r = s.Drag(new Vec2(0.1, 0));
            Assert.IsFalse(r.Valid);
            Assert.AreEqual(PlacementIssue.OverlapsBody, r.Issue);
            Assert.IsFalse(s.Confirm());
            Assert.AreEqual(0, s.Moves);
        }

        [Test]
        public void CancelRestoresBeaconWithoutCost()
        {
            var s = NewSession();
            var before = s.State.Sources[0].Position;
            s.Select(0);
            s.Drag(new Vec2(0, 0));
            s.Cancel();
            Assert.AreEqual(before, s.State.Sources[0].Position);
            Assert.AreEqual(0, s.Moves);
            Assert.AreEqual(GameState.ReadyForInput, s.Current);
        }

        [Test]
        public void FreeBeaconDoesNotCostMoves()
        {
            var l = TestLevels.Simple();
            l.Sources[0].CostsMove = false;
            var s = NewSession(l);
            Place(s, "b1", new Vec2(0, 0));
            Assert.AreEqual(0, s.Moves);
        }

        [Test]
        public void MoveLimitBlocksFurtherPlacements()
        {
            var l = TestLevels.Simple();
            l.MaxMoves = 1;
            var s = NewSession(l);
            Place(s, "b1", new Vec2(0, 0));
            Assert.AreEqual(SelectResult.OutOfMoves, s.Select(0));
            Assert.AreEqual(0, s.MovesRemaining);
        }

        [Test]
        public void LimitedUseBeaconRunsOut()
        {
            var l = TestLevels.Simple();
            l.Sources[0].Mobility = BeaconMobility.LimitedUse;
            l.Sources[0].Uses = 1;
            var s = NewSession(l);
            Place(s, "b1", new Vec2(0, 0));
            Assert.AreEqual(SelectResult.NoUsesLeft, s.Select(0));
            s.Undo();
            Assert.AreEqual(SelectResult.Selected, s.Select(0), "undo restores the use");
        }

        [Test]
        public void UndoRestoresStateAfterFailedLaunch()
        {
            var s = NewSession();
            Place(s, "b1", new Vec2(0, 0));
            var beforeLaunch = s.State.Clone();
            s.Launch();
            s.RunToEnd();
            Assert.AreEqual(GameState.LevelFailed, s.Current);
            Assert.IsTrue(s.Undo());
            Assert.AreEqual(GameState.ReadyForInput, s.Current);
            Assert.IsTrue(s.State.StateEquals(beforeLaunch));
            Assert.AreEqual(1, s.Moves);
            Assert.IsTrue(s.Undo());
            Assert.AreEqual(0, s.Moves);
            Assert.AreEqual(new Vec2(3, -4), s.State.Sources[0].Position);
            Assert.IsFalse(s.Undo(), "history exhausted");
        }

        [Test]
        public void UndoDuringSimulationAbortsLaunch()
        {
            var s = NewSession();
            var before = s.State.Clone();
            s.Launch();
            for (int i = 0; i < 100; i++) s.StepOnce();
            Assert.IsTrue(s.Undo());
            Assert.IsTrue(s.State.StateEquals(before));
        }

        [Test]
        public void ResetRestoresCompleteAuthoredState()
        {
            var l = TestLevels.Simple();
            var s = NewSession(l);
            Place(s, "b1", TestLevels.SimpleSolution);
            s.Launch();
            s.RunToEnd();
            Assert.AreEqual(GameState.LevelCompleted, s.Current);
            s.Reset();
            Assert.AreEqual(GameState.ReadyForInput, s.Current);
            Assert.IsTrue(s.State.StateEquals(LevelFactory.CreateInitialState(l)));
            Assert.AreEqual(0, s.Moves);
            Assert.AreEqual(0, s.CanUndoCount);
            Assert.IsFalse(s.State.Targets[0].Captured);
            Assert.AreEqual(PlanetStatus.Free, s.State.Planets[0].Status);
        }

        [Test]
        public void ResetThenReplayGivesIdenticalResult()
        {
            var s = NewSession();
            Place(s, "b1", TestLevels.SimpleSolution);
            s.Launch();
            s.RunToEnd();
            var first = s.State.Clone();
            s.Reset();
            Place(s, "b1", TestLevels.SimpleSolution);
            s.Launch();
            s.RunToEnd();
            Assert.IsTrue(s.State.StateEquals(first));
        }

        [Test]
        public void AdvanceIsFrameRateIndependent()
        {
            var a = NewSession();
            var b = NewSession();
            Place(a, "b1", TestLevels.SimpleSolution);
            Place(b, "b1", TestLevels.SimpleSolution);
            a.Launch();
            b.Launch();
            // 30 fps vs irregular frame times: the same number of fixed steps yields identical states.
            for (int i = 0; i < 60; i++) a.Advance(1.0 / 30.0);
            double[] frames = { 0.004, 0.051, 0.0167, 0.033, 0.009 };
            int f = 0;
            while (b.State.StepCount < a.State.StepCount) b.Advance(frames[f++ % frames.Length]);
            while (b.State.StepCount > a.State.StepCount) a.StepOnce();
            Assert.AreEqual(a.State.StepCount, b.State.StepCount);
            Assert.IsTrue(a.State.StateEquals(b.State));
        }

        [Test]
        public void PauseBlocksSimulationAndResumeContinues()
        {
            var s = NewSession();
            s.Launch();
            s.Pause();
            Assert.AreEqual(GameState.Paused, s.Current);
            Assert.AreEqual(0, s.Advance(1.0));
            s.Resume();
            Assert.AreEqual(GameState.Simulating, s.Current);
            Assert.Greater(s.Advance(0.1), 0);
        }

        [Test]
        public void HintMarksAttemptAndLimitsStars()
        {
            var s = NewSession();
            Assert.IsNotNull(s.RevealHint());
            Assert.IsTrue(s.HintUsed);
            Place(s, "b1", TestLevels.SimpleSolution);
            s.Launch();
            s.RunToEnd();
            Assert.AreEqual(GameState.LevelCompleted, s.Current);
            Assert.AreEqual(2, s.Stars, "completion still allowed, but no third star");
        }

        [Test]
        public void HintsRunOut()
        {
            var s = NewSession();
            Assert.IsNotNull(s.RevealHint());
            Assert.IsNotNull(s.RevealHint());
            Assert.IsNull(s.RevealHint());
            Assert.IsTrue(s.CurrentHint.RevealsSolution);
        }

        [Test]
        public void TurnBasedLevelReturnsControlMidFlight()
        {
            var l = TestLevels.Simple();
            l.TurnDuration = 1.0;
            var s = NewSession(l);
            s.Launch();
            s.RunToEnd();
            Assert.AreEqual(GameState.ReadyForInput, s.Current);
            Assert.AreEqual(120, s.State.StepCount);
            Assert.AreNotEqual(Vec2.Zero, s.State.Planets[0].Velocity, "velocity is kept between turns");
            Assert.AreEqual(SelectResult.Selected, s.Select(0));
        }

        [Test]
        public void StateChangedEventsFire()
        {
            var s = NewSession();
            var seen = new List<GameState>();
            s.StateChanged += (_, next) => seen.Add(next);
            Place(s, "b1", TestLevels.SimpleSolution);
            s.Launch();
            s.RunToEnd();
            CollectionAssert.AreEqual(new[] { GameState.SelectingObject, GameState.PreviewingMove, GameState.ReadyForInput, GameState.Simulating, GameState.LevelCompleted }, seen);
        }
    }

    public class TrajectoryTests
    {
        /// <summary>
        /// Regression: the preview must match the live simulation. Both run the identical step function, so the
        /// documented tolerance is 1e-9 world units (effectively exact).
        /// </summary>
        const double Tolerance = 1e-9;

        static void AssertPreviewMatchesLive(PuzzleSession session)
        {
            var pred = session.Predict(1);
            var predicted = new List<Vec2>(pred.Paths[0]);
            var end = pred.End;
            session.Launch();
            int i = 1;
            while (session.Current == GameState.Simulating && i < predicted.Count)
            {
                session.StepOnce();
                Assert.AreEqual(predicted[i].X, session.State.Planets[0].Position.X, Tolerance, "step " + i);
                Assert.AreEqual(predicted[i].Y, session.State.Planets[0].Position.Y, Tolerance, "step " + i);
                i++;
            }
            Assert.Greater(i, 10);
            Assert.AreNotEqual(GameState.LevelFailed, session.Current);
            // Turn-based levels hand control back mid-flight; the prediction covers the whole remaining flight.
            bool turnEnded = session.Current == GameState.ReadyForInput;
            if (end == PredictionEnd.AllTargetsCaptured && !turnEnded) Assert.AreEqual(GameState.LevelCompleted, session.Current);
        }

        [Test]
        public void PreviewOfPendingDragMatchesLiveRun()
        {
            var s = new PuzzleSession(TestLevels.Simple());
            s.Select(0);
            s.Drag(TestLevels.SimpleSolution);
            var pred = s.Predict(1);
            Assert.AreEqual(PredictionEnd.AllTargetsCaptured, pred.End);
            var copy = new List<Vec2>(pred.Paths[0]);
            s.Confirm();
            s.Launch();
            for (int i = 1; i < copy.Count && s.Current == GameState.Simulating; i++)
            {
                s.StepOnce();
                Assert.AreEqual(0, Vec2.Distance(copy[i], s.State.Planets[0].Position), Tolerance);
            }
            Assert.AreEqual(GameState.LevelCompleted, s.Current);
        }

        [Test]
        public void PredictionDoesNotMutateLiveState()
        {
            var s = new PuzzleSession(TestLevels.Simple());
            var before = s.State.Clone();
            s.Select(0);
            s.Drag(new Vec2(1, 1));
            s.Predict();
            Assert.IsTrue(s.State.StateEquals(before));
        }

        [TestCaseSource(typeof(TestLevels), nameof(TestLevels.ShippedIds))]
        public void ShippedLevelPreviewMatchesLive(string id)
        {
            var level = TestLevels.Shipped(id);
            var s = new PuzzleSession(level);
            foreach (var a in level.Solution.Actions)
            {
                if (s.Current == GameState.LevelCompleted) break;
                if (a.Type == SolutionActionType.Launch)
                {
                    AssertPreviewMatchesLive(s);
                    s.RunToEnd();
                }
                else
                {
                    s.Select(s.State.FindSource(a.Beacon));
                    s.Drag(a.Position);
                    s.Confirm();
                }
            }
        }

        [Test]
        public void PredictionStopsOnCrash()
        {
            var l = TestLevels.Simple();
            var s = new PuzzleSession(l);
            s.Select(0);
            s.Drag(new Vec2(-2.5, -3.2)); // directly in the planet's path
            var pred = s.Predict();
            Assert.AreEqual(PredictionEnd.PlanetDestroyed, pred.End);
        }
    }
}
