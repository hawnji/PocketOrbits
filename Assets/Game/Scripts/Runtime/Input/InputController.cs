using System;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Simulation;
using PocketOrbits.Gameplay;
using PocketOrbits.Rendering;
using UnityEngine;

namespace PocketOrbits.Input
{
    public enum PointerPhase { Began, Moved, Ended, Canceled }

    /// <summary>
    /// Touch (and mouse in the editor) selection, dragging and placement of gravity beacons.
    /// Only acts in states where the session accepts edits, so nothing can be moved while a launch resolves.
    /// Gestures that begin on a UI control are ignored. Tests can drive it through <see cref="Pointer"/>.
    /// </summary>
    public sealed class InputController : MonoBehaviour
    {
        LevelManager _levels;
        CameraRig _camera;
        Func<Vector2, bool> _isOverUi;

        bool _gestureActive;
        bool _gestureIgnored;
        bool _dragging;
        Vec2 _grabOffset;
        Vector2 _downScreen;
        Vector2 _lastScreen;

        /// <summary>Minimum finger target radius in density-independent pixels.</summary>
        public float TouchRadiusDp = 36f;
        public bool LegacyInputAvailable { get; private set; } = true;

        public void Init(LevelManager levels, CameraRig camera, Func<Vector2, bool> isOverUi)
        {
            _levels = levels;
            _camera = camera;
            _isOverUi = isOverUi;
        }

        static float Dp => Screen.dpi > 0 ? Screen.dpi / 160f : Mathf.Max(1f, Screen.height / 800f);

        void Update()
        {
            if (_levels == null || !_levels.IsPlaying || !LegacyInputAvailable) return;
            try
            {
                ReadDevice();
            }
            catch (InvalidOperationException)
            {
                // Project set to "Input System Package" only: legacy input is unavailable (see Docs/Setup.md).
                LegacyInputAvailable = false;
                Debug.LogError("[PocketOrbits] Legacy input is disabled. Set Player > Active Input Handling to 'Both' or run Pocket Orbits > Set Up Project.");
            }
        }

        void ReadDevice()
        {
            if (UnityEngine.Input.touchCount > 0)
            {
                var t = UnityEngine.Input.GetTouch(0);
                switch (t.phase)
                {
                    case TouchPhase.Began: Pointer(t.position, PointerPhase.Began); break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary: Pointer(t.position, PointerPhase.Moved); break;
                    case TouchPhase.Ended: Pointer(t.position, PointerPhase.Ended); break;
                    case TouchPhase.Canceled: Pointer(t.position, PointerPhase.Canceled); break;
                }
                return;
            }
            Vector2 mouse = UnityEngine.Input.mousePosition;
            if (UnityEngine.Input.GetMouseButtonDown(0)) Pointer(mouse, PointerPhase.Began);
            else if (UnityEngine.Input.GetMouseButton(0)) Pointer(mouse, PointerPhase.Moved);
            else if (UnityEngine.Input.GetMouseButtonUp(0)) Pointer(mouse, PointerPhase.Ended);

            // Editor / keyboard shortcuts.
            var s = _levels.Session;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) _levels.Launch();
            if (UnityEngine.Input.GetKeyDown(KeyCode.Z)) _levels.Undo();
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) _levels.ResetLevel();
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) && s.IsDragging) _levels.Confirm();
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && s.IsDragging) _levels.Cancel();
        }

        /// <summary>Feeds one pointer sample (screen pixels, origin bottom-left).</summary>
        public void Pointer(Vector2 screen, PointerPhase phase)
        {
            var session = _levels.Session;
            if (session == null) return;
            switch (phase)
            {
                case PointerPhase.Began:
                    _gestureActive = true;
                    _dragging = false;
                    _downScreen = screen;
                    _lastScreen = screen;
                    _gestureIgnored = _isOverUi != null && _isOverUi(screen);
                    if (_gestureIgnored) return;
                    BeginOnBeacon(session, screen);
                    break;
                case PointerPhase.Moved:
                    if (!_gestureActive || _gestureIgnored || !_dragging) return;
                    if (!session.IsDragging) { _dragging = false; return; }
                    if ((screen - _lastScreen).sqrMagnitude < 0.25f) return; // finger resting: nothing to update
                    _lastScreen = screen;
                    _levels.DragTo(_camera.ScreenToSim(screen) + _grabOffset);
                    break;
                case PointerPhase.Ended:
                    if (_dragging && session.IsDragging && (screen - _downScreen).sqrMagnitude > 4f)
                        _levels.DragTo(_camera.ScreenToSim(screen) + _grabOffset);
                    _gestureActive = false;
                    _dragging = false;
                    break;
                case PointerPhase.Canceled:
                    _gestureActive = false;
                    _dragging = false;
                    break;
            }
        }

        void BeginOnBeacon(PuzzleSession session, Vector2 screen)
        {
            if (session.Current != GameState.ReadyForInput && !session.IsDragging) return;
            Vec2 p = _camera.ScreenToSim(screen);
            int hit = PickSource(session, p);
            if (hit < 0) return;

            if (session.IsDragging && hit != session.SelectedSource)
            {
                // Switching beacons abandons the uncommitted placement of the previous one.
                _levels.Cancel();
            }
            if (!session.IsDragging && !_levels.TrySelect(hit)) return;

            Vec2 current = session.IsDragging ? session.PendingPosition : session.State.Sources[hit].Position;
            _grabOffset = current - p;
            _dragging = true;
        }

        /// <summary>Nearest movable beacon within a finger-sized radius of the touch.</summary>
        public int PickSource(PuzzleSession session, Vec2 p)
        {
            double minFinger = TouchRadiusDp * Dp * _camera.WorldPerPixel;
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < session.Level.Sources.Count; i++)
            {
                var def = session.Level.Sources[i];
                if (def.Kind != SourceKind.Beacon) continue;
                Vec2 pos = session.IsDragging && i == session.SelectedSource ? session.PendingPosition : session.State.Sources[i].Position;
                double d = Vec2.Distance(pos, p);
                double reach = Math.Max(session.State.Sources[i].Radius + 0.35, minFinger);
                if (d <= reach && d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
