using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitSceneGenerator
    {
        private const string UndoName = "衣装セットアップを生成";

        private readonly IOutfitSetupAdapter _setupAdapter;
        private readonly IOutfitSetupValidator _validator;

        internal OutfitSceneGenerator()
            : this(ModularAvatarOutfitSetupAdapter.Instance, OutfitSetupValidator.Instance)
        {
        }

        internal OutfitSceneGenerator(
            IOutfitSetupAdapter setupAdapter,
            IOutfitSetupValidator validator)
        {
            _setupAdapter = setupAdapter ?? throw new ArgumentNullException(nameof(setupAdapter));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        internal GameObject Generate(OutfitSetupPlan plan)
        {
            var validation = _validator.Validate(plan);
            if (!validation.IsValid) throw new OutfitValidationException(validation);

            var avatar = plan.AvatarReference.Resolve();
            var placement = plan.PlacementReference.Resolve();
            var exclusions = plan.ExclusionTargets.Select(reference => reference.Resolve()).ToArray();
            var sourceHashBefore = AssetDatabase.GetAssetDependencyHash(plan.SourceAssetPath).ToString();

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            try
            {
                var generatedRoot = CreateChild(plan.OutputName, placement.transform);
                var submenu = Undo.AddComponent<ModularAvatarMenuItem>(generatedRoot);
                InitializeMenuItem(submenu, plan.SubmenuLabel, PortableControlType.SubMenu, false);

                var reservedMasterNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    plan.SourcePrefab.name,
                };
                var masterObjectName = MakeUniqueChildName(
                    generatedRoot.transform,
                    NormalizeObjectName(plan.MasterToggleLabel, "ON"),
                    reservedMasterNames);
                var masterObject = CreateChild(masterObjectName, generatedRoot.transform);
                var masterMenuItem = Undo.AddComponent<ModularAvatarMenuItem>(masterObject);
                InitializeMenuItem(masterMenuItem, plan.MasterToggleLabel, PortableControlType.Toggle,
                    plan.MasterDefaultOn);
                var masterToggle = Undo.AddComponent<ModularAvatarObjectToggle>(masterObject);

                var outfitInstance = InstantiatePrefab(plan.SourcePrefab, generatedRoot.transform);
                _setupAdapter.Configure(outfitInstance, avatar, plan.SetupMode);

                var partBindings = ResolvePartBindings(plan, outfitInstance);
                var blendshapeBindings = ResolveBlendshapeBindings(plan, outfitInstance);
                var partToggleComponents = new List<PartToggleComponentBinding>();
                if (partBindings.Count > 0)
                {
                    Undo.AddComponent<ModularAvatarMenuGroup>(outfitInstance);
                    var menuRootName = MakeUniqueChildName(outfitInstance.transform, "メニュー", null);
                    var menuRoot = CreateChild(menuRootName, outfitInstance.transform);
                    Undo.AddComponent<ModularAvatarMenuGroup>(menuRoot);

                    foreach (var binding in partBindings)
                    {
                        var itemObjectName = MakeUniqueChildName(
                            menuRoot.transform,
                            NormalizeObjectName(binding.Plan.Label, "項目"),
                            null);
                        var itemObject = CreateChild(itemObjectName, menuRoot.transform);
                        var item = Undo.AddComponent<ModularAvatarMenuItem>(itemObject);
                        InitializeMenuItem(item, binding.Plan.Label, PortableControlType.Toggle, binding.InitialOn);
                        var toggle = Undo.AddComponent<ModularAvatarObjectToggle>(itemObject);
                        partToggleComponents.Add(new PartToggleComponentBinding(toggle, binding.Targets));
                    }
                }

                var blendshapeSyncComponents = CreateBlendshapeSyncComponents(blendshapeBindings);

                SetPrefabInstanceActive(outfitInstance, false);
                foreach (var binding in partBindings)
                {
                    foreach (var target in binding.Targets)
                    {
                        SetPrefabInstanceActive(target.GameObject, !target.ActiveWhenOn);
                    }
                }

                masterToggle.Objects = new List<ToggledObject>
                {
                    CreateToggledObject(outfitInstance, true),
                };
                foreach (var exclusion in exclusions)
                {
                    masterToggle.Objects.Add(CreateToggledObject(exclusion, false));
                }

                foreach (var binding in partToggleComponents)
                {
                    binding.Component.Objects = binding.Targets
                        .Select(target => CreateToggledObject(target.GameObject, target.ActiveWhenOn))
                        .ToList();
                }

                ValidateGeneratedResult(plan, avatar, generatedRoot, outfitInstance, masterToggle,
                    partToggleComponents, blendshapeSyncComponents, sourceHashBefore);

                Undo.SetCurrentGroupName(UndoName);
                Undo.CollapseUndoOperations(undoGroup);
                EditorSceneManager.MarkSceneDirty(generatedRoot.scene);
                Selection.activeGameObject = generatedRoot;
                return generatedRoot;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (exception is OutfitGenerationException) throw;
                throw new OutfitGenerationException("衣装セットアップの生成に失敗したため、変更を元に戻しました。", exception);
            }
        }

        private static GameObject InstantiatePrefab(GameObject sourcePrefab, Transform parent)
        {
            var instance = PrefabUtility.InstantiatePrefab(sourcePrefab, parent) as GameObject;
            if (instance == null)
            {
                throw new OutfitGenerationException("衣装Prefabをシーンに配置できませんでした。");
            }

            Undo.RegisterCreatedObjectUndo(instance, UndoName);
            return instance;
        }

        private static List<ResolvedPartBinding> ResolvePartBindings(
            OutfitSetupPlan plan,
            GameObject outfitInstance)
        {
            var result = new List<ResolvedPartBinding>();
            foreach (var part in plan.PartToggles)
            {
                if (!part.TryGetEffectiveInitialOn(plan.Analysis, out var initialOn))
                {
                    throw new OutfitGenerationException($"個別項目「{part.Label}」の初期状態を確定できません。");
                }

                var targets = part.Targets
                    .Select(key => new ResolvedPartTarget(
                        key.Resolve(outfitInstance, plan.DependencyHash),
                        part.GetTargetActiveWhenOn(key)))
                    .ToArray();
                if (targets.Any(target => target.GameObject == null))
                {
                    throw new OutfitGenerationException($"個別項目「{part.Label}」の対象を生成インスタンス上で解決できません。");
                }

                result.Add(new ResolvedPartBinding(part, initialOn, targets));
            }

            return result;
        }

        private static List<ResolvedBlendshapeBinding> ResolveBlendshapeBindings(
            OutfitSetupPlan plan,
            GameObject outfitInstance)
        {
            var result = new List<ResolvedBlendshapeBinding>();
            foreach (var sync in plan.BlendshapeSyncs)
            {
                if (sync == null)
                {
                    throw new OutfitGenerationException("Blendshape Sync設定を解決できません。");
                }

                var localObject = sync.LocalRendererKey.Resolve(outfitInstance, plan.DependencyHash);
                var localRenderer = localObject != null ? localObject.GetComponent<SkinnedMeshRenderer>() : null;
                if (localRenderer == null || localRenderer.sharedMesh == null)
                {
                    throw new OutfitGenerationException("衣装側SkinnedMeshRendererまたはMeshを解決できません。");
                }

                if (localObject.GetComponent<ModularAvatarBlendshapeSync>() != null)
                {
                    throw new OutfitGenerationException(
                        "既存のMA Blendshape Syncがある衣装Rendererへ新規設定を追加できません。");
                }

                var sourceObject = sync.SourceRendererReference?.Resolve();
                var sourceRenderer = sourceObject != null ? sourceObject.GetComponent<SkinnedMeshRenderer>() : null;
                if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
                {
                    throw new OutfitGenerationException("同期元SkinnedMeshRendererまたはMeshを解決できません。");
                }

                if (sync.Mappings.Count == 0)
                {
                    throw new OutfitGenerationException("Blendshape Syncにshape対応がありません。");
                }

                result.Add(new ResolvedBlendshapeBinding(sync, localRenderer, sourceRenderer));
            }

            return result;
        }

        private static List<BlendshapeSyncComponentBinding> CreateBlendshapeSyncComponents(
            IEnumerable<ResolvedBlendshapeBinding> bindings)
        {
            var result = new List<BlendshapeSyncComponentBinding>();
            foreach (var binding in bindings)
            {
                var component = Undo.AddComponent<ModularAvatarBlendshapeSync>(binding.LocalRenderer.gameObject);
                Undo.RecordObject(component, UndoName);
                component.Bindings = binding.Plan.Mappings.Select(mapping =>
                {
                    var reference = new AvatarObjectReference();
                    reference.Set(binding.SourceRenderer.gameObject);
                    return new BlendshapeBinding
                    {
                        ReferenceMesh = reference,
                        Blendshape = mapping.SourceShape,
                        LocalBlendshape = mapping.LocalShape,
                        RemapCurve = AnimationCurve.Linear(0f, 0f, 100f, 100f),
                    };
                }).ToList();
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }

                result.Add(new BlendshapeSyncComponentBinding(
                    component,
                    binding.LocalRenderer,
                    binding.SourceRenderer,
                    binding.Plan.Mappings.ToArray()));
            }

            return result;
        }

        private static void InitializeMenuItem(
            ModularAvatarMenuItem item,
            string label,
            PortableControlType controlType,
            bool isDefault)
        {
            item.Control = new VRCExpressionsMenu.Control
            {
                type = controlType.ToVRCSDK(),
                parameter = new VRCExpressionsMenu.Control.Parameter { name = string.Empty },
                value = 1f,
                subMenu = null,
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
                icon = null,
            };
            item.MenuSource = SubmenuSource.Children;
            item.menuSource_otherObjectChildren = null;
            item.label = label ?? string.Empty;
            item.isSynced = true;
            item.isSaved = true;
            item.isDefault = isDefault;
            item.automaticValue = true;
        }

        private static ToggledObject CreateToggledObject(GameObject target, bool active)
        {
            if (target == null) throw new OutfitGenerationException("Toggle対象を解決できませんでした。");
            var reference = new AvatarObjectReference();
            reference.Set(target);
            return new ToggledObject
            {
                Object = reference,
                Active = active,
            };
        }

        private static void SetPrefabInstanceActive(GameObject target, bool active)
        {
            if (target.activeSelf == active) return;
            Undo.RecordObject(target, UndoName);
            target.SetActive(active);
            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(gameObject, UndoName);
            Undo.SetTransformParent(gameObject.transform, parent, UndoName);
            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = Vector3.one;
            return gameObject;
        }

        private static string NormalizeObjectName(string value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return normalized.Replace('/', '／');
        }

        private static string MakeUniqueChildName(
            Transform parent,
            string preferred,
            ISet<string> reservedNames)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform child in parent)
            {
                usedNames.Add(child.name);
            }

            if (reservedNames != null) usedNames.UnionWith(reservedNames);
            if (!usedNames.Contains(preferred)) return preferred;

            for (var suffix = 2;; suffix++)
            {
                var candidate = preferred + " (" + suffix + ")";
                if (!usedNames.Contains(candidate)) return candidate;
            }
        }

        private static void ValidateGeneratedResult(
            OutfitSetupPlan plan,
            GameObject avatar,
            GameObject generatedRoot,
            GameObject outfitInstance,
            ModularAvatarObjectToggle masterToggle,
            IReadOnlyList<PartToggleComponentBinding> partToggles,
            IReadOnlyList<BlendshapeSyncComponentBinding> blendshapeSyncs,
            string sourceHashBefore)
        {
            if (generatedRoot == null || generatedRoot.transform.parent == null)
            {
                throw new OutfitGenerationException("生成ルートがシーン階層にありません。");
            }

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outfitInstance);
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(outfitInstance)
                || !string.Equals(prefabPath, plan.SourceAssetPath, StringComparison.Ordinal))
            {
                throw new OutfitGenerationException("元衣装Prefabとの接続を維持できませんでした。");
            }

            ValidateToggleReferences(masterToggle, avatar);
            foreach (var partToggle in partToggles)
            {
                ValidateToggleReferences(partToggle.Component, avatar);
            }

            foreach (var blendshapeSync in blendshapeSyncs)
            {
                ValidateBlendshapeSync(blendshapeSync, avatar);
            }

            var sourceHashAfter = AssetDatabase.GetAssetDependencyHash(plan.SourceAssetPath).ToString();
            if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal)
                || !string.Equals(sourceHashAfter, plan.DependencyHash, StringComparison.Ordinal))
            {
                throw new OutfitGenerationException("生成中に元衣装Prefabまたは依存アセットが変更されました。");
            }
        }

        private static void ValidateToggleReferences(ModularAvatarObjectToggle toggle, GameObject avatar)
        {
            if (toggle == null || toggle.Objects == null || toggle.Objects.Count == 0)
            {
                throw new OutfitGenerationException("MA Object Toggleの対象がありません。");
            }

            foreach (var toggledObject in toggle.Objects)
            {
                var resolved = toggledObject.Object?.Get(toggle);
                if (resolved == null
                    || (resolved != avatar && !resolved.transform.IsChildOf(avatar.transform)))
                {
                    throw new OutfitGenerationException(
                        "MA Object ToggleのAvatarObjectReferenceを対象アバター内に解決できませんでした。");
                }
            }
        }

        private static void ValidateBlendshapeSync(BlendshapeSyncComponentBinding generated, GameObject avatar)
        {
            var component = generated.Component;
            if (component == null || component.GetComponent<SkinnedMeshRenderer>() != generated.LocalRenderer)
            {
                throw new OutfitGenerationException("生成したMA Blendshape Syncの衣装Rendererが一致しません。");
            }

            if (component.Bindings == null || component.Bindings.Count != generated.Mappings.Length)
            {
                throw new OutfitGenerationException("生成したMA Blendshape Syncのbinding数が一致しません。");
            }

            for (var index = 0; index < generated.Mappings.Length; index++)
            {
                var expected = generated.Mappings[index];
                var actual = component.Bindings[index];
                var sourceObject = actual.ReferenceMesh?.Get(component);
                if (sourceObject != generated.SourceRenderer.gameObject
                    || (sourceObject != avatar && !sourceObject.transform.IsChildOf(avatar.transform)))
                {
                    throw new OutfitGenerationException(
                        "MA Blendshape Syncの同期元AvatarObjectReferenceを対象アバター内に解決できませんでした。");
                }

                if (!string.Equals(actual.Blendshape, expected.SourceShape, StringComparison.Ordinal)
                    || !string.Equals(actual.LocalBlendshape, expected.LocalShape, StringComparison.Ordinal)
                    || generated.SourceRenderer.sharedMesh.GetBlendShapeIndex(actual.Blendshape) < 0
                    || generated.LocalRenderer.sharedMesh.GetBlendShapeIndex(actual.LocalBlendshape) < 0)
                {
                    throw new OutfitGenerationException("MA Blendshape Syncのshape対応を生成後に検証できませんでした。");
                }

                if (!IsIdentityCurve(actual.RemapCurve))
                {
                    throw new OutfitGenerationException(
                        "MA Blendshape Syncのremap curveが0→0、100→100の恒等曲線ではありません。");
                }
            }
        }

        private static bool IsIdentityCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length != 2) return false;
            var first = curve[0];
            var last = curve[1];
            return Mathf.Approximately(first.time, 0f)
                   && Mathf.Approximately(first.value, 0f)
                   && Mathf.Approximately(last.time, 100f)
                   && Mathf.Approximately(last.value, 100f);
        }

        private sealed class ResolvedPartBinding
        {
            internal ResolvedPartBinding(PartTogglePlan plan, bool initialOn, ResolvedPartTarget[] targets)
            {
                Plan = plan;
                InitialOn = initialOn;
                Targets = targets;
            }

            internal PartTogglePlan Plan { get; }
            internal bool InitialOn { get; }
            internal ResolvedPartTarget[] Targets { get; }
        }

        private sealed class ResolvedPartTarget
        {
            internal ResolvedPartTarget(GameObject gameObject, bool activeWhenOn)
            {
                GameObject = gameObject;
                ActiveWhenOn = activeWhenOn;
            }

            internal GameObject GameObject { get; }
            internal bool ActiveWhenOn { get; }
        }

        private sealed class PartToggleComponentBinding
        {
            internal PartToggleComponentBinding(ModularAvatarObjectToggle component, ResolvedPartTarget[] targets)
            {
                Component = component;
                Targets = targets;
            }

            internal ModularAvatarObjectToggle Component { get; }
            internal ResolvedPartTarget[] Targets { get; }
        }

        private sealed class ResolvedBlendshapeBinding
        {
            internal ResolvedBlendshapeBinding(
                BlendshapeSyncPlan plan,
                SkinnedMeshRenderer localRenderer,
                SkinnedMeshRenderer sourceRenderer)
            {
                Plan = plan;
                LocalRenderer = localRenderer;
                SourceRenderer = sourceRenderer;
            }

            internal BlendshapeSyncPlan Plan { get; }
            internal SkinnedMeshRenderer LocalRenderer { get; }
            internal SkinnedMeshRenderer SourceRenderer { get; }
        }

        private sealed class BlendshapeSyncComponentBinding
        {
            internal BlendshapeSyncComponentBinding(
                ModularAvatarBlendshapeSync component,
                SkinnedMeshRenderer localRenderer,
                SkinnedMeshRenderer sourceRenderer,
                BlendshapeMappingPlan[] mappings)
            {
                Component = component;
                LocalRenderer = localRenderer;
                SourceRenderer = sourceRenderer;
                Mappings = mappings;
            }

            internal ModularAvatarBlendshapeSync Component { get; }
            internal SkinnedMeshRenderer LocalRenderer { get; }
            internal SkinnedMeshRenderer SourceRenderer { get; }
            internal BlendshapeMappingPlan[] Mappings { get; }
        }
    }
}
