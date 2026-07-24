using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Gokoukotori.SetupOutfitComponent.Editor
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
                ValidateMasterSceneTargets(plan, avatar, placement, messages);
            }

            ValidateParts(plan, avatar, placement, messages);
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

        private static void ValidateMasterSceneTargets(
            OutfitSetupPlan plan,
            GameObject avatar,
            GameObject placement,
            ICollection<ValidationMessage> messages)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var resolvedTargets = new Dictionary<GameObject, MasterSceneTargetPlan>();
            foreach (var configuredTarget in plan.MasterSceneTargets)
            {
                if (configuredTarget == null)
                {
                    AddError(messages, "SCENE_TARGET_NULL", "Scene対象の設定が不正です。");
                    continue;
                }

                var reference = configuredTarget.Reference;
                var target = ResolveSceneReference(reference, "Scene対象", "SCENE_TARGET", messages);
                if (target == null) continue;

                if (!seen.Add(reference.GlobalObjectId))
                {
                    AddError(messages, "SCENE_TARGET_DUPLICATE", "同じScene対象が複数回指定されています。");
                }

                if (target == avatar || !target.transform.IsChildOf(avatar.transform))
                {
                    AddError(messages, "SCENE_TARGET_OUTSIDE_AVATAR",
                        "Scene対象は対象アバターの子孫である必要があります。");
                }

                if (placement == target || placement.transform.IsChildOf(target.transform))
                {
                    AddError(messages, "SCENE_TARGET_CONTAINS_OUTPUT",
                        "Scene対象に配置先またはその祖先を指定することはできません。");
                }

                resolvedTargets[target] = configuredTarget;
            }

            ValidateMasterSceneTargetVisibility(avatar, resolvedTargets, messages);
        }

        private static void ValidateMasterSceneTargetVisibility(
            GameObject avatar,
            IReadOnlyDictionary<GameObject, MasterSceneTargetPlan> configuredTargets,
            ICollection<ValidationMessage> messages)
        {
            if (avatar == null) return;

            foreach (var pair in configuredTargets)
            {
                if (pair.Key == null || pair.Value == null || !pair.Value.ActiveWhenOn) continue;

                var ancestor = pair.Key.transform.parent;
                while (ancestor != null)
                {
                    if (configuredTargets.TryGetValue(ancestor.gameObject, out var ancestorTarget))
                    {
                        if (!ancestorTarget.ActiveWhenOn)
                        {
                            AddWarning(messages, "SCENE_TARGET_HIDDEN_BY_MASTER_ANCESTOR",
                                $"Scene対象「{pair.Key.name}」を表示にしても、非表示に設定された祖先「{ancestor.name}」のため表示されません。");
                            break;
                        }
                    }
                    else if (!ancestor.gameObject.activeSelf)
                    {
                        AddWarning(messages, "SCENE_TARGET_INACTIVE_ANCESTOR",
                            $"Scene対象「{pair.Key.name}」を表示にしても、制御対象外の非アクティブな祖先「{ancestor.name}」のため表示されません。");
                        break;
                    }

                    if (ancestor.gameObject == avatar) break;
                    ancestor = ancestor.parent;
                }
            }
        }
        private static void ValidateParts(
            OutfitSetupPlan plan,
            GameObject avatar,
            GameObject placement,
            ICollection<ValidationMessage> messages)
        {
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            var prefabTargets = new List<PrefabTargetKey>();
            var sceneTargets = new List<GameObject>();
            var masterSceneTargetIds = new HashSet<string>(
                plan.MasterSceneTargets
                    .Where(target => target?.Reference != null)
                    .Select(target => target.Reference.GlobalObjectId),
                StringComparer.Ordinal);
            for (var partIndex = 0; partIndex < plan.PartToggles.Count; partIndex++)
            {
                var part = plan.PartToggles[partIndex];
                if (part == null)
                {
                    AddError(messages, "PART_NULL", $"個別項目{partIndex + 1}が不正です。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(part.ItemId))
                {
                    AddError(messages, "PART_ITEM_ID", $"個別項目{partIndex + 1}の識別子がありません。");
                }
                else if (!itemIds.Add(part.ItemId))
                {
                    AddError(messages, "PART_ITEM_ID_DUPLICATE", "個別項目の識別子が重複しています。再作成してください。");
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

                var partStableIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var target in part.Targets)
                {
                    if (target == null || string.IsNullOrEmpty(target.StableId))
                    {
                        AddError(messages, "PART_TARGET_INVALID",
                            $"個別項目「{part.Label}」の対象が不正です。");
                        continue;
                    }

                    var isFirstInPart = partStableIds.Add(target.StableId);
                    if (!isFirstInPart)
                    {
                        AddError(messages, "PART_TARGET_DUPLICATE",
                            target.Source == PartTargetSource.OutfitPrefab
                                ? "同じPrefab内オブジェクトが同一の個別項目へ複数回指定されています。"
                                : "同じSceneオブジェクトが同一の個別項目へ複数回指定されています。");
                    }

                    if (target.Source == PartTargetSource.OutfitPrefab)
                    {
                        var key = target.PrefabKey;
                        if (key.IsRoot)
                        {
                            AddError(messages, "PART_ROOT",
                                "衣装Prefabのルート自体は個別パーツに指定できません。");
                        }

                        if (!string.Equals(
                                key.DependencyHash,
                                plan.DependencyHash,
                                StringComparison.Ordinal)
                            || key.Resolve(plan.SourcePrefab, plan.DependencyHash) == null
                            || plan.Analysis.FindTarget(key) == null)
                        {
                            AddError(messages, "PART_STALE",
                                $"個別項目「{part.Label}」の対象が解析時のPrefabと一致しません。");
                        }

                        if (isFirstInPart) prefabTargets.Add(key);
                        continue;
                    }

                    if (target.Source != PartTargetSource.SceneObject)
                    {
                        AddError(messages, "PART_TARGET_INVALID",
                            $"個別項目「{part.Label}」の対象種別が不正です。");
                        continue;
                    }

                    if (target.SceneReference == null
                        || string.IsNullOrEmpty(target.SceneReference.GlobalObjectId))
                    {
                        AddError(messages, "PART_SCENE_REFERENCE_MISSING",
                            $"個別項目「{part.Label}」のScene対象参照がありません。");
                        continue;
                    }

                    if (!masterSceneTargetIds.Contains(target.SceneReference.GlobalObjectId))
                    {
                        AddError(messages, "PART_SCENE_NOT_IN_MASTER_TARGETS",
                            $"個別項目「{part.Label}」のScene対象はステップ3のScene対象にも存在する必要があります。");
                    }

                    var sceneTarget = target.SceneReference.Resolve();
                    if (sceneTarget == null
                        || !sceneTarget.scene.IsValid()
                        || !sceneTarget.scene.isLoaded
                        || EditorUtility.IsPersistent(sceneTarget))
                    {
                        AddError(messages, "PART_SCENE_REFERENCE_UNRESOLVED",
                            $"個別項目「{part.Label}」のScene対象をGlobalObjectIdから再解決できません。");
                        continue;
                    }

                    if (avatar == null
                        || (sceneTarget != avatar
                            && !sceneTarget.transform.IsChildOf(avatar.transform)))
                    {
                        AddError(messages, "PART_SCENE_OUTSIDE_AVATAR",
                            "個別パーツのScene対象は対象アバターの子孫である必要があります。");
                    }
                    else if (sceneTarget == avatar)
                    {
                        AddError(messages, "PART_SCENE_AVATAR_ROOT",
                            "対象アバターのRootは個別パーツのScene対象に指定できません。");
                    }

                    if (placement != null
                        && (placement == sceneTarget
                            || placement.transform.IsChildOf(sceneTarget.transform)))
                    {
                        AddError(messages, "PART_SCENE_PLACEMENT_CONFLICT",
                            "個別パーツのScene対象に配置先またはその祖先を指定できません。");
                    }

                    if (isFirstInPart) sceneTargets.Add(sceneTarget);
                }

            }

            var uniquePrefabTargets = prefabTargets.Distinct().ToArray();
            for (var left = 0; left < uniquePrefabTargets.Length; left++)
            {
                for (var right = left + 1; right < uniquePrefabTargets.Length; right++)
                {
                    if (uniquePrefabTargets[left].IsAncestorOf(uniquePrefabTargets[right])
                        || uniquePrefabTargets[right].IsAncestorOf(uniquePrefabTargets[left]))
                    {
                        AddError(messages, "PART_ANCESTOR_CONFLICT",
                            "個別パーツの対象に祖先・子孫関係のあるGameObjectを同時指定できません。");
                    }
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

                    AddError(messages, "PART_SCENE_ANCESTOR_CONFLICT",
                        "個別パーツのScene対象に祖先・子孫関係のあるGameObjectを同時指定できません。");
                }
            }

            var allSceneTargets = plan.MasterSceneTargets
                .Where(target => target?.Reference != null)
                .Select(target => target.Reference.Resolve())
                .Where(target => target != null)
                .Concat(uniqueSceneTargets)
                .Distinct()
                .ToArray();
            ValidateExistingObjectToggleConflicts(avatar, allSceneTargets, messages);
        }

        private static void ValidateExistingObjectToggleConflicts(
            GameObject avatar,
            IReadOnlyList<GameObject> sceneTargets,
            ICollection<ValidationMessage> messages)
        {
            if (avatar == null || sceneTargets.Count == 0) return;

            foreach (var toggle in avatar.GetComponentsInChildren<ModularAvatarObjectToggle>(true))
            {
                if (toggle == null || toggle.Objects == null) continue;
                foreach (var configured in toggle.Objects)
                {
                    var controlled = configured.Object?.Get(toggle);
                    if (controlled == null) continue;
                    if (!sceneTargets.Any(target =>
                            target == controlled
                            || target.transform.IsChildOf(controlled.transform)
                            || controlled.transform.IsChildOf(target.transform)))
                    {
                        continue;
                    }

                    AddWarning(messages, "SCENE_TARGET_EXISTING_MA_CONFLICT",
                        "Scene対象は既存のMA Object Toggleと同一または祖先・子孫関係で競合します。最終結果はHierarchy順に依存します。");
                    return;
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
