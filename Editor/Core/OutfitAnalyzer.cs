using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Gokoukotori.SetupComponents.Editor
{
    internal static class OutfitAnalyzer
    {
        internal static OutfitAnalysis Analyze(GameObject sourcePrefab)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var targets = new List<PrefabTargetInfo>();
            var partCandidates = new List<OutfitPartCandidate>();
            var blendshapeRenderers = new List<OutfitRendererInfo>();

            if (sourcePrefab == null)
            {
                errors.Add("衣装Prefabが選択されていません。");
                return new OutfitAnalysis(null, string.Empty, string.Empty, string.Empty, string.Empty,
                    targets, partCandidates, blendshapeRenderers, errors, warnings);
            }

            var assetPath = AssetDatabase.GetAssetPath(sourcePrefab);
            var assetGuid = string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
            var dependencyHash = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(assetPath).ToString();

            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || !EditorUtility.IsPersistent(sourcePrefab))
            {
                errors.Add("Project上の.prefabアセットを選択してください。");
            }
            else
            {
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainAsset != sourcePrefab)
                {
                    errors.Add("PrefabのルートGameObjectを選択してください。");
                }

                var assetType = PrefabUtility.GetPrefabAssetType(sourcePrefab);
                if (assetType != PrefabAssetType.Regular && assetType != PrefabAssetType.Variant)
                {
                    errors.Add("Regular PrefabまたはPrefab Variantだけを使用できます。");
                }
            }

            if (sourcePrefab.GetComponentInChildren<VRCAvatarDescriptor>(true) != null)
            {
                errors.Add("VRCAvatarDescriptorを含むPrefabは衣装として使用できません。");
            }

            var missingScriptCount = CountMissingScripts(sourcePrefab);
            if (missingScriptCount > 0)
            {
                errors.Add($"Prefab内にMissing Scriptが{missingScriptCount}件あります。");
            }

            if (sourcePrefab.GetComponentInChildren<ModularAvatarMenuItem>(true) != null
                || sourcePrefab.GetComponentInChildren<ModularAvatarMenuGroup>(true) != null
                || sourcePrefab.GetComponentInChildren<ModularAvatarObjectToggle>(true) != null)
            {
                warnings.Add("Prefabには既存のModular Avatarメニュー構成があります。生成される構成と併存します。");
            }

            if (sourcePrefab.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null)
            {
                warnings.Add("Prefabには既存のMA Merge Armatureがあります。自動モードでは有効な設定を優先します。");
            }

            if (errors.Count == 0)
            {
                CollectTargets(sourcePrefab, dependencyHash, targets, partCandidates, blendshapeRenderers);
                if (blendshapeRenderers.Any(renderer => renderer.HasExistingBlendshapeSync))
                {
                    warnings.Add("Prefabには既存のMA Blendshape Syncがあります。既存設定は保持され、新規設定の追加対象にはできません。");
                }
            }

            return new OutfitAnalysis(
                sourcePrefab,
                assetGuid,
                assetPath,
                dependencyHash,
                sourcePrefab.name,
                targets,
                partCandidates,
                blendshapeRenderers,
                errors,
                warnings);
        }

        internal static IReadOnlyList<VRCAvatarDescriptor> FindLoadedAvatars()
        {
            return Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>()
                .Where(descriptor => descriptor != null
                                     && !EditorUtility.IsPersistent(descriptor)
                                     && descriptor.gameObject.scene.IsValid()
                                     && descriptor.gameObject.scene.isLoaded)
                .OrderBy(descriptor => descriptor.gameObject.scene.path, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.gameObject.name, StringComparer.Ordinal)
                .ToArray();
        }

        private static int CountMissingScripts(GameObject root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Sum(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));
        }

        private static void CollectTargets(
            GameObject root,
            string dependencyHash,
            ICollection<PrefabTargetInfo> targets,
            ICollection<OutfitPartCandidate> partCandidates,
            ICollection<OutfitRendererInfo> blendshapeRenderers)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var targetKey = PrefabTargetKey.FromTransform(root.transform, transform, dependencyHash);
                var displayPath = transform == root.transform
                    ? root.name
                    : GetRelativePath(root.transform, transform);
                var skinnedMeshRenderer = transform.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRenderer != null)
                {
                    var mesh = skinnedMeshRenderer.sharedMesh;
                    var blendshapeNames = mesh == null
                        ? Array.Empty<string>()
                        : Enumerable.Range(0, mesh.blendShapeCount)
                            .Select(mesh.GetBlendShapeName)
                            .ToArray();
                    blendshapeRenderers.Add(new OutfitRendererInfo(
                        targetKey,
                        displayPath,
                        transform.name,
                        blendshapeNames,
                        transform.GetComponent<ModularAvatarBlendshapeSync>() != null));
                }

                if (transform == root.transform) continue;

                var isRendererCandidate = transform.GetComponent<Renderer>() != null;
                var info = new PrefabTargetInfo(
                    targetKey,
                    displayPath,
                    transform.name,
                    transform.gameObject.activeSelf,
                    isRendererCandidate,
                    targetKey.Depth);
                targets.Add(info);

                if (isRendererCandidate)
                {
                    partCandidates.Add(new OutfitPartCandidate(
                        targetKey,
                        displayPath,
                        transform.name,
                        transform.gameObject.activeSelf,
                        targetKey.Depth));
                }
            }
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
    }
}
