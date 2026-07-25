using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitSceneActiveStateResolver
    {
        private readonly ImmutableDictionary<GameObject, TransformRule> _rules;

        internal OutfitSceneActiveStateResolver(
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts)
        {
            var mutableRules = new Dictionary<GameObject, MutableTransformRule>();
            foreach (var master in masterSceneTargets
                         ?? Enumerable.Empty<OutfitMasterSceneTargetPreviewSnapshot>())
            {
                if (master.SceneObject == null)
                    throw new InvalidOperationException("ステップ3のScene対象を解決できませんでした。");
                var rule = GetOrCreateRule(mutableRules, master.SceneObject);
                if (rule.MasterActiveWhenOn.HasValue)
                    throw new InvalidOperationException("同じScene対象が複数回指定されています。");
                rule.MasterActiveWhenOn = master.ActiveWhenOn;
            }

            foreach (var part in parts ?? Enumerable.Empty<OutfitPartPreviewSnapshot>())
            {
                foreach (var target in part.Targets)
                {
                    if (target.Source != PartTargetSource.SceneObject) continue;
                    if (target.SceneObject == null)
                    {
                        throw new InvalidOperationException(
                            $"個別項目「{part.Label}」のScene対象を解決できませんでした。");
                    }

                    GetOrCreateRule(mutableRules, target.SceneObject)
                        .PartControls.Add(new PartControl(
                            part.ItemId,
                            target.ActiveWhenOn));
                }
            }

            _rules = mutableRules.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutable());
        }

        internal IEnumerable<GameObject> ControlledObjects => _rules.Keys;

        internal bool IsHierarchyActive(
            GameObject target,
            Transform stopExclusive,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            if (target == null) return false;
            if (stopExclusive != null
                && target.transform != stopExclusive
                && !target.transform.IsChildOf(stopExclusive))
            {
                return false;
            }

            var current = target.transform;
            while (current != null && current != stopExclusive)
            {
                var activeSelf = current.gameObject.activeSelf;
                if (previewOn
                    && _rules.TryGetValue(current.gameObject, out var rule))
                {
                    activeSelf = rule.MasterActiveWhenOn
                                 ?? rule.OriginalActiveSelf;
                    if (PartToggleMenuOrderResolver.TryResolveLastEnabled(
                            rule.PartControls,
                            control => control.ItemId,
                            control => control.ActiveWhenOn,
                            partStates,
                            out var partActive))
                    {
                        activeSelf = partActive;
                    }
                }

                if (!activeSelf) return false;
                current = current.parent;
            }

            return stopExclusive == null || current == stopExclusive;
        }

        private static MutableTransformRule GetOrCreateRule(
            IDictionary<GameObject, MutableTransformRule> rules,
            GameObject target)
        {
            if (!rules.TryGetValue(target, out var rule))
            {
                rule = new MutableTransformRule(target.activeSelf);
                rules.Add(target, rule);
            }

            return rule;
        }

        private sealed class MutableTransformRule
        {
            internal MutableTransformRule(bool originalActiveSelf)
            {
                OriginalActiveSelf = originalActiveSelf;
            }

            internal bool OriginalActiveSelf { get; }
            internal bool? MasterActiveWhenOn { get; set; }
            internal List<PartControl> PartControls { get; } =
                new List<PartControl>();

            internal TransformRule ToImmutable()
            {
                return new TransformRule(
                    OriginalActiveSelf,
                    MasterActiveWhenOn,
                    PartControls.ToImmutableArray());
            }
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

        private readonly struct TransformRule
        {
            internal TransformRule(
                bool originalActiveSelf,
                bool? masterActiveWhenOn,
                ImmutableArray<PartControl> partControls)
            {
                OriginalActiveSelf = originalActiveSelf;
                MasterActiveWhenOn = masterActiveWhenOn;
                PartControls = partControls.IsDefault
                    ? ImmutableArray<PartControl>.Empty
                    : partControls;
            }

            internal bool OriginalActiveSelf { get; }
            internal bool? MasterActiveWhenOn { get; }
            internal ImmutableArray<PartControl> PartControls { get; }
        }
    }
}
