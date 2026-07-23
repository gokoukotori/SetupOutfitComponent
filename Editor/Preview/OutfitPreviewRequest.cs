using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitPreviewRequest
    {
        private OutfitPreviewRequest(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IReadOnlyList<GameObject> exclusions,
            string dependencyHash,
            bool initialOn,
            PlacementState[] placementStates,
            int[] exclusionRendererIds)
        {
            SourcePrefab = sourcePrefab;
            AvatarRoot = avatarRoot;
            Placement = placement;
            Exclusions = exclusions;
            DependencyHash = dependencyHash;
            InitialOn = initialOn;
            PlacementStates = placementStates;
            ExclusionRendererIds = exclusionRendererIds;
        }

        internal GameObject SourcePrefab { get; }
        internal GameObject AvatarRoot { get; }
        internal Transform Placement { get; }
        internal IReadOnlyList<GameObject> Exclusions { get; }
        internal string DependencyHash { get; }
        internal bool InitialOn { get; }
        private PlacementState[] PlacementStates { get; }
        private int[] ExclusionRendererIds { get; }

        internal static bool TryCreate(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IEnumerable<GameObject> exclusions,
            string dependencyHash,
            bool initialOn,
            out OutfitPreviewRequest request,
            out string error)
        {
            request = null;
            error = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                error = "Play Mode中は適用プレビューを表示できません。";
                return false;
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                error = "Prefab Stageでは適用プレビューを表示できません。";
                return false;
            }

            if (sourcePrefab == null || !EditorUtility.IsPersistent(sourcePrefab))
            {
                error = "Project上の衣装Prefabを解決できません。";
                return false;
            }

            var sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            if (string.IsNullOrEmpty(sourcePath)
                || !sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "入力はProject上のPrefabである必要があります。";
                return false;
            }

            var currentHash = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
            if (string.IsNullOrEmpty(dependencyHash)
                || !string.Equals(currentHash, dependencyHash, StringComparison.Ordinal))
            {
                error = "入力Prefabが解析後に変更されています。ウィザードを開き直してください。";
                return false;
            }

            if (avatarRoot == null
                || EditorUtility.IsPersistent(avatarRoot)
                || !avatarRoot.scene.IsValid()
                || !avatarRoot.scene.isLoaded)
            {
                error = "対象アバターをScene上で解決できません。";
                return false;
            }

            if (placement == null
                || (placement != avatarRoot.transform && !placement.IsChildOf(avatarRoot.transform)))
            {
                error = "配置先は対象アバター自身またはその子孫である必要があります。";
                return false;
            }

            var resolvedExclusions = (exclusions ?? Enumerable.Empty<GameObject>()).ToArray();
            var seen = new HashSet<int>();
            foreach (var exclusion in resolvedExclusions)
            {
                if (exclusion == null)
                {
                    error = "排他対象に未指定の行があります。";
                    return false;
                }

                if (EditorUtility.IsPersistent(exclusion)
                    || !exclusion.scene.IsValid()
                    || !exclusion.scene.isLoaded)
                {
                    error = "排他対象はロード済みScene上のGameObjectである必要があります。";
                    return false;
                }

                if (exclusion == avatarRoot || !exclusion.transform.IsChildOf(avatarRoot.transform))
                {
                    error = "排他対象は対象アバターの子孫である必要があります。";
                    return false;
                }

                if (placement == exclusion.transform || placement.IsChildOf(exclusion.transform))
                {
                    error = "排他対象に配置先またはその祖先を指定することはできません。";
                    return false;
                }

                if (!seen.Add(exclusion.GetInstanceID()))
                {
                    error = "同じ排他対象が複数回指定されています。";
                    return false;
                }
            }

            request = new OutfitPreviewRequest(
                sourcePrefab,
                avatarRoot,
                placement,
                resolvedExclusions,
                dependencyHash,
                initialOn,
                CapturePlacementStates(avatarRoot.transform, placement),
                CaptureExclusionRendererIds(resolvedExclusions));
            return true;
        }

        internal bool IsStructurallyEquivalentTo(OutfitPreviewRequest other)
        {
            if (other == null
                || SourcePrefab != other.SourcePrefab
                || AvatarRoot != other.AvatarRoot
                || Placement != other.Placement
                || !string.Equals(DependencyHash, other.DependencyHash, StringComparison.Ordinal)
                || Exclusions.Count != other.Exclusions.Count
                || !PlacementStates.SequenceEqual(other.PlacementStates)
                || !ExclusionRendererIds.SequenceEqual(other.ExclusionRendererIds))
            {
                return false;
            }

            for (var index = 0; index < Exclusions.Count; index++)
            {
                if (Exclusions[index] != other.Exclusions[index]) return false;
            }

            return true;
        }

        private static PlacementState[] CapturePlacementStates(
            Transform avatarRoot,
            Transform placement)
        {
            var states = new List<PlacementState>();
            var current = placement;
            while (current != null)
            {
                states.Add(new PlacementState(current));
                if (current == avatarRoot) break;
                current = current.parent;
            }

            states.Reverse();
            return states.ToArray();
        }

        private static int[] CaptureExclusionRendererIds(
            IEnumerable<GameObject> exclusions)
        {
            return exclusions
                .SelectMany(exclusion => exclusion.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer is MeshRenderer or SkinnedMeshRenderer)
                .Select(renderer => renderer.GetInstanceID())
                .Distinct()
                .OrderBy(instanceId => instanceId)
                .ToArray();
        }

        private readonly struct PlacementState : IEquatable<PlacementState>
        {
            private readonly int _instanceId;
            private readonly int _parentInstanceId;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _lossyScale;
            private readonly Vector3 _localPosition;
            private readonly Quaternion _localRotation;
            private readonly Vector3 _localScale;
            private readonly bool _activeSelf;

            internal PlacementState(Transform transform)
            {
                _instanceId = transform.GetInstanceID();
                _parentInstanceId = transform.parent != null
                    ? transform.parent.GetInstanceID()
                    : 0;
                _position = transform.position;
                _rotation = transform.rotation;
                _lossyScale = transform.lossyScale;
                _localPosition = transform.localPosition;
                _localRotation = transform.localRotation;
                _localScale = transform.localScale;
                _activeSelf = transform.gameObject.activeSelf;
            }

            public bool Equals(PlacementState other)
            {
                return _instanceId == other._instanceId
                       && _parentInstanceId == other._parentInstanceId
                       && _position == other._position
                       && _rotation == other._rotation
                       && _lossyScale == other._lossyScale
                       && _localPosition == other._localPosition
                       && _localRotation == other._localRotation
                       && _localScale == other._localScale
                       && _activeSelf == other._activeSelf;
            }

            public override bool Equals(object obj)
            {
                return obj is PlacementState other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = _instanceId;
                    hashCode = (hashCode * 397) ^ _parentInstanceId;
                    hashCode = (hashCode * 397) ^ _position.GetHashCode();
                    hashCode = (hashCode * 397) ^ _rotation.GetHashCode();
                    hashCode = (hashCode * 397) ^ _lossyScale.GetHashCode();
                    hashCode = (hashCode * 397) ^ _localPosition.GetHashCode();
                    hashCode = (hashCode * 397) ^ _localRotation.GetHashCode();
                    hashCode = (hashCode * 397) ^ _localScale.GetHashCode();
                    return (hashCode * 397) ^ _activeSelf.GetHashCode();
                }
            }
        }
    }
}
