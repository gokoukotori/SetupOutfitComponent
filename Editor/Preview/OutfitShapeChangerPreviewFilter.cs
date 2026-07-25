using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitShapeChangerPreviewFilter : IRenderFilter
    {
        private readonly SkinnedMeshRenderer[] _renderers;
        private readonly PublishedValue<ShapeState> _state;

        internal OutfitShapeChangerPreviewFilter(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            _state = new PublishedValue<ShapeState>(
                CreateState(
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    parts,
                    changes,
                    previewOn,
                    partStates),
                "SetupOutfitComponent/ShapeChanger");
            _renderers = _state.Value.Rules.Keys
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
        }

        public bool CanEnableRenderers => false;
        internal int TargetGroupEvaluationCount { get; private set; }
        internal int NodeCreationCount { get; private set; }
        internal int RuleBuildCountForTests { get; private set; } = 1;

        internal bool HasEquivalentRendererSet(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes)
        {
            return _renderers.SequenceEqual(
                CollectRenderers(
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    changes));
        }

        internal void UpdateRules(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var next = CreateState(
                sourcePrefab,
                dependencyHash,
                sourceToMirror,
                parts,
                changes,
                previewOn,
                partStates);
            var nextRenderers = next.Rules.Keys
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
            if (!_renderers.SequenceEqual(nextRenderers))
            {
                throw new InvalidOperationException(
                    "Shape Changer対象のRenderer集合が変更されたためFilterの再登録が必要です。");
            }

            _state.Value = next;
            RuleBuildCountForTests++;
        }

        internal void SetPreviewState(
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            _state.Value = _state.Value.WithPreviewState(previewOn, partStates);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            TargetGroupEvaluationCount++;
            return _renderers
                .Select(renderer => RenderGroup.For(renderer))
                .ToImmutableList();
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            NodeCreationCount++;
            return Task.FromResult<IRenderFilterNode>(new Node(_state));
        }

        private static ShapeState CreateState(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var activeResolver = new OutfitPrefabActiveStateResolver(
                sourcePrefab,
                dependencyHash,
                sourceToMirror,
                parts);
            var mutableRules =
                new Dictionary<SkinnedMeshRenderer, Dictionary<string, List<ShapeControl>>>();
            foreach (var change in changes
                         ?? Enumerable.Empty<OutfitShapeChangePreviewSnapshot>())
            {
                var renderer = ResolveRenderer(
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    change);
                if (renderer == null || renderer.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        "Shape Changerプレビュー対象のRendererを解決できませんでした。");
                }

                var shapeIndex = renderer.sharedMesh.GetBlendShapeIndex(change.ShapeName);
                if (shapeIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Shape Changerプレビュー対象にBlendShape「{change.ShapeName}」がありません。");
                }

                if (!mutableRules.TryGetValue(renderer, out var shapeRules))
                {
                    shapeRules = new Dictionary<string, List<ShapeControl>>(
                        StringComparer.Ordinal);
                    mutableRules.Add(renderer, shapeRules);
                }

                if (!shapeRules.TryGetValue(change.ShapeName, out var controls))
                {
                    controls = new List<ShapeControl>();
                    shapeRules.Add(change.ShapeName, controls);
                }

                controls.Add(new ShapeControl(
                    change.OwnerItemId,
                    change.IsMaster,
                    change.HasOutfitOwner,
                    change.OutfitOwnerKey,
                    change.OwnerHierarchyOrder,
                    change.Value));
            }

            var rules = mutableRules.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .Select(shape => new ShapeRule(
                        pair.Key.sharedMesh.GetBlendShapeIndex(shape.Key),
                        shape.Value.ToImmutableArray()))
                    .ToImmutableArray());
            return new ShapeState(
                rules,
                activeResolver,
                CopyPartStates(partStates),
                previewOn);
        }

        private static SkinnedMeshRenderer[] CollectRenderers(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes)
        {
            return (changes ?? Enumerable.Empty<OutfitShapeChangePreviewSnapshot>())
                .Select(change => ResolveRenderer(
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    change))
                .Where(renderer => renderer != null)
                .Distinct()
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
        }

        private static SkinnedMeshRenderer ResolveRenderer(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            OutfitShapeChangePreviewSnapshot change)
        {
            if (change.Source == PartTargetSource.SceneObject)
                return change.SceneRenderer;

            var sourceObject = change.PrefabRendererKey.Resolve(
                sourcePrefab,
                dependencyHash);
            if (sourceObject == null
                || sourceToMirror == null
                || !sourceToMirror.TryGetValue(sourceObject.transform, out var mirrorTransform)
                || mirrorTransform == null)
            {
                return null;
            }

            return mirrorTransform.GetComponent<SkinnedMeshRenderer>();
        }

        private static ImmutableDictionary<string, bool> CopyPartStates(
            IReadOnlyDictionary<string, bool> partStates)
        {
            return (partStates ?? new Dictionary<string, bool>())
                .ToImmutableDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
        }

        private readonly struct ShapeControl
        {
            internal ShapeControl(
                string ownerItemId,
                bool isMaster,
                bool hasOutfitOwner,
                PrefabTargetKey outfitOwnerKey,
                int ownerHierarchyOrder,
                float value)
            {
                OwnerItemId = ownerItemId ?? string.Empty;
                IsMaster = isMaster;
                HasOutfitOwner = hasOutfitOwner;
                OutfitOwnerKey = outfitOwnerKey;
                OwnerHierarchyOrder = ownerHierarchyOrder;
                Value = value;
            }

            internal string OwnerItemId { get; }
            internal bool IsMaster { get; }
            internal bool HasOutfitOwner { get; }
            internal PrefabTargetKey OutfitOwnerKey { get; }
            internal int OwnerHierarchyOrder { get; }
            internal float Value { get; }
        }

        private readonly struct ShapeRule
        {
            internal ShapeRule(
                int shapeIndex,
                ImmutableArray<ShapeControl> controls)
            {
                ShapeIndex = shapeIndex;
                Controls = controls.IsDefault
                    ? ImmutableArray<ShapeControl>.Empty
                    : controls;
            }

            internal int ShapeIndex { get; }
            internal ImmutableArray<ShapeControl> Controls { get; }
        }

        private sealed class ShapeState
        {
            internal ShapeState(
                ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<ShapeRule>> rules,
                OutfitPrefabActiveStateResolver activeResolver,
                ImmutableDictionary<string, bool> partStates,
                bool previewOn)
            {
                Rules = rules;
                ActiveResolver = activeResolver;
                PartStates = partStates;
                PreviewOn = previewOn;
            }

            internal ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<ShapeRule>> Rules { get; }
            internal OutfitPrefabActiveStateResolver ActiveResolver { get; }
            internal ImmutableDictionary<string, bool> PartStates { get; }
            internal bool PreviewOn { get; }

            internal ShapeState WithPreviewState(
                bool previewOn,
                IReadOnlyDictionary<string, bool> partStates)
            {
                return new ShapeState(
                    Rules,
                    ActiveResolver,
                    CopyPartStates(partStates),
                    previewOn);
            }
        }

        private sealed class Node : IRenderFilterNode
        {
            private readonly PublishedValue<ShapeState> _state;

            internal Node(PublishedValue<ShapeState> state)
            {
                _state = state;
            }

            public RenderAspects WhatChanged => RenderAspects.Shapes;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (original is not SkinnedMeshRenderer originalSmr
                    || proxy is not SkinnedMeshRenderer proxySmr)
                {
                    return;
                }

                var state = _state.Value;
                if (!state.PreviewOn
                    || !state.Rules.TryGetValue(originalSmr, out var rules))
                {
                    return;
                }

                foreach (var rule in rules)
                {
                    for (var index = rule.Controls.Length - 1; index >= 0; index--)
                    {
                        var control = rule.Controls[index];
                        var controlActive = control.IsMaster;
                        if (control.HasOutfitOwner)
                        {
                            controlActive = state.ActiveResolver.IsActive(
                                control.OutfitOwnerKey,
                                state.PreviewOn,
                                state.PartStates);
                        }
                        else if (!control.IsMaster)
                        {
                            controlActive = state.PartStates.TryGetValue(
                                                control.OwnerItemId,
                                                out var partOn)
                                            && partOn;
                        }

                        if (!controlActive)
                        {
                            continue;
                        }

                        if (rule.ShapeIndex >= 0
                            && proxySmr.sharedMesh != null
                            && rule.ShapeIndex < proxySmr.sharedMesh.blendShapeCount)
                        {
                            proxySmr.SetBlendShapeWeight(rule.ShapeIndex, control.Value);
                        }

                        break;
                    }
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
