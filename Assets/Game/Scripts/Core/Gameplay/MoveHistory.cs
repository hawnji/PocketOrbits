using System.Collections.Generic;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Core.Gameplay
{
    /// <summary>
    /// Stack of complete gameplay snapshots. A snapshot holds the whole <see cref="SimState"/>
    /// (positions, velocities, beacon positions and uses, target capture/dwell progress, hazards, time)
    /// plus the move count, so undo can never leave hidden velocities or capture flags behind.
    /// Snapshot objects are pooled to avoid garbage during play.
    /// </summary>
    public sealed class MoveHistory
    {
        public sealed class Snapshot
        {
            public readonly SimState State = new SimState(0, 0, 0, 0);
            public int Moves;
        }

        readonly List<Snapshot> _stack = new List<Snapshot>();
        readonly Stack<Snapshot> _pool = new Stack<Snapshot>();

        public int Count => _stack.Count;

        public void Push(SimState state, int moves)
        {
            var snap = _pool.Count > 0 ? _pool.Pop() : new Snapshot();
            snap.State.CopyFrom(state);
            snap.Moves = moves;
            _stack.Add(snap);
        }

        /// <summary>Restores the most recent snapshot into <paramref name="state"/>. Returns false if empty.</summary>
        public bool Pop(SimState state, out int moves)
        {
            moves = 0;
            if (_stack.Count == 0) return false;
            var snap = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            state.CopyFrom(snap.State);
            moves = snap.Moves;
            _pool.Push(snap);
            return true;
        }

        public void Clear()
        {
            foreach (var s in _stack) _pool.Push(s);
            _stack.Clear();
        }
    }
}
