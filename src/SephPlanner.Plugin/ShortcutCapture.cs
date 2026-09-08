using System;
using System.Collections.Generic;

namespace SephPlanner.Plugin
{
    internal sealed class ShortcutCapture<T> where T : struct
    {
        private enum Stage { Idle, ReleaseBefore, Listening, ReleaseAfter }

        private readonly Func<T, bool> _isModifier;
        private readonly T _cancelKey;
        private Stage _stage;
        private int _startedFrame;

        public ShortcutCapture(Func<T, bool> isModifier, T cancelKey)
        {
            _isModifier = isModifier;
            _cancelKey = cancelKey;
        }

        public bool Capturing => _stage == Stage.ReleaseBefore || _stage == Stage.Listening;
        public bool BlocksShortcuts => _stage != Stage.Idle;

        public void Begin(int frame)
        {
            _startedFrame = frame;
            _stage = Stage.ReleaseBefore;
        }

        public void Cancel()
        {
            if (BlocksShortcuts) _stage = Stage.ReleaseAfter;
        }

        public bool Update(int frame, IReadOnlyList<T> held, IReadOnlyList<T> pressed, out T? captured)
        {
            captured = null;
            if (!BlocksShortcuts) return false;
            if (frame == _startedFrame) return true;

            if (_stage == Stage.ReleaseBefore || _stage == Stage.ReleaseAfter)
            {
                if (held.Count == 0)
                    _stage = _stage == Stage.ReleaseBefore ? Stage.Listening : Stage.Idle;
                return true;
            }

            foreach (var key in pressed)
            {
                if (!EqualityComparer<T>.Default.Equals(key, _cancelKey)) continue;
                Cancel();
                return true;
            }
            foreach (var key in pressed)
            {
                if (_isModifier(key)) continue;
                captured = key;
                _stage = Stage.ReleaseAfter;
                break;
            }
            return true;
        }
    }
}
