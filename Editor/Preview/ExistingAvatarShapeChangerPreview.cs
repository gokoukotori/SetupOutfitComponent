using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal readonly struct ShapeChangerHierarchyOrder :
        IComparable<ShapeChangerHierarchyOrder>,
        IEquatable<ShapeChangerHierarchyOrder>
    {
        internal ShapeChangerHierarchyOrder(
            IEnumerable<int> siblingIndices,
            int componentIndex,
            int entryIndex)
        {
            SiblingIndices = (siblingIndices ?? Enumerable.Empty<int>())
                .ToImmutableArray();
            ComponentIndex = componentIndex;
            EntryIndex = entryIndex;
        }

        internal ImmutableArray<int> SiblingIndices { get; }
        internal int ComponentIndex { get; }
        internal int EntryIndex { get; }

        public int CompareTo(ShapeChangerHierarchyOrder other)
        {
            var commonLength = Math.Min(SiblingIndices.Length, other.SiblingIndices.Length);
            for (var index = 0; index < commonLength; index++)
            {
                var comparison = SiblingIndices[index]
                    .CompareTo(other.SiblingIndices[index]);
                if (comparison != 0) return comparison;
            }

            var depthComparison = SiblingIndices.Length
                .CompareTo(other.SiblingIndices.Length);
            if (depthComparison != 0) return depthComparison;

            var componentComparison = ComponentIndex.CompareTo(other.ComponentIndex);
            return componentComparison != 0
                ? componentComparison
                : EntryIndex.CompareTo(other.EntryIndex);
        }

        public bool Equals(ShapeChangerHierarchyOrder other)
        {
            return ComponentIndex == other.ComponentIndex
                   && EntryIndex == other.EntryIndex
                   && SiblingIndices.SequenceEqual(other.SiblingIndices);
        }

        public override bool Equals(object obj)
        {
            return obj is ShapeChangerHierarchyOrder other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ComponentIndex;
                hashCode = (hashCode * 397) ^ EntryIndex;
                foreach (var siblingIndex in SiblingIndices)
                    hashCode = (hashCode * 397) ^ siblingIndex;
                return hashCode;
            }
        }

        internal static ShapeChangerHierarchyOrder ForScene(
            Transform avatarRoot,
            Transform owner,
            int componentIndex,
            int entryIndex)
        {
            return new ShapeChangerHierarchyOrder(
                CaptureSiblingIndices(avatarRoot, owner),
                componentIndex,
                entryIndex);
        }

        internal static ShapeChangerHierarchyOrder ForGeneratedMaster(
            Transform avatarRoot,
            Transform placement,
            int entryIndex)
        {
            return ForGenerated(
                avatarRoot,
                placement,
                new[] { placement.childCount, 0 },
                0,
                entryIndex);
        }

        internal static ShapeChangerHierarchyOrder ForGeneratedOutfitOwner(
            Transform avatarRoot,
            Transform placement,
            PrefabTargetKey ownerKey,
            int entryIndex)
        {
            return ForGenerated(
                avatarRoot,
                placement,
                new[] { placement.childCount, 1 }
                    .Concat(ownerKey.SiblingIndices),
                int.MaxValue,
                entryIndex);
        }

        internal static ShapeChangerHierarchyOrder ForGeneratedPart(
            Transform avatarRoot,
            Transform placement,
            int sourcePrefabChildCount,
            int partIndex,
            int entryIndex)
        {
            return ForGenerated(
                avatarRoot,
                placement,
                new[]
                {
                    placement.childCount,
                    1,
                    sourcePrefabChildCount,
                    partIndex,
                },
                0,
                entryIndex);
        }

        private static ShapeChangerHierarchyOrder ForGenerated(
            Transform avatarRoot,
            Transform placement,
            IEnumerable<int> relativePath,
            int componentIndex,
            int entryIndex)
        {
            return new ShapeChangerHierarchyOrder(
                CaptureSiblingIndices(avatarRoot, placement).Concat(relativePath),
                componentIndex,
                entryIndex);
        }

        private static IEnumerable<int> CaptureSiblingIndices(
            Transform root,
            Transform target)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target != root && !target.IsChildOf(root))
            {
                throw new ArgumentException(
                    "Shape Changer owner must be inside the selected avatar.",
                    nameof(target));
            }

            var reversed = new List<int>();
            var current = target;
            while (current != root)
            {
                reversed.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            reversed.Reverse();
            return reversed;
        }
    }

    internal readonly struct ExistingAvatarShapeChangePreviewSnapshot :
        IEquatable<ExistingAvatarShapeChangePreviewSnapshot>
    {
        internal ExistingAvatarShapeChangePreviewSnapshot(
            GameObject owner,
            string ownerStableId,
            SkinnedMeshRenderer renderer,
            string rendererStableId,
            string shapeName,
            float value,
            bool inverted,
            bool hasMenuCondition,
            bool menuInitiallyActive,
            ShapeChangerHierarchyOrder hierarchyOrder)
        {
            Owner = owner;
            OwnerStableId = ownerStableId ?? string.Empty;
            Renderer = renderer;
            RendererStableId = rendererStableId ?? string.Empty;
            ShapeName = shapeName ?? string.Empty;
            Value = value;
            Inverted = inverted;
            HasMenuCondition = hasMenuCondition;
            MenuInitiallyActive = menuInitiallyActive;
            HierarchyOrder = hierarchyOrder;
        }

        internal GameObject Owner { get; }
        internal string OwnerStableId { get; }
        internal SkinnedMeshRenderer Renderer { get; }
        internal string RendererStableId { get; }
        internal string ShapeName { get; }
        internal float Value { get; }
        internal bool Inverted { get; }
        internal bool HasMenuCondition { get; }
        internal bool MenuInitiallyActive { get; }
        internal ShapeChangerHierarchyOrder HierarchyOrder { get; }

        public bool Equals(ExistingAvatarShapeChangePreviewSnapshot other)
        {
            return Owner == other.Owner
                   && string.Equals(
                       OwnerStableId,
                       other.OwnerStableId,
                       StringComparison.Ordinal)
                   && Renderer == other.Renderer
                   && string.Equals(
                       RendererStableId,
                       other.RendererStableId,
                       StringComparison.Ordinal)
                   && string.Equals(ShapeName, other.ShapeName, StringComparison.Ordinal)
                   && Value.Equals(other.Value)
                   && Inverted == other.Inverted
                   && HasMenuCondition == other.HasMenuCondition
                   && MenuInitiallyActive == other.MenuInitiallyActive
                   && HierarchyOrder.Equals(other.HierarchyOrder);
        }

        public override bool Equals(object obj)
        {
            return obj is ExistingAvatarShapeChangePreviewSnapshot other
                   && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Owner != null ? Owner.GetInstanceID() : 0;
                hashCode = (hashCode * 397)
                           ^ StringComparer.Ordinal.GetHashCode(OwnerStableId);
                hashCode = (hashCode * 397)
                           ^ (Renderer != null ? Renderer.GetInstanceID() : 0);
                hashCode = (hashCode * 397)
                           ^ StringComparer.Ordinal.GetHashCode(RendererStableId);
                hashCode = (hashCode * 397)
                           ^ StringComparer.Ordinal.GetHashCode(ShapeName);
                hashCode = (hashCode * 397) ^ Value.GetHashCode();
                hashCode = (hashCode * 397) ^ Inverted.GetHashCode();
                hashCode = (hashCode * 397) ^ HasMenuCondition.GetHashCode();
                hashCode = (hashCode * 397) ^ MenuInitiallyActive.GetHashCode();
                return (hashCode * 397) ^ HierarchyOrder.GetHashCode();
            }
        }
    }

    internal sealed class ExistingAvatarShapeChangerPreviewAnalysis
    {
        internal ExistingAvatarShapeChangerPreviewAnalysis(
            ImmutableArray<ExistingAvatarShapeChangePreviewSnapshot> sets,
            ImmutableArray<string> warnings,
            int deleteCount,
            int skippedCount)
        {
            Sets = sets.IsDefault
                ? ImmutableArray<ExistingAvatarShapeChangePreviewSnapshot>.Empty
                : sets;
            Warnings = warnings.IsDefault
                ? ImmutableArray<string>.Empty
                : warnings;
            DeleteCount = deleteCount;
            SkippedCount = skippedCount;
        }

        internal ImmutableArray<ExistingAvatarShapeChangePreviewSnapshot> Sets { get; }
        internal ImmutableArray<string> Warnings { get; }
        internal int DeleteCount { get; }
        internal int SkippedCount { get; }
    }

    internal static class ExistingAvatarShapeChangerPreviewAnalyzer
    {
        internal static ExistingAvatarShapeChangerPreviewAnalysis Analyze(
            GameObject avatarRoot)
        {
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            var sets =
                ImmutableArray.CreateBuilder<ExistingAvatarShapeChangePreviewSnapshot>();
            var skippedCount = 0;
            var deleteCount = 0;
            foreach (var changer in avatarRoot
                         .GetComponentsInChildren<ModularAvatarShapeChanger>(true))
            {
                if (changer == null) continue;
                var components = changer.gameObject
                    .GetComponents<ModularAvatarShapeChanger>();
                var componentIndex = Array.IndexOf(components, changer);
                var ownerStableId = GetStableId(changer.gameObject);
                ResolveMenuInitialCondition(
                    avatarRoot.transform,
                    changer.transform,
                    out var hasMenuCondition,
                    out var menuInitiallyActive);
                var shapes = changer.Shapes;
                if (shapes == null) continue;

                for (var entryIndex = 0; entryIndex < shapes.Count; entryIndex++)
                {
                    var shape = shapes[entryIndex];
                    if (shape == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (shape.ChangeType == ShapeChangeType.Delete)
                    {
                        deleteCount++;
                        continue;
                    }

                    if (shape.ChangeType != ShapeChangeType.Set
                        || shape.Object == null
                        || string.IsNullOrWhiteSpace(shape.ShapeName)
                        || float.IsNaN(shape.Value)
                        || float.IsInfinity(shape.Value))
                    {
                        skippedCount++;
                        continue;
                    }

                    GameObject target;
                    try
                    {
                        target = shape.Object.Get(changer);
                    }
                    catch
                    {
                        skippedCount++;
                        continue;
                    }

                    var renderer = target != null
                        ? target.GetComponent<SkinnedMeshRenderer>()
                        : null;
                    if (renderer == null
                        || renderer.sharedMesh == null
                        || renderer.sharedMesh.GetBlendShapeIndex(shape.ShapeName) < 0
                        || (target != avatarRoot
                            && !target.transform.IsChildOf(avatarRoot.transform)))
                    {
                        skippedCount++;
                        continue;
                    }

                    sets.Add(new ExistingAvatarShapeChangePreviewSnapshot(
                        changer.gameObject,
                        ownerStableId,
                        renderer,
                        GetStableId(renderer.gameObject),
                        shape.ShapeName,
                        Mathf.Clamp(shape.Value, 0f, 100f),
                        changer.Inverted,
                        hasMenuCondition,
                        menuInitiallyActive,
                        ShapeChangerHierarchyOrder.ForScene(
                            avatarRoot.transform,
                            changer.transform,
                            componentIndex,
                            entryIndex)));
                }
            }

            var warnings = ImmutableArray.CreateBuilder<string>();
            if (skippedCount > 0)
            {
                warnings.Add(
                    $"既存アバターShape Changerの解決できないSet設定{skippedCount}件をプレビューから除外しています。");
            }

            if (deleteCount > 0)
            {
                warnings.Add(
                    $"既存アバターShape ChangerのDelete設定{deleteCount}件は、通常のNDMF／MAプレビューが有効な場合の現在状態だけを表示します。専用プレビューの一時ON/OFFには追従しません。");
            }

            return new ExistingAvatarShapeChangerPreviewAnalysis(
                sets.ToImmutable(),
                warnings.ToImmutable(),
                deleteCount,
                skippedCount);
        }

        private static void ResolveMenuInitialCondition(
            Transform avatarRoot,
            Transform owner,
            out bool hasMenuCondition,
            out bool menuInitiallyActive)
        {
            hasMenuCondition = false;
            menuInitiallyActive = true;
            var current = owner;
            while (current != null && current != avatarRoot)
            {
                var menuItem = current.GetComponent<ModularAvatarMenuItem>();
                if (menuItem != null)
                {
                    var type = menuItem.PortableControl?.Type;
                    hasMenuCondition =
                        type == PortableControlType.Toggle
                        || type == PortableControlType.Button;
                    menuInitiallyActive = !hasMenuCondition || menuItem.isDefault;
                    return;
                }

                current = current.parent;
            }
        }

        private static string GetStableId(UnityEngine.Object target)
        {
            if (target == null) return string.Empty;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(target);
            return id + "#" + target.GetInstanceID();
        }
    }
}
