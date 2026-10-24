using System;
using System.Collections.Generic;
using System.IO;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Tests.EditMode
{
    /// <summary>Shared fixtures: loads the shipped level files and builds small synthetic levels.</summary>
    public static class TestLevels
    {
        public const string LevelsRelativePath = "Assets/Game/Data/Resources/Levels";

        /// <summary>Finds the project root from the Unity editor (cwd = project) or from dotnet test (bin folder).</summary>
        public static string LevelsDirectory()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, LevelsRelativePath);
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate " + LevelsRelativePath);
        }

        public static List<LevelDefinition> LoadShipped()
        {
            var list = new List<LevelDefinition>();
            var files = Directory.GetFiles(LevelsDirectory(), "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files) list.Add(LevelJson.Parse(File.ReadAllText(f), Path.GetFileName(f)));
            return list;
        }

        public static IEnumerable<string> ShippedIds()
        {
            foreach (var l in LoadShipped()) yield return l.Id;
        }

        public static LevelDefinition Shipped(string id) => LoadShipped().Find(l => l.Id == id);

        /// <summary>A minimal valid level: one planet moving up, one movable beacon, one dock.</summary>
        public static LevelDefinition Simple()
        {
            var l = new LevelDefinition { Id = "test-simple", World = 1, Number = 1, Name = "Simple", Difficulty = 1 };
            l.Objective.Text = "Dock the planet.";
            l.Planets.Add(new PlanetDefinition { Id = "p1", Position = new Vec2(-2.5, -6), Velocity = new Vec2(0, 2.4), Radius = 0.35 });
            var b = new GravitySourceDefinition { Id = "b1", Kind = SourceKind.Beacon, Position = new Vec2(3, -4), Strength = 4, Mobility = BeaconMobility.Movable, CostsMove = true };
            b.Regions.Add(new PlacementRegion { Shape = RegionShape.Rect, Rect = new Rect2(-1.5, -4.5, 3.5, 1.5) });
            l.Sources.Add(b);
            l.Targets.Add(new TargetDefinition { Id = "t1", Kind = TargetKind.Dock, Position = new Vec2(2, 3), Radius = 1.0, CaptureSpeed = 2.2 });
            l.Stars.TwoStarMoves = 2;
            l.Stars.ThreeStarMoves = 1;
            l.Hints.Add(new HintDefinition { Text = "Pull the planet right.", Focus = "b1" });
            l.Hints.Add(new HintDefinition { Text = "Full solution.", RevealsSolution = true });
            l.Solution.Actions.Add(new SolutionAction { Type = SolutionActionType.Place, Beacon = "b1", Position = SimpleSolution });
            l.Solution.Actions.Add(new SolutionAction { Type = SolutionActionType.Launch });
            return l;
        }

        public static readonly Vec2 SimpleSolution = new Vec2(-1.3, -1.5);

        /// <summary>Empty sandbox level for physics tests (no validation requirements).</summary>
        public static LevelDefinition Sandbox(double width = 40, double height = 40)
        {
            var l = new LevelDefinition { Id = "sandbox", World = 1, Number = 1, Name = "Sandbox" };
            l.Bounds = new Rect2(-width / 2, -height / 2, width / 2, height / 2);
            l.Constants.MaxDuration = 200;
            return l;
        }
    }
}
