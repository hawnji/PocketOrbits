using System.Collections;
using NUnit.Framework;
using PocketOrbits.Bootstrap;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Saving;
using PocketOrbits.Core.Simulation;
using PocketOrbits.Input;
using PocketOrbits.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace PocketOrbits.Tests.PlayMode
{
    /// <summary>
    /// End-to-end tests through the real bootstrap, input controller, level manager and progression.
    /// Each test uses an in-memory save so the developer's own progress file is never touched.
    /// </summary>
    public class GameplayPlayModeTests
    {
        MemorySaveStorage _storage;
        GameBootstrap _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _storage = new MemorySaveStorage();
            yield return Boot();
        }

        IEnumerator Boot()
        {
            GameBootstrap.StorageOverride = _storage;
            if (GameBootstrap.Instance != null) Object.Destroy(GameBootstrap.Instance.gameObject);
            yield return null;
            _game = new GameObject("PocketOrbits").AddComponent<GameBootstrap>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_game != null) Object.Destroy(_game.gameObject);
            GameBootstrap.StorageOverride = null;
            yield return null;
        }

        LevelDefinition First => _game.Services.Catalog.Levels[0];

        IEnumerator StartFirstLevel()
        {
            _game.UI.PlayLevel(First);
            yield return null;
            yield return null;
        }

        Vector2 Screen(Vec2 sim) => _game.Services.Camera.SimToScreen(sim);

        IEnumerator DragBeacon(string beaconId, Vec2 target)
        {
            var session = _game.Levels.Session;
            int b = session.State.FindSource(beaconId);
            var input = _game.InputController;
            input.Pointer(Screen(session.State.Sources[b].Position), PointerPhase.Began);
            yield return null;
            var from = session.State.Sources[b].Position;
            for (int i = 1; i <= 10; i++)
            {
                input.Pointer(Screen(Vec2.Lerp(from, target, i / 10.0)), PointerPhase.Moved);
                yield return null;
            }
            input.Pointer(Screen(target), PointerPhase.Ended);
            yield return null;
        }

        IEnumerator WaitForResolution(float timeoutSeconds = 30f)
        {
            _game.Levels.FastForward = true;
            float t = 0;
            while (_game.Levels.Session.Current == GameState.Simulating && t < timeoutSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator BootstrapShowsMainMenuWithLevels()
        {
            Assert.AreEqual(Screen2.MainMenu, _game.UI.Current);
            Assert.GreaterOrEqual(_game.Services.Catalog.Levels.Count, 5);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartingALevelBuildsTheScene()
        {
            yield return StartFirstLevel();
            Assert.AreEqual(Screen2.Gameplay, _game.UI.Current);
            Assert.IsNotNull(_game.Levels.Session);
            Assert.AreEqual(GameState.ReadyForInput, _game.Levels.Session.Current);
            Assert.Greater(_game.Levels.View.transform.childCount, 3);
        }

        [UnityTest]
        public IEnumerator DraggingPlacesBeaconAndShowsPreview()
        {
            yield return StartFirstLevel();
            var target = First.Solution.Actions[0].Position;
            yield return DragBeacon("b1", target);
            var session = _game.Levels.Session;
            Assert.AreEqual(GameState.PreviewingMove, session.Current);
            // Touch-to-world conversion is float precision; 2 cm on screen is far below a finger.
            Assert.Less(Vec2.Distance(session.PendingPosition, target), 0.05);
            yield return new WaitForSeconds(0.15f);
            var line = _game.Levels.View.Trajectory.GetComponentInChildren<LineRenderer>();
            Assert.Greater(line.positionCount, 2, "trajectory preview is drawn");
            Assert.IsTrue(_game.Levels.Confirm());
            Assert.AreEqual(1, session.Moves);
        }

        [UnityTest]
        public IEnumerator SolvingCompletesUnlocksNextAndSaves()
        {
            yield return StartFirstLevel();
            var place = First.Solution.Actions[0];
            var session = _game.Levels.Session;
            session.Select(session.State.FindSource(place.Beacon));
            session.Drag(place.Position);
            Assert.IsTrue(_game.Levels.Confirm());
            Assert.IsTrue(_game.Levels.Launch());
            yield return WaitForResolution();
            Assert.AreEqual(GameState.LevelCompleted, session.Current);
            Assert.AreEqual(3, _game.Levels.LastCompletion.Stars);
            var second = _game.Services.Catalog.Levels[1];
            Assert.IsTrue(_game.Services.Progression.IsUnlocked(second.Id));
            Assert.IsNotNull(_storage.Contents, "progress written to storage");
        }

        [UnityTest]
        public IEnumerator FailedLaunchCanBeUndone()
        {
            yield return StartFirstLevel();
            var session = _game.Levels.Session;
            var before = session.State.Clone();
            _game.Levels.Launch();
            yield return WaitForResolution();
            Assert.AreEqual(GameState.LevelFailed, session.Current);
            _game.Levels.Undo();
            Assert.AreEqual(GameState.ReadyForInput, session.Current);
            Assert.IsTrue(session.State.StateEquals(before));
        }

        [UnityTest]
        public IEnumerator ResetRestoresInitialState()
        {
            yield return StartFirstLevel();
            yield return DragBeacon("b1", new Vec2(1, 0));
            _game.Levels.Confirm();
            _game.Levels.Launch();
            yield return new WaitForSeconds(0.3f);
            _game.Levels.ResetLevel();
            var session = _game.Levels.Session;
            Assert.IsTrue(session.State.StateEquals(LevelFactory.CreateInitialState(First)));
            Assert.AreEqual(0, session.Moves);
        }

        [UnityTest]
        public IEnumerator ProgressSurvivesRestart()
        {
            yield return SolvingCompletesUnlocksNextAndSaves();
            // Simulated app restart with the same storage.
            yield return Boot();
            Assert.AreEqual(LoadStatus.Loaded, _game.Services.Save.LastLoadStatus);
            Assert.AreEqual(3, _game.Services.Save.Data.StarsFor(First.Id));
            Assert.IsTrue(_game.Services.Progression.IsUnlocked(_game.Services.Catalog.Levels[1].Id));
        }

        [UnityTest]
        public IEnumerator InputIsIgnoredWhileSimulating()
        {
            yield return StartFirstLevel();
            var session = _game.Levels.Session;
            var start = session.State.Sources[0].Position;
            _game.Levels.Launch();
            yield return DragBeacon("b1", new Vec2(0, 0));
            Assert.AreEqual(start, session.State.Sources[0].Position);
            Assert.AreNotEqual(GameState.PreviewingMove, session.Current);
        }
    }
}
