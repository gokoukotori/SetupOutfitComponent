using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal readonly struct OutfitMasterSceneTargetPreviewSnapshot :
        IEquatable<OutfitMasterSceneTargetPreviewSnapshot>
    {
        internal OutfitMasterSceneTargetPreviewSnapshot(
            GameObject sceneObject,
            string stableId,
            bool activeWhenOn)
        {
            SceneObject = sceneObject;
            StableId = stableId ?? string.Empty;
            ActiveWhenOn = activeWhenOn;
        }

        internal GameObject SceneObject { get; }
        internal string StableId { get; }
        internal bool ActiveWhenOn { get; }

        public bool Equals(OutfitMasterSceneTargetPreviewSnapshot other)
        {
            return SceneObject == other.SceneObject
                   && string.Equals(StableId, other.StableId, StringComparison.Ordinal)
                   && ActiveWhenOn == other.ActiveWhenOn;
        }

        public override bool Equals(object obj)
        {
            return obj is OutfitMasterSceneTargetPreviewSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = SceneObject != null ? SceneObject.GetInstanceID() : 0;
                hashCode = (hashCode * 397)
                           ^ StringComparer.Ordinal.GetHashCode(StableId);
                return (hashCode * 397) ^ ActiveWhenOn.GetHashCode();
            }
        }
    }

    internal readonly struct OutfitPartTargetPreviewSnapshot :
        IEquatable<OutfitPartTargetPreviewSnapshot>
    {
        internal OutfitPartTargetPreviewSnapshot(
            PrefabTargetKey targetKey,
            bool activeWhenOn)
            : this(
                PartTargetSource.OutfitPrefab,
                targetKey,
                null,
                "P:" + targetKey,
                activeWhenOn)
        {
        }

        internal OutfitPartTargetPreviewSnapshot(
            PartTargetSource source,
            PrefabTargetKey prefabKey,
            GameObject sceneObject,
            string stableId,
            bool activeWhenOn)
        {
            Source = source;
            PrefabKey = prefabKey;
            SceneObject = sceneObject;
            StableId = stableId ?? string.Empty;
            ActiveWhenOn = activeWhenOn;
        }

        internal PartTargetSource Source { get; }
        internal PrefabTargetKey PrefabKey { get; }
        internal PrefabTargetKey TargetKey => PrefabKey;
        internal GameObject SceneObject { get; }
        internal string StableId { get; }
        internal bool ActiveWhenOn { get; }

        public bool Equals(OutfitPartTargetPreviewSnapshot other)
        {
            return Source == other.Source
                   && PrefabKey.Equals(other.PrefabKey)
                   && SceneObject == other.SceneObject
                   && string.Equals(StableId, other.StableId, StringComparison.Ordinal)
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
                var hashCode = (int)Source;
                hashCode = (hashCode * 397) ^ PrefabKey.GetHashCode();
                hashCode = (hashCode * 397) ^ (SceneObject != null ? SceneObject.GetInstanceID() : 0);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(StableId);
                return (hashCode * 397) ^ ActiveWhenOn.GetHashCode();
            }
        }
    }

    internal sealed class OutfitPartPreviewSnapshot :
        IEquatable<OutfitPartPreviewSnapshot>
    {
        internal OutfitPartPreviewSnapshot(
            string itemId,
            string label,
            bool initialOn,
            bool initialResolved,
            ImmutableArray<OutfitPartTargetPreviewSnapshot> targets)
        {
            ItemId = itemId ?? string.Empty;
            Label = label ?? string.Empty;
            InitialOn = initialOn;
            InitialResolved = initialResolved;
            Targets = targets.IsDefault
                ? ImmutableArray<OutfitPartTargetPreviewSnapshot>.Empty
                : targets;
        }

        internal string ItemId { get; }
        internal string Key => ItemId;
        internal string Label { get; }
        internal bool InitialOn { get; }
        internal bool InitialResolved { get; }
        internal ImmutableArray<OutfitPartTargetPreviewSnapshot> Targets { get; }

        public bool Equals(OutfitPartPreviewSnapshot other)
        {
            return other != null
                   && string.Equals(ItemId, other.ItemId, StringComparison.Ordinal)
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
                var hashCode = StringComparer.Ordinal.GetHashCode(ItemId);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Label);
                hashCode = (hashCode * 397) ^ InitialOn.GetHashCode();
                hashCode = (hashCode * 397) ^ InitialResolved.GetHashCode();
                foreach (var target in Targets)
                    hashCode = (hashCode * 397) ^ target.GetHashCode();
                return hashCode;
            }
        }
    }

    internal static class PartToggleMenuOrderResolver
    {
        internal static bool TryResolveLastEnabled<T>(
            IReadOnlyList<T> controls,
            Func<T, string> itemIdSelector,
            Func<T, bool> activeWhenOnSelector,
            IReadOnlyDictionary<string, bool> partStates,
            out bool activeWhenOn)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            if (itemIdSelector == null) throw new ArgumentNullException(nameof(itemIdSelector));
            if (activeWhenOnSelector == null)
                throw new ArgumentNullException(nameof(activeWhenOnSelector));

            partStates ??= new Dictionary<string, bool>();
            for (var index = controls.Count - 1; index >= 0; index--)
            {
                var control = controls[index];
                if (!partStates.TryGetValue(itemIdSelector(control), out var selected)
                    || !selected)
                {
                    continue;
                }

                activeWhenOn = activeWhenOnSelector(control);
                return true;
            }

            activeWhenOn = false;
            return false;
        }
    }

    internal readonly struct OutfitShapeChangePreviewSnapshot :
        IEquatable<OutfitShapeChangePreviewSnapshot>
    {
        internal OutfitShapeChangePreviewSnapshot(
            string ownerItemId,
            bool isMaster,
            PartTargetSource source,
            PrefabTargetKey prefabRendererKey,
            SkinnedMeshRenderer sceneRenderer,
            string stableRendererId,
            string shapeName,
            float value)
            : this(
                ownerItemId,
                isMaster,
                false,
                default,
                -1,
                source,
                prefabRendererKey,
                sceneRenderer,
                stableRendererId,
                shapeName,
                value)
        {
        }

        internal OutfitShapeChangePreviewSnapshot(
            string ownerItemId,
            bool isMaster,
            bool hasOutfitOwner,
            PrefabTargetKey outfitOwnerKey,
            int ownerHierarchyOrder,
            PartTargetSource source,
            PrefabTargetKey prefabRendererKey,
            SkinnedMeshRenderer sceneRenderer,
            string stableRendererId,
            string shapeName,
            float value)
        {
            OwnerItemId = ownerItemId ?? string.Empty;
            IsMaster = isMaster;
            HasOutfitOwner = hasOutfitOwner;
            OutfitOwnerKey = outfitOwnerKey;
            OwnerHierarchyOrder = ownerHierarchyOrder;
            Source = source;
            PrefabRendererKey = prefabRendererKey;
            SceneRenderer = sceneRenderer;
            StableRendererId = stableRendererId ?? string.Empty;
            ShapeName = shapeName ?? string.Empty;
            Value = value;
        }

        internal string OwnerItemId { get; }
        internal bool IsMaster { get; }
        internal bool HasOutfitOwner { get; }
        internal PrefabTargetKey OutfitOwnerKey { get; }
        internal int OwnerHierarchyOrder { get; }
        internal PartTargetSource Source { get; }
        internal PrefabTargetKey PrefabRendererKey { get; }
        internal SkinnedMeshRenderer SceneRenderer { get; }
        internal string StableRendererId { get; }
        internal string ShapeName { get; }
        internal float Value { get; }

        public bool Equals(OutfitShapeChangePreviewSnapshot other)
        {
            return string.Equals(OwnerItemId, other.OwnerItemId, StringComparison.Ordinal)
                   && IsMaster == other.IsMaster
                   && HasOutfitOwner == other.HasOutfitOwner
                   && OutfitOwnerKey.Equals(other.OutfitOwnerKey)
                   && OwnerHierarchyOrder == other.OwnerHierarchyOrder
                   && Source == other.Source
                   && PrefabRendererKey.Equals(other.PrefabRendererKey)
                   && SceneRenderer == other.SceneRenderer
                   && string.Equals(
                       StableRendererId,
                       other.StableRendererId,
                       StringComparison.Ordinal)
                   && string.Equals(ShapeName, other.ShapeName, StringComparison.Ordinal)
                   && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            return obj is OutfitShapeChangePreviewSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = StringComparer.Ordinal.GetHashCode(OwnerItemId);
                hashCode = (hashCode * 397) ^ IsMaster.GetHashCode();
                hashCode = (hashCode * 397) ^ HasOutfitOwner.GetHashCode();
                hashCode = (hashCode * 397) ^ OutfitOwnerKey.GetHashCode();
                hashCode = (hashCode * 397) ^ OwnerHierarchyOrder;
                hashCode = (hashCode * 397) ^ (int)Source;
                hashCode = (hashCode * 397) ^ PrefabRendererKey.GetHashCode();
                hashCode = (hashCode * 397)
                           ^ (SceneRenderer != null ? SceneRenderer.GetInstanceID() : 0);
                hashCode = (hashCode * 397)
                           ^ StringComparer.Ordinal.GetHashCode(StableRendererId);
                hashCode = (hashCode * 397)
                           ^ StringComparer.Ordinal.GetHashCode(ShapeName);
                return (hashCode * 397) ^ Value.GetHashCode();
            }
        }
    }

    internal sealed class OutfitPreviewRequest
    {
        private OutfitPreviewRequest(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            ImmutableArray<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            string dependencyHash,
            bool initialOn,
            PlacementState[] placementStates,
            ImmutableArray<OutfitPartPreviewSnapshot> parts,
            ImmutableArray<OutfitShapeChangePreviewSnapshot> shapeChanges,
            ImmutableArray<ExistingAvatarShapeChangePreviewSnapshot> existingAvatarShapeChanges,
            ImmutableArray<string> shapeChangeWarnings,
            int existingAvatarShapeChangerDeleteCount,
            int existingAvatarShapeChangerSkippedCount)
        {
            SourcePrefab = sourcePrefab;
            AvatarRoot = avatarRoot;
            Placement = placement;
            MasterSceneTargets = masterSceneTargets.IsDefault
                ? ImmutableArray<OutfitMasterSceneTargetPreviewSnapshot>.Empty
                : masterSceneTargets;
            DependencyHash = dependencyHash;
            InitialOn = initialOn;
            PlacementStates = placementStates;
            Parts = parts.IsDefault
                ? ImmutableArray<OutfitPartPreviewSnapshot>.Empty
                : parts;
            ShapeChanges = shapeChanges.IsDefault
                ? ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty
                : shapeChanges;
            ExistingAvatarShapeChanges = existingAvatarShapeChanges.IsDefault
                ? ImmutableArray<ExistingAvatarShapeChangePreviewSnapshot>.Empty
                : existingAvatarShapeChanges;
            ExistingAvatarShapeChangerDeleteCount = existingAvatarShapeChangerDeleteCount;
            ExistingAvatarShapeChangerSkippedCount = existingAvatarShapeChangerSkippedCount;
            ShapeChangeWarnings = shapeChangeWarnings.IsDefault
                ? ImmutableArray<string>.Empty
                : shapeChangeWarnings;
        }

        internal GameObject SourcePrefab { get; }
        internal GameObject AvatarRoot { get; }
        internal Transform Placement { get; }
        internal ImmutableArray<OutfitMasterSceneTargetPreviewSnapshot> MasterSceneTargets { get; }
        internal string DependencyHash { get; }
        internal bool InitialOn { get; }
        internal ImmutableArray<OutfitPartPreviewSnapshot> Parts { get; }
        internal ImmutableArray<OutfitShapeChangePreviewSnapshot> ShapeChanges { get; }
        internal ImmutableArray<ExistingAvatarShapeChangePreviewSnapshot> ExistingAvatarShapeChanges { get; }
        internal int ExistingAvatarShapeChangerSetCount => ExistingAvatarShapeChanges.Length;
        internal int ExistingAvatarShapeChangerDeleteCount { get; }
        internal int ExistingAvatarShapeChangerSkippedCount { get; }
        internal ImmutableArray<string> ShapeChangeWarnings { get; }
        private PlacementState[] PlacementStates { get; }

        internal static bool TryCreate(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IEnumerable<MasterSceneTargetPlan> masterSceneTargets,
            string dependencyHash,
            bool initialOn,
            out OutfitPreviewRequest request,
            out string error)
        {
            return TryCreateCore(
                sourcePrefab,
                avatarRoot,
                placement,
                ResolveMasterSceneTargets(masterSceneTargets),
                dependencyHash,
                initialOn,
                null,
                null,
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
            IEnumerable<OutfitMasterSceneTargetPreviewSnapshot> masterSceneTargets,
            string dependencyHash,
            bool initialOn,
            out OutfitPreviewRequest request,
            out string error)
        {
            var resolved = (masterSceneTargets
                            ?? Enumerable.Empty<OutfitMasterSceneTargetPreviewSnapshot>())
                .Select(target => new ResolvedMasterSceneTarget(
                    target.SceneObject,
                    target.StableId,
                    target.ActiveWhenOn));
            return TryCreateCore(
                sourcePrefab,
                avatarRoot,
                placement,
                resolved,
                dependencyHash,
                initialOn,
                null,
                null,
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
            IEnumerable<MasterSceneTargetPlan> masterSceneTargets,
            string dependencyHash,
            bool initialOn,
            OutfitAnalysis analysis,
            IEnumerable<PartTogglePlan> partToggles,
            out OutfitPreviewRequest request,
            out string error)
        {
            return TryCreate(
                sourcePrefab,
                avatarRoot,
                placement,
                masterSceneTargets,
                dependencyHash,
                initialOn,
                analysis,
                partToggles,
                null,
                out request,
                out error);
        }

        internal static bool TryCreate(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IEnumerable<MasterSceneTargetPlan> masterSceneTargets,
            string dependencyHash,
            bool initialOn,
            OutfitAnalysis analysis,
            IEnumerable<PartTogglePlan> partToggles,
            IEnumerable<ShapeChangerSettingPlan> masterShapeChanges,
            out OutfitPreviewRequest request,
            out string error)
        {
            return TryCreateCore(
                sourcePrefab,
                avatarRoot,
                placement,
                ResolveMasterSceneTargets(masterSceneTargets),
                dependencyHash,
                initialOn,
                analysis,
                partToggles,
                masterShapeChanges,
                null,
                true,
                out request,
                out error);
        }

        internal static bool TryCreate(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IEnumerable<MasterSceneTargetPlan> masterSceneTargets,
            string dependencyHash,
            bool initialOn,
            OutfitAnalysis analysis,
            IEnumerable<PartTogglePlan> partToggles,
            IEnumerable<ShapeChangerSettingPlan> masterShapeChanges,
            IEnumerable<OutfitRendererShapeChangerPlan> outfitRendererShapeChangers,
            out OutfitPreviewRequest request,
            out string error)
        {
            return TryCreateCore(
                sourcePrefab,
                avatarRoot,
                placement,
                ResolveMasterSceneTargets(masterSceneTargets),
                dependencyHash,
                initialOn,
                analysis,
                partToggles,
                masterShapeChanges,
                outfitRendererShapeChangers,
                true,
                out request,
                out error);
        }

        private static bool TryCreateCore(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IEnumerable<ResolvedMasterSceneTarget> masterSceneTargets,
            string dependencyHash,
            bool initialOn,
            OutfitAnalysis analysis,
            IEnumerable<PartTogglePlan> partToggles,
            IEnumerable<ShapeChangerSettingPlan> masterShapeChanges,
            IEnumerable<OutfitRendererShapeChangerPlan> outfitRendererShapeChangers,
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

            var resolvedTargets = (masterSceneTargets
                                   ?? Enumerable.Empty<ResolvedMasterSceneTarget>())
                .ToArray();
            var targetBuilder =
                ImmutableArray.CreateBuilder<OutfitMasterSceneTargetPreviewSnapshot>(
                    resolvedTargets.Length);
            var seenObjects = new HashSet<int>();
            var seenStableIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in resolvedTargets)
            {
                var sceneObject = target.SceneObject;
                if (sceneObject == null)
                {
                    error = "Scene対象に未指定または再解決できない行があります。";
                    return false;
                }

                if (string.IsNullOrEmpty(target.StableId))
                {
                    error = "Scene対象の安定IDを解決できません。";
                    return false;
                }

                if (EditorUtility.IsPersistent(sceneObject)
                    || !sceneObject.scene.IsValid()
                    || !sceneObject.scene.isLoaded)
                {
                    error = "Scene対象はロード済みScene上のGameObjectである必要があります。";
                    return false;
                }

                if (sceneObject == avatarRoot
                    || !sceneObject.transform.IsChildOf(avatarRoot.transform))
                {
                    error = "Scene対象は対象アバターの子孫である必要があります。";
                    return false;
                }

                if (placement == sceneObject.transform
                    || placement.IsChildOf(sceneObject.transform))
                {
                    error = "Scene対象に配置先またはその祖先を指定することはできません。";
                    return false;
                }

                if (!seenObjects.Add(sceneObject.GetInstanceID())
                    || !seenStableIds.Add(target.StableId))
                {
                    error = "同じScene対象が複数回指定されています。";
                    return false;
                }

                targetBuilder.Add(new OutfitMasterSceneTargetPreviewSnapshot(
                    sceneObject,
                    target.StableId,
                    target.ActiveWhenOn));
            }

            var masterSnapshots = targetBuilder.ToImmutable();
            var resolvedSceneObjects = masterSnapshots
                .Select(target => target.SceneObject)
                .ToArray();
            var parts = ImmutableArray<OutfitPartPreviewSnapshot>.Empty;
            var partPlans = (partToggles ?? Enumerable.Empty<PartTogglePlan>()).ToArray();
            if (validateParts
                && !TryCreatePartSnapshots(
                    sourcePrefab,
                    avatarRoot,
                    placement,
                    resolvedSceneObjects,
                    dependencyHash,
                    analysis,
                    partPlans,
                    out parts,
                    out error))
            {
                return false;
            }

            var shapeChanges = ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty;
            var shapeChangeWarnings = ImmutableArray<string>.Empty;
            if (validateParts
                && !TryCreateShapeChangeSnapshots(
                    sourcePrefab,
                    avatarRoot,
                    dependencyHash,
                    masterShapeChanges,
                    outfitRendererShapeChangers,
                    partPlans,
                    out shapeChanges,
                    out shapeChangeWarnings,
                    out error))
            {
                return false;
            }

            var existingShapeChanges =
                ExistingAvatarShapeChangerPreviewAnalyzer.Analyze(avatarRoot);
            shapeChangeWarnings = shapeChangeWarnings.AddRange(
                existingShapeChanges.Warnings);

            request = new OutfitPreviewRequest(
                sourcePrefab,
                avatarRoot,
                placement,
                masterSnapshots,
                dependencyHash,
                initialOn,
                CapturePlacementStates(avatarRoot.transform, placement),
                parts,
                shapeChanges,
                existingShapeChanges.Sets,
                shapeChangeWarnings,
                existingShapeChanges.DeleteCount,
                existingShapeChanges.SkippedCount);
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

        internal bool HasEquivalentMasterSceneTargetsTo(OutfitPreviewRequest other)
        {
            return other != null
                   && MasterSceneTargets.SequenceEqual(other.MasterSceneTargets);
        }

        internal bool HasEquivalentPartsTo(OutfitPreviewRequest other)
        {
            return other != null && Parts.SequenceEqual(other.Parts);
        }

        internal bool HasEquivalentShapeChangesTo(OutfitPreviewRequest other)
        {
            return other != null && ShapeChanges.SequenceEqual(other.ShapeChanges);
        }

        internal bool HasEquivalentExistingAvatarShapeChangesTo(OutfitPreviewRequest other)
        {
            return other != null
                   && ExistingAvatarShapeChanges.SequenceEqual(
                       other.ExistingAvatarShapeChanges)
                   && ExistingAvatarShapeChangerDeleteCount
                   == other.ExistingAvatarShapeChangerDeleteCount
                   && ExistingAvatarShapeChangerSkippedCount
                   == other.ExistingAvatarShapeChangerSkippedCount;
        }

        internal bool HasEquivalentShapeChangeWarningsTo(OutfitPreviewRequest other)
        {
            return other != null
                   && ShapeChangeWarnings.SequenceEqual(other.ShapeChangeWarnings);
        }

        internal bool IsStructurallyEquivalentTo(OutfitPreviewRequest other)
        {
            return IsMirrorStructureEquivalentTo(other)
                   && HasEquivalentMasterSceneTargetsTo(other)
                   && HasEquivalentPartsTo(other)
                   && HasEquivalentShapeChangesTo(other)
                   && HasEquivalentExistingAvatarShapeChangesTo(other)
                   && HasEquivalentShapeChangeWarningsTo(other);
        }

        private static bool TryCreateShapeChangeSnapshots(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            string dependencyHash,
            IEnumerable<ShapeChangerSettingPlan> masterShapeChanges,
            IEnumerable<OutfitRendererShapeChangerPlan> outfitRendererShapeChangers,
            IEnumerable<PartTogglePlan> partToggles,
            out ImmutableArray<OutfitShapeChangePreviewSnapshot> snapshots,
            out ImmutableArray<string> warnings,
            out string error)
        {
            var builder = ImmutableArray.CreateBuilder<OutfitShapeChangePreviewSnapshot>();
            var warningBuilder = ImmutableArray.CreateBuilder<string>();
            if (!TryAppendShapeChangeSnapshots(
                    sourcePrefab,
                    avatarRoot,
                    dependencyHash,
                    string.Empty,
                    true,
                    false,
                    default,
                    -1,
                    "衣装全体ON",
                    masterShapeChanges,
                    builder,
                    warningBuilder,
                    out error))
            {
                snapshots = ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty;
                warnings = ImmutableArray<string>.Empty;
                return false;
            }

            var ownerPlans = (outfitRendererShapeChangers
                              ?? Enumerable.Empty<OutfitRendererShapeChangerPlan>())
                .ToArray();
            if (ownerPlans.Any(owner => owner == null))
            {
                snapshots = ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty;
                warnings = ImmutableArray<string>.Empty;
                error = "衣装内Shape Changerに不正な所有GameObject設定があります。";
                return false;
            }

            var seenOwners = new HashSet<PrefabTargetKey>();
            var orderedOwners = ownerPlans
                .OrderBy(owner => owner.OwnerKey, PrefabHierarchyComparer.Instance)
                .ToArray();
            for (var ownerIndex = 0; ownerIndex < orderedOwners.Length; ownerIndex++)
            {
                var owner = orderedOwners[ownerIndex];
                if (!seenOwners.Add(owner.OwnerKey)
                    || !string.Equals(
                        owner.OwnerKey.DependencyHash,
                        dependencyHash,
                        StringComparison.Ordinal))
                {
                    snapshots = ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty;
                    warnings = ImmutableArray<string>.Empty;
                    error = "衣装内Shape Changerの所有GameObject参照が重複または古くなっています。";
                    return false;
                }

                var ownerObject = owner.OwnerKey.Resolve(sourcePrefab, dependencyHash);
                if (ownerObject == null || ownerObject.GetComponent<Renderer>() == null)
                {
                    snapshots = ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty;
                    warnings = ImmutableArray<string>.Empty;
                    error = "衣装内Shape Changerの所有GameObjectを解決できません。";
                    return false;
                }

                if (!TryAppendShapeChangeSnapshots(
                        sourcePrefab,
                        avatarRoot,
                        dependencyHash,
                        string.Empty,
                        false,
                        true,
                        owner.OwnerKey,
                        ownerIndex,
                        string.IsNullOrEmpty(ownerObject.name)
                            ? "<衣装内GameObject>"
                            : ownerObject.name,
                        owner.ShapeChanges,
                        builder,
                        warningBuilder,
                        out error))
                {
                    snapshots = ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty;
                    warnings = ImmutableArray<string>.Empty;
                    return false;
                }
            }

            foreach (var part in partToggles ?? Enumerable.Empty<PartTogglePlan>())
            {
                if (part == null) continue;
                if (!TryAppendShapeChangeSnapshots(
                        sourcePrefab,
                        avatarRoot,
                        dependencyHash,
                        part.ItemId,
                        false,
                        false,
                        default,
                        -1,
                        string.IsNullOrWhiteSpace(part.Label) ? "<個別項目>" : part.Label,
                        part.ShapeChanges,
                        builder,
                        warningBuilder,
                        out error))
                {
                    snapshots = ImmutableArray<OutfitShapeChangePreviewSnapshot>.Empty;
                    warnings = ImmutableArray<string>.Empty;
                    return false;
                }
            }

            snapshots = builder.ToImmutable();
            warnings = warningBuilder.ToImmutable();
            error = null;
            return true;
        }

        private static bool TryAppendShapeChangeSnapshots(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            string dependencyHash,
            string ownerItemId,
            bool isMaster,
            bool hasOutfitOwner,
            PrefabTargetKey outfitOwnerKey,
            int ownerHierarchyOrder,
            string ownerLabel,
            IEnumerable<ShapeChangerSettingPlan> changes,
            ImmutableArray<OutfitShapeChangePreviewSnapshot>.Builder builder,
            ImmutableArray<string>.Builder warnings,
            out string error)
        {
            var changePlans = (changes ?? Enumerable.Empty<ShapeChangerSettingPlan>())
                .ToArray();
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            var skippedCount = 0;
            foreach (var change in changePlans)
            {
                if (change == null)
                {
                    error = $"Shape Changer「{ownerLabel}」に不正な設定があります。";
                    return false;
                }

                if (float.IsNaN(change.Value)
                    || float.IsInfinity(change.Value)
                    || change.Value < 0f
                    || change.Value > 100f)
                {
                    error = $"Shape Changer「{ownerLabel}」の値は0～100で指定してください。";
                    return false;
                }

                if (change.Source != PartTargetSource.OutfitPrefab
                    && change.Source != PartTargetSource.SceneObject)
                {
                    error = $"Shape Changer「{ownerLabel}」の対象種別が不正です。";
                    return false;
                }

                SkinnedMeshRenderer renderer = null;
                if (change.Source == PartTargetSource.OutfitPrefab)
                {
                    if (string.IsNullOrEmpty(change.PrefabRendererKey.DependencyHash))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (!string.Equals(
                            change.PrefabRendererKey.DependencyHash,
                            dependencyHash,
                            StringComparison.Ordinal))
                    {
                        error = $"Shape Changer「{ownerLabel}」のPrefab参照が古くなっています。";
                        return false;
                    }

                    renderer = change.PrefabRendererKey
                        .Resolve(sourcePrefab, dependencyHash)
                        ?.GetComponent<SkinnedMeshRenderer>();
                }
                else if (change.Source == PartTargetSource.SceneObject)
                {
                    if (change.SceneRendererReference == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var sceneObject = change.SceneRendererReference.Resolve();
                    if (sceneObject == null
                        || EditorUtility.IsPersistent(sceneObject)
                        || !sceneObject.scene.IsValid()
                        || !sceneObject.scene.isLoaded
                        || (sceneObject != avatarRoot
                            && !sceneObject.transform.IsChildOf(avatarRoot.transform)))
                    {
                        error =
                            $"Shape Changer「{ownerLabel}」のScene Rendererを対象アバター内で解決できません。";
                        return false;
                    }

                    renderer = sceneObject.GetComponent<SkinnedMeshRenderer>();
                }

                if (renderer == null
                    || renderer.sharedMesh == null)
                {
                    error =
                        $"Shape Changer「{ownerLabel}」のRendererまたはMeshを解決できません。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(change.ShapeName))
                {
                    skippedCount++;
                    continue;
                }

                if (renderer.sharedMesh.GetBlendShapeIndex(change.ShapeName) < 0)
                {
                    error = $"Shape Changer「{ownerLabel}」のBlendShapeを解決できません。";
                    return false;
                }

                var duplicateKey = change.StableRendererId + "\n" + change.ShapeName;
                if (!duplicates.Add(duplicateKey))
                {
                    error =
                        $"Shape Changer「{ownerLabel}」内で同じRendererとBlendShapeが重複しています。";
                    return false;
                }

                builder.Add(new OutfitShapeChangePreviewSnapshot(
                    ownerItemId,
                    isMaster,
                    hasOutfitOwner,
                    outfitOwnerKey,
                    ownerHierarchyOrder,
                    change.Source,
                    change.PrefabRendererKey,
                    change.Source == PartTargetSource.SceneObject ? renderer : null,
                    change.StableRendererId,
                    change.ShapeName,
                    change.Value));
            }

            if (skippedCount > 0)
            {
                warnings.Add(
                    $"Shape Changer「{ownerLabel}」の未指定設定{skippedCount}件をプレビューから除外しています。生成前に設定が必要です。");
            }
            else if (hasOutfitOwner && changePlans.Length == 0)
            {
                warnings.Add(
                    $"衣装Renderer表示連動「{ownerLabel}」のShape設定が0件のため、プレビューから除外しています。生成前に設定が必要です。");
            }

            error = null;
            return true;
        }

        private static bool TryCreatePartSnapshots(
            GameObject sourcePrefab,
            GameObject avatarRoot,
            Transform placement,
            IReadOnlyList<GameObject> masterSceneTargets,
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
            var prefabTargets = new List<PrefabTargetKey>();
            var sceneTargets = new List<GameObject>();
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            var masterSceneTargetIds = new HashSet<int>(
                masterSceneTargets.Select(target => target.GetInstanceID()));
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

                if (string.IsNullOrWhiteSpace(part.ItemId))
                {
                    error = $"個別項目「{part.Label}」の項目IDが不正です。";
                    return false;
                }

                if (!itemIds.Add(part.ItemId))
                {
                    error = $"個別項目「{part.Label}」の項目IDが重複しています。";
                    return false;
                }

                var partStableIds = new HashSet<string>(StringComparer.Ordinal);
                var targetBuilder =
                    ImmutableArray.CreateBuilder<OutfitPartTargetPreviewSnapshot>(
                        part.Targets.Count);
                foreach (var target in part.Targets)
                {
                    if (target == null || string.IsNullOrEmpty(target.StableId))
                    {
                        error = $"個別項目「{part.Label}」の対象が不正です。";
                        return false;
                    }

                    if (!partStableIds.Add(target.StableId))
                    {
                        error = target.Source == PartTargetSource.OutfitPrefab
                            ? $"個別項目「{part.Label}」内で同じPrefab内オブジェクトが重複しています。"
                            : $"個別項目「{part.Label}」内で同じSceneオブジェクトが重複しています。";
                        return false;
                    }


                    GameObject sceneObject = null;
                    if (target.Source == PartTargetSource.OutfitPrefab)
                    {
                        var targetKey = target.PrefabKey;
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

                        prefabTargets.Add(targetKey);
                    }
                    else if (target.Source == PartTargetSource.SceneObject)
                    {
                        sceneObject = target.SceneReference?.Resolve();
                        if (sceneObject == null)
                        {
                            error = $"個別項目「{part.Label}」のScene対象を再解決できません。";
                            return false;
                        }

                        if (!sceneObject.scene.IsValid()
                            || !sceneObject.scene.isLoaded
                            || EditorUtility.IsPersistent(sceneObject))
                        {
                            error = "個別パーツのScene対象はロード済みScene上にある必要があります。";
                            return false;
                        }

                        if (sceneObject == avatarRoot
                            || !sceneObject.transform.IsChildOf(avatarRoot.transform))
                        {
                            error = "個別パーツのScene対象は対象アバターの子孫である必要があります。";
                            return false;
                        }

                        if (placement == sceneObject.transform
                            || placement.IsChildOf(sceneObject.transform))
                        {
                            error = "個別パーツのScene対象に配置先またはその祖先を指定できません。";
                            return false;
                        }

                        if (!masterSceneTargetIds.Contains(sceneObject.GetInstanceID()))
                        {
                            error = "個別パーツのScene対象はステップ3のScene対象にも存在する必要があります。";
                            return false;
                        }

                        sceneTargets.Add(sceneObject);
                    }
                    else
                    {
                        error = $"個別項目「{part.Label}」の対象種別が不正です。";
                        return false;
                    }

                    targetBuilder.Add(new OutfitPartTargetPreviewSnapshot(
                        target.Source,
                        target.PrefabKey,
                        sceneObject,
                        target.StableId,
                        target.ActiveWhenOn));
                }

                var targets = targetBuilder
                    .ToImmutable()
                    .OrderBy(
                        target => target.StableId,
                        StringComparer.Ordinal)
                    .ToImmutableArray();
                var initialResolved = part.TryGetEffectiveInitialOn(analysis, out var initialOn);
                if (!initialResolved) initialOn = false;
                builder.Add(new OutfitPartPreviewSnapshot(
                    part.ItemId,
                    part.Label,
                    initialOn,
                    initialResolved,
                    targets));
            }

            var uniquePrefabTargets = prefabTargets.Distinct().ToArray();
            for (var left = 0; left < uniquePrefabTargets.Length; left++)
            {
                for (var right = left + 1; right < uniquePrefabTargets.Length; right++)
                {
                    if (!uniquePrefabTargets[left].IsAncestorOf(uniquePrefabTargets[right])
                        && !uniquePrefabTargets[right].IsAncestorOf(uniquePrefabTargets[left]))
                    {
                        continue;
                    }

                    error = "個別パーツの対象に祖先・子孫関係のあるGameObjectを同時指定できません。";
                    return false;
                }
            }

            var uniqueSceneTargets = sceneTargets.Distinct().ToArray();
            for (var left = 0; left < uniqueSceneTargets.Length; left++)
            {
                for (var right = left + 1; right < uniqueSceneTargets.Length; right++)
                {
                    if (!uniqueSceneTargets[left].transform.IsChildOf(uniqueSceneTargets[right].transform)
                        && !uniqueSceneTargets[right].transform.IsChildOf(uniqueSceneTargets[left].transform))
                    {
                        continue;
                    }

                    error = "個別パーツのScene対象に祖先・子孫関係のあるGameObjectを同時指定できません。";
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

        private static IEnumerable<ResolvedMasterSceneTarget> ResolveMasterSceneTargets(
            IEnumerable<MasterSceneTargetPlan> targets)
        {
            return (targets ?? Enumerable.Empty<MasterSceneTargetPlan>())
                .Select(target => target == null
                    ? default
                    : new ResolvedMasterSceneTarget(
                        target.Reference?.Resolve(),
                        target.StableId,
                        target.ActiveWhenOn));
        }

        private sealed class PrefabHierarchyComparer : IComparer<PrefabTargetKey>
        {
            internal static PrefabHierarchyComparer Instance { get; } =
                new PrefabHierarchyComparer();

            public int Compare(PrefabTargetKey left, PrefabTargetKey right)
            {
                var dependencyComparison = string.Compare(
                    left.DependencyHash,
                    right.DependencyHash,
                    StringComparison.Ordinal);
                if (dependencyComparison != 0) return dependencyComparison;

                var commonLength = Math.Min(
                    left.SiblingIndices.Count,
                    right.SiblingIndices.Count);
                for (var index = 0; index < commonLength; index++)
                {
                    var siblingComparison = left.SiblingIndices[index]
                        .CompareTo(right.SiblingIndices[index]);
                    if (siblingComparison != 0) return siblingComparison;
                }

                return left.SiblingIndices.Count.CompareTo(right.SiblingIndices.Count);
            }
        }

        private readonly struct ResolvedMasterSceneTarget
        {
            internal ResolvedMasterSceneTarget(
                GameObject sceneObject,
                string stableId,
                bool activeWhenOn)
            {
                SceneObject = sceneObject;
                StableId = stableId ?? string.Empty;
                ActiveWhenOn = activeWhenOn;
            }

            internal GameObject SceneObject { get; }
            internal string StableId { get; }
            internal bool ActiveWhenOn { get; }
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
