using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.preview;
using UnityEngine.Rendering;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitShapeDeletePreviewState
    {
        internal OutfitShapeDeletePreviewState(
            ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<string>> activeShapes,
            int revision)
        {
            ActiveShapes = activeShapes
                           ?? ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<string>>.Empty;
            Revision = revision;
        }

        internal ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<string>> ActiveShapes { get; }
        internal int Revision { get; }
    }

    internal sealed class OutfitShapeChangerPreviewFilter : IRenderFilter
    {
        private readonly SkinnedMeshRenderer[] _renderers;
        private readonly PublishedValue<ShapeState> _state;

        private readonly PublishedValue<OutfitShapeDeletePreviewState> _deleteState;
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
            var initialState = CreateState(
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
            _state = new PublishedValue<ShapeState>(
                initialState,
                "SetupOutfitComponent/ShapeChanger");
            _renderers = _state.Value.Rules.Keys
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
            _deleteState = new PublishedValue<OutfitShapeDeletePreviewState>(
                new OutfitShapeDeletePreviewState(CollectActiveDeleteShapes(initialState), 0),
                "SetupOutfitComponent/ShapeChangerDelete");
        }

        public bool CanEnableRenderers => false;
        internal int TargetGroupEvaluationCount { get; private set; }
        internal int NodeCreationCount { get; private set; }
        internal int RuleBuildCountForTests { get; private set; } = 1;
        internal int ExistingSetCountForTests => _state.Value.ExistingSetCount;

        internal OutfitShapeDeletePreviewFilter CreateDeletePreviewFilter() =>
            new OutfitShapeDeletePreviewFilter(_renderers, _deleteState);

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

            PublishState(next);
            RuleBuildCountForTests++;
        }

        internal void SetPreviewState(
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            PublishState(_state.Value.WithPreviewState(previewOn, partStates));
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
                        existing.Value,
                        ShapeChangeType.Set));
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
                        change.Inverted,
                        false,
                        true,
                        order,
                        sequence++,
                        change.Value,
                        change.ChangeType));
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
        private void PublishState(ShapeState next)
        {
            _state.Value = next;
            var activeShapes = CollectActiveDeleteShapes(next);
            var current = _deleteState.Value;
            if (HaveEquivalentDeleteShapes(current.ActiveShapes, activeShapes)) return;

            _deleteState.Value = new OutfitShapeDeletePreviewState(
                activeShapes,
                current.Revision + 1);
        }

        private static ImmutableDictionary<SkinnedMeshRenderer, ImmutableArray<string>>
            CollectActiveDeleteShapes(ShapeState state)
        {
            var result = ImmutableDictionary
                .CreateBuilder<SkinnedMeshRenderer, ImmutableArray<string>>();
            foreach (var rendererRules in state.Rules)
            {
                var shapes = ImmutableArray.CreateBuilder<string>();
                foreach (var rule in rendererRules.Value)
                {
                    if (TryGetWinningControl(state, rule, out var winner)
                        && winner.ChangeType == ShapeChangeType.Delete)
                    {
                        shapes.Add(rule.ShapeName);
                    }
                }

                if (shapes.Count > 0)
                    result.Add(rendererRules.Key, shapes.ToImmutable());
            }

            return result.ToImmutable();
        }

        private static bool TryGetWinningControl(
            ShapeState state,
            ShapeRule rule,
            out ShapeControl winner)
        {
            for (var index = rule.Controls.Length - 1; index >= 0; index--)
            {
                var control = rule.Controls[index];
                if (!Node.IsControlActive(state, control)) continue;
                winner = control;
                return true;
            }

            winner = default;
            return false;
        }

        private static bool HaveEquivalentDeleteShapes(
            IReadOnlyDictionary<SkinnedMeshRenderer, ImmutableArray<string>> left,
            IReadOnlyDictionary<SkinnedMeshRenderer, ImmutableArray<string>> right)
        {
            if (left.Count != right.Count) return false;
            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var other)
                    || !pair.Value.SequenceEqual(other))
                {
                    return false;
                }
            }

            return true;
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
                float value,
                ShapeChangeType changeType)
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
                ChangeType = changeType;
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
            internal ShapeChangeType ChangeType { get; }
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
                        if (control.ChangeType != ShapeChangeType.Set
                            || !IsControlActive(state, control)) continue;
                        value = control.Value;
                        break;
                    }

                    proxySmr.SetBlendShapeWeight(proxyShapeIndex, value);
                }
            }

            internal static bool IsControlActive(
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

                bool plannedActive;
                if (control.IsMaster)
                {
                    plannedActive = state.PreviewOn;
                }
                else if (control.HasOutfitOwner)
                {
                    plannedActive = state.PreviewOn
                             && state.PrefabActiveResolver.IsActive(
                                 control.OutfitOwnerKey,
                                 true,
                                 state.PartStates);
                }
                else
                {
                    plannedActive = state.PreviewOn
                             && state.PartStates.TryGetValue(
                                 control.OwnerItemId,
                                 out var partOn)
                             && partOn;
                }

                return plannedActive ^ control.Inverted;
            }

            public void Dispose()
            {
            }
        }
    }

    internal sealed class OutfitShapeDeletePreviewFilter : IRenderFilter
    {
        internal const float Threshold = 0.01f;

        private readonly SkinnedMeshRenderer[] _renderers;
        private readonly PublishedValue<OutfitShapeDeletePreviewState> _state;
        private readonly Dictionary<SkinnedMeshRenderer, VisibilityCacheEntry> _visibilityCache =
            new Dictionary<SkinnedMeshRenderer, VisibilityCacheEntry>();

        internal OutfitShapeDeletePreviewFilter(
            IEnumerable<SkinnedMeshRenderer> renderers,
            PublishedValue<OutfitShapeDeletePreviewState> state)
        {
            _renderers = (renderers ?? Enumerable.Empty<SkinnedMeshRenderer>())
                .Where(renderer => renderer != null)
                .Distinct()
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public bool CanEnableRenderers => false;
        internal int TargetGroupEvaluationCount { get; private set; }
        internal int NodeCreationCount { get; private set; }
        internal int MeshBuildCountForTests { get; private set; }
        internal int MeshDestroyCountForTests { get; private set; }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            TargetGroupEvaluationCount++;
            return _renderers
                .Select(RenderGroup.For)
                .ToImmutableList();
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            var pair = proxyPairs.First();
            NodeCreationCount++;
            return Task.FromResult<IRenderFilterNode>(new Node(
                this,
                _state,
                pair.Item1 as SkinnedMeshRenderer));
        }

        internal bool HasVisiblePrimitives(Renderer renderer)
        {
            if (renderer is not SkinnedMeshRenderer smr || smr.sharedMesh == null) return true;
            var state = _state.Value;
            var shapes = state.ActiveShapes.TryGetValue(smr, out var activeShapes)
                ? activeShapes
                : ImmutableArray<string>.Empty;

            if (_visibilityCache.TryGetValue(smr, out var cached)
                && cached.Shapes.SequenceEqual(shapes))
            {
                return cached.HasVisiblePrimitives;
            }

            if (shapes.Length == 0) return true;
            var hasVisiblePrimitives = HasRetainedPrimitives(
                smr.sharedMesh,
                shapes,
                Threshold);
            RecordVisibility(smr, smr.sharedMesh, shapes, hasVisiblePrimitives);
            return hasVisiblePrimitives;
        }

        private void RecordVisibility(
            SkinnedMeshRenderer renderer,
            Mesh upstreamMesh,
            ImmutableArray<string> shapes,
            bool hasVisiblePrimitives)
        {
            if (renderer == null) return;
            _visibilityCache[renderer] = new VisibilityCacheEntry(
                upstreamMesh,
                shapes,
                hasVisiblePrimitives);
        }

        private void ClearVisibility(SkinnedMeshRenderer renderer)
        {
            if (renderer != null) _visibilityCache.Remove(renderer);
        }

        private void RecordMeshBuilt()
        {
            MeshBuildCountForTests++;
        }

        private void RecordMeshDestroyed()
        {
            MeshDestroyCountForTests++;
        }

        private static Mesh CreateFilteredMesh(
            Mesh source,
            ImmutableArray<string> shapes,
            float threshold,
            out bool hasVisiblePrimitives)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var selectedVertices = BuildSelectedVertexMask(source, shapes, threshold);
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + " (Setup Outfit Delete Preview)";
            clone.hideFlags = HideFlags.HideAndDontSave;
            hasVisiblePrimitives = false;
            try
            {
                for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
                {
                    var topology = source.GetTopology(subMesh);
                    var indices = source.GetIndices(subMesh, true);
                    var retained = BuildRetainedIndices(
                        indices,
                        topology,
                        selectedVertices,
                        out var subMeshHasVisiblePrimitives);
                    hasVisiblePrimitives |= subMeshHasVisiblePrimitives;
                    if (retained.Count == 0 && source.vertexCount > 0)
                    {
                        var primitiveSize = GetPrimitiveSize(topology);
                        var placeholderIndex = indices.Length > 0 ? indices[0] : 0;
                        for (var index = 0; index < primitiveSize; index++)
                            retained.Add(placeholderIndex);
                    }

                    SetRetainedIndices(clone, retained, topology, subMesh);
                }

                clone.bounds = source.bounds;
                return clone;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(clone);
                throw;
            }
        }

        private static void SetRetainedIndices(
            Mesh mesh,
            IReadOnlyList<int> retained,
            MeshTopology topology,
            int subMesh)
        {
            if (retained.Count == 0)
            {
                mesh.SetIndices(Array.Empty<int>(), topology, subMesh, false, 0);
                return;
            }

            if (mesh.indexFormat != IndexFormat.UInt16)
            {
                mesh.SetIndices(retained.ToArray(), topology, subMesh, false, 0);
                return;
            }

            var baseVertex = Math.Max(0, retained.Min());
            var relative = new List<ushort>(retained.Count);
            foreach (var absoluteIndex in retained)
            {
                var adjusted = absoluteIndex - baseVertex;
                if (adjusted < 0 || adjusted > ushort.MaxValue)
                {
                    throw new InvalidOperationException(
                        "UInt16 Index BufferのbaseVertexを保持できません。");
                }

                relative.Add((ushort)adjusted);
            }

            mesh.SetIndices(
                relative,
                0,
                relative.Count,
                topology,
                subMesh,
                false,
                baseVertex);
        }

        private static bool HasRetainedPrimitives(
            Mesh source,
            ImmutableArray<string> shapes,
            float threshold)
        {
            var selectedVertices = BuildSelectedVertexMask(source, shapes, threshold);
            for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                BuildRetainedIndices(
                    source.GetIndices(subMesh, true),
                    source.GetTopology(subMesh),
                    selectedVertices,
                    out var hasVisiblePrimitives);
                if (hasVisiblePrimitives) return true;
            }

            return false;
        }

        private static bool[] BuildSelectedVertexMask(
            Mesh mesh,
            IEnumerable<string> shapes,
            float threshold)
        {
            var selected = new bool[mesh.vertexCount];
            var deltaPositions = new Vector3[mesh.vertexCount];
            var squaredThreshold = threshold * threshold;
            foreach (var shapeName in shapes.Distinct(StringComparer.Ordinal))
            {
                var shapeIndex = mesh.GetBlendShapeIndex(shapeName);
                if (shapeIndex < 0) continue;
                for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(shapeIndex); frame++)
                {
                    mesh.GetBlendShapeFrameVertices(
                        shapeIndex,
                        frame,
                        deltaPositions,
                        null,
                        null);
                    for (var vertex = 0; vertex < deltaPositions.Length; vertex++)
                    {
                        if (deltaPositions[vertex].sqrMagnitude > squaredThreshold)
                            selected[vertex] = true;
                    }
                }
            }

            return selected;
        }

        private static List<int> BuildRetainedIndices(
            IReadOnlyList<int> indices,
            MeshTopology topology,
            IReadOnlyList<bool> selectedVertices,
            out bool hasVisiblePrimitives)
        {
            var primitiveSize = GetPrimitiveSize(topology);
            var retained = new List<int>(indices.Count);
            hasVisiblePrimitives = false;
            for (var offset = 0; offset + primitiveSize <= indices.Count; offset += primitiveSize)
            {
                var deleted = false;
                for (var index = 0; index < primitiveSize; index++)
                {
                    var vertex = indices[offset + index];
                    if (vertex >= 0
                        && vertex < selectedVertices.Count
                        && selectedVertices[vertex])
                    {
                        deleted = true;
                        break;
                    }
                }

                if (deleted) continue;
                hasVisiblePrimitives |= IsRenderablePrimitive(
                    indices, offset, primitiveSize, topology);
                for (var index = 0; index < primitiveSize; index++)
                    retained.Add(indices[offset + index]);
            }

            return retained;
        }

        private static bool IsRenderablePrimitive(
            IReadOnlyList<int> indices,
            int offset,
            int primitiveSize,
            MeshTopology topology)
        {
            if (topology != MeshTopology.Triangles
                && topology != MeshTopology.Quads)
            {
                return true;
            }

            if (primitiveSize < 3) return false;
            var first = indices[offset];
            var second = -1;
            for (var index = 1; index < primitiveSize; index++)
            {
                var vertex = indices[offset + index];
                if (vertex == first) continue;
                if (second < 0)
                {
                    second = vertex;
                    continue;
                }

                if (vertex != second) return true;
            }


            return false;
        }
        private static int GetPrimitiveSize(MeshTopology topology)
        {
            return topology switch
            {
                MeshTopology.Triangles => 3,
                MeshTopology.Quads => 4,
                _ => 1,
            };
        }

        private readonly struct VisibilityCacheEntry
        {
            internal VisibilityCacheEntry(
                Mesh mesh,
                ImmutableArray<string> shapes,
                bool hasVisiblePrimitives)
            {
                Mesh = mesh;
                Shapes = shapes;
                HasVisiblePrimitives = hasVisiblePrimitives;
            }

            internal Mesh Mesh { get; }
            internal ImmutableArray<string> Shapes { get; }
            internal bool HasVisiblePrimitives { get; }
        }

        private sealed class Node : IRenderFilterNode
        {
            private readonly OutfitShapeDeletePreviewFilter _owner;
            private readonly PublishedValue<OutfitShapeDeletePreviewState> _state;
            private readonly SkinnedMeshRenderer _original;
            private ImmutableArray<string> _lastShapes = ImmutableArray<string>.Empty;
            private Mesh _upstreamMesh;
            private Mesh _generatedMesh;

            internal Node(
                OutfitShapeDeletePreviewFilter owner,
                PublishedValue<OutfitShapeDeletePreviewState> state,
                SkinnedMeshRenderer original)
            {
                _owner = owner;
                _state = state;
                _original = original;
            }

            public RenderAspects WhatChanged => RenderAspects.Mesh;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (_original == null
                    || proxy is not SkinnedMeshRenderer proxySmr
                    || proxySmr.sharedMesh == null)
                {
                    DisposeGeneratedMesh();
                    _owner.ClearVisibility(_original);
                    return;
                }

                var upstreamMesh = proxySmr.sharedMesh == _generatedMesh
                    ? _upstreamMesh
                    : proxySmr.sharedMesh;
                var state = _state.Value;
                var shapes = state.ActiveShapes.TryGetValue(_original, out var activeShapes)
                    ? activeShapes
                    : ImmutableArray<string>.Empty;
                if (shapes.Length == 0)
                {
                    var visibilityChanged = _upstreamMesh != upstreamMesh
                                            || !_lastShapes.SequenceEqual(shapes);
                    DisposeGeneratedMesh();
                    _upstreamMesh = upstreamMesh;
                    _lastShapes = shapes;
                    if (visibilityChanged)
                    {
                        _owner.RecordVisibility(
                            _original,
                            upstreamMesh,
                            shapes,
                            HasRetainedPrimitives(upstreamMesh, shapes, Threshold));
                    }

                    proxySmr.sharedMesh = upstreamMesh;
                    return;
                }

                if (_generatedMesh == null
                    || _upstreamMesh != upstreamMesh
                    || !_lastShapes.SequenceEqual(shapes))
                {
                    DisposeGeneratedMesh();
                    _generatedMesh = CreateFilteredMesh(
                        upstreamMesh,
                        shapes,
                        Threshold,
                        out var hasVisiblePrimitives);
                    _owner.RecordMeshBuilt();
                    _owner.RecordVisibility(
                        _original,
                        upstreamMesh,
                        shapes,
                        hasVisiblePrimitives);
                    _upstreamMesh = upstreamMesh;
                    _lastShapes = shapes;
                }

                proxySmr.sharedMesh = _generatedMesh;
            }

            public Task<IRenderFilterNode> Refresh(
                IEnumerable<(Renderer, Renderer)> proxyPairs,
                ComputeContext context,
                RenderAspects updatedAspects)
            {
                return (updatedAspects & RenderAspects.Mesh) != 0
                    ? Task.FromResult<IRenderFilterNode>(null)
                    : Task.FromResult<IRenderFilterNode>(this);
            }

            public void Dispose()
            {
                DisposeGeneratedMesh();
                _owner.ClearVisibility(_original);
            }

            private void DisposeGeneratedMesh()
            {
                if (_generatedMesh == null) return;
                UnityEngine.Object.DestroyImmediate(_generatedMesh);
                _generatedMesh = null;
                _owner.RecordMeshDestroyed();
            }
        }
    }
}
