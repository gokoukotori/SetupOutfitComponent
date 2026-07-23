using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal readonly struct OutfitPartTargetPreviewSnapshot :
        IEquatable<OutfitPartTargetPreviewSnapshot>
    {
        internal OutfitPartTargetPreviewSnapshot(
            PrefabTargetKey targetKey,
            bool activeWhenOn)
        {
            TargetKey = targetKey;
            ActiveWhenOn = activeWhenOn;
        }

        internal PrefabTargetKey TargetKey { get; }
        internal bool ActiveWhenOn { get; }

        public bool Equals(OutfitPartTargetPreviewSnapshot other)
        {
            return TargetKey.Equals(other.TargetKey)
                   && ActiveWhenOn == other.ActiveWhenOn;
        }

        public override bool Equals(object obj)
        {
            return obj is OutfitPartTargetPreviewSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (TargetKey.GetHashCode() * 397) ^ ActiveWhenOn.GetHashCode();
            }
        }
    }

    internal sealed class OutfitPartPreviewSnapshot :
        IEquatable<OutfitPartPreviewSnapshot>
    {
        internal OutfitPartPreviewSnapshot(
            string key,
            string label,
            bool initialOn,
            bool initialResolved,
            ImmutableArray<OutfitPartTargetPreviewSnapshot> targets)
        {
            Key = key ?? string.Empty;
            Label = label ?? string.Empty;
            InitialOn = initialOn;
            InitialResolved = initialResolved;
            Targets = targets.IsDefault
                ? ImmutableArray<OutfitPartTargetPreviewSnapshot>.Empty
                : targets;
        }

        internal string Key { get; }
        internal string Label { get; }
        internal bool InitialOn { get; }
        internal bool InitialResolved { get; }
        internal ImmutableArray<OutfitPartTargetPreviewSnapshot> Targets { get; }

        public bool Equals(OutfitPartPreviewSnapshot other)
        {
            return other != null
                   && string.Equals(Key, other.Key, StringComparison.Ordinal)
                   && string.Equals(Label, other.Label, StringComparison.Ordinal)
                   && InitialOn == other.InitialOn
                   && InitialResolved == other.InitialResolved
                   && Targets.SequenceEqual(other.Targets);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as OutfitPartPreviewSnapshot);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = StringComparer.Ordinal.GetHashCode(Key);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Label);
                hashCode = (hashCode * 397) ^ InitialOn.GetHashCode();
                hashCode = (hashCode * 397) ^ InitialResolved.GetHashCode();
                foreach (var target in Targets)
                    hashCode = (hashCode * 397) ^ target.GetHashCode();
                return hashCode;
            }
        }
    }

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
            int[] exclusionRendererIds,
            ImmutableArray<OutfitPartPreviewSnapshot> parts)
        {
            SourcePrefab = sourcePrefab;
            AvatarRoot = avatarRoot;
            Placement = placement;
            Exclusions = exclusions;
            DependencyHash = dependencyHash;
            InitialOn = initialOn;
            PlacementStates = placementStates;
            ExclusionRendererIds = exclusionRendererIds;
            Parts = parts.IsDefault
                ? ImmutableArray<OutfitPartPreviewSnapshot>.Empty
                : parts;
        }

        internal GameObject SourcePrefab { get; }
        internal GameObject AvatarRoot { get; }
        internal Transform Placement { get; }
        internal IReadOnlyList<GameObject> Exclusions { get; }
        internal string DependencyHash { get; }
        internal bool InitialOn { get; }
        internal ImmutableArray<OutfitPartPreviewSnapshot> Parts { get; }
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
            return TryCreateCore(
                sourcePrefab,
                avatarRoot,
                placement,
                exclusions,
                dependencyHash,
                initialOn,
                null,
                null,
                false,
                out request,
                out error);
        }

        internal static bool TryCreate(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IEnumerable<GameObject> exclusions,
            string dependencyHash,
            bool initialOn,
            OutfitAnalysis analysis,
            IEnumerable<PartTogglePlan> partToggles,
            out OutfitPreviewRequest request,
            out string error)
        {
            return TryCreateCore(
                sourcePrefab,
                avatarRoot,
                placement,
                exclusions,
                dependencyHash,
                initialOn,
                analysis,
                partToggles,
                true,
                out request,
                out error);
        }

        private static bool TryCreateCore(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IEnumerable<GameObject> exclusions,
            string dependencyHash,
            bool initialOn,
            OutfitAnalysis analysis,
            IEnumerable<PartTogglePlan> partToggles,
            bool validateParts,
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

            var parts = ImmutableArray<OutfitPartPreviewSnapshot>.Empty;
            if (validateParts
                && !TryCreatePartSnapshots(
                    sourcePrefab,
                    dependencyHash,
                    analysis,
                    partToggles,
                    out parts,
                    out error))
            {
                return false;
            }

            request = new OutfitPreviewRequest(
                sourcePrefab,
                avatarRoot,
                placement,
                resolvedExclusions,
                dependencyHash,
                initialOn,
                CapturePlacementStates(avatarRoot.transform, placement),
                CaptureExclusionRendererIds(resolvedExclusions),
                parts);
            return true;
        }

        internal bool IsMirrorStructureEquivalentTo(OutfitPreviewRequest other)
        {
            return other != null
                   && SourcePrefab == other.SourcePrefab
                   && AvatarRoot == other.AvatarRoot
                   && Placement == other.Placement
                   && string.Equals(DependencyHash, other.DependencyHash, StringComparison.Ordinal)
                   && PlacementStates.SequenceEqual(other.PlacementStates);
        }

        internal bool HasEquivalentExclusionRendererSetTo(OutfitPreviewRequest other)
        {
            return other != null
                   && ExclusionRendererIds.SequenceEqual(other.ExclusionRendererIds);
        }

        internal bool HasEquivalentPartsTo(OutfitPreviewRequest other)
        {
            return other != null && Parts.SequenceEqual(other.Parts);
        }

        internal bool IsStructurallyEquivalentTo(OutfitPreviewRequest other)
        {
            if (!IsMirrorStructureEquivalentTo(other)
                || Exclusions.Count != other.Exclusions.Count
                || !HasEquivalentExclusionRendererSetTo(other)
                || !HasEquivalentPartsTo(other))
            {
                return false;
            }

            for (var index = 0; index < Exclusions.Count; index++)
            {
                if (Exclusions[index] != other.Exclusions[index]) return false;
            }

            return true;
        }

        private static bool TryCreatePartSnapshots(
            GameObject sourcePrefab,
            string dependencyHash,
            OutfitAnalysis analysis,
            IEnumerable<PartTogglePlan> partToggles,
            out ImmutableArray<OutfitPartPreviewSnapshot> snapshots,
            out string error)
        {
            snapshots = ImmutableArray<OutfitPartPreviewSnapshot>.Empty;
            error = null;

            if (analysis == null
                || analysis.SourcePrefab != sourcePrefab
                || !string.Equals(analysis.DependencyHash, dependencyHash, StringComparison.Ordinal))
            {
                error = "個別パーツの解析結果が入力Prefabと一致しません。ウィザードを開き直してください。";
                return false;
            }

            var parts = (partToggles ?? Enumerable.Empty<PartTogglePlan>()).ToArray();
            var allTargetKeys = new List<PrefabTargetKey>();
            var builder = ImmutableArray.CreateBuilder<OutfitPartPreviewSnapshot>(parts.Length);
            for (var partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                var part = parts[partIndex];
                if (part == null)
                {
                    error = $"個別項目{partIndex + 1}が不正です。";
                    return false;
                }

                if (part.Targets.Count == 0)
                {
                    error = $"個別項目「{part.Label}」に対象がありません。";
                    return false;
                }

                var targetBuilder =
                    ImmutableArray.CreateBuilder<OutfitPartTargetPreviewSnapshot>(
                        part.Targets.Count);
                foreach (var targetKey in part.Targets)
                {
                    if (targetKey.IsRoot)
                    {
                        error = "衣装Prefabのルート自体は個別パーツに指定できません。";
                        return false;
                    }

                    if (!string.Equals(
                            targetKey.DependencyHash,
                            dependencyHash,
                            StringComparison.Ordinal)
                        || targetKey.Resolve(sourcePrefab, dependencyHash) == null
                        || analysis.FindTarget(targetKey) == null)
                    {
                        error = $"個別項目「{part.Label}」の対象が解析時のPrefabと一致しません。";
                        return false;
                    }

                    if (allTargetKeys.Contains(targetKey))
                    {
                        error = "同じPrefab内オブジェクトが複数の個別項目に指定されています。";
                        return false;
                    }

                    allTargetKeys.Add(targetKey);
                    targetBuilder.Add(new OutfitPartTargetPreviewSnapshot(
                        targetKey,
                        part.GetTargetActiveWhenOn(targetKey)));
                }

                var targets = targetBuilder
                    .ToImmutable()
                    .OrderBy(
                        target => target.TargetKey.SiblingIndexPath,
                        StringComparer.Ordinal)
                    .ToImmutableArray();
                var initialResolved = part.TryGetEffectiveInitialOn(analysis, out var initialOn);
                if (!initialResolved) initialOn = false;
                var key = string.Join(
                    "|",
                    targets.Select(target => target.TargetKey.SiblingIndexPath));
                builder.Add(new OutfitPartPreviewSnapshot(
                    key,
                    part.Label,
                    initialOn,
                    initialResolved,
                    targets));
            }

            for (var left = 0; left < allTargetKeys.Count; left++)
            {
                for (var right = left + 1; right < allTargetKeys.Count; right++)
                {
                    if (!allTargetKeys[left].IsAncestorOf(allTargetKeys[right])
                        && !allTargetKeys[right].IsAncestorOf(allTargetKeys[left]))
                    {
                        continue;
                    }

                    error = "個別パーツの対象に祖先・子孫関係のあるGameObjectを同時指定できません。";
                    return false;
                }
            }

            snapshots = builder.ToImmutable();
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
