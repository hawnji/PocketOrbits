using System.Collections.Generic;
using PocketOrbits.Audio;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Objectives;
using PocketOrbits.Core.Progression;
using PocketOrbits.Core.Simulation;
using PocketOrbits.Gameplay;
using PocketOrbits.Rendering;
using UnityEngine;

namespace PocketOrbits.UI
{
    public enum Screen2 { MainMenu, Worlds, Levels, Gameplay }

    /// <summary>
    /// Coordinates every screen: main menu, world and level selection, gameplay HUD, pause, settings,
    /// completion and failure prompts, hints and toasts. Built with IMGUI so the whole UI is code (no prefabs)
    /// and works with touch out of the box. It only calls public methods on <see cref="LevelManager"/>;
    /// simulation and rules never depend on UI code.
    /// </summary>
    public sealed class UIController : MonoBehaviour
    {
        GameServices _services;
        LevelManager _levels;
        UiSkin _skin;
        float _skinDp, _skinScale;

        public Screen2 Current { get; private set; } = Screen2.MainMenu;
        int _world = 1;
        bool _settingsOpen;
        bool _confirmProgressReset;
        bool _paused;
        string _toast;
        float _toastTimer;
        float _fade;
        float _resultTimer;
        int _starsShown;
        string _hintText;

        readonly List<Rect> _blockingRects = new List<Rect>();
        readonly List<Rect> _pendingRects = new List<Rect>();
        float _topInset = -1, _bottomInset = -1;

        public void Init(GameServices services, LevelManager levels)
        {
            _services = services;
            _levels = levels;
            _levels.Message += ShowToast;
            _levels.LevelCompleted += r => { _resultTimer = 0; _starsShown = 0; };
            _levels.LevelFailed += reason => { _resultTimer = 0; };
            _levels.StateChanged += s => { if (s == GameState.ReadyForInput && _levels.Session.Level.TurnDuration > 0 && _levels.Session.Launches > 0) ShowToast("Flight paused. Adjust the beacon or launch again."); };
        }

        static float Dp => UnityEngine.Screen.dpi > 0 ? Mathf.Clamp(UnityEngine.Screen.dpi / 160f, 0.75f, 4f) : Mathf.Max(1f, UnityEngine.Screen.height / 800f);

        /// <summary>True if a screen point (pixels, origin bottom-left) is over a UI control. Used by input.</summary>
        public bool IsPointerOverUi(Vector2 screen)
        {
            var gui = new Vector2(screen.x, UnityEngine.Screen.height - screen.y);
            if (Current != Screen2.Gameplay || _paused || _settingsOpen) return true;
            foreach (var r in _blockingRects) if (r.Contains(gui)) return true;
            return false;
        }

        public void ShowToast(string message)
        {
            _toast = message;
            _toastTimer = 3f;
        }

        public void GoTo(Screen2 screen)
        {
            Current = screen;
            _fade = 1f;
            _settingsOpen = false;
            _paused = false;
            _confirmProgressReset = false;
            if (screen != Screen2.Gameplay && _levels.IsPlaying) _levels.ExitLevel();
            if (screen != Screen2.Gameplay) _services.Camera.Frame(new Rect2(-4.5, -8, 4.5, 8), 0, 0);
            _topInset = _bottomInset = -1;
        }

        public void PlayLevel(LevelDefinition level)
        {
            _hintText = null;
            _paused = false;
            Current = Screen2.Gameplay;
            _fade = 1f;
            _levels.StartLevel(level);
            _topInset = _bottomInset = -1;
            if (level.World == 1 && level.Number == 1) ShowToast("Drag the glowing beacon. The dotted line shows where the planet will go.");
        }

        void Update()
        {
            if (_toastTimer > 0) _toastTimer -= Time.unscaledDeltaTime;
            if (_fade > 0) _fade = Mathf.Max(0, _fade - Time.unscaledDeltaTime * 4f);
            _resultTimer += Time.unscaledDeltaTime;

            if (Current == Screen2.Gameplay && _levels.Session != null && _levels.Session.Current == GameState.LevelCompleted)
            {
                int target = _levels.LastCompletion.Stars;
                if (_starsShown < target && _resultTimer > 0.5f + _starsShown * 0.35f)
                {
                    _starsShown++;
                    _services.Audio.Play(Sfx.Star, 1f + _starsShown * 0.12f);
                }
            }

            // Android back button / Escape.
            bool back = false;
            try { back = UnityEngine.Input.GetKeyDown(KeyCode.Escape); } catch (System.InvalidOperationException) { }
            if (back) OnBack();
        }

        void OnBack()
        {
            if (_settingsOpen) { CloseSettings(); return; }
            switch (Current)
            {
                case Screen2.Gameplay:
                    if (_levels.Session != null && _levels.Session.IsDragging) return; // handled by input (cancel)
                    if (_paused) Resume(); else Pause();
                    break;
                case Screen2.Levels: GoTo(Screen2.Worlds); break;
                case Screen2.Worlds: GoTo(Screen2.MainMenu); break;
            }
        }

        public void Pause()
        {
            _paused = true;
            _levels.Pause();
        }

        public void Resume()
        {
            _paused = false;
            _levels.Resume();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Current == Screen2.Gameplay && !_paused) Pause();
        }

        void CloseSettings()
        {
            _settingsOpen = false;
            _confirmProgressReset = false;
            _services.Save.Save();
        }

        // ---- IMGUI ------------------------------------------------------------------------------

        void OnGUI()
        {
            var settings = _services.Settings;
            if (_skin == null || !Mathf.Approximately(_skinDp, Dp) || !Mathf.Approximately(_skinScale, settings.TextScale))
            {
                _skinDp = Dp;
                _skinScale = settings.TextScale;
                _skin = new UiSkin(_skinDp, _skinScale);
            }
            if (Event.current.type == EventType.Layout) _pendingRects.Clear();

            Rect safe = SafeArea();
            switch (Current)
            {
                case Screen2.MainMenu: DrawMainMenu(safe); break;
                case Screen2.Worlds: DrawWorlds(safe); break;
                case Screen2.Levels: DrawLevels(safe); break;
                case Screen2.Gameplay: DrawGameplay(safe); break;
            }
            if (_settingsOpen) DrawSettings(safe);
            DrawToast(safe);

            if (_fade > 0)
            {
                GUI.color = new Color(0, 0, 0, _fade);
                GUI.DrawTexture(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), _skin.Solid);
                GUI.color = Color.white;
            }
            if (Event.current.type == EventType.Repaint)
            {
                _blockingRects.Clear();
                _blockingRects.AddRange(_pendingRects);
            }
        }

        static Rect SafeArea()
        {
            var sa = UnityEngine.Screen.safeArea;
            return new Rect(sa.x, UnityEngine.Screen.height - sa.yMax, sa.width, sa.height);
        }

        float U => _skin.U;

        void Block(Rect r) => _pendingRects.Add(r);

        bool Button(Rect r, string text, bool primary = false, bool enabled = true)
        {
            Block(r);
            bool was = GUI.enabled;
            GUI.enabled = enabled;
            bool clicked = GUI.Button(r, text, primary ? _skin.PrimaryButton : _skin.Button);
            GUI.enabled = was;
            if (clicked)
            {
                _services.Audio.Play(Sfx.Click);
                _services.Audio.Haptic();
            }
            return clicked;
        }

        void Panel(Rect r, Color? color = null)
        {
            Block(r);
            GUI.color = color ?? UiSkin.PanelColor;
            GUI.Box(r, GUIContent.none, _skin.Panel);
            GUI.color = Color.white;
        }

        void Dim()
        {
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), _skin.Solid);
            GUI.color = Color.white;
            Block(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height));
        }

        void Stars(Rect r, int filled, int total = 3, float pop = 1f)
        {
            float size = Mathf.Min(r.height, r.width / total);
            float x0 = r.center.x - size * total / 2f;
            for (int i = 0; i < total; i++)
            {
                bool on = i < filled;
                float s = size * (on ? pop : 0.85f);
                var sr = new Rect(x0 + i * size + (size - s) / 2, r.y + (r.height - s) / 2, s, s);
                GUI.color = on ? UiSkin.Gold : new Color(1, 1, 1, 0.22f);
                GUI.DrawTexture(sr, _services.Art.StarIcon);
            }
            GUI.color = Color.white;
        }

        // ---- Menus ------------------------------------------------------------------------------

        void DrawMainMenu(Rect safe)
        {
            float w = Mathf.Min(safe.width - 32 * U, 420 * U);
            float x = safe.center.x - w / 2;
            float y = safe.y + safe.height * 0.16f;
            GUI.Label(new Rect(x, y, w, 60 * U), "Pocket Orbits", _skin.Title);
            GUI.Label(new Rect(x, y + 58 * U, w, 30 * U), "Bend gravity. Guide tiny worlds home.", _skin.Small);

            var prog = _services.Progression;
            y = safe.y + safe.height * 0.5f;
            var next = prog.SuggestedLevel();
            string play = next == null ? "Play" : "Play  " + next.DisplayNumber;
            if (Button(new Rect(x, y, w, 64 * U), play, true) && next != null) PlayLevel(next);
            if (Button(new Rect(x, y + 76 * U, w, 56 * U), "Levels")) GoTo(Screen2.Worlds);
            if (Button(new Rect(x, y + 144 * U, w, 56 * U), "Settings")) _settingsOpen = true;

            GUI.Label(new Rect(x, safe.yMax - 40 * U, w, 24 * U), "Stars " + prog.TotalStars + " / " + _services.Catalog.Levels.Count * 3 + "   ·   prototype build", _skin.Small);
        }

        void DrawWorlds(Rect safe)
        {
            float w = Mathf.Min(safe.width - 32 * U, 480 * U);
            float x = safe.center.x - w / 2;
            float y = safe.y + 16 * U;
            if (Button(new Rect(x, y, 96 * U, 48 * U), "Back")) GoTo(Screen2.MainMenu);
            GUI.Label(new Rect(x, y, w, 48 * U), "Worlds", _skin.Heading);
            y += 72 * U;
            for (int world = 1; world <= LevelCatalog.WorldNames.Length; world++)
            {
                var levels = _services.Catalog.InWorld(world);
                bool unlocked = levels.Count > 0 && _services.Progression.IsWorldUnlocked(world);
                var r = new Rect(x, y, w, 96 * U);
                string sub = levels.Count == 0 ? "Coming soon" : unlocked ? levels.Count + " levels" : "Finish the previous world to unlock";
                string label = "World " + world + "  ·  " + LevelCatalog.WorldName(world);
                if (Button(r, "", false, unlocked) && unlocked)
                {
                    _world = world;
                    GoTo(Screen2.Levels);
                }
                GUI.Label(new Rect(r.x, r.y + 12 * U, r.width, 30 * U), label, _skin.Heading);
                GUI.Label(new Rect(r.x, r.y + 46 * U, r.width, 22 * U), sub, _skin.Small);
                if (levels.Count > 0)
                    GUI.Label(new Rect(r.x, r.y + 66 * U, r.width, 24 * U), _services.Progression.StarsInWorld(world) + " / " + levels.Count * 3 + " stars", _skin.Caption);
                y += 108 * U;
            }
        }

        void DrawLevels(Rect safe)
        {
            float w = Mathf.Min(safe.width - 24 * U, 520 * U);
            float x = safe.center.x - w / 2;
            float y = safe.y + 16 * U;
            if (Button(new Rect(x, y, 96 * U, 48 * U), "Back")) GoTo(Screen2.Worlds);
            GUI.Label(new Rect(x, y, w, 48 * U), LevelCatalog.WorldName(_world), _skin.Heading);
            y += 72 * U;
            var levels = _services.Catalog.InWorld(_world);
            const int cols = 4;
            float gap = 10 * U;
            float tw = (w - gap * (cols - 1)) / cols;
            float th = tw * 1.1f;
            for (int i = 0; i < levels.Count; i++)
            {
                var l = levels[i];
                var r = new Rect(x + (i % cols) * (tw + gap), y + (i / cols) * (th + gap), tw, th);
                bool unlocked = _services.Progression.IsUnlocked(l.Id);
                Block(r);
                GUI.enabled = unlocked;
                if (GUI.Button(r, unlocked ? l.Number.ToString() : "", _skin.Tile) && unlocked)
                {
                    _services.Audio.Play(Sfx.Click);
                    PlayLevel(l);
                }
                GUI.enabled = true;
                if (unlocked)
                {
                    GUI.Label(new Rect(r.x, r.y + r.height * 0.45f, r.width, 22 * U), l.Name, _skin.Small);
                    Stars(new Rect(r.x + 8 * U, r.yMax - 30 * U, r.width - 16 * U, 22 * U), _services.Save.Data.StarsFor(l.Id));
                }
                else
                {
                    GUI.Label(new Rect(r.x, r.y, r.width, r.height), "Locked", _skin.Small);
                }
            }
        }

        // ---- Gameplay HUD -----------------------------------------------------------------------

        void DrawGameplay(Rect safe)
        {
            var session = _levels.Session;
            if (session == null) return;
            var level = session.Level;
            float pad = 12 * U;

            // Top bar.
            float topH = 132 * U;
            var top = new Rect(0, 0, UnityEngine.Screen.width, safe.y + topH);
            GUI.color = new Color(0.03f, 0.04f, 0.12f, 0.78f);
            GUI.DrawTexture(top, _skin.Solid);
            GUI.color = Color.white;
            Block(top);
            float tx = safe.x + pad, ty = safe.y + pad, tw = safe.width - pad * 2;
            if (Button(new Rect(tx, ty, 56 * U, 52 * U), "II")) Pause();
            GUI.Label(new Rect(tx + 64 * U, ty, tw - 128 * U, 28 * U), level.DisplayNumber + "  " + level.Name, _skin.Heading);
            Stars(new Rect(tx + tw - 96 * U, ty + 8 * U, 96 * U, 28 * U), session.Current == GameState.LevelCompleted ? session.Stars : session.ProjectedStars);
            GUI.Label(new Rect(tx + 64 * U, ty + 30 * U, tw - 128 * U, 46 * U), level.Objective.Text, _skin.Body);
            string moves = session.HasMoveLimit ? "Moves " + session.Moves + " / " + level.MaxMoves : "Moves " + session.Moves;
            string info = moves + "    " + PuzzleRules.Progress(session.State) + "    3 stars: " + level.Stars.ThreeStarMoves + (level.Stars.ThreeStarMoves == 1 ? " move" : " moves");
            GUI.Label(new Rect(tx, ty + 82 * U, tw, 26 * U), info, _skin.Small);

            // Bottom bar.
            float barH = 84 * U;
            var bottom = new Rect(0, safe.yMax - barH, UnityEngine.Screen.width, barH + (UnityEngine.Screen.height - safe.yMax));
            GUI.color = new Color(0.03f, 0.04f, 0.12f, 0.78f);
            GUI.DrawTexture(bottom, _skin.Solid);
            GUI.color = Color.white;
            Block(bottom);
            DrawBottomControls(session, new Rect(safe.x + pad, safe.yMax - barH + pad, safe.width - pad * 2, barH - pad * 2));

            UpdateCameraInsets(top.height, bottom.height);
            DrawWorldLabels(session);

            // Hint banner.
            var hint = session.CurrentHint;
            if (hint != null && session.Current != GameState.LevelCompleted)
            {
                var hr = new Rect(safe.x + pad, top.yMax + 8 * U, safe.width - pad * 2, 56 * U);
                Panel(hr, new Color(0.30f, 0.24f, 0.05f, 0.9f));
                GUI.Label(new Rect(hr.x + 8 * U, hr.y, hr.width - 16 * U, hr.height), hint.Text, _skin.Body);
            }

            if (session.Current == GameState.LevelFailed) DrawFailed(session, safe, bottom.y);
            if (session.Current == GameState.LevelCompleted) DrawCompleted(session, safe);
            if (_paused && !_settingsOpen) DrawPause(safe);
        }

        void UpdateCameraInsets(float top, float bottom)
        {
            if (Mathf.Approximately(top, _topInset) && Mathf.Approximately(bottom, _bottomInset)) return;
            _topInset = top;
            _bottomInset = bottom;
            _services.Camera.Frame(_levels.Level.Bounds, top, bottom);
        }

        void DrawBottomControls(PuzzleSession session, Rect r)
        {
            float gap = 8 * U;
            if (session.IsDragging)
            {
                float half = (r.width - gap) / 2;
                if (Button(new Rect(r.x, r.y, half, r.height), "Cancel")) _levels.Cancel();
                bool valid = session.PendingPlacement.Valid;
                string costs = session.Level.Sources[session.SelectedSource].CostsMove ? "  (1 move)" : "  (free)";
                if (Button(new Rect(r.x + half + gap, r.y, half, r.height), valid ? "Confirm" + costs : "Blocked", true, valid)) _levels.Confirm();
                return;
            }
            if (session.Current == GameState.Simulating)
            {
                float half = (r.width - gap) / 2;
                if (Button(new Rect(r.x, r.y, half, r.height), "Stop & undo")) _levels.Undo();
                if (Button(new Rect(r.x + half + gap, r.y, half, r.height), _levels.FastForward ? "Speed  2.5x" : "Speed  1x")) _levels.FastForward = !_levels.FastForward;
                return;
            }
            if (session.Current == GameState.LevelCompleted || session.Current == GameState.LevelFailed) return;

            // Undo | Reset | Preview | Hint | Launch (double width)
            float unit = (r.width - gap * 4) / 6f;
            float x = r.x;
            if (Button(new Rect(x, r.y, unit, r.height), "Undo", false, session.CanUndo)) _levels.Undo();
            x += unit + gap;
            if (Button(new Rect(x, r.y, unit, r.height), "Reset")) _levels.ResetLevel();
            x += unit + gap;
            var settings = _services.Settings;
            if (Button(new Rect(x, r.y, unit, r.height), settings.TrajectoryPreview ? "Path\nOn" : "Path\nOff", false, session.Level.PreviewEnabled))
            {
                settings.TrajectoryPreview = !settings.TrajectoryPreview;
                _levels.ApplySettings();
                _services.Save.Save();
            }
            x += unit + gap;
            if (Button(new Rect(x, r.y, unit, r.height), "Hint", false, session.HasMoreHints))
            {
                var h = _levels.RevealHint();
                if (h != null && session.HintsRevealed == 1) ShowToast("Hints are free, but the third star needs a hint-free solve.");
            }
            x += unit + gap;
            if (Button(new Rect(x, r.y, unit * 2, r.height), session.Launches > 0 && session.Level.TurnDuration > 0 ? "Continue" : "Launch", true)) _levels.Launch();
        }

        /// <summary>Labels drawn over the 3D scene: sequence numbers, planet/dock pairing letters, first-level prompt.</summary>
        void DrawWorldLabels(PuzzleSession session)
        {
            var s = session.State;
            var cam = _services.Camera;
            bool ordered = false;
            for (int i = 0; i < s.Targets.Length; i++) if (s.Targets[i].Order != s.Targets[0].Order) ordered = true;
            for (int i = 0; i < s.Targets.Length; i++)
            {
                string label = null;
                if (ordered) label = (s.Targets[i].Order + 1).ToString();
                if (s.Targets[i].RequiredPlanet >= 0) label = ((char)('A' + s.Targets[i].RequiredPlanet)).ToString();
                if (label == null) continue;
                Tag(cam.SimToScreen(s.TargetCenter(i)), label, s.Targets[i].Captured);
            }
            bool paired = false;
            for (int i = 0; i < s.Targets.Length; i++) if (s.Targets[i].RequiredPlanet >= 0) paired = true;
            if (paired)
            {
                for (int p = 0; p < s.Planets.Length; p++)
                {
                    if (!s.Planets[p].IsAlive || s.Planets[p].Status == PlanetStatus.Docked) continue;
                    Tag(cam.SimToScreen(s.Planets[p].Position + new Vec2(0, s.Planets[p].Radius + 0.35)), ((char)('A' + p)).ToString(), false);
                }
            }

            // First-level coaching: point at the beacon until the player has moved it once.
            if (session.Level.World == 1 && session.Level.Number == 1 && session.Moves == 0 && session.Current == GameState.ReadyForInput)
            {
                int b = s.FindSource("b1");
                if (b >= 0)
                {
                    var sp = cam.SimToScreen(s.Sources[b].Position);
                    float a = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 4f);
                    GUI.color = new Color(1, 1, 1, a);
                    GUI.Label(new Rect(sp.x - 80 * U, UnityEngine.Screen.height - sp.y - 64 * U, 160 * U, 28 * U), "Drag me", _skin.Heading);
                    GUI.color = Color.white;
                }
            }
        }

        void Tag(Vector2 screen, string text, bool done)
        {
            float size = 30 * U;
            var r = new Rect(screen.x - size / 2, UnityEngine.Screen.height - screen.y - size / 2, size, size);
            GUI.backgroundColor = done ? new Color(0.45f, 1f, 0.7f) : (_services.Settings.HighContrast ? new Color(1f, 1f, 0.3f) : new Color(0.35f, 1f, 0.72f));
            GUI.Label(r, text, _skin.Tag);
            GUI.backgroundColor = Color.white;
        }

        void DrawFailed(PuzzleSession session, Rect safe, float bottomY)
        {
            float w = Mathf.Min(safe.width - 24 * U, 460 * U);
            float h = 176 * U;
            var r = new Rect(safe.center.x - w / 2, bottomY - h - 12 * U, w, h);
            Panel(r);
            GUI.Label(new Rect(r.x, r.y + 10 * U, r.width, 30 * U), "Not quite", _skin.Heading);
            GUI.Label(new Rect(r.x + 12 * U, r.y + 42 * U, r.width - 24 * U, 44 * U), PuzzleRules.Describe(session.LastOutcome.Reason), _skin.Body);
            float gap = 8 * U, bw = (r.width - 24 * U - gap * 2) / 3, by = r.yMax - 72 * U;
            if (Button(new Rect(r.x + 12 * U, by, bw, 60 * U), "Undo", true)) _levels.Undo();
            if (Button(new Rect(r.x + 12 * U + bw + gap, by, bw, 60 * U), "Reset")) _levels.ResetLevel();
            if (Button(new Rect(r.x + 12 * U + (bw + gap) * 2, by, bw, 60 * U), "Hint", false, session.HasMoreHints))
            {
                _levels.Undo();
                _levels.RevealHint();
            }
        }

        void DrawCompleted(PuzzleSession session, Rect safe)
        {
            if (_resultTimer < 0.6f) return; // let the capture effect play first
            Dim();
            var result = _levels.LastCompletion;
            float w = Mathf.Min(safe.width - 32 * U, 440 * U);
            float h = 420 * U;
            var r = new Rect(safe.center.x - w / 2, safe.center.y - h / 2, w, h);
            Panel(r);
            float y = r.y + 16 * U;
            GUI.Label(new Rect(r.x, y, r.width, 36 * U), "Level complete", _skin.Heading);
            y += 44 * U;
            float pop = 1f + 0.15f * Mathf.Max(0, Mathf.Sin(_resultTimer * 6f)) * (_starsShown < result.Stars ? 1 : 0);
            Stars(new Rect(r.x, y, r.width, 64 * U), _starsShown, 3, pop);
            y += 74 * U;
            string movesText = session.Moves + (session.Moves == 1 ? " move" : " moves") + (session.HintUsed ? " · hint used" : "");
            GUI.Label(new Rect(r.x, y, r.width, 24 * U), movesText + (result.NewBest && !result.FirstCompletion ? "  ·  new best!" : ""), _skin.Body);
            y += 32 * U;
            var lines = StarEvaluator.Describe(session.Level.Stars);
            for (int i = 0; i < lines.Length; i++)
            {
                bool met = result.Stars > i;
                GUI.color = met ? UiSkin.Gold : new Color(1, 1, 1, 0.25f);
                GUI.DrawTexture(new Rect(r.x + 20 * U, y + 2 * U, 18 * U, 18 * U), _services.Art.StarIcon);
                GUI.color = Color.white;
                var ls = new GUIStyle(_skin.Small) { alignment = TextAnchor.MiddleLeft };
                GUI.Label(new Rect(r.x + 46 * U, y, r.width - 62 * U, 22 * U), lines[i], ls);
                y += 24 * U;
            }
            y = r.yMax - 140 * U;
            if (result.NextLevel != null)
            {
                if (Button(new Rect(r.x + 16 * U, y, r.width - 32 * U, 60 * U), "Next level", true)) PlayLevel(result.NextLevel);
            }
            else
            {
                GUI.Label(new Rect(r.x + 16 * U, y, r.width - 32 * U, 60 * U), "World complete! More worlds are on the way.", _skin.Body);
            }
            float half = (r.width - 32 * U - 8 * U) / 2;
            if (Button(new Rect(r.x + 16 * U, r.yMax - 72 * U, half, 56 * U), "Replay")) _levels.ResetLevel();
            if (Button(new Rect(r.x + 24 * U + half, r.yMax - 72 * U, half, 56 * U), "Levels"))
            {
                _world = session.Level.World;
                GoTo(Screen2.Levels);
            }
        }

        void DrawPause(Rect safe)
        {
            Dim();
            float w = Mathf.Min(safe.width - 32 * U, 400 * U);
            float h = 380 * U;
            var r = new Rect(safe.center.x - w / 2, safe.center.y - h / 2, w, h);
            Panel(r);
            float x = r.x + 16 * U, bw = r.width - 32 * U, y = r.y + 16 * U;
            GUI.Label(new Rect(r.x, y, r.width, 36 * U), "Paused", _skin.Heading);
            y += 52 * U;
            if (Button(new Rect(x, y, bw, 60 * U), "Resume", true)) Resume();
            y += 70 * U;
            if (Button(new Rect(x, y, bw, 56 * U), "Restart level")) { Resume(); _levels.ResetLevel(); }
            y += 66 * U;
            if (Button(new Rect(x, y, bw, 56 * U), "Settings")) _settingsOpen = true;
            y += 66 * U;
            float half = (bw - 8 * U) / 2;
            if (Button(new Rect(x, y, half, 56 * U), "Levels")) { _world = _levels.Level.World; GoTo(Screen2.Levels); }
            if (Button(new Rect(x + half + 8 * U, y, half, 56 * U), "Main menu")) GoTo(Screen2.MainMenu);
        }

        // ---- Settings ---------------------------------------------------------------------------

        void DrawSettings(Rect safe)
        {
            Dim();
            var s = _services.Settings;
            float w = Mathf.Min(safe.width - 24 * U, 460 * U);
            float h = Mathf.Min(safe.height - 24 * U, 640 * U);
            var r = new Rect(safe.center.x - w / 2, safe.center.y - h / 2, w, h);
            Panel(r);
            float x = r.x + 16 * U, cw = r.width - 32 * U, y = r.y + 12 * U;
            GUI.Label(new Rect(r.x, y, r.width, 36 * U), "Settings", _skin.Heading);
            y += 44 * U;
            bool changed = false;
            changed |= Slider(ref s.MusicVolume, "Music", x, ref y, cw, 0, 1);
            changed |= Slider(ref s.SfxVolume, "Sound effects", x, ref y, cw, 0, 1);
            changed |= Slider(ref s.TextScale, "Text size", x, ref y, cw, 0.8f, 1.6f);
            changed |= Slider(ref s.EffectsIntensity, "Visual effects", x, ref y, cw, 0, 1);
            changed |= Toggle(ref s.Haptics, "Haptics", x, ref y, cw);
            changed |= Toggle(ref s.HighContrast, "High-contrast targets", x, ref y, cw);
            changed |= Toggle(ref s.TrajectoryPreview, "Trajectory preview", x, ref y, cw);
            GUI.Label(new Rect(x, y, cw, 24 * U), "Quality", _skin.Caption);
            y += 26 * U;
            string[] q = { "Battery saver", "Balanced", "High" };
            float qw = (cw - 16 * U) / 3;
            for (int i = 0; i < 3; i++)
            {
                if (Button(new Rect(x + i * (qw + 8 * U), y, qw, 48 * U), q[i], s.Quality == i) && s.Quality != i)
                {
                    s.Quality = i;
                    changed = true;
                }
            }
            y += 58 * U;
            if (changed)
            {
                s.Sanitize();
                GameBootstrapHooks.ApplySettings?.Invoke();
            }

            float bh = 52 * U;
            if (Current == Screen2.MainMenu)
            {
                string resetLabel = _confirmProgressReset ? "Tap again to erase progress" : "Reset progress";
                if (Button(new Rect(x, r.yMax - bh * 2 - 22 * U, cw, bh), resetLabel))
                {
                    if (_confirmProgressReset)
                    {
                        _services.Save.Data.Levels.Clear();
                        _services.Save.Save();
                        _confirmProgressReset = false;
                        ShowToast("Progress erased.");
                    }
                    else _confirmProgressReset = true;
                }
            }
            if (Button(new Rect(x, r.yMax - bh - 12 * U, cw, bh), "Done", true)) CloseSettings();
        }

        bool Slider(ref float value, string label, float x, ref float y, float w, float min, float max)
        {
            GUI.Label(new Rect(x, y, w, 22 * U), label + "  " + Mathf.RoundToInt((value - min) / (max - min) * 100) + "%", _skin.Caption);
            y += 22 * U;
            var track = new Rect(x, y + 14 * U, w, 10 * U);
            GUI.color = UiSkin.AccentDark;
            GUI.DrawTexture(track, _skin.Rounded);
            GUI.color = UiSkin.Accent;
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * Mathf.InverseLerp(min, max, value), track.height), _skin.Rounded);
            GUI.color = Color.white;
            var hit = new Rect(x, y, w, 38 * U);
            Block(hit);
            float nv = GUI.HorizontalSlider(hit, value, min, max, GUIStyle.none, _skin.SliderThumb);
            y += 44 * U;
            if (Mathf.Abs(nv - value) < 0.0001f) return false;
            value = nv;
            return true;
        }

        bool Toggle(ref bool value, string label, float x, ref float y, float w)
        {
            GUI.Label(new Rect(x, y, w - 110 * U, 48 * U), label, _skin.Body);
            bool clicked = Button(new Rect(x + w - 100 * U, y, 100 * U, 44 * U), value ? "On" : "Off", value);
            y += 52 * U;
            if (clicked) value = !value;
            return clicked;
        }

        void DrawToast(Rect safe)
        {
            if (_toastTimer <= 0 || string.IsNullOrEmpty(_toast)) return;
            float w = Mathf.Min(safe.width - 32 * U, 460 * U);
            float h = _skin.Toast.CalcHeight(new GUIContent(_toast), w);
            float y = Current == Screen2.Gameplay ? safe.yMax - 84 * U - h - 16 * U : safe.yMax - h - 24 * U;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(_toastTimer * 2f));
            GUI.Label(new Rect(safe.center.x - w / 2, y, w, h), _toast, _skin.Toast);
            GUI.color = Color.white;
        }
    }

    /// <summary>Small static hook so the UI can ask the bootstrap to re-apply settings without a hard reference.</summary>
    public static class GameBootstrapHooks
    {
        public static System.Action ApplySettings;
    }
}
