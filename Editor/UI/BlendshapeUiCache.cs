using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class BlendshapeOptionSet
    {
        internal static readonly BlendshapeOptionSet Empty =
            new BlendshapeOptionSet(Array.Empty<string>());

        internal BlendshapeOptionSet(IEnumerable<string> names)
        {
            var uniqueNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names ?? Enumerable.Empty<string>())
            {
                var normalizedName = name ?? string.Empty;
                if (seenNames.Add(normalizedName)) uniqueNames.Add(normalizedName);
            }

            Names = uniqueNames.ToArray();
        }

        internal IReadOnlyList<string> Names { get; }
    }

    internal sealed class BlendshapeUiCache
    {
        private readonly Dictionary<Mesh, CachedMeshOptions> _meshOptions =
            new Dictionary<Mesh, CachedMeshOptions>();
        private readonly Dictionary<PrefabTargetKey, BlendshapeOptionSet> _localOptions =
            new Dictionary<PrefabTargetKey, BlendshapeOptionSet>();
        private readonly Dictionary<PrefabTargetKey, string> _localDisplayTexts =
            new Dictionary<PrefabTargetKey, string>();
        private readonly Dictionary<SkinnedMeshRenderer, string> _rendererLabels =
            new Dictionary<SkinnedMeshRenderer, string>();

        private VRCAvatarDescriptor _cachedAvatar;
        private SkinnedMeshRenderer[] _avatarRenderers = Array.Empty<SkinnedMeshRenderer>();
        private RendererSnapshot[] _rendererSnapshots = Array.Empty<RendererSnapshot>();
        private bool _avatarCacheDirty = true;

        internal int AvatarRebuildCount { get; private set; }
        internal int MeshOptionBuildCount { get; private set; }
        internal int LocalOptionBuildCount { get; private set; }

        internal void SetAnalysis(OutfitAnalysis analysis)
        {
            InvalidateAvatar();
            _localOptions.Clear();
            _localDisplayTexts.Clear();
            LocalOptionBuildCount = 0;

            if (analysis == null) return;
            foreach (var renderer in analysis.BlendshapeRenderers)
            {
                _localOptions[renderer.TargetKey] = new BlendshapeOptionSet(renderer.BlendshapeNames);
                _localDisplayTexts[renderer.TargetKey] = string.Join(" / ", renderer.BlendshapeNames);
                LocalOptionBuildCount++;
            }
        }

        internal IReadOnlyList<SkinnedMeshRenderer> GetAvatarRenderers(VRCAvatarDescriptor avatar)
        {
            if (!IsAvatarCacheValid(avatar)) RebuildAvatarCache(avatar);
            return _avatarRenderers;
        }

        internal string GetRendererLabel(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) return "<missing>";
            return _rendererLabels.TryGetValue(renderer, out var label)
                ? label
                : GetHierarchyPath(renderer.transform);
        }

        internal BlendshapeOptionSet GetSourceOptions(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null) return BlendshapeOptionSet.Empty;

            var mesh = renderer.sharedMesh;
            if (_meshOptions.TryGetValue(mesh, out var cached)
                && cached.BlendshapeCount == mesh.blendShapeCount)
            {
                return cached.Options;
            }

            var names = new string[mesh.blendShapeCount];
            for (var index = 0; index < names.Length; index++)
                names[index] = mesh.GetBlendShapeName(index);

            var options = new BlendshapeOptionSet(names);
            _meshOptions[mesh] = new CachedMeshOptions(mesh.blendShapeCount, options);
            MeshOptionBuildCount++;
            return options;
        }

        internal BlendshapeOptionSet GetLocalOptions(OutfitRendererInfo renderer)
        {
            if (renderer == null) return BlendshapeOptionSet.Empty;
            return _localOptions.TryGetValue(renderer.TargetKey, out var options)
                ? options
                : BlendshapeOptionSet.Empty;
        }

        internal string GetLocalDisplayText(OutfitRendererInfo renderer)
        {
            if (renderer == null) return string.Empty;
            return _localDisplayTexts.TryGetValue(renderer.TargetKey, out var displayText)
                ? displayText
                : string.Empty;
        }

        internal void InvalidateAvatar()
        {
            _avatarCacheDirty = true;
        }

        internal void InvalidateProject()
        {
            _meshOptions.Clear();
            _avatarCacheDirty = true;
        }

        private bool IsAvatarCacheValid(VRCAvatarDescriptor avatar)
        {
            if (_avatarCacheDirty || _cachedAvatar != avatar) return false;
            if (avatar == null) return _avatarRenderers.Length == 0;
            if (_avatarRenderers.Length != _rendererSnapshots.Length) return false;

            for (var index = 0; index < _avatarRenderers.Length; index++)
            {
                var renderer = _avatarRenderers[index];
                var snapshot = _rendererSnapshots[index];
                if (renderer == null
                    || (renderer.transform != avatar.transform
                        && !renderer.transform.IsChildOf(avatar.transform))
                    || renderer.sharedMesh != snapshot.Mesh
                    || renderer.sharedMesh == null
                    || renderer.sharedMesh.blendShapeCount != snapshot.BlendshapeCount)
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildAvatarCache(VRCAvatarDescriptor avatar)
        {
            _cachedAvatar = avatar;
            _rendererLabels.Clear();
            _avatarCacheDirty = false;
            AvatarRebuildCount++;

            if (avatar == null)
            {
                _avatarRenderers = Array.Empty<SkinnedMeshRenderer>();
                _rendererSnapshots = Array.Empty<RendererSnapshot>();
                return;
            }

            _avatarRenderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer != null
                                   && renderer.sharedMesh != null
                                   && renderer.sharedMesh.blendShapeCount > 0)
                .OrderBy(renderer => GetHierarchyPath(renderer.transform), StringComparer.Ordinal)
                .ToArray();
            _rendererSnapshots = _avatarRenderers
                .Select(renderer => new RendererSnapshot(
                    renderer.sharedMesh,
                    renderer.sharedMesh.blendShapeCount))
                .ToArray();
            foreach (var renderer in _avatarRenderers)
                _rendererLabels[renderer] = GetHierarchyPath(renderer.transform);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "<missing>";
            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private readonly struct CachedMeshOptions
        {
            internal CachedMeshOptions(int blendshapeCount, BlendshapeOptionSet options)
            {
                BlendshapeCount = blendshapeCount;
                Options = options;
            }

            internal int BlendshapeCount { get; }
            internal BlendshapeOptionSet Options { get; }
        }

        private readonly struct RendererSnapshot
        {
            internal RendererSnapshot(Mesh mesh, int blendshapeCount)
            {
                Mesh = mesh;
                BlendshapeCount = blendshapeCount;
            }

            internal Mesh Mesh { get; }
            internal int BlendshapeCount { get; }
        }
    }
}
