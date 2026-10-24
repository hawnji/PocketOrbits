using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Simulation;
using PocketOrbits.Core.Utilities;

namespace PocketOrbits.LevelLab
{
    /// <summary>
    /// Level authoring tool.
    ///   check &lt;levelsDir&gt;                 validate every level and run its stored solution
    ///   solve &lt;level.json&gt; [step]          grid-search single-beacon (and two-beacon) solutions
    ///   trace &lt;level.json&gt; &lt;out.json&gt;     dump geometry + solution path for rendering
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length < 2) { Console.WriteLine("usage: check <dir> | solve <file> [step] | trace <file> <out>"); return 2; }
            switch (args[0])
            {
                case "check": return Check(args[1]);
                case "solve": return Solve(args[1], args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 0.1);
                case "trace": return Trace(args[1], args[2]);
                default: Console.WriteLine("unknown command"); return 2;
            }
        }

        static LevelDefinition Load(string path) => LevelJson.Parse(File.ReadAllText(path), Path.GetFileName(path));

        static int Check(string dir)
        {
            var levels = Directory.GetFiles(dir, "*.json").OrderBy(f => f).Select(Load).ToList();
            var report = LevelValidator.ValidateCatalog(levels);
            foreach (var l in new LevelCatalog(levels).Levels)
            {
                var r = SolutionRunner.Run(l, l.Solution.Actions);
                Console.WriteLine($"{l.Id,-8} {l.Name,-22} diff={l.Difficulty} moves={r.Moves} stars={r.Stars} completed={r.Completed} t={r.SimTime:0.00}s {r.Error}");
            }
            Console.WriteLine(report.Ok ? "VALID: " + levels.Count + " levels" : "INVALID:\n" + report);
            if (report.Warnings.Count > 0 && report.Ok) Console.WriteLine("Warnings:\n" + string.Join("\n", report.Warnings));
            return report.Ok ? 0 : 1;
        }

        static IEnumerable<Vec2> Grid(GravitySourceDefinition src, double step)
        {
            foreach (var r in src.Regions)
            {
                Rect2 box = r.Shape == RegionShape.Rect ? r.Rect : new Rect2(r.Center.X - r.Radius, r.Center.Y - r.Radius, r.Center.X + r.Radius, r.Center.Y + r.Radius);
                for (double x = Math.Ceiling(box.MinX / step) * step; x <= box.MaxX + 1e-9; x += step)
                for (double y = Math.Ceiling(box.MinY / step) * step; y <= box.MaxY + 1e-9; y += step)
                {
                    var p = new Vec2(Math.Round(x, 3), Math.Round(y, 3));
                    if (r.Contains(p)) yield return p;
                }
            }
        }

        static int Solve(string file, double step)
        {
            var level = Load(file);
            var movable = level.Sources.Where(s => s.IsMovable).ToList();
            Console.WriteLine($"{level.Id}: {movable.Count} movable beacon(s)");
            var idle = SolutionRunner.Run(level, new List<SolutionAction> { new SolutionAction { Type = SolutionActionType.Launch } });
            Console.WriteLine($"  no-move launch: completed={idle.Completed} failure={idle.Failure} t={idle.SimTime:0.00}");

            foreach (var b in movable)
            {
                var pts = Grid(b, step).ToList();
                var ok = new System.Collections.Concurrent.ConcurrentDictionary<Vec2, double>();
                Parallel.ForEach(pts, p =>
                {
                    var r = SolutionRunner.Run(level, new List<SolutionAction>
                    {
                        new SolutionAction { Type = SolutionActionType.Place, Beacon = b.Id, Position = p },
                        new SolutionAction { Type = SolutionActionType.Launch },
                    });
                    if (r.Completed) ok[p] = r.SimTime;
                });
                Console.WriteLine($"  beacon {b.Id}: {ok.Count}/{pts.Count} grid points solve in one move ({100.0 * ok.Count / Math.Max(1, pts.Count):0.0}%)");
                if (ok.Count > 0)
                {
                    // Most robust point = most solving neighbours within 2 grid steps.
                    var best = ok.Keys.OrderByDescending(p => ok.Keys.Count(q => Vec2.Distance(p, q) <= 2.01 * step)).ThenBy(p => p.X).ThenBy(p => p.Y).First();
                    int neigh = ok.Keys.Count(q => Vec2.Distance(best, q) <= 2.01 * step);
                    Console.WriteLine($"    most robust: {best} (neighbours {neigh}), t={ok[best]:0.00}s");
                    var minX = ok.Keys.Min(p => p.X); var maxX = ok.Keys.Max(p => p.X);
                    var minY = ok.Keys.Min(p => p.Y); var maxY = ok.Keys.Max(p => p.Y);
                    Console.WriteLine($"    solution bbox x[{minX:0.##},{maxX:0.##}] y[{minY:0.##},{maxY:0.##}]");
                }
            }

            if (movable.Count == 2)
            {
                double coarse = Math.Max(step, 0.25);
                var a = Grid(movable[0], coarse).ToList();
                var bb = Grid(movable[1], coarse).ToList();
                var pairs = a.SelectMany(p => bb.Select(q => (p, q))).ToList();
                var ok = new System.Collections.Concurrent.ConcurrentBag<(Vec2, Vec2)>();
                Parallel.ForEach(pairs, pq =>
                {
                    var r = SolutionRunner.Run(level, new List<SolutionAction>
                    {
                        new SolutionAction { Type = SolutionActionType.Place, Beacon = movable[0].Id, Position = pq.p },
                        new SolutionAction { Type = SolutionActionType.Place, Beacon = movable[1].Id, Position = pq.q },
                        new SolutionAction { Type = SolutionActionType.Launch },
                    });
                    if (r.Completed) ok.Add(pq);
                });
                Console.WriteLine($"  two-beacon: {ok.Count}/{pairs.Count} pairs solve ({100.0 * ok.Count / Math.Max(1, pairs.Count):0.00}%)");
                var list = ok.ToList();
                if (list.Count > 0)
                {
                    var best = list.OrderByDescending(x => list.Count(y => Vec2.Distance(x.Item1, y.Item1) <= 1.01 * coarse && Vec2.Distance(x.Item2, y.Item2) <= 1.01 * coarse)).First();
                    Console.WriteLine($"    most robust pair: {movable[0].Id}={best.Item1} {movable[1].Id}={best.Item2}");
                }
            }
            return 0;
        }

        static int Trace(string file, string outPath)
        {
            var level = Load(file);
            level.PreviewDuration = level.Constants.MaxDuration;
            var root = new Dictionary<string, object>
            {
                { "id", level.Id },
                { "bounds", new List<object> { level.Bounds.MinX, level.Bounds.MinY, level.Bounds.MaxX, level.Bounds.MaxY } },
            };
            var sources = new List<object>();
            foreach (var s in level.Sources)
            {
                var regions = new List<object>();
                foreach (var r in s.Regions)
                    regions.Add(r.Shape == RegionShape.Circle
                        ? new Dictionary<string, object> { { "circle", new List<object> { r.Center.X, r.Center.Y, r.Radius } } }
                        : new Dictionary<string, object> { { "rect", new List<object> { r.Rect.MinX, r.Rect.MinY, r.Rect.MaxX, r.Rect.MaxY } } });
                sources.Add(new Dictionary<string, object> { { "id", s.Id }, { "kind", s.Kind.ToString() }, { "pos", P(s.Position) }, { "radius", s.Radius }, { "strength", s.Strength }, { "movable", s.IsMovable }, { "regions", regions } });
            }
            root["sources"] = sources;
            root["hazards"] = level.Hazards.Select(h => (object)new Dictionary<string, object> { { "pos", P(h.Position) }, { "radius", h.Radius } }).ToList();
            var initial = LevelFactory.CreateInitialState(level);
            var targets = new List<object>();
            for (int i = 0; i < level.Targets.Count; i++)
            {
                var t = level.Targets[i];
                targets.Add(new Dictionary<string, object> { { "kind", t.Kind.ToString() }, { "pos", P(initial.TargetCenter(i)) }, { "radius", t.Radius }, { "tolerance", t.Tolerance }, { "order", t.Order } });
            }
            root["targets"] = targets;
            root["planets"] = level.Planets.Select(p => (object)new Dictionary<string, object> { { "pos", P(p.Position) }, { "radius", p.Radius } }).ToList();

            // Idle path (initial preview) and solution path.
            var session = new PuzzleSession(level);
            root["idlePath"] = Paths(session.Predict(2));
            var solved = new List<object>();
            foreach (var a in level.Solution.Actions)
            {
                if (a.Type == SolutionActionType.Launch)
                {
                    var pred = session.Predict(2);
                    solved.Add(Paths(pred));
                    session.Launch();
                    session.RunToEnd();
                }
                else
                {
                    session.Select(session.State.FindSource(a.Beacon));
                    session.Drag(a.Position);
                    session.Confirm();
                    solved.Add(new Dictionary<string, object> { { "beacon", a.Beacon }, { "pos", P(a.Position) } });
                }
            }
            root["solution"] = solved;
            root["result"] = session.Current.ToString() + " stars=" + session.Stars;
            File.WriteAllText(outPath, MiniJson.Serialize(root));
            Console.WriteLine(level.Id + ": " + root["result"]);
            return 0;
        }

        static List<object> P(Vec2 v) => new List<object> { v.X, v.Y };

        static List<object> Paths(TrajectoryPrediction pred) => pred.Paths.Select(path => (object)path.Select(p => (object)P(p)).ToList()).ToList();
    }
}
