using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitPartVisibilityPreviewFilter : IRenderFilter
    {
        private readonly Renderer[] _outfitRenderers;
        private readonly bool _canEnableRenderers;
        private readonly PublishedValue<VisibilityState> _visibility;

        internal OutfitPartVisibilityPreviewFilter(
            IEnumerable<Renderer> outfitRenderers,
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            _outfitRenderers = NormalizeRenderers(outfitRenderers);
            var partArray = (parts ?? Enumerable.Empty<OutfitPartPreviewSnapshot>()).ToArray();
            _canEnableRenderers = partArray.Any(
                part => part.Targets.Any(
                    target => target.Source == PartTargetSource.OutfitPrefab));
            _visibility = new PublishedValue<VisibilityState>(
                CreateVisibilityState(
                    _outfitRenderers,
                    sourcePrefab,
                    dependencyHash,
                    sourceToMirror,
                    partArray,
                    previewOn,
                    partStates),
                "SetupOutfitComponent/PartVisibility");
        }

        public bool CanEnableRenderers => _canEnableRenderers;
        internal int TargetGroupEvaluationCount { get; private set; }
        internal int NodeCreationCount { get; private set; }
        internal int RuleBuildCount { get; private set; } = 1;
        internal IReadOnlyList<string> Warnings => _visibility.Value.Warnings;

        internal void UpdateRules(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            _visibility.Value = CreateVisibilityState(
                _outfitRenderers,
                sourcePrefab,
                dependencyHash,
                sourceToMirror,
                parts,
                previewOn,
                partStates);
            RuleBuildCount++;
        }

        internal void SetPreviewState(
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            var current = _visibility.Value;
            _visibility.Value = current.WithPreviewState(previewOn, partStates);
        }

        internal bool IsRendererVisible(Renderer renderer)
        {
            if (renderer == null) return false;
            return TryEvaluateVisibility(_visibility.Value, renderer, out var visible) && visible;
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            TargetGroupEvaluationCount++;
            return _outfitRenderers.Length == 0
                ? ImmutableList<RenderGroup>.Empty
                : ImmutableList.Create(RenderGroup.For(_outfitRenderers));
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            NodeCreationCount++;
            return Task.FromResult<IRenderFilterNode>(new Node(_visibility));
        }

        internal static bool EvaluateVisibility(
            bool previewOn,
            bool rendererEnabled,
            bool staticHierarchyActive,
            bool hasPart,
            bool partOn,
            bool activeWhenOn)
        {
            if (!previewOn || !rendererEnabled || !staticHierarchyActive) return false;
            return !hasPart || !partOn || activeWhenOn;
        }

        private static bool TryEvaluateVisibility(
            VisibilityState state,
            Renderer renderer,
            out bool visible)
        {
            visible = false;
            if (state == null || renderer == null || !state.Rules.TryGetValue(renderer, out var rule))
            {
                return false;
            }

            var targetActive = rule.DefaultTargetActive;
            if (rule.Controls.Length > 0
                && PartToggleMenuOrderResolver.TryResolveLastEnabled(
                    rule.Controls,
                    control => control.ItemId,
                    control => control.ActiveWhenOn,
                    state.PartStates,
                    out var selectedState))
            {
                targetActive = selectedState;
            }

            visible = state.PreviewOn
                      && rule.RendererEnabled
                      && rule.StaticHierarchyActive
                      && (rule.Controls.Length == 0 || targetActive);
            return true;
        }

        private static VisibilityState CreateVisibilityState(
            IEnumerable<Renderer> outfitRenderers,
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            bool previewOn,
            IReadOnlyDictionary<string, bool> partStates)
        {
            if (sourcePrefab == null) throw new ArgumentNullException(nameof(sourcePrefab));
            if (sourceToMirror == null) throw new ArgumentNullException(nameof(sourceToMirror));

            var partArray = (parts ?? Enumerable.Empty<OutfitPartPreviewSnapshot>()).ToArray();
            var controls = new Dictionary<Transform, List<PartControl>>();
            foreach (var part in partArray)
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

                    targetControls.Add(new PartControl(
                        part.ItemId,
                        target.ActiveWhenOn));
                }
            }

            var mirrorToSource = sourceToMirror.ToDictionary(
                pair => pair.Value,
                pair => pair.Key);
            var placementHierarchyActive = true;
            if (sourceToMirror.TryGetValue(sourcePrefab.transform, out var mirrorOutfitRoot))
            {
                var placementCursor = mirrorOutfitRoot.parent;
                while (placementCursor != null)
                {
                    placementHierarchyActive &= placementCursor.gameObject.activeSelf;
                    placementCursor = placementCursor.parent;
                }
            }

            var rules = new Dictionary<Renderer, RendererRule>();
            var usedPartTargets = new HashSet<Transform>();
            foreach (var renderer in outfitRenderers)
            {
                if (renderer == null
                    || !mirrorToSource.TryGetValue(renderer.transform, out var sourceTransform))
                {
                    continue;
                }

                var staticHierarchyActive = placementHierarchyActive;
                var defaultTargetActive = true;
                List<PartControl> rendererControls = null;
                var current = sourceTransform;
                while (current != null && current != sourcePrefab.transform)
                {
                    if (controls.TryGetValue(current, out var controlsAtTransform))
                    {
                        if (rendererControls != null)
                        {
                            throw new InvalidOperationException(
                                "個別パーツの対象に祖先・子孫関係のあるGameObjectを同時指定できません。");
                        }

                        rendererControls = controlsAtTransform;
                        defaultTargetActive = current.gameObject.activeSelf;
                        usedPartTargets.Add(current);
                    }
                    else
                    {
                        staticHierarchyActive &= current.gameObject.activeSelf;
                    }

                    current = current.parent;
                }

                rules[renderer] = new RendererRule(
                    renderer.enabled,
                    staticHierarchyActive,
                    defaultTargetActive,
                    rendererControls?.ToImmutableArray()
                    ?? ImmutableArray<PartControl>.Empty);
            }

            var warnings = controls.Keys
                .Where(target => !usedPartTargets.Contains(target))
                .Select(target => $"「{GetRelativePath(sourcePrefab.transform, target)}」の配下にプレビュー対応Rendererがありません。")
                .OrderBy(message => message, StringComparer.Ordinal)
                .ToImmutableArray();

            return new VisibilityState(
                rules.ToImmutableDictionary(),
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

        private static Renderer[] NormalizeRenderers(IEnumerable<Renderer> renderers)
        {
            return (renderers ?? Enumerable.Empty<Renderer>())
                .Where(renderer => renderer is MeshRenderer or SkinnedMeshRenderer)
                .Distinct()
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
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

        private readonly struct RendererRule
        {
            internal RendererRule(
                bool rendererEnabled,
                bool staticHierarchyActive,
                bool defaultTargetActive,
                ImmutableArray<PartControl> controls)
            {
                RendererEnabled = rendererEnabled;
                StaticHierarchyActive = staticHierarchyActive;
                DefaultTargetActive = defaultTargetActive;
                Controls = controls.IsDefault
                    ? ImmutableArray<PartControl>.Empty
                    : controls;
            }

            internal bool RendererEnabled { get; }
            internal bool StaticHierarchyActive { get; }
            internal bool DefaultTargetActive { get; }
            internal ImmutableArray<PartControl> Controls { get; }
        }

        private sealed class VisibilityState
        {
            internal VisibilityState(
                ImmutableDictionary<Renderer, RendererRule> rules,
                ImmutableDictionary<string, bool> partStates,
                bool previewOn,
                ImmutableArray<string> warnings)
            {
                Rules = rules;
                PartStates = partStates;
                PreviewOn = previewOn;
                Warnings = warnings;
            }

            internal ImmutableDictionary<Renderer, RendererRule> Rules { get; }
            internal ImmutableDictionary<string, bool> PartStates { get; }
            internal bool PreviewOn { get; }
            internal ImmutableArray<string> Warnings { get; }

            internal VisibilityState WithPreviewState(
                bool previewOn,
                IReadOnlyDictionary<string, bool> partStates)
            {
                return new VisibilityState(
                    Rules,
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
                if (TryEvaluateVisibility(state, original, out var visible))
                    proxy.enabled = visible;
            }

            public void Dispose()
            {
            }
        }
    }
}
