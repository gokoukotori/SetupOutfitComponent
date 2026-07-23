using System;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class ReviewValidationCache
    {
        private ValidationResult _validation;
        private bool _dirty = true;

        internal int EvaluationCount { get; private set; }

        internal void Invalidate()
        {
            _validation = null;
            _dirty = true;
        }

        internal ValidationResult Get(Func<ValidationResult> validate)
        {
            if (!_dirty && _validation != null) return _validation;
            if (validate == null) throw new ArgumentNullException(nameof(validate));

            _validation = validate();
            _dirty = false;
            EvaluationCount++;
            return _validation;
        }
    }
}
