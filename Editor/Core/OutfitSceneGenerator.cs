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
        private const float ShapeChangerThreshold = 0.01f;

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
            var masterSceneTargets = ResolveMasterSceneTargets(plan);
            var sourceHashBefore = AssetDatabase.GetAssetDependencyHash(plan.SourceAssetPath).ToString();
            var sceneTargetStates = CaptureSceneTargetStates(plan, masterSceneTargets);

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
                var partOwnerObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
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
                        partToggleComponents.Add(new PartToggleComponentBinding(
                            binding.Plan,
                            toggle,
                            binding.Targets));
                        partOwnerObjects.Add(binding.Plan.ItemId, itemObject);
                    }
                }

                var blendshapeSyncComponents = CreateBlendshapeSyncComponents(blendshapeBindings);
                var shapeChangerComponents = CreateShapeChangerComponents(
                    plan,
                    masterObject,
                    partOwnerObjects,
                    outfitInstance);

                SetPrefabInstanceActive(outfitInstance, false);

                masterToggle.Objects = new List<ToggledObject>
                {
                    CreateToggledObject(outfitInstance, true),
                };
                foreach (var sceneTarget in masterSceneTargets)
                {
                    masterToggle.Objects.Add(CreateToggledObject(
                        sceneTarget.GameObject,
                        sceneTarget.ActiveWhenOn));
                }

                foreach (var binding in partToggleComponents)
                {
                    binding.Component.Objects = binding.Targets
                        .Select(target => CreateToggledObject(target.GameObject, target.ActiveWhenOn))
                        .ToList();
                }

                ValidateGeneratedResult(plan, avatar, generatedRoot, outfitInstance, masterToggle,
                    masterSceneTargets, partToggleComponents, blendshapeSyncComponents,
                    shapeChangerComponents,
                    sourceHashBefore, sceneTargetStates);

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

        private static ResolvedMasterSceneTarget[] ResolveMasterSceneTargets(OutfitSetupPlan plan)
        {
            return plan.MasterSceneTargets
                .Select(target =>
                {
                    if (target?.Reference == null)
                    {
                        throw new OutfitGenerationException("Scene対象参照がありません。");
                    }

                    var resolved = target.Reference.Resolve();
                    if (resolved == null)
                    {
                        throw new OutfitGenerationException(
                            "Scene対象をGlobalObjectIdから再解決できません。");
                    }

                    return new ResolvedMasterSceneTarget(
                        target.StableId,
                        resolved,
                        target.ActiveWhenOn);
                })
                .ToArray();
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
                    .OrderBy(target => target.StableId, StringComparer.Ordinal)
                    .Select(target => new ResolvedPartTarget(
                        target.StableId,
                        target.Source,
                        target.Source == PartTargetSource.OutfitPrefab
                            ? target.PrefabKey.Resolve(outfitInstance, plan.DependencyHash)
                            : target.SceneReference?.Resolve(),
                        target.ActiveWhenOn))
                    .ToArray();
                if (targets.Any(target => target.GameObject == null))
                {
                    throw new OutfitGenerationException($"個別項目「{part.Label}」の対象を生成インスタンス上で解決できません。");
                }

                result.Add(new ResolvedPartBinding(part, initialOn, targets));
            }

            return result;
        }

        private static SceneTargetState[] CaptureSceneTargetStates(
            OutfitSetupPlan plan,
            IEnumerable<ResolvedMasterSceneTarget> masterSceneTargets)
        {
            var partSceneTargets = plan.PartToggles
                .Where(part => part != null)
                .SelectMany(part => part.Targets)
                .Where(target => target != null
                                 && target.Source == PartTargetSource.SceneObject)
                .Select(target => target.SceneReference?.Resolve());
            var shapeTargets = plan.MasterShapeChanges
                .Concat(plan.PartToggles
                    .Where(part => part != null)
                    .SelectMany(part => part.ShapeChanges))
                .Concat(plan.OutfitRendererShapeChangers
                    .Where(owner => owner != null)
                    .SelectMany(owner => owner.ShapeChanges))
                .Where(setting => setting != null)
                .Select(setting => setting.Source == PartTargetSource.OutfitPrefab
                    ? setting.PrefabRendererKey.Resolve(plan.SourcePrefab, plan.DependencyHash)
                    : setting.SceneRendererReference?.Resolve());
            return masterSceneTargets
                .Select(target => target.GameObject)
                .Concat(partSceneTargets)
                .Concat(shapeTargets)
                .Where(target => target != null)
                .Distinct()
                .Select(target => new SceneTargetState(target))
                .ToArray();
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

        private static List<ShapeChangerComponentBinding> CreateShapeChangerComponents(
            OutfitSetupPlan plan,
            GameObject masterOwner,
            IReadOnlyDictionary<string, GameObject> partOwnerObjects,
            GameObject outfitInstance)
        {
            var result = new List<ShapeChangerComponentBinding>();
            if (plan.MasterShapeChanges.Count > 0)
            {
                result.Add(CreateShapeChangerComponent(
                    masterOwner,
                    plan.MasterShapeChanges,
                    outfitInstance,
                    plan.DependencyHash));
            }

            foreach (var part in plan.PartToggles.Where(part => part != null))
            {
                if (part.ShapeChanges.Count == 0) continue;
                if (!partOwnerObjects.TryGetValue(part.ItemId, out var owner) || owner == null)
                {
                    throw new OutfitGenerationException(
                        $"個別項目「{part.Label}」のShape Changer生成先を解決できません。");
                }

                result.Add(CreateShapeChangerComponent(
                    owner,
                    part.ShapeChanges,
                    outfitInstance,
                    plan.DependencyHash));
            }

            var seenRendererOwners = new HashSet<GameObject>();
            foreach (var rendererOwner in plan.OutfitRendererShapeChangers)
            {
                if (rendererOwner == null)
                {
                    throw new OutfitGenerationException(
                        "衣装Renderer Shape Changer owner設定を解決できません。");
                }

                var owner = rendererOwner.OwnerKey.Resolve(
                    outfitInstance,
                    plan.DependencyHash);
                if (owner == null || owner.GetComponent<Renderer>() == null)
                {
                    throw new OutfitGenerationException(
                        "Shape Changer ownerを生成した衣装Renderer GameObjectへ解決できません。");
                }

                if (!seenRendererOwners.Add(owner))
                {
                    throw new OutfitGenerationException(
                        "同じ衣装Renderer GameObjectへ複数のShape Changer owner設定があります。");
                }

                result.Add(CreateShapeChangerComponent(
                    owner,
                    rendererOwner.ShapeChanges,
                    outfitInstance,
                    plan.DependencyHash,
                    true));
            }

            return result;
        }

        private static ShapeChangerComponentBinding CreateShapeChangerComponent(
            GameObject owner,
            IReadOnlyList<ShapeChangerSettingPlan> settings,
            GameObject outfitInstance,
            string dependencyHash,
            bool requiresAddedComponentOverride = false)
        {
            if (owner == null || settings == null || settings.Count == 0)
            {
                throw new OutfitGenerationException("Shape Changerの生成先または設定がありません。");
            }

            var resolvedShapes = settings.Select(setting =>
            {
                if (setting == null)
                {
                    throw new OutfitGenerationException("Shape Changer設定を解決できません。");
                }

                var targetObject = setting.Source == PartTargetSource.OutfitPrefab
                    ? setting.PrefabRendererKey.Resolve(outfitInstance, dependencyHash)
                    : setting.SceneRendererReference?.Resolve();
                var renderer = targetObject != null
                    ? targetObject.GetComponent<SkinnedMeshRenderer>()
                    : null;
                if (renderer == null
                    || renderer.sharedMesh == null
                    || renderer.sharedMesh.GetBlendShapeIndex(setting.ShapeName) < 0)
                {
                    throw new OutfitGenerationException(
                        "Shape Changer対象Renderer、Mesh、またはBlendShapeを解決できません。");
                }

                if (float.IsNaN(setting.Value)
                    || float.IsInfinity(setting.Value)
                    || setting.Value < 0f
                    || setting.Value > 100f)
                {
                    throw new OutfitGenerationException("Shape ChangerのSet値が0～100の有限値ではありません。");
                }

                return new ResolvedShapeChange(setting, renderer);
            }).ToArray();

            var component = Undo.AddComponent<ModularAvatarShapeChanger>(owner);
            Undo.RecordObject(component, UndoName);
            component.Inverted = false;
            component.Threshold = ShapeChangerThreshold;
            component.Shapes = resolvedShapes.Select(resolved =>
            {
                var reference = new AvatarObjectReference();
                reference.Set(resolved.Renderer.gameObject);
                return new ChangedShape
                {
                    Object = reference,
                    ShapeName = resolved.Setting.ShapeName,
                    ChangeType = ShapeChangeType.Set,
                    Value = resolved.Setting.Value,
                };
            }).ToList();
            EditorUtility.SetDirty(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }

            return new ShapeChangerComponentBinding(
                component,
                owner,
                resolvedShapes,
                requiresAddedComponentOverride);
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
            IReadOnlyList<ResolvedMasterSceneTarget> masterSceneTargets,
            IReadOnlyList<PartToggleComponentBinding> partToggles,
            IReadOnlyList<BlendshapeSyncComponentBinding> blendshapeSyncs,
            IReadOnlyList<ShapeChangerComponentBinding> shapeChangers,
            string sourceHashBefore,
            IReadOnlyList<SceneTargetState> sceneTargetStates)
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
            ValidateMasterToggle(masterToggle, outfitInstance, masterSceneTargets);
            foreach (var partToggle in partToggles)
            {
                ValidateToggleReferences(partToggle.Component, avatar);
            }

            foreach (var blendshapeSync in blendshapeSyncs)
            {
                ValidateBlendshapeSync(blendshapeSync, avatar);
            }

            foreach (var shapeChanger in shapeChangers)
            {
                ValidateShapeChanger(shapeChanger, avatar);
            }

            var sourceHashAfter = AssetDatabase.GetAssetDependencyHash(plan.SourceAssetPath).ToString();
            if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal)
                || !string.Equals(sourceHashAfter, plan.DependencyHash, StringComparison.Ordinal))
            {
                throw new OutfitGenerationException("生成中に元衣装Prefabまたは依存アセットが変更されました。");
            }

            foreach (var sceneTargetState in sceneTargetStates)
            {
                if (!sceneTargetState.IsUnchanged())
                {
                    throw new OutfitGenerationException(
                        "生成中にScene対象またはShape Changer対象Rendererが変更されました。");
                }
            }
        }

        private static void ValidateMasterToggle(
            ModularAvatarObjectToggle masterToggle,
            GameObject outfitInstance,
            IReadOnlyList<ResolvedMasterSceneTarget> sceneTargets)
        {
            var expected = new Dictionary<GameObject, bool>
            {
                { outfitInstance, true },
            };
            foreach (var sceneTarget in sceneTargets)
            {
                if (expected.ContainsKey(sceneTarget.GameObject))
                {
                    throw new OutfitGenerationException("全体トグルのScene対象が重複しています。");
                }

                expected.Add(sceneTarget.GameObject, sceneTarget.ActiveWhenOn);
            }

            if (masterToggle.Objects == null || masterToggle.Objects.Count != expected.Count)
            {
                throw new OutfitGenerationException("全体トグルの対象数が一致しません。");
            }

            foreach (var configured in masterToggle.Objects)
            {
                var resolved = configured.Object?.Get(masterToggle);
                if (resolved == null
                    || !expected.TryGetValue(resolved, out var expectedActive)
                    || configured.Active != expectedActive)
                {
                    throw new OutfitGenerationException(
                        "全体トグルのScene対象またはON時の表示設定が一致しません。");
                }

                expected.Remove(resolved);
            }

            if (expected.Count != 0)
            {
                throw new OutfitGenerationException("全体トグルの対象をすべて検証できませんでした。");
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

        private static void ValidateShapeChanger(
            ShapeChangerComponentBinding generated,
            GameObject avatar)
        {
            var component = generated.Component;
            if (component == null || component.gameObject != generated.Owner)
            {
                throw new OutfitGenerationException("生成したMA Shape Changerの配置先が一致しません。");
            }

            if (generated.RequiresAddedComponentOverride
                && (!PrefabUtility.IsPartOfPrefabInstance(generated.Owner)
                    || !PrefabUtility.IsAddedComponentOverride(component)
                    || PrefabUtility.GetCorrespondingObjectFromSource(generated.Owner) == null))
            {
                throw new OutfitGenerationException(
                    "衣装Rendererへ追加したMA Shape ChangerがAdded Component Overrideとして保持されていません。");
            }

            if (component.Inverted
                || !Mathf.Approximately(component.Threshold, ShapeChangerThreshold))
            {
                throw new OutfitGenerationException("生成したMA Shape Changerの固定設定が一致しません。");
            }

            if (component.Shapes == null || component.Shapes.Count != generated.Shapes.Length)
            {
                throw new OutfitGenerationException("生成したMA Shape ChangerのShape数が一致しません。");
            }

            for (var index = 0; index < generated.Shapes.Length; index++)
            {
                var expected = generated.Shapes[index];
                var actual = component.Shapes[index];
                var resolvedObject = actual?.Object?.Get(component);
                if (actual == null
                    || resolvedObject != expected.Renderer.gameObject
                    || (resolvedObject != avatar
                        && !resolvedObject.transform.IsChildOf(avatar.transform)))
                {
                    throw new OutfitGenerationException(
                        "MA Shape ChangerのAvatarObjectReferenceを対象アバター内に解決できませんでした。");
                }

                if (actual.ChangeType != ShapeChangeType.Set
                    || !string.Equals(
                        actual.ShapeName,
                        expected.Setting.ShapeName,
                        StringComparison.Ordinal)
                    || !Mathf.Approximately(actual.Value, expected.Setting.Value)
                    || expected.Renderer.sharedMesh == null
                    || expected.Renderer.sharedMesh.GetBlendShapeIndex(actual.ShapeName) < 0)
                {
                    throw new OutfitGenerationException(
                        "生成したMA Shape ChangerのSet設定を検証できませんでした。");
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

        private sealed class ResolvedMasterSceneTarget
        {
            internal ResolvedMasterSceneTarget(
                string stableId,
                GameObject gameObject,
                bool activeWhenOn)
            {
                StableId = stableId ?? string.Empty;
                GameObject = gameObject;
                ActiveWhenOn = activeWhenOn;
            }

            internal string StableId { get; }
            internal GameObject GameObject { get; }
            internal bool ActiveWhenOn { get; }
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
            internal ResolvedPartTarget(
                string stableId,
                PartTargetSource source,
                GameObject gameObject,
                bool activeWhenOn)
            {
                StableId = stableId ?? string.Empty;
                Source = source;
                GameObject = gameObject;
                ActiveWhenOn = activeWhenOn;
            }

            internal string StableId { get; }
            internal PartTargetSource Source { get; }
            internal GameObject GameObject { get; }
            internal bool ActiveWhenOn { get; }
        }

        private sealed class PartToggleComponentBinding
        {
            internal PartToggleComponentBinding(
                PartTogglePlan plan,
                ModularAvatarObjectToggle component,
                ResolvedPartTarget[] targets)
            {
                Plan = plan;
                Component = component;
                Targets = targets;
            }

            internal PartTogglePlan Plan { get; }
            internal ModularAvatarObjectToggle Component { get; }
            internal ResolvedPartTarget[] Targets { get; }
        }

        private sealed class SceneTargetState
        {
            private readonly GameObject _target;
            private readonly bool _activeSelf;
            private readonly Transform _parent;
            private readonly int _siblingIndex;
            private readonly Vector3 _localPosition;
            private readonly Quaternion _localRotation;
            private readonly Vector3 _localScale;
            private readonly RendererState[] _renderers;

            internal SceneTargetState(GameObject target)
            {
                _target = target;
                _activeSelf = target.activeSelf;
                _parent = target.transform.parent;
                _siblingIndex = target.transform.GetSiblingIndex();
                _localPosition = target.transform.localPosition;
                _localRotation = target.transform.localRotation;
                _localScale = target.transform.localScale;
                _renderers = target.GetComponentsInChildren<Renderer>(true)
                    .Select(renderer => new RendererState(renderer))
                    .ToArray();
            }

            internal bool IsUnchanged()
            {
                return _target != null
                       && _target.activeSelf == _activeSelf
                       && _target.transform.parent == _parent
                       && _target.transform.GetSiblingIndex() == _siblingIndex
                       && _target.transform.localPosition == _localPosition
                       && _target.transform.localRotation == _localRotation
                       && _target.transform.localScale == _localScale
                       && _renderers.All(renderer => renderer.IsUnchanged());
            }
        }

        private readonly struct RendererState
        {
            private readonly Renderer _renderer;
            private readonly bool _enabled;
            private readonly Material[] _sharedMaterials;
            private readonly bool _isSkinned;
            private readonly Mesh _sharedMesh;
            private readonly float[] _blendshapeWeights;

            internal RendererState(Renderer renderer)
            {
                _renderer = renderer;
                _enabled = renderer != null && renderer.enabled;
                _sharedMaterials = renderer != null
                    ? renderer.sharedMaterials.ToArray()
                    : Array.Empty<Material>();
                var skinnedRenderer = renderer as SkinnedMeshRenderer;
                _isSkinned = skinnedRenderer != null;
                _sharedMesh = skinnedRenderer != null ? skinnedRenderer.sharedMesh : null;
                _blendshapeWeights = skinnedRenderer != null && _sharedMesh != null
                    ? Enumerable.Range(0, _sharedMesh.blendShapeCount)
                        .Select(skinnedRenderer.GetBlendShapeWeight)
                        .ToArray()
                    : Array.Empty<float>();
            }

            internal bool IsUnchanged()
            {
                if (_renderer == null
                    || _renderer.enabled != _enabled
                    || !_renderer.sharedMaterials.SequenceEqual(_sharedMaterials))
                {
                    return false;
                }

                if (!_isSkinned) return !(_renderer is SkinnedMeshRenderer);
                var skinnedRenderer = _renderer as SkinnedMeshRenderer;
                if (skinnedRenderer == null || skinnedRenderer.sharedMesh != _sharedMesh)
                {
                    return false;
                }

                var blendshapeWeights = _blendshapeWeights;
                return Enumerable.Range(0, blendshapeWeights.Length)
                    .All(index => skinnedRenderer.GetBlendShapeWeight(index) == blendshapeWeights[index]);
            }
        }

        private sealed class ResolvedShapeChange
        {
            internal ResolvedShapeChange(
                ShapeChangerSettingPlan setting,
                SkinnedMeshRenderer renderer)
            {
                Setting = setting;
                Renderer = renderer;
            }

            internal ShapeChangerSettingPlan Setting { get; }
            internal SkinnedMeshRenderer Renderer { get; }
        }

        private sealed class ShapeChangerComponentBinding
        {
            internal ShapeChangerComponentBinding(
                ModularAvatarShapeChanger component,
                GameObject owner,
                ResolvedShapeChange[] shapes,
                bool requiresAddedComponentOverride)
            {
                Component = component;
                Owner = owner;
                Shapes = shapes;
                RequiresAddedComponentOverride = requiresAddedComponentOverride;
            }

            internal ModularAvatarShapeChanger Component { get; }
            internal GameObject Owner { get; }
            internal ResolvedShapeChange[] Shapes { get; }
            internal bool RequiresAddedComponentOverride { get; }
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
