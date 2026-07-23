using System;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class SceneReferenceRefreshGate
    {
        private bool _dirty = true;

        internal int RefreshCount { get; private set; }
        internal bool IsDirty => _dirty;

        internal void Invalidate()
        {
            _dirty = true;
        }

        internal bool EnsureCurrent(Action refresh)
        {
            if (!_dirty) return false;
            if (refresh == null) throw new ArgumentNullException(nameof(refresh));

            refresh();
            RefreshCount++;
            _dirty = false;
            return true;
        }
    }
}
