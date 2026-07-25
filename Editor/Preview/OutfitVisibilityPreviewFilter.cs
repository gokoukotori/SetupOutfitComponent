using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitVisibilityPreviewFilter : IRenderFilter
    {
        private readonly Renderer[] _renderers;
        private readonly PublishedValue<VisibilityState> _visibility;

        internal OutfitVisibilityPreviewFilter(
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            _visibility = new PublishedValue<VisibilityState>(
                CreateVisibilityState(masterSceneTargets, parts, previewOn, partStates),
                "SetupOutfitComponent/SceneVisibility");
            _renderers = _visibility.Value.Rules.Keys
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
        }

        public bool CanEnableRenderers => true;
        internal bool PreviewOn => _visibility.Value.PreviewOn;
        internal int TargetGroupEvaluationCount { get; private set; }
        internal int NodeCreationCount { get; private set; }
        internal int RuleBuildCountForTests { get; private set; } = 1;
        internal IReadOnlyList<string> Warnings => _visibility.Value.Warnings;

        internal bool HasEquivalentRendererSet(
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts)
        {
            return _renderers.SequenceEqual(
                CollectControlledRenderers(masterSceneTargets, parts));
        }

        internal void UpdateRules(
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var next = CreateVisibilityState(
                masterSceneTargets,
                parts,
                previewOn,
                partStates);
            var nextRenderers = next.Rules.Keys
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
            if (!_renderers.SequenceEqual(nextRenderers))
            {
                throw new InvalidOperationException(
                    "Scene対象のRenderer集合が変更されたためFilterの再登録が必要です。");
            }

            _visibility.Value = next;
            RuleBuildCountForTests++;
        }

        internal void SetPreviewState(
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            _visibility.Value = _visibility.Value.WithPreviewState(previewOn, partStates);
        }

        internal bool IsRendererVisible(Renderer renderer)
        {
            if (renderer == null
                || !_visibility.Value.Rules.TryGetValue(renderer, out var rule))
            {
                return false;
            }

            return EvaluateControlledVisibility(_visibility.Value, renderer, rule);
        }

        internal bool ControlsRenderer(Renderer renderer)
        {
            return renderer != null && _visibility.Value.Rules.ContainsKey(renderer);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            TargetGroupEvaluationCount++;
            return _renderers.Length == 0
                ? ImmutableList<RenderGroup>.Empty
                : ImmutableList.Create(RenderGroup.For(_renderers));
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            NodeCreationCount++;
            return Task.FromResult<IRenderFilterNode>(new Node(_visibility));
        }

        private static bool EvaluateControlledVisibility(
            VisibilityState state,
            Renderer renderer,
            RendererRule rule)
        {
            if (!state.PreviewOn) return rule.OriginalVisible;
            return rule.RendererEnabled
                   && renderer != null
                   && state.ActiveResolver.IsHierarchyActive(
                       renderer.gameObject,
                       null,
                       true,
                       state.PartStates);
        }

        private static Renderer[] CollectControlledRenderers(
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts)
        {
            return CollectControlledObjects(masterSceneTargets, parts)
                .SelectMany(target => target.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer is MeshRenderer or SkinnedMeshRenderer)
                .Distinct()
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
        }

        private static IEnumerable<GameObject> CollectControlledObjects(
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts)
        {
            var masters = (masterSceneTargets
                           ?? Enumerable.Empty<OutfitMasterSceneTargetPreviewSnapshot>())
                .Select(target => target.SceneObject);
            var partTargets = (parts ?? Enumerable.Empty<OutfitPartPreviewSnapshot>())
                .SelectMany(part => part.Targets)
                .Where(target => target.Source == PartTargetSource.SceneObject)
                .Select(target => target.SceneObject);
            return masters.Concat(partTargets)
                .Where(target => target != null)
                .Distinct();
        }

        private static VisibilityState CreateVisibilityState(
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var activeResolver = new OutfitSceneActiveStateResolver(
                masterSceneTargets,
                parts);
            var targets = new Dictionary<GameObject, MutableTransformRule>();
            var targetPaths = new Dictionary<GameObject, string>();
            foreach (var master in masterSceneTargets
                         ?? Enumerable.Empty<OutfitMasterSceneTargetPreviewSnapshot>())
            {
                if (master.SceneObject == null)
                    throw new InvalidOperationException("ステップ3のScene対象を解決できませんでした。");
                if (!targets.TryGetValue(master.SceneObject, out var rule))
                {
                    rule = new MutableTransformRule(master.SceneObject.activeSelf);
                    targets.Add(master.SceneObject, rule);
                }

                if (rule.MasterActiveWhenOn.HasValue)
                    throw new InvalidOperationException("同じScene対象が複数回指定されています。");
                rule.MasterActiveWhenOn = master.ActiveWhenOn;
                targetPaths[master.SceneObject] = GetHierarchyPath(master.SceneObject.transform);
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

                    if (!targets.TryGetValue(target.SceneObject, out var rule))
                    {
                        rule = new MutableTransformRule(target.SceneObject.activeSelf);
                        targets.Add(target.SceneObject, rule);
                    }

                    rule.PartControls.Add(new PartControl(
                        part.ItemId,
                        target.ActiveWhenOn));
                    targetPaths[target.SceneObject] =
                        GetHierarchyPath(target.SceneObject.transform);
                }
            }

            var renderers = CollectControlledRenderers(masterSceneTargets, parts);
            var rules = new Dictionary<Renderer, RendererRule>();
            var targetsWithRenderer = new HashSet<GameObject>();
            foreach (var renderer in renderers)
            {
                var staticHierarchyActive = true;
                var transformRules = ImmutableArray.CreateBuilder<TransformRule>();
                var current = renderer.transform;
                while (current != null)
                {
                    if (targets.TryGetValue(current.gameObject, out var targetRule))
                    {
                        transformRules.Add(targetRule.ToImmutable());
                        targetsWithRenderer.Add(current.gameObject);
                    }
                    else
                    {
                        staticHierarchyActive &= current.gameObject.activeSelf;
                    }

                    current = current.parent;
                }

                rules.Add(renderer, new RendererRule(
                    renderer.enabled && renderer.gameObject.activeInHierarchy,
                    renderer.enabled,
                    staticHierarchyActive,
                    transformRules.ToImmutable()));
            }

            var warnings = targets.Keys
                .Where(target => !targetsWithRenderer.Contains(target))
                .Select(target =>
                    $"Scene対象「{targetPaths[target]}」の配下にプレビュー対応Rendererがありません。")
                .OrderBy(message => message, StringComparer.Ordinal)
                .ToImmutableArray();
            return new VisibilityState(
                rules.ToImmutableDictionary(),
                activeResolver,
                CopyPartStates(partStates),
                previewOn,
                warnings);
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

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "<未解決>";
            var names = new Stack<string>();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }

            return string.Join("/", names);
        }

        private sealed class MutableTransformRule
        {
            internal MutableTransformRule(bool originalActiveSelf)
            {
                OriginalActiveSelf = originalActiveSelf;
            }

            internal bool OriginalActiveSelf { get; }
            internal bool? MasterActiveWhenOn { get; set; }
            internal List<PartControl> PartControls { get; } = new List<PartControl>();

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

        private readonly struct RendererRule
        {
            internal RendererRule(
                bool originalVisible,
                bool rendererEnabled,
                bool staticHierarchyActive,
                ImmutableArray<TransformRule> transformRules)
            {
                OriginalVisible = originalVisible;
                RendererEnabled = rendererEnabled;
                StaticHierarchyActive = staticHierarchyActive;
                TransformRules = transformRules.IsDefault
                    ? ImmutableArray<TransformRule>.Empty
                    : transformRules;
            }

            internal bool OriginalVisible { get; }
            internal bool RendererEnabled { get; }
            internal bool StaticHierarchyActive { get; }
            internal ImmutableArray<TransformRule> TransformRules { get; }
        }

        private sealed class VisibilityState
        {
            internal VisibilityState(
                ImmutableDictionary<Renderer, RendererRule> rules,
                OutfitSceneActiveStateResolver activeResolver,
                ImmutableDictionary<string, bool> partStates,
                bool previewOn,
                ImmutableArray<string> warnings)
            {
                Rules = rules;
                ActiveResolver = activeResolver
                                 ?? throw new ArgumentNullException(nameof(activeResolver));
                PartStates = partStates;
                PreviewOn = previewOn;
                Warnings = warnings;
            }

            internal ImmutableDictionary<Renderer, RendererRule> Rules { get; }
            internal OutfitSceneActiveStateResolver ActiveResolver { get; }
            internal ImmutableDictionary<string, bool> PartStates { get; }
            internal bool PreviewOn { get; }
            internal ImmutableArray<string> Warnings { get; }

            internal VisibilityState WithPreviewState(
                bool previewOn,
                IReadOnlyDictionary<string, bool> partStates)
            {
                return new VisibilityState(
                    Rules,
                    ActiveResolver,
                    CopyPartStates(partStates),
                    previewOn,
                    Warnings);
            }
        }

        private sealed class Node : IRenderFilterNode
        {
            private readonly PublishedValue<VisibilityState> _visibility;

            internal Node(PublishedValue<VisibilityState> visibility)
            {
                _visibility = visibility;
            }

            public RenderAspects WhatChanged => 0;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (proxy == null) return;
                var state = _visibility.Value;
                if (!state.Rules.TryGetValue(original, out var rule)) return;
                proxy.enabled = EvaluateControlledVisibility(state, original, rule);
            }

            public void Dispose()
            {
            }
        }
    }
}