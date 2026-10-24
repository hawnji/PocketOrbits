using System;
using System.Collections.Generic;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Objectives;
using PocketOrbits.Core.Progression;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Core.Gameplay
{
    public enum GameState
    {
        Initializing = 0,
        ReadyForInput = 1,
        /// <summary>A beacon is held but has not moved yet.</summary>
        SelectingObject = 2,
        /// <summary>A beacon has a pending position awaiting Confirm / Cancel.</summary>
        PreviewingMove = 3,
        Simulating = 4,
        LevelCompleted = 5,
        LevelFailed = 6,
        Paused = 7,
    }

    public enum SelectResult { Selected = 0, InvalidState = 1, NotMovable = 2, OutOfMoves = 3, NoUsesLeft = 4 }

    /// <summary>
    /// One play session of one level: the explicit state machine, moves, undo/reset, hints and the fixed-step
    /// simulation clock. Contains no engine or UI code; the Unity layer drives it and renders its state.
    ///
    /// Valid input per state:
    ///   ReadyForInput   : Select, Launch, Undo, Reset, RevealHint, Pause
    ///   SelectingObject : Drag, Confirm, Cancel (Undo/Reset/Pause cancel the drag first)
    ///   PreviewingMove  : Drag, Confirm, Cancel (as above)
    ///   Simulating      : Advance, Undo (aborts the launch), Reset, Pause
    ///   Paused          : Resume, Reset
    ///   LevelFailed     : Undo, Reset, RevealHint
    ///   LevelCompleted  : Reset (replay)
    /// </summary>
    public sealed class PuzzleSession
    {
        public readonly LevelDefinition Level;
        public readonly SimConstants Constants;
        public readonly SimState State;
        readonly MoveHistory _history = new MoveHistory();
        readonly TrajectoryPredictor _predictor = new TrajectoryPredictor();
        readonly List<SimEvent> _stepEvents = new List<SimEvent>(8);
        double _accumulator;
        int _segmentEndStep;
        GameState _stateBeforePause;

        public GameState Current { get; private set; } = GameState.Initializing;
        public int Moves { get; private set; }
        public int Launches { get; private set; }
        public bool HintUsed { get; private set; }
        public int HintsRevealed { get; private set; }
        public Outcome LastOutcome { get; private set; } = Outcome.InProgress;

        public int SelectedSource { get; private set; } = -1;
        public Vec2 PendingPosition { get; private set; }
        public PlacementResult PendingPlacement { get; private set; }

        /// <summary>Upper bound of fixed steps per Advance call, keeps a frame hitch from freezing the device.</summary>
        public int MaxStepsPerAdvance = 240;

        public event Action<GameState, GameState> StateChanged;
        public event Action<SimEvent> SimEventRaised;

        public PuzzleSession(LevelDefinition level)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));
            Constants = level.Constants.Clone();
            State = LevelFactory.CreateInitialState(level);
            SetState(GameState.ReadyForInput);
        }

        public int CanUndoCount => _history.Count;
        public bool CanUndo => _history.Count > 0 && (Current == GameState.ReadyForInput || Current == GameState.LevelFailed || Current == GameState.Simulating || IsDragging);
        public bool IsDragging => Current == GameState.SelectingObject || Current == GameState.PreviewingMove;
        public bool HasMoveLimit => Level.MaxMoves > 0;
        public int MovesRemaining => HasMoveLimit ? Math.Max(0, Level.MaxMoves - Moves) : int.MaxValue;
        public int Stars => Current == GameState.LevelCompleted ? StarEvaluator.Evaluate(Level.Stars, true, Moves, HintUsed) : 0;
        /// <summary>Stars the player would get if they completed right now with the current move count.</summary>
        public int ProjectedStars => StarEvaluator.Evaluate(Level.Stars, true, Moves, HintUsed);
        public bool CanLaunch => Current == GameState.ReadyForInput;

        void SetState(GameState next)
        {
            if (next == Current) return;
            var prev = Current;
            Current = next;
            StateChanged?.Invoke(prev, next);
        }

        // ---- Beacon placement ------------------------------------------------------------------

        public bool CanEdit(int sourceIndex, out SelectResult reason)
        {
            reason = SelectResult.Selected;
            if (Current != GameState.ReadyForInput) { reason = SelectResult.InvalidState; return false; }
            if (sourceIndex < 0 || sourceIndex >= Level.Sources.Count || !Level.Sources[sourceIndex].IsMovable) { reason = SelectResult.NotMovable; return false; }
            var src = State.Sources[sourceIndex];
            if (src.Mobility == BeaconMobility.LimitedUse && src.UsesRemaining <= 0) { reason = SelectResult.NoUsesLeft; return false; }
            if (src.CostsMove && HasMoveLimit && Moves >= Level.MaxMoves) { reason = SelectResult.OutOfMoves; return false; }
            return true;
        }

        public SelectResult Select(int sourceIndex)
        {
            if (!CanEdit(sourceIndex, out var reason)) return reason;
            SelectedSource = sourceIndex;
            PendingPosition = State.Sources[sourceIndex].Position;
            PendingPlacement = new PlacementResult { Position = PendingPosition, Valid = true };
            SetState(GameState.SelectingObject);
            return SelectResult.Selected;
        }

        /// <summary>Moves the held beacon towards <paramref name="desired"/>; the pending position is clamped to legal areas.</summary>
        public PlacementResult Drag(Vec2 desired)
        {
            if (!IsDragging) return new PlacementResult { Valid = false, Issue = PlacementIssue.NotMovable };
            var result = PlacementRules.Evaluate(Level, State, SelectedSource, desired);
            PendingPlacement = result;
            PendingPosition = result.Position;
            SetState(GameState.PreviewingMove);
            return result;
        }

        /// <summary>Commits the pending placement. Costs a move if the beacon says so. Returns false if invalid.</summary>
        public bool Confirm()
        {
            if (!IsDragging) return false;
            int src = SelectedSource;
            if (!PendingPlacement.Valid) return false;
            if (PendingPosition == State.Sources[src].Position)
            {
                Cancel();
                return true;
            }
            _history.Push(State, Moves);
            State.Sources[src].Position = PendingPosition;
            if (State.Sources[src].Mobility == BeaconMobility.LimitedUse) State.Sources[src].UsesRemaining--;
            if (State.Sources[src].CostsMove) Moves++;
            SelectedSource = -1;
            SetState(GameState.ReadyForInput);
            return true;
        }

        public void Cancel()
        {
            if (!IsDragging) return;
            SelectedSource = -1;
            SetState(GameState.ReadyForInput);
        }

        // ---- Simulation ------------------------------------------------------------------------

        public bool Launch()
        {
            if (Current != GameState.ReadyForInput) return false;
            _history.Push(State, Moves);
            Launches++;
            _accumulator = 0;
            int turnSteps = Level.TurnDuration > 0 ? (int)Math.Round(Level.TurnDuration / Constants.TimeStep) : int.MaxValue;
            long end = (long)State.StepCount + turnSteps;
            _segmentEndStep = end > Constants.MaxSteps ? Constants.MaxSteps : (int)end;
            LastOutcome = Outcome.InProgress;
            SetState(GameState.Simulating);
            return true;
        }

        /// <summary>
        /// Advances the simulation by real time scaled by <paramref name="speed"/>. Work is done in whole fixed
        /// steps, so the outcome depends only on the number of steps, never on frame timing.
        /// </summary>
        public int Advance(double realDeltaSeconds, double speed = 1.0)
        {
            if (Current != GameState.Simulating) return 0;
            _accumulator += Math.Max(0, realDeltaSeconds) * speed;
            int steps = 0;
            while (_accumulator >= Constants.TimeStep && steps < MaxStepsPerAdvance && Current == GameState.Simulating)
            {
                _accumulator -= Constants.TimeStep;
                StepOnce();
                steps++;
            }
            if (steps >= MaxStepsPerAdvance) _accumulator = 0; // drop backlog instead of spiralling
            return steps;
        }

        /// <summary>Runs exactly one fixed step (used by Advance and tests).</summary>
        public void StepOnce()
        {
            if (Current != GameState.Simulating) return;
            _stepEvents.Clear();
            GravitySimulation.Step(State, Constants, _stepEvents);
            for (int i = 0; i < _stepEvents.Count; i++) SimEventRaised?.Invoke(_stepEvents[i]);

            var outcome = PuzzleRules.Evaluate(Level, State);
            if (outcome.Kind == OutcomeKind.Success)
            {
                LastOutcome = outcome;
                SetState(GameState.LevelCompleted);
            }
            else if (outcome.Kind == OutcomeKind.Failure)
            {
                LastOutcome = outcome;
                SetState(GameState.LevelFailed);
            }
            else if (State.StepCount >= _segmentEndStep)
            {
                // Turn-based level: hand control back with planets frozen mid-flight.
                SetState(GameState.ReadyForInput);
            }
        }

        /// <summary>Runs the current launch to its end without rendering (tests, solution harness).</summary>
        public void RunToEnd()
        {
            while (Current == GameState.Simulating) StepOnce();
        }

        // ---- Undo / reset ----------------------------------------------------------------------

        public bool Undo()
        {
            if (IsDragging) Cancel();
            if (Current != GameState.ReadyForInput && Current != GameState.LevelFailed && Current != GameState.Simulating) return false;
            if (!_history.Pop(State, out int moves)) return false;
            Moves = moves;
            LastOutcome = Outcome.InProgress;
            _accumulator = 0;
            SetState(GameState.ReadyForInput);
            return true;
        }

        /// <summary>Restores the complete authored initial state. Hint usage is kept for the session (see docs).</summary>
        public void Reset()
        {
            if (IsDragging) Cancel();
            State.CopyFrom(LevelFactory.CreateInitialState(Level));
            _history.Clear();
            Moves = 0;
            Launches = 0;
            _accumulator = 0;
            SelectedSource = -1;
            LastOutcome = Outcome.InProgress;
            SetState(GameState.ReadyForInput);
        }

        // ---- Pause -----------------------------------------------------------------------------

        public void Pause()
        {
            if (Current == GameState.Paused || Current == GameState.Initializing) return;
            if (IsDragging) Cancel();
            _stateBeforePause = Current;
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (Current != GameState.Paused) return;
            SetState(_stateBeforePause);
        }

        // ---- Hints -----------------------------------------------------------------------------

        public bool HasMoreHints => HintsRevealed < Level.Hints.Count;

        /// <summary>Reveals the next hint tier and marks the attempt as hinted. Returns null when none remain.</summary>
        public HintDefinition RevealHint()
        {
            if (Current == GameState.LevelCompleted || Current == GameState.Simulating || Current == GameState.Paused) return null;
            if (!HasMoreHints) return null;
            HintUsed = true;
            return Level.Hints[HintsRevealed++];
        }

        public HintDefinition CurrentHint => HintsRevealed > 0 ? Level.Hints[HintsRevealed - 1] : null;

        // ---- Prediction ------------------------------------------------------------------------

        public bool PreviewAvailable => Level.PreviewEnabled && (Current == GameState.ReadyForInput || IsDragging);

        /// <summary>Predicts from the live state, applying the pending beacon position when one is held.</summary>
        public TrajectoryPrediction Predict(int sampleEvery = 4)
        {
            _predictor.SampleEvery = sampleEvery;
            int src = IsDragging ? SelectedSource : -1;
            double remaining = Level.TurnDuration > 0 ? Math.Min(Level.PreviewDuration, Level.Constants.MaxDuration - State.Time) : Level.PreviewDuration;
            return _predictor.Predict(State, Constants, src, PendingPosition, Math.Max(0, remaining));
        }
    }
}
