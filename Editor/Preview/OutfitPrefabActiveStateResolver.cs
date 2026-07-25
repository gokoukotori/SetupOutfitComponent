using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitPrefabActiveStateResolver
    {
        private readonly GameObject _sourcePrefab;
        private readonly string _dependencyHash;
        private readonly ImmutableDictionary<Transform, ImmutableArray<PartControl>> _controls;
        private readonly bool _placementHierarchyActive;

        internal OutfitPrefabActiveStateResolver(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitPartPreviewSnapshot> parts)
        {
            _sourcePrefab = sourcePrefab != null
                ? sourcePrefab
                : throw new ArgumentNullException(nameof(sourcePrefab));
            _dependencyHash = dependencyHash ?? string.Empty;
            if (sourceToMirror == null) throw new ArgumentNullException(nameof(sourceToMirror));

            var controls = new Dictionary<Transform, List<PartControl>>();
            foreach (var part in parts ?? Enumerable.Empty<OutfitPartPreviewSnapshot>())
            {
                foreach (var target in part.Targets)
                {
                    if (target.Source != PartTargetSource.OutfitPrefab) continue;
                    var sourceTarget = target.PrefabKey.Resolve(sourcePrefab, dependencyHash);
                    if (sourceTarget == null)
                    {
                        throw new InvalidOperationException(
                            $"個別項目「{part.Label}」の対象をPrefab上で解決できませんでした。");
                    }

                    if (!controls.TryGetValue(sourceTarget.transform, out var targetControls))
                    {
                        targetControls = new List<PartControl>();
                        controls.Add(sourceTarget.transform, targetControls);
                    }

                    targetControls.Add(new PartControl(part.ItemId, target.ActiveWhenOn));
                }
            }

            _controls = controls.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutableArray());

            _placementHierarchyActive = true;
            if (sourceToMirror.TryGetValue(sourcePrefab.transform, out var mirrorOutfitRoot))
            {
                var placementCursor = mirrorOutfitRoot.parent;
                while (placementCursor != null)
                {
                    _placementHierarchyActive &= placementCursor.gameObject.activeSelf;
                    placementCursor = placementCursor.parent;
                }
            }
        }

        internal IEnumerable<Transform> ControlledTransforms => _controls.Keys;

        internal bool IsActive(
            PrefabTargetKey targetKey,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var target = targetKey.Resolve(_sourcePrefab, _dependencyHash);
            if (target == null)
            {
                throw new InvalidOperationException(
                    "衣装内GameObjectのactive状態をPrefab上で解決できませんでした。");
            }

            return IsActive(target.transform, previewOn, partStates);
        }

        internal bool IsActive(
            Transform sourceTransform,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            if (!previewOn || !_placementHierarchyActive || sourceTransform == null)
                return false;
            if (sourceTransform != _sourcePrefab.transform
                && !sourceTransform.IsChildOf(_sourcePrefab.transform))
            {
                throw new InvalidOperationException(
                    "衣装外GameObjectのactive状態は解決できません。");
            }

            var current = sourceTransform;
            while (current != null)
            {
                var activeSelf = current == _sourcePrefab.transform
                    ? true
                    : current.gameObject.activeSelf;
                if (_controls.TryGetValue(current, out var controls)
                    && PartToggleMenuOrderResolver.TryResolveLastEnabled(
                        controls,
                        control => control.ItemId,
                        control => control.ActiveWhenOn,
                        partStates,
                        out var controlledActive))
                {
                    activeSelf = controlledActive;
                }

                if (!activeSelf) return false;
                if (current == _sourcePrefab.transform) return true;
                current = current.parent;
            }

            return false;
        }

        private readonly struct PartControl
        {
            internal PartControl(string itemId, bool activeWhenOn)
            {
                ItemId = itemId ?? string.Empty;
                ActiveWhenOn = activeWhenOn;
            }

            internal string ItemId { get; }
            internal bool ActiveWhenOn { get; }
        }
    }
}
