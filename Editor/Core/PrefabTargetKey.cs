using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal readonly struct PrefabTargetKey : IEquatable<PrefabTargetKey>
    {
        private readonly int[] _siblingIndices;

        internal PrefabTargetKey(string dependencyHash, IEnumerable<int> siblingIndices)
        {
            DependencyHash = dependencyHash ?? string.Empty;
            _siblingIndices = (siblingIndices ?? Enumerable.Empty<int>()).ToArray();
        }

        internal string DependencyHash { get; }
        internal IReadOnlyList<int> SiblingIndices => _siblingIndices ?? Array.Empty<int>();
        internal int Depth => _siblingIndices?.Length ?? 0;
        internal bool IsRoot => Depth == 0;
        internal string SiblingIndexPath => string.Join("/", SiblingIndices);

        internal static PrefabTargetKey FromTransform(
            Transform prefabRoot,
            Transform target,
            string dependencyHash)
        {
            if (prefabRoot == null) throw new ArgumentNullException(nameof(prefabRoot));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target != prefabRoot && !target.IsChildOf(prefabRoot))
            {
                throw new ArgumentException("Target must be the prefab root or one of its descendants.", nameof(target));
            }

            var reversed = new List<int>();
            var current = target;
            while (current != prefabRoot)
            {
                reversed.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            reversed.Reverse();
            return new PrefabTargetKey(dependencyHash, reversed);
        }

        internal GameObject Resolve(GameObject prefabRoot, string expectedDependencyHash)
        {
            if (prefabRoot == null || !string.Equals(DependencyHash, expectedDependencyHash, StringComparison.Ordinal))
            {
                return null;
            }

            var current = prefabRoot.transform;
            foreach (var siblingIndex in SiblingIndices)
            {
                if (siblingIndex < 0 || siblingIndex >= current.childCount)
                {
                    return null;
                }

                current = current.GetChild(siblingIndex);
            }

            return current.gameObject;
        }

        internal bool IsAncestorOf(PrefabTargetKey other)
        {
            if (!string.Equals(DependencyHash, other.DependencyHash, StringComparison.Ordinal) || Depth >= other.Depth)
            {
                return false;
            }

            for (var index = 0; index < Depth; index++)
            {
                if (SiblingIndices[index] != other.SiblingIndices[index]) return false;
            }

            return true;
        }

        public bool Equals(PrefabTargetKey other)
        {
            return string.Equals(DependencyHash, other.DependencyHash, StringComparison.Ordinal)
                   && SiblingIndices.SequenceEqual(other.SiblingIndices);
        }

        public override bool Equals(object obj)
        {
            return obj is PrefabTargetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = StringComparer.Ordinal.GetHashCode(DependencyHash ?? string.Empty);
                foreach (var siblingIndex in SiblingIndices)
                {
                    hashCode = (hashCode * 397) ^ siblingIndex;
                }

                return hashCode;
            }
        }

        public override string ToString()
        {
            return DependencyHash + ":" + SiblingIndexPath;
        }

        public static bool operator ==(PrefabTargetKey left, PrefabTargetKey right) => left.Equals(right);
        public static bool operator !=(PrefabTargetKey left, PrefabTargetKey right) => !left.Equals(right);
    }
}
