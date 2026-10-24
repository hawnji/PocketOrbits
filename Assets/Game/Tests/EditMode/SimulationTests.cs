using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Tests.EditMode
{
    public class GravityTests
    {
        static SourceState Src(double x, double y, double m) => new SourceState { Position = new Vec2(x, y), Strength = m, Radius = 0.1 };

        [Test]
        public void Acceleration_MatchesSoftenedFormula()
        {
            var sources = new[] { Src(3, 4, 2) };
            const double g = 1.5, eps = 0.5;
            Vec2 a = GravitySimulation.Acceleration(Vec2.Zero, sources, g, eps);
            double d2 = 25 + eps * eps;
            double expected = g * 2 / (d2 * Math.Sqrt(d2));
            Assert.AreEqual(3 * expected, a.X, 1e-12);
            Assert.AreEqual(4 * expected, a.Y, 1e-12);
        }

        [Test]
        public void Acceleration_SumsSourcesAndCancelsWhenSymmetric()
        {
            var sources = new[] { Src(-2, 0, 3), Src(2, 0, 3) };
            Vec2 a = GravitySimulation.Acceleration(Vec2.Zero, sources, 1, 0.35);
            Assert.AreEqual(0, a.X, 1e-12);
            Assert.AreEqual(0, a.Y, 1e-12);
        }

        [Test]
        public void Acceleration_IsFiniteAtSourceCentre()
        {
            Vec2 a = GravitySimulation.Acceleration(new Vec2(1, 1), new[] { Src(1, 1, 100) }, 1, 0.35);
            Assert.IsFalse(double.IsNaN(a.X) || double.IsInfinity(a.X));
            Assert.AreEqual(0, a.Length, 1e-12);
        }

        [Test]
        public void Acceleration_ApproachesInverseSquareFarAway()
        {
            Vec2 a = GravitySimulation.Acceleration(new Vec2(100, 0), new[] { Src(0, 0, 4) }, 1, 0.35);
            Assert.AreEqual(-4.0 / (100 * 100), a.X, 1e-8);
        }
    }

    public class IntegrationTests
    {
        static SimState OrbitState(out SimConstants c, double r = 3, double m = 6)
        {
            var level = TestLevels.Sandbox();
            level.Constants.Softening = 0.05;
            level.Constants.MaxSpeed = 50;
            level.Sources.Add(new GravitySourceDefinition { Id = "s", Kind = SourceKind.Star, Strength = m, Radius = 0.2 });
            double v = Math.Sqrt(m / r);
            level.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(r, 0), Velocity = new Vec2(0, v), Radius = 0.1 });
            c = level.Constants;
            return LevelFactory.CreateInitialState(level);
        }

        [Test]
        public void SemiImplicitEuler_KeepsCircularOrbitStable()
        {
            var s = OrbitState(out var c);
            double period = 2 * Math.PI * 3 / Math.Sqrt(6.0 / 3);
            int steps = (int)(5 * period / c.TimeStep);
            double minR = double.MaxValue, maxR = 0;
            for (int i = 0; i < steps; i++)
            {
                GravitySimulation.Step(s, c, null);
                double r = s.Planets[0].Position.Length;
                minR = Math.Min(minR, r);
                maxR = Math.Max(maxR, r);
            }
            Assert.AreEqual(PlanetStatus.Free, s.Planets[0].Status);
            // Documented tolerance: radius stays within 3% over five orbits at dt = 1/120.
            Assert.Less(maxR / 3 - 1, 0.03);
            Assert.Less(1 - minR / 3, 0.03);
        }

        [Test]
        public void Step_IsDeterministic()
        {
            var a = OrbitState(out var c);
            var b = OrbitState(out _);
            for (int i = 0; i < 2000; i++)
            {
                GravitySimulation.Step(a, c, null);
                GravitySimulation.Step(b, c, null);
            }
            Assert.IsTrue(a.StateEquals(b));
        }

        [Test]
        public void TimeAdvancesInFixedSteps()
        {
            var s = OrbitState(out var c);
            for (int i = 0; i < 120; i++) GravitySimulation.Step(s, c, null);
            Assert.AreEqual(120, s.StepCount);
            Assert.AreEqual(1.0, s.Time, 1e-12);
        }

        [Test]
        public void SpeedIsClampedToMaxSpeed()
        {
            var level = TestLevels.Sandbox();
            level.Constants.MaxSpeed = 2;
            level.Planets.Add(new PlanetDefinition { Id = "p", Position = Vec2.Zero, Velocity = new Vec2(10, 0), Radius = 0.1 });
            var s = LevelFactory.CreateInitialState(level);
            GravitySimulation.Step(s, level.Constants, null);
            Assert.AreEqual(2, s.Planets[0].Velocity.Length, 1e-12);
        }

        [Test]
        public void InitialStateMatchesDefinition()
        {
            var level = TestLevels.Simple();
            var s = LevelFactory.CreateInitialState(level);
            Assert.AreEqual(new Vec2(-2.5, -6), s.Planets[0].Position);
            Assert.AreEqual(new Vec2(0, 2.4), s.Planets[0].Velocity);
            Assert.AreEqual(PlanetStatus.Free, s.Planets[0].Status);
            Assert.AreEqual(-1, s.Planets[0].CapturedTarget);
            Assert.AreEqual(new Vec2(3, -4), s.Sources[0].Position);
            Assert.IsFalse(s.Targets[0].Captured);
            Assert.AreEqual(0, s.Time);
        }
    }

    public class CollisionTests
    {
        static (SimState, SimConstants, List<SimEvent>) Run(LevelDefinition level, int maxSteps = 5000)
        {
            var s = LevelFactory.CreateInitialState(level);
            var events = new List<SimEvent>();
            for (int i = 0; i < maxSteps && s.AnyPlanetFree; i++) GravitySimulation.Step(s, level.Constants, events);
            return (s, level.Constants, events);
        }

        [Test]
        public void PlanetCrashesIntoStar()
        {
            var l = TestLevels.Sandbox();
            l.Sources.Add(new GravitySourceDefinition { Id = "s", Kind = SourceKind.Star, Strength = 5, Radius = 0.6 });
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(0, -4), Radius = 0.3 });
            var (s, _, ev) = Run(l);
            Assert.AreEqual(PlanetStatus.Destroyed, s.Planets[0].Status);
            Assert.AreEqual(SimEventType.PlanetCrashedIntoSource, ev[0].Type);
        }

        [Test]
        public void PlanetHitsAsteroid()
        {
            var l = TestLevels.Sandbox();
            l.Hazards.Add(new HazardDefinition { Id = "a", Position = new Vec2(0, 2), Radius = 0.5 });
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(0, -2), Velocity = new Vec2(0, 3), Radius = 0.3 });
            var (s, _, ev) = Run(l);
            Assert.AreEqual(PlanetStatus.Destroyed, s.Planets[0].Status);
            Assert.AreEqual(SimEventType.PlanetHitHazard, ev[0].Type);
            Assert.AreEqual(0, ev[0].Other);
        }

        [Test]
        public void PlanetsCollideAndBothAreDestroyed()
        {
            var l = TestLevels.Sandbox();
            l.Planets.Add(new PlanetDefinition { Id = "a", Position = new Vec2(-2, 0), Velocity = new Vec2(2, 0), Radius = 0.3 });
            l.Planets.Add(new PlanetDefinition { Id = "b", Position = new Vec2(2, 0), Velocity = new Vec2(-2, 0), Radius = 0.3 });
            var (s, _, _) = Run(l);
            Assert.AreEqual(PlanetStatus.Destroyed, s.Planets[0].Status);
            Assert.AreEqual(PlanetStatus.Destroyed, s.Planets[1].Status);
        }

        [Test]
        public void PlanetLeavingBoundsEscapes()
        {
            var l = TestLevels.Sandbox(10, 10);
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = Vec2.Zero, Velocity = new Vec2(4, 0), Radius = 0.3 });
            var (s, c, ev) = Run(l);
            Assert.AreEqual(PlanetStatus.Escaped, s.Planets[0].Status);
            Assert.AreEqual(SimEventType.PlanetEscaped, ev[0].Type);
            Assert.Greater(s.Planets[0].Position.X, 5 + c.OutOfBoundsMargin);
        }

        [Test]
        public void FastPlanetCannotTunnelThroughThinHazard()
        {
            var l = TestLevels.Sandbox();
            l.Constants.MaxSpeed = 9;
            l.Hazards.Add(new HazardDefinition { Id = "a", Position = new Vec2(0, 0), Radius = 0.05 });
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(0, -5), Velocity = new Vec2(0, 9), Radius = 0.1 });
            var (s, _, _) = Run(l);
            Assert.AreEqual(PlanetStatus.Destroyed, s.Planets[0].Status);
        }
    }

    public class CaptureTests
    {
        static LevelDefinition DockLevel(Vec2 velocity, double drag = 4, double dwell = 0.35)
        {
            var l = TestLevels.Sandbox();
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(0, -3), Velocity = velocity, Radius = 0.3 });
            l.Targets.Add(new TargetDefinition { Id = "t", Kind = TargetKind.Dock, Position = Vec2.Zero, Radius = 0.8, CaptureSpeed = 1.6, DwellTime = dwell, Drag = drag });
            return l;
        }

        static SimState Run(LevelDefinition l, List<SimEvent> events = null, int steps = 2400)
        {
            var s = LevelFactory.CreateInitialState(l);
            for (int i = 0; i < steps && s.AnyPlanetFree; i++) GravitySimulation.Step(s, l.Constants, events);
            return s;
        }

        [Test]
        public void SlowPlanetDocksAndIsLocked()
        {
            var events = new List<SimEvent>();
            var s = Run(DockLevel(new Vec2(0, 2.5)), events);
            Assert.IsTrue(s.Targets[0].Captured);
            Assert.AreEqual(PlanetStatus.Docked, s.Planets[0].Status);
            Assert.AreEqual(Vec2.Zero, s.Planets[0].Position);
            Assert.AreEqual(Vec2.Zero, s.Planets[0].Velocity);
            Assert.AreEqual(SimEventType.TargetCaptured, events[0].Type);
        }

        [Test]
        public void FastPlanetCrossingDockIsNotCaptured()
        {
            var s = Run(DockLevel(new Vec2(0, 8), drag: 1));
            Assert.IsFalse(s.Targets[0].Captured);
            Assert.AreEqual(PlanetStatus.Escaped, s.Planets[0].Status);
        }

        [Test]
        public void BriefSlowGrazeDoesNotCaptureWithoutDwell()
        {
            // Planet just clips the edge of the dock: speed is low but it leaves before the stability window.
            var l = DockLevel(new Vec2(0, 1.5), drag: 0, dwell: 0.6);
            l.Planets[0].Position = new Vec2(0.75, -3);
            var s = Run(l);
            Assert.IsFalse(s.Targets[0].Captured);
        }

        [Test]
        public void SequentialTargetsRespectOrder()
        {
            var l = TestLevels.Sandbox();
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(0, -6), Velocity = new Vec2(0, 2), Radius = 0.3 });
            // Planet reaches the dock (order 1) before the waypoint (order 0): the dock must ignore it.
            l.Targets.Add(new TargetDefinition { Id = "dock", Kind = TargetKind.Dock, Position = new Vec2(0, -3), Radius = 0.8, CaptureSpeed = 5, DwellTime = 0.1, Drag = 0, Order = 1 });
            l.Targets.Add(new TargetDefinition { Id = "way", Kind = TargetKind.Waypoint, Position = new Vec2(0, 0), Radius = 0.5, Order = 0 });
            var s = Run(l, null, 400);
            Assert.IsTrue(s.Targets[1].Captured, "waypoint reached");
            Assert.IsFalse(s.Targets[0].Captured, "dock passed before waypoint must not capture");
            Assert.AreEqual(PlanetStatus.Free, s.Planets[0].Status);
        }

        [Test]
        public void WaypointThenDockCompletesInOrder()
        {
            var l = TestLevels.Sandbox();
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(0, -6), Velocity = new Vec2(0, 2), Radius = 0.3 });
            l.Targets.Add(new TargetDefinition { Id = "way", Kind = TargetKind.Waypoint, Position = new Vec2(0, -3), Radius = 0.5, Order = 0 });
            l.Targets.Add(new TargetDefinition { Id = "dock", Kind = TargetKind.Dock, Position = new Vec2(0, 0), Radius = 0.8, CaptureSpeed = 1.6, Order = 1 });
            var s = Run(l);
            Assert.IsTrue(s.AllTargetsCaptured);
            Assert.AreEqual(PlanetStatus.Docked, s.Planets[0].Status);
        }

        [Test]
        public void OrbitRingCapturesTangentialPlanetAndPutsItOnRails()
        {
            var l = TestLevels.Sandbox();
            l.Sources.Add(new GravitySourceDefinition { Id = "star", Kind = SourceKind.Star, Strength = 4, Radius = 0.5 });
            // Moving tangentially at slightly-too-fast speed through the ring band.
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(2, -3), Velocity = new Vec2(0, 1.6), Radius = 0.25 });
            l.Targets.Add(new TargetDefinition { Id = "ring", Kind = TargetKind.OrbitRing, CenterSource = "star", Radius = 2, Tolerance = 0.35, CaptureSpeed = 0.6, DwellTime = 0.5, Drag = 4 });
            var s = Run(l);
            Assert.IsTrue(s.Targets[0].Captured);
            Assert.AreEqual(PlanetStatus.Orbiting, s.Planets[0].Status);
            for (int i = 0; i < 600; i++) GravitySimulation.Step(s, l.Constants, null);
            Assert.AreEqual(2, s.Planets[0].Position.Length, 1e-9, "planet stays exactly on the ring");
        }

        [Test]
        public void OrbitRingIgnoresRadialFlyThrough()
        {
            var l = TestLevels.Sandbox();
            l.Planets.Add(new PlanetDefinition { Id = "p", Position = new Vec2(0, -4), Velocity = new Vec2(0, 3), Radius = 0.25 });
            l.Targets.Add(new TargetDefinition { Id = "ring", Kind = TargetKind.OrbitRing, Position = new Vec2(0, 0), Radius = 2, Tolerance = 0.3, CaptureSpeed = 0.6, DwellTime = 0.5, Drag = 2 });
            var s = Run(l, null, 600);
            Assert.IsFalse(s.Targets[0].Captured);
        }

        [Test]
        public void TargetRestrictedToPlanetIgnoresOthers()
        {
            var l = DockLevel(new Vec2(0, 2.5));
            l.Planets.Add(new PlanetDefinition { Id = "other", Position = new Vec2(10, 10), Radius = 0.3 });
            l.Targets[0].Planet = "other";
            var s = Run(l, null, 600);
            Assert.IsFalse(s.Targets[0].Captured);
        }
    }
}
