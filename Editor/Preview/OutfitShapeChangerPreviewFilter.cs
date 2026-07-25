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
            : this(
                sourcePrefab,
                null,
                null,
                dependencyHash,
                sourceToMirror,
                Enumerable.Empty<OutfitMasterSceneTargetPreviewSnapshot>(),
                parts,
                changes,
                Enumerable.Empty<ExistingAvatarShapeChangePreviewSnapshot>(),
                previewOn,
                partStates)
        {
        }

        internal OutfitShapeChangerPreviewFilter(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            IEnumerable<ExistingAvatarShapeChangePreviewSnapshot> existingChanges,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            _state = new PublishedValue<ShapeState>(
                CreateState(
                    sourcePrefab,
                    avatarRoot,
                    placement,
                    dependencyHash,
                    sourceToMirror,
                    masterSceneTargets,
                    parts,
                    changes,
                    existingChanges,
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
        internal int ExistingSetCountForTests => _state.Value.ExistingSetCount;

        internal bool HasEquivalentRendererSet(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes)
        {
            return HasEquivalentRendererSet(
                sourcePrefab,
                dependencyHash,
                sourceToMirror,
                changes,
                Enumerable.Empty<ExistingAvatarShapeChangePreviewSnapshot>());
        }

        internal bool HasEquivalentRendererSet(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            IEnumerable<ExistingAvatarShapeChangePreviewSnapshot> existingChanges)
        {
            return _renderers.SequenceEqual(
                CollectRenderers(
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    changes,
                    existingChanges));
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
            UpdateRules(
                sourcePrefab,
                null,
                null,
                dependencyHash,
                sourceToMirror,
                Enumerable.Empty<OutfitMasterSceneTargetPreviewSnapshot>(),
                parts,
                changes,
                Enumerable.Empty<ExistingAvatarShapeChangePreviewSnapshot>(),
                previewOn,
                partStates);
        }

        internal void UpdateRules(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            IEnumerable<ExistingAvatarShapeChangePreviewSnapshot> existingChanges,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var next = CreateState(
                sourcePrefab,
                avatarRoot,
                placement,
                dependencyHash,
                sourceToMirror,
                masterSceneTargets,
                parts,
                changes,
                existingChanges,
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
            GameObject avatarRoot,
            Transform placement,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            IEnumerable<ExistingAvatarShapeChangePreviewSnapshot> existingChanges,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var partArray = (parts ?? Enumerable.Empty<OutfitPartPreviewSnapshot>())
                .ToArray();
            var prefabActiveResolver = new OutfitPrefabActiveStateResolver(
                sourcePrefab,
                dependencyHash,
                sourceToMirror,
                partArray);
            var sceneActiveResolver = new OutfitSceneActiveStateResolver(
                masterSceneTargets,
                partArray);
            var mutableRules =
                new Dictionary<SkinnedMeshRenderer, Dictionary<string, List<ShapeControl>>>();
            var sequence = 0;
            var existingSetCount = 0;

            foreach (var existing in existingChanges
                         ?? Enumerable.Empty<ExistingAvatarShapeChangePreviewSnapshot>())
            {
                AddControl(
                    existing.Renderer,
                    existing.ShapeName,
                    new ShapeControl(
                        string.Empty,
                        false,
                        false,
                        default,
                        existing.Owner,
                        true,
                        existing.Inverted,
                        existing.HasMenuCondition,
                        existing.MenuInitiallyActive,
                        existing.HierarchyOrder,
                        sequence++,
                        existing.Value));
                existingSetCount++;
            }

            var partIndices = partArray
                .Select((part, index) => (part.ItemId, index))
                .ToDictionary(pair => pair.ItemId, pair => pair.index, StringComparer.Ordinal);
            foreach (var change in changes
                         ?? Enumerable.Empty<OutfitShapeChangePreviewSnapshot>())
            {
                var renderer = ResolveRenderer(
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    change);
                var order = CreatePlannedOrder(
                    sourcePrefab,
                    avatarRoot,
                    placement,
                    partIndices,
                    change,
                    sequence);
                AddControl(
                    renderer,
                    change.ShapeName,
                    new ShapeControl(
                        change.OwnerItemId,
                        change.IsMaster,
                        change.HasOutfitOwner,
                        change.OutfitOwnerKey,
                        null,
                        false,
                        false,
                        false,
                        true,
                        order,
                        sequence++,
                        change.Value));
            }

            var rules = mutableRules.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .OrderBy(shape => shape.Key, StringComparer.Ordinal)
                    .Select(shape => new ShapeRule(
                        shape.Key,
                        pair.Key.sharedMesh.GetBlendShapeIndex(shape.Key),
                        shape.Value
                            .OrderBy(control => control.HierarchyOrder)
                            .ThenBy(control => control.Sequence)
                            .ToImmutableArray()))
                    .ToImmutableArray());
            return new ShapeState(
                rules,
                prefabActiveResolver,
                sceneActiveResolver,
                avatarRoot != null ? avatarRoot.transform : null,
                CopyPartStates(partStates),
                previewOn,
                existingSetCount);

            void AddControl(
                SkinnedMeshRenderer renderer,
                string shapeName,
                ShapeControl control)
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        "Shape Changerプレビュー対象のRendererを解決できませんでした。");
                }

                var shapeIndex = renderer.sharedMesh.GetBlendShapeIndex(shapeName);
                if (shapeIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Shape Changerプレビュー対象にBlendShape「{shapeName}」がありません。");
                }

                if (!mutableRules.TryGetValue(renderer, out var shapeRules))
                {
                    shapeRules = new Dictionary<string, List<ShapeControl>>(
                        StringComparer.Ordinal);
                    mutableRules.Add(renderer, shapeRules);
                }

                if (!shapeRules.TryGetValue(shapeName, out var controls))
                {
                    controls = new List<ShapeControl>();
                    shapeRules.Add(shapeName, controls);
                }

                controls.Add(control);
            }
        }

        private static ShapeChangerHierarchyOrder CreatePlannedOrder(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IReadOnlyDictionary<string, int> partIndices,
            OutfitShapeChangePreviewSnapshot change,
            int sequence)
        {
            if (avatarRoot == null
                || placement == null
                || (placement != avatarRoot.transform
                    && !placement.IsChildOf(avatarRoot.transform)))
            {
                return new ShapeChangerHierarchyOrder(
                    new[] { sequence },
                    0,
                    sequence);
            }

            if (change.IsMaster)
            {
                return ShapeChangerHierarchyOrder.ForGeneratedMaster(
                    avatarRoot.transform,
                    placement,
                    sequence);
            }

            if (change.HasOutfitOwner)
            {
                return ShapeChangerHierarchyOrder.ForGeneratedOutfitOwner(
                    avatarRoot.transform,
                    placement,
                    change.OutfitOwnerKey,
                    sequence);
            }

            var partIndex = partIndices.TryGetValue(
                change.OwnerItemId,
                out var resolvedPartIndex)
                ? resolvedPartIndex
                : partIndices.Count;
            return ShapeChangerHierarchyOrder.ForGeneratedPart(
                avatarRoot.transform,
                placement,
                sourcePrefab.transform.childCount,
                partIndex,
                sequence);
        }

        private static SkinnedMeshRenderer[] CollectRenderers(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitShapeChangePreviewSnapshot> changes,
            IEnumerable<ExistingAvatarShapeChangePreviewSnapshot> existingChanges)
        {
            var plannedRenderers =
                (changes ?? Enumerable.Empty<OutfitShapeChangePreviewSnapshot>())
                .Select(change => ResolveRenderer(
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    change));
            var existingRenderers =
                (existingChanges
                 ?? Enumerable.Empty<ExistingAvatarShapeChangePreviewSnapshot>())
                .Select(change => change.Renderer);
            return plannedRenderers
                .Concat(existingRenderers)
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
                GameObject existingOwner,
                bool isExisting,
                bool inverted,
                bool hasMenuCondition,
                bool menuInitiallyActive,
                ShapeChangerHierarchyOrder hierarchyOrder,
                int sequence,
                float value)
            {
                OwnerItemId = ownerItemId ?? string.Empty;
                IsMaster = isMaster;
                HasOutfitOwner = hasOutfitOwner;
                OutfitOwnerKey = outfitOwnerKey;
                ExistingOwner = existingOwner;
                IsExisting = isExisting;
                Inverted = inverted;
                HasMenuCondition = hasMenuCondition;
                MenuInitiallyActive = menuInitiallyActive;
                HierarchyOrder = hierarchyOrder;
                Sequence = sequence;
                Value = value;
            }

            internal string OwnerItemId { get; }
            internal bool IsMaster { get; }
            internal bool HasOutfitOwner { get; }
            internal PrefabTargetKey OutfitOwnerKey { get; }
            internal GameObject ExistingOwner { get; }
            internal bool IsExisting { get; }
            internal bool Inverted { get; }
            internal bool HasMenuCondition { get; }
            internal bool MenuInitiallyActive { get; }
            internal ShapeChangerHierarchyOrder HierarchyOrder { get; }
            internal int Sequence { get; }
            internal float Value { get; }
        }

        private readonly struct ShapeRule
        {
            internal ShapeRule(
                string shapeName,
                int shapeIndex,
                ImmutableArray<ShapeControl> controls)
            {
                ShapeName = shapeName ?? string.Empty;
                ShapeIndex = shapeIndex;
                Controls = controls.IsDefault
                    ? ImmutableArray<ShapeControl>.Empty
                    : controls;
            }

            internal string ShapeName { get; }
            internal int ShapeIndex { get; }
            internal ImmutableArray<ShapeControl> Controls { get; }
        }

        private sealed class ShapeState
        {
            internal ShapeState(
                ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<ShapeRule>> rules,
                OutfitPrefabActiveStateResolver prefabActiveResolver,
                OutfitSceneActiveStateResolver sceneActiveResolver,
                Transform avatarRoot,
                ImmutableDictionary<string, bool> partStates,
                bool previewOn,
                int existingSetCount)
            {
                Rules = rules;
                PrefabActiveResolver = prefabActiveResolver;
                SceneActiveResolver = sceneActiveResolver;
                AvatarRoot = avatarRoot;
                PartStates = partStates;
                PreviewOn = previewOn;
                ExistingSetCount = existingSetCount;
            }

            internal ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<ShapeRule>> Rules { get; }
            internal OutfitPrefabActiveStateResolver PrefabActiveResolver { get; }
            internal OutfitSceneActiveStateResolver SceneActiveResolver { get; }
            internal Transform AvatarRoot { get; }
            internal ImmutableDictionary<string, bool> PartStates { get; }
            internal bool PreviewOn { get; }
            internal int ExistingSetCount { get; }

            internal ShapeState WithPreviewState(
                bool previewOn,
                IReadOnlyDictionary<string, bool> partStates)
            {
                return new ShapeState(
                    Rules,
                    PrefabActiveResolver,
                    SceneActiveResolver,
                    AvatarRoot,
                    CopyPartStates(partStates),
                    previewOn,
                    ExistingSetCount);
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
                    || proxy is not SkinnedMeshRenderer proxySmr
                    || proxySmr.sharedMesh == null)
                {
                    return;
                }

                var state = _state.Value;
                if (!state.Rules.TryGetValue(originalSmr, out var rules)) return;

                foreach (var rule in rules)
                {
                    var proxyShapeIndex = proxySmr.sharedMesh.GetBlendShapeIndex(
                        rule.ShapeName);
                    if (rule.ShapeIndex < 0 || proxyShapeIndex < 0)
                    {
                        continue;
                    }

                    var value = originalSmr.GetBlendShapeWeight(rule.ShapeIndex);
                    for (var index = rule.Controls.Length - 1; index >= 0; index--)
                    {
                        var control = rule.Controls[index];
                        if (!IsControlActive(state, control)) continue;
                        value = control.Value;
                        break;
                    }

                    proxySmr.SetBlendShapeWeight(proxyShapeIndex, value);
                }
            }

            private static bool IsControlActive(
                ShapeState state,
                ShapeControl control)
            {
                if (control.IsExisting)
                {
                    var active = state.SceneActiveResolver.IsHierarchyActive(
                                     control.ExistingOwner,
                                     state.AvatarRoot,
                                     state.PreviewOn,
                                     state.PartStates)
                                 && (!control.HasMenuCondition
                                     || control.MenuInitiallyActive);
                    return active ^ control.Inverted;
                }

                if (!state.PreviewOn) return false;
                if (control.IsMaster) return true;
                if (control.HasOutfitOwner)
                {
                    return state.PrefabActiveResolver.IsActive(
                        control.OutfitOwnerKey,
                        true,
                        state.PartStates);
                }

                return state.PartStates.TryGetValue(
                           control.OwnerItemId,
                           out var partOn)
                       && partOn;
            }

            public void Dispose()
            {
            }
        }
    }
}
