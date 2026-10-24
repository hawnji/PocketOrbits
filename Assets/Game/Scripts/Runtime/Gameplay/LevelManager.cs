using System;
using PocketOrbits.Audio;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Objectives;
using PocketOrbits.Core.Progression;
using PocketOrbits.Core.Saving;
using PocketOrbits.Core.Simulation;
using PocketOrbits.Rendering;
using UnityEngine;

namespace PocketOrbits.Gameplay
{
    /// <summary>Services created by <see cref="Bootstrap.GameBootstrap"/> and shared by the runtime systems.</summary>
    public sealed class GameServices
    {
        public ArtLibrary Art;
        public AudioManager Audio;
        public SaveService Save;
        public ProgressionManager Progression;
        public LevelCatalog Catalog;
        public CameraRig Camera;
        public EffectsView Effects;
        public BackdropView Backdrop;

        public GameSettings Settings => Save.Data.Settings;
    }

    /// <summary>
    /// Loads, starts, resets and completes levels. Owns the active <see cref="PuzzleSession"/>, drives its fixed-step
    /// clock from frame time, refreshes the trajectory preview at a controlled rate and turns simulation events into
    /// audio, haptics and effects. Progress is recorded (and saved) the moment a level is completed.
    /// </summary>
    public sealed class LevelManager : MonoBehaviour
    {
        GameServices _services;
        LevelView _view;
        float _predictionTimer;
        bool _predictionDirty = true;
        bool _fastForward;

        public PuzzleSession Session { get; private set; }
        public LevelDefinition Level => Session?.Level;
        public LevelView View => _view;
        public CompletionResult LastCompletion { get; private set; }
        public bool FastForward { get => _fastForward; set => _fastForward = value; }
        /// <summary>Prediction refresh interval while dragging (seconds).</summary>
        public float PredictionInterval = 0.05f;

        public event Action<CompletionResult> LevelCompleted;
        public event Action<FailureReason> LevelFailed;
        public event Action<string> Message;
        public event Action<GameState> StateChanged;

        public void Init(GameServices services)
        {
            _services = services;
            var go = new GameObject("LevelView");
            go.transform.SetParent(transform, false);
            _view = go.AddComponent<LevelView>();
        }

        public bool IsPlaying => Session != null;

        public void StartLevel(LevelDefinition level)
        {
            if (Session != null) Session.StateChanged -= OnStateChanged;
            if (Session != null) Session.SimEventRaised -= OnSimEvent;
            Session = new PuzzleSession(level);
            Session.StateChanged += OnStateChanged;
            Session.SimEventRaised += OnSimEvent;
            _fastForward = false;
            var s = _services.Settings;
            _view.gameObject.SetActive(true);
            _view.Build(_services.Art, Session, s.HighContrast, s.EffectsIntensity);
            _services.Progression.RecordAttempt(level.Id);
            _predictionDirty = true;
            StateChanged?.Invoke(Session.Current);
        }

        public void ExitLevel()
        {
            if (Session != null)
            {
                Session.StateChanged -= OnStateChanged;
                Session.SimEventRaised -= OnSimEvent;
            }
            Session = null;
            _view.ShowHint(null);
            _view.gameObject.SetActive(false);
            _services.Save.Save();
        }

        public void ApplySettings()
        {
            var s = _services.Settings;
            if (Session != null) _view.SetAccessibility(s.HighContrast, s.EffectsIntensity);
            _predictionDirty = true;
        }

        // ---- Player actions (called by input and UI) -------------------------------------------

        public bool TrySelect(int sourceIndex)
        {
            if (Session == null) return false;
            var r = Session.Select(sourceIndex);
            switch (r)
            {
                case SelectResult.Selected:
                    _services.Audio.Play(Sfx.Select);
                    _services.Audio.Haptic();
                    _predictionDirty = true;
                    return true;
                case SelectResult.OutOfMoves: Message?.Invoke("No moves left. Launch, undo or reset."); break;
                case SelectResult.NoUsesLeft: Message?.Invoke("This beacon has already been moved."); break;
                case SelectResult.NotMovable: Message?.Invoke("This one is fixed in place."); break;
                default: return false;
            }
            _services.Audio.Play(Sfx.Invalid);
            return false;
        }

        bool _wasValid = true;

        public void DragTo(Vec2 desired)
        {
            if (Session == null || !Session.IsDragging) return;
            var r = Session.Drag(desired);
            if (!r.Valid && _wasValid)
            {
                _services.Audio.Play(Sfx.Invalid, 1.2f);
                _services.Effects.Invalid(Plane2D.ToWorld(r.Position));
            }
            _wasValid = r.Valid;
            _predictionDirty = true;
        }

        public bool Confirm()
        {
            if (Session == null) return false;
            bool ok = Session.Confirm();
            if (ok)
            {
                _services.Audio.Play(Sfx.Place);
                _services.Audio.Haptic();
            }
            else
            {
                _services.Audio.Play(Sfx.Invalid);
                Message?.Invoke("That spot is blocked. Move the beacon somewhere clear.");
            }
            _predictionDirty = true;
            return ok;
        }

        public void Cancel()
        {
            Session?.Cancel();
            _services.Audio.Play(Sfx.Undo);
            _predictionDirty = true;
        }

        public bool Launch()
        {
            if (Session == null) return false;
            if (Session.IsDragging && !Session.Confirm()) return false;
            if (!Session.Launch()) return false;
            _services.Audio.Play(Sfx.Launch);
            _services.Audio.Haptic();
            _services.Save.Data.TotalLaunches++;
            _view.Trajectory.Hide();
            return true;
        }

        public void Undo()
        {
            if (Session == null) return;
            if (Session.Undo())
            {
                _services.Audio.Play(Sfx.Undo);
                _view.SyncImmediate();
            }
            _predictionDirty = true;
        }

        public void ResetLevel()
        {
            if (Session == null) return;
            Session.Reset();
            _fastForward = false;
            _services.Audio.Play(Sfx.Undo, 0.8f);
            _view.SyncImmediate();
            // Keep showing the most recent hint after a reset, it is still relevant.
            _view.ShowHint(Session.CurrentHint);
            _predictionDirty = true;
        }

        public HintDefinition RevealHint()
        {
            if (Session == null) return null;
            var h = Session.RevealHint();
            if (h != null)
            {
                _services.Audio.Play(Sfx.Hint);
                _view.ShowHint(h);
            }
            return h;
        }

        public void Pause() => Session?.Pause();
        public void Resume() => Session?.Resume();

        // ---- Frame loop --------------------------------------------------------------------------

        void Update()
        {
            if (Session == null) return;
            if (Session.Current == GameState.Simulating)
            {
                Session.Advance(Time.deltaTime, _fastForward ? 2.5 : 1.0);
                return;
            }

            bool want = Session.PreviewAvailable && _services.Settings.TrajectoryPreview;
            if (!want)
            {
                _view.Trajectory.Hide();
                return;
            }
            _predictionTimer -= Time.unscaledDeltaTime;
            if (_predictionDirty && _predictionTimer <= 0f)
            {
                // Low quality samples the path less densely; the physics is identical either way.
                int sampleEvery = _services.Settings.Quality == 0 ? 6 : 3;
                _view.Trajectory.Show(Session.Predict(sampleEvery));
                _predictionDirty = false;
                _predictionTimer = PredictionInterval;
            }
        }

        void OnStateChanged(GameState prev, GameState next)
        {
            _predictionDirty = true;
            if (next == GameState.LevelCompleted)
            {
                LastCompletion = _services.Progression.RecordCompletion(Session.Level, Session.Moves, Session.HintUsed);
                _services.Audio.Play(Sfx.Complete);
                _services.Audio.Haptic(true);
                LevelCompleted?.Invoke(LastCompletion);
            }
            else if (next == GameState.LevelFailed)
            {
                _services.Audio.Play(Sfx.Fail);
                _services.Audio.Haptic(true);
                _services.Save.Save();
                LevelFailed?.Invoke(Session.LastOutcome.Reason);
            }
            StateChanged?.Invoke(next);
        }

        void OnSimEvent(SimEvent e)
        {
            var pos = Plane2D.ToWorld(e.Position);
            switch (e.Type)
            {
                case SimEventType.TargetCaptured:
                    _services.Effects.Capture(pos, Palette.PreviewSuccess);
                    _services.Audio.Play(Sfx.Capture);
                    _services.Audio.Haptic();
                    break;
                case SimEventType.WaypointReached:
                    _services.Effects.Waypoint(pos, Palette.Target(_services.Settings.HighContrast));
                    _services.Audio.Play(Sfx.Waypoint);
                    break;
                default:
                    _services.Effects.Crash(pos);
                    break;
            }
        }
    }
}
