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
        private readonly Renderer[] _outfitRenderers;
        private readonly Renderer[] _exclusionRenderers;
        private readonly PublishedValue<bool> _previewOn;

        internal OutfitVisibilityPreviewFilter(
            IEnumerable<Renderer> outfitRenderers,
            IEnumerable<Renderer> exclusionRenderers,
            bool previewOn)
        {
            _outfitRenderers = NormalizeRenderers(outfitRenderers);
            _exclusionRenderers = NormalizeRenderers(exclusionRenderers);
            _previewOn = new PublishedValue<bool>(
                previewOn,
                "SetupOutfitComponent/ApplyPreviewOn");
        }

        public bool CanEnableRenderers => false;
        internal bool PreviewOn => _previewOn.Value;
        internal int TargetGroupEvaluationCount { get; private set; }
        internal int NodeCreationCount { get; private set; }

        internal void SetPreviewOn(bool previewOn)
        {
            if (_previewOn.Value == previewOn) return;
            _previewOn.Value = previewOn;
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            TargetGroupEvaluationCount++;
            var renderers = _outfitRenderers
                .Concat(_exclusionRenderers)
                .Distinct()
                .ToArray();
            return renderers.Length == 0
                ? ImmutableList<RenderGroup>.Empty
                : ImmutableList.Create(RenderGroup.For(renderers));
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            NodeCreationCount++;
            return Task.FromResult<IRenderFilterNode>(
                new Node(_outfitRenderers, _exclusionRenderers, _previewOn));
        }

        internal static bool ApplyVisibilityMask(
            bool outfitRenderer,
            bool previewOn,
            bool currentProxyEnabled)
        {
            return currentProxyEnabled && (outfitRenderer ? previewOn : !previewOn);
        }

        private static Renderer[] NormalizeRenderers(IEnumerable<Renderer> renderers)
        {
            return (renderers ?? Enumerable.Empty<Renderer>())
                .Where(renderer => renderer is MeshRenderer or SkinnedMeshRenderer)
                .Distinct()
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
        }

        private sealed class Node : IRenderFilterNode
        {
            private readonly HashSet<Renderer> _outfitRenderers;
            private readonly HashSet<Renderer> _exclusionRenderers;
            private readonly PublishedValue<bool> _previewOn;

            internal Node(
                IEnumerable<Renderer> outfitRenderers,
                IEnumerable<Renderer> exclusionRenderers,
                PublishedValue<bool> previewOn)
            {
                _outfitRenderers = new HashSet<Renderer>(outfitRenderers);
                _exclusionRenderers = new HashSet<Renderer>(exclusionRenderers);
                _previewOn = previewOn;
            }

            public RenderAspects WhatChanged => 0;

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (proxy == null) return;
                if (!_outfitRenderers.Contains(original)
                    && !_exclusionRenderers.Contains(original))
                {
                    return;
                }

                proxy.enabled = ApplyVisibilityMask(
                    _outfitRenderers.Contains(original),
                    _previewOn.Value,
                    proxy.enabled);
            }

            public void Dispose()
            {
            }
        }
    }
}
