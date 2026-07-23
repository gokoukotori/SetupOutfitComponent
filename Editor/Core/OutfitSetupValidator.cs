using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Gokoukotori.SetupComponents.Editor
{
    internal interface IOutfitSetupValidator
    {
        ValidationResult Validate(OutfitSetupPlan plan);
    }

    internal sealed class OutfitSetupValidator : IOutfitSetupValidator
    {
        internal static readonly OutfitSetupValidator Instance = new OutfitSetupValidator();

        public ValidationResult Validate(OutfitSetupPlan plan)
        {
            var messages = new List<ValidationMessage>();
            if (plan == null)
            {
                AddError(messages, "PLAN_NULL", "生成計画がありません。");
                return new ValidationResult(messages);
            }

            ValidateEditorState(messages);
            ValidateSource(plan, messages);

            foreach (var error in plan.Analysis.Errors)
            {
                AddError(messages, "SOURCE_ANALYSIS", error);
            }

            foreach (var warning in plan.Analysis.Warnings)
            {
                AddWarning(messages, "SOURCE_WARNING", warning);
            }

            var avatar = ResolveSceneReference(plan.AvatarReference, "対象アバター", "AVATAR", messages);
            var placement = ResolveSceneReference(plan.PlacementReference, "配置先", "PLACEMENT", messages);
            if (avatar != null)
            {
                if (avatar.GetComponent<VRCAvatarDescriptor>() == null)
                {
                    AddError(messages, "AVATAR_DESCRIPTOR", "対象アバターのルートにVRCAvatarDescriptorがありません。");
                }

                if (!avatar.scene.isLoaded)
                {
                    AddError(messages, "AVATAR_SCENE", "対象アバターのSceneがロードされていません。");
                }
            }

            if (avatar != null && placement != null)
            {
                if (placement != avatar && !placement.transform.IsChildOf(avatar.transform))
                {
                    AddError(messages, "PLACEMENT_OUTSIDE_AVATAR", "配置先は対象アバター自身またはその子孫である必要があります。");
                }

                if (placement.scene != avatar.scene)
                {
                    AddError(messages, "PLACEMENT_SCENE", "対象アバターと配置先は同じSceneにある必要があります。");
                }

                ValidateOutput(plan, avatar, placement, messages);
                ValidateExclusions(plan, avatar, placement, messages);
            }

            ValidateParts(plan, messages);
            if (avatar != null)
            {
                ValidateBlendshapeSyncs(plan, avatar, messages);
            }
            if (!Enum.IsDefined(typeof(OutfitSetupMode), plan.SetupMode))
            {
                AddError(messages, "SETUP_MODE", "装着モードが不正です。");
            }

            return new ValidationResult(messages);
        }

        private static void ValidateEditorState(ICollection<ValidationMessage> messages)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                AddError(messages, "PLAY_MODE", "Play Mode中は生成できません。");
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                AddError(messages, "PREFAB_STAGE", "Prefab Mode中は生成できません。");
            }
        }

        private static void ValidateSource(OutfitSetupPlan plan, ICollection<ValidationMessage> messages)
        {
            if (plan.SourcePrefab == null)
            {
                AddError(messages, "SOURCE_MISSING", "衣装Prefabが見つかりません。");
                return;
            }

            var guidPath = AssetDatabase.GUIDToAssetPath(plan.SourceAssetGuid);
            if (!string.Equals(guidPath, plan.SourceAssetPath, StringComparison.Ordinal))
            {
                AddError(messages, "SOURCE_MOVED", "衣装PrefabのGUIDまたはパスが解析時から変わりました。再解析してください。");
            }

            var currentSource = AssetDatabase.LoadAssetAtPath<GameObject>(plan.SourceAssetPath);
            if (currentSource != plan.SourcePrefab)
            {
                AddError(messages, "SOURCE_REPLACED", "衣装Prefabが解析時のアセットと一致しません。再解析してください。");
            }

            if (string.IsNullOrEmpty(plan.SourceAssetPath)
                || !plan.SourceAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                AddError(messages, "SOURCE_NOT_PREFAB", "入力は.prefabアセットである必要があります。");
                return;
            }

            var currentHash = AssetDatabase.GetAssetDependencyHash(plan.SourceAssetPath).ToString();
            if (!string.Equals(currentHash, plan.DependencyHash, StringComparison.Ordinal))
            {
                AddError(messages, "SOURCE_CHANGED", "衣装Prefabまたは依存アセットが解析時から変更されました。再解析してください。");
            }
        }

        private static void ValidateOutput(
            OutfitSetupPlan plan,
            GameObject avatar,
            GameObject placement,
            ICollection<ValidationMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(plan.OutputName))
            {
                AddError(messages, "OUTPUT_NAME", "出力名を入力してください。");
            }
            else if (plan.OutputName.IndexOf('/') >= 0)
            {
                AddError(messages, "OUTPUT_NAME_SLASH", "出力名には'/'を使用できません。");
            }
            else if (placement.transform.Cast<Transform>()
                         .Any(child => string.Equals(child.name, plan.OutputName, StringComparison.Ordinal)))
            {
                AddError(messages, "OUTPUT_COLLISION", "配置先に同名のGameObjectがあります。");
            }

            if (string.IsNullOrWhiteSpace(plan.SubmenuLabel))
            {
                AddError(messages, "SUBMENU_LABEL", "SubMenu名を入力してください。");
            }

            if (string.IsNullOrWhiteSpace(plan.MasterToggleLabel))
            {
                AddError(messages, "MASTER_LABEL", "全体トグル名を入力してください。");
            }

            if (!plan.AllowDuplicate && ContainsPrefabInstance(avatar, plan.SourceAssetPath))
            {
                AddError(messages, "DUPLICATE_PREFAB",
                    "同じ衣装Prefabが対象アバター内に既に配置されています。重複配置を明示的に許可してください。");
            }
        }

        private static void ValidateExclusions(
            OutfitSetupPlan plan,
            GameObject avatar,
            GameObject placement,
            ICollection<ValidationMessage> messages)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in plan.ExclusionTargets)
            {
                var target = ResolveSceneReference(reference, "排他対象", "EXCLUSION", messages);
                if (target == null) continue;

                if (!seen.Add(reference.GlobalObjectId))
                {
                    AddError(messages, "EXCLUSION_DUPLICATE", "同じ排他対象が複数回指定されています。");
                }

                if (target == avatar || !target.transform.IsChildOf(avatar.transform))
                {
                    AddError(messages, "EXCLUSION_OUTSIDE_AVATAR", "排他対象は対象アバターの子孫である必要があります。");
                }

                if (placement == target || placement.transform.IsChildOf(target.transform))
                {
                    AddError(messages, "EXCLUSION_CONTAINS_OUTPUT",
                        "排他対象に配置先またはその祖先を指定することはできません。");
                }
            }
        }

        private static void ValidateParts(OutfitSetupPlan plan, ICollection<ValidationMessage> messages)
        {
            var allTargets = new List<PrefabTargetKey>();
            for (var partIndex = 0; partIndex < plan.PartToggles.Count; partIndex++)
            {
                var part = plan.PartToggles[partIndex];
                if (part == null)
                {
                    AddError(messages, "PART_NULL", $"個別項目{partIndex + 1}が不正です。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(part.Label))
                {
                    AddError(messages, "PART_LABEL", $"個別項目{partIndex + 1}の表示名を入力してください。");
                }

                if (part.Targets.Count == 0)
                {
                    AddError(messages, "PART_EMPTY", $"個別項目「{part.Label}」に対象がありません。");
                    continue;
                }

                foreach (var key in part.Targets)
                {
                    if (key.IsRoot)
                    {
                        AddError(messages, "PART_ROOT", "衣装Prefabのルート自体は個別パーツに指定できません。");
                    }

                    if (!string.Equals(key.DependencyHash, plan.DependencyHash, StringComparison.Ordinal)
                        || key.Resolve(plan.SourcePrefab, plan.DependencyHash) == null
                        || plan.Analysis.FindTarget(key) == null)
                    {
                        AddError(messages, "PART_STALE", $"個別項目「{part.Label}」の対象が解析時のPrefabと一致しません。");
                    }

                    if (allTargets.Contains(key))
                    {
                        AddError(messages, "PART_DUPLICATE_TARGET", "同じPrefab内オブジェクトが複数の個別項目に指定されています。");
                    }

                    allTargets.Add(key);
                }

                if (!part.TryGetEffectiveInitialOn(plan.Analysis, out _))
                {
                    AddError(messages, "PART_MIXED_INITIAL",
                        $"個別項目「{part.Label}」はPrefab状態とON時設定から初期状態を一意に決められません。初期ON/OFFを指定してください。");
                }
            }

            for (var left = 0; left < allTargets.Count; left++)
            {
                for (var right = left + 1; right < allTargets.Count; right++)
                {
                    if (allTargets[left].IsAncestorOf(allTargets[right])
                        || allTargets[right].IsAncestorOf(allTargets[left]))
                    {
                        AddError(messages, "PART_ANCESTOR_CONFLICT",
                            "個別パーツの対象に祖先・子孫関係のあるGameObjectを同時指定できません。");
                    }
                }
            }
        }

        private static void ValidateBlendshapeSyncs(
            OutfitSetupPlan plan,
            GameObject avatar,
            ICollection<ValidationMessage> messages)
        {
            var configuredLocalRenderers = new HashSet<PrefabTargetKey>();
            for (var syncIndex = 0; syncIndex < plan.BlendshapeSyncs.Count; syncIndex++)
            {
                var sync = plan.BlendshapeSyncs[syncIndex];
                if (sync == null)
                {
                    AddError(messages, "BLENDSYNC_NULL", $"Blendshape Sync設定{syncIndex + 1}が不正です。");
                    continue;
                }

                if (!configuredLocalRenderers.Add(sync.LocalRendererKey))
                {
                    AddError(messages, "BLENDSYNC_LOCAL_CONFLICT",
                        "同じ衣装Rendererに複数のBlendshape Sync設定を追加できません。");
                }

                GameObject localObject = null;
                if (!string.Equals(sync.LocalRendererKey.DependencyHash, plan.DependencyHash, StringComparison.Ordinal)
                    || (localObject = sync.LocalRendererKey.Resolve(plan.SourcePrefab, plan.DependencyHash)) == null
                    || plan.Analysis.FindBlendshapeRenderer(sync.LocalRendererKey) == null)
                {
                    AddError(messages, "BLENDSYNC_STALE",
                        "衣装Rendererが解析時のPrefabまたはdependency hashと一致しません。再解析してください。");
                }

                var localRenderer = localObject != null ? localObject.GetComponent<SkinnedMeshRenderer>() : null;
                if (localObject != null && localRenderer == null)
                {
                    AddError(messages, "BLENDSYNC_LOCAL_RENDERER",
                        "衣装側の同期先にSkinnedMeshRendererがありません。");
                }

                if (localObject != null
                    && (localObject.GetComponent<ModularAvatarBlendshapeSync>() != null
                        || plan.Analysis.FindBlendshapeRenderer(sync.LocalRendererKey)?.HasExistingBlendshapeSync == true))
                {
                    AddError(messages, "BLENDSYNC_EXISTING_COMPONENT",
                        "既存のMA Blendshape Syncがある衣装Rendererは保持され、新規設定を追加できません。");
                }

                var sourceObject = ResolveSceneReference(
                    sync.SourceRendererReference,
                    "Blendshape同期元Renderer",
                    "BLENDSYNC_SOURCE",
                    messages);
                SkinnedMeshRenderer sourceRenderer = null;
                if (sourceObject != null)
                {
                    if (sourceObject != avatar && !sourceObject.transform.IsChildOf(avatar.transform))
                    {
                        AddError(messages, "BLENDSYNC_SOURCE_OUTSIDE_AVATAR",
                            "Blendshape同期元Rendererは対象アバター自身またはその子孫である必要があります。");
                    }

                    sourceRenderer = sourceObject.GetComponent<SkinnedMeshRenderer>();
                    if (sourceRenderer == null)
                    {
                        AddError(messages, "BLENDSYNC_SOURCE_RENDERER",
                            "Blendshape同期元にSkinnedMeshRendererがありません。");
                    }
                }

                if (localRenderer != null && localRenderer.sharedMesh == null)
                {
                    AddError(messages, "BLENDSYNC_LOCAL_MESH", "衣装側SkinnedMeshRendererにMeshがありません。");
                }

                if (sourceRenderer != null && sourceRenderer.sharedMesh == null)
                {
                    AddError(messages, "BLENDSYNC_SOURCE_MESH", "同期元SkinnedMeshRendererにMeshがありません。");
                }

                if (sync.Mappings.Count == 0)
                {
                    AddError(messages, "BLENDSYNC_EMPTY_MAPPING",
                        "Blendshape Syncには1件以上のshape対応を指定してください。");
                    continue;
                }

                var sourceShapes = new HashSet<string>(StringComparer.Ordinal);
                var localShapes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var mapping in sync.Mappings)
                {
                    if (mapping == null)
                    {
                        AddError(messages, "BLENDSYNC_MAPPING_NULL", "Blendshape対応に不正な行があります。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(mapping.SourceShape))
                    {
                        AddError(messages, "BLENDSYNC_SOURCE_SHAPE_EMPTY", "同期元shape名を入力してください。");
                    }
                    else
                    {
                        if (!sourceShapes.Add(mapping.SourceShape))
                        {
                            AddError(messages, "BLENDSYNC_SOURCE_DUPLICATE",
                                "同じ同期元shapeを1つの衣装Rendererへ複数回割り当てることはできません。");
                        }

                        if (sourceRenderer != null
                            && sourceRenderer.sharedMesh != null
                            && sourceRenderer.sharedMesh.GetBlendShapeIndex(mapping.SourceShape) < 0)
                        {
                            AddError(messages, "BLENDSYNC_SOURCE_SHAPE",
                                $"同期元MeshにBlendShape「{mapping.SourceShape}」がありません。");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(mapping.LocalShape))
                    {
                        AddError(messages, "BLENDSYNC_LOCAL_SHAPE_EMPTY", "衣装側shape名を入力してください。");
                    }
                    else
                    {
                        if (!localShapes.Add(mapping.LocalShape))
                        {
                            AddError(messages, "BLENDSYNC_LOCAL_DUPLICATE",
                                "同じ衣装側shapeへ複数の同期元shapeを割り当てることはできません。");
                        }

                        if (localRenderer != null
                            && localRenderer.sharedMesh != null
                            && localRenderer.sharedMesh.GetBlendShapeIndex(mapping.LocalShape) < 0)
                        {
                            AddError(messages, "BLENDSYNC_LOCAL_SHAPE",
                                $"衣装MeshにBlendShape「{mapping.LocalShape}」がありません。");
                        }
                    }
                }
            }
        }

        private static GameObject ResolveSceneReference(
            SceneObjectReference reference,
            string displayName,
            string codePrefix,
            ICollection<ValidationMessage> messages)
        {
            if (reference == null)
            {
                AddError(messages, codePrefix + "_MISSING", displayName + "を選択してください。");
                return null;
            }

            var resolved = reference.Resolve();
            if (resolved == null || !resolved.scene.IsValid() || !resolved.scene.isLoaded)
            {
                AddError(messages, codePrefix + "_UNRESOLVED", displayName + "をGlobalObjectIdから再解決できません。");
                return null;
            }

            return resolved;
        }

        private static bool ContainsPrefabInstance(GameObject avatar, string prefabAssetPath)
        {
            foreach (var transform in avatar.GetComponentsInChildren<Transform>(true))
            {
                var candidate = transform.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate)) continue;
                var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate);
                if (string.Equals(path, prefabAssetPath, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static void AddError(ICollection<ValidationMessage> messages, string code, string message)
        {
            messages.Add(new ValidationMessage(code, message, ValidationSeverity.Error));
        }

        private static void AddWarning(ICollection<ValidationMessage> messages, string code, string message)
        {
            messages.Add(new ValidationMessage(code, message, ValidationSeverity.Warning));
        }
    }
}
