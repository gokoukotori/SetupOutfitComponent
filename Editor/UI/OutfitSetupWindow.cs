using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace Gokoukotori.SetupComponents.Editor
{
    internal sealed class OutfitSetupWindow : EditorWindow
    {
        private static readonly string[] StepNames =
        {
            "1. 衣装Prefab", "2. 配置先", "3. 装着と全体動作", "4. 個別パーツ",
            "5. BlendShape Sync", "6. 確認",
        };

        private static readonly string[] SetupModeLabels =
        {
            "自動（既存を優先）", "MA標準セットアップを実行", "装着処理を行わない",
        };

        private static readonly string[] PartInitialStateLabels =
        {
            "Prefab状態から自動", "初期OFF", "初期ON",
        };

        private static readonly string[] TargetOnStateLabels =
        {
            "表示", "非表示",
        };

        private OutfitAnalysis _analysis;
        private OutfitSetupPlan _plan;
        private GameObject _sourcePrefab;
        private int _step;
        private Vector2 _scrollPosition;
        private readonly List<VRCAvatarDescriptor> _avatars = new List<VRCAvatarDescriptor>();
        private VRCAvatarDescriptor _selectedAvatar;
        private Transform _placement;
        private readonly List<GameObject> _exclusionObjects = new List<GameObject>();
        private readonly HashSet<PrefabTargetKey> _selectedPartTargets = new HashSet<PrefabTargetKey>();
        private readonly Dictionary<PrefabTargetKey, SkinnedMeshRenderer> _blendshapeSourceRenderers =
            new Dictionary<PrefabTargetKey, SkinnedMeshRenderer>();
        private bool _showAllPrefabObjects;
        private string _localReferenceError;
        private string _exclusionDropMessage;

        internal static void Open(GameObject sourcePrefab)
        {
            var window = GetWindow<OutfitSetupWindow>(true, "衣装セットアップ", true);
            window.minSize = new Vector2(640f, 520f);
            window.Initialize(sourcePrefab);
            window.Show();
        }

        private void Initialize(GameObject sourcePrefab)
        {
            _sourcePrefab = sourcePrefab;
            _analysis = OutfitAnalyzer.Analyze(sourcePrefab);
            _plan = new OutfitSetupPlan(_analysis);
            _step = 0;
            _scrollPosition = Vector2.zero;
            _selectedPartTargets.Clear();
            _blendshapeSourceRenderers.Clear();
            _exclusionObjects.Clear();
            _localReferenceError = null;
            _exclusionDropMessage = null;
            RefreshAvatars(true);
        }

        private void OnEnable() => titleContent = new GUIContent("衣装セットアップ");

        private void OnHierarchyChange()
        {
            if (_plan == null) return;
            RefreshAvatars(false);
            Repaint();
        }

        private void OnGUI()
        {
            if (_sourcePrefab == null || _analysis == null || _plan == null)
            {
                EditorGUILayout.HelpBox(
                    "Project上の衣装Prefabを右クリックし、Assets/Setup Components/衣装セットアップ... から開いてください。",
                    MessageType.Info);
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Play Mode中は衣装を生成できません。", MessageType.Error);
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                EditorGUILayout.HelpBox("Prefab Stageでは衣装を生成できません。", MessageType.Error);

            DrawStepHeader();
            EditorGUILayout.Space(6f);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            switch (_step)
            {
                case 0: DrawSourceStep(); break;
                case 1: DrawPlacementStep(); break;
                case 2: DrawMasterStep(); break;
                case 3: DrawPartsStep(); break;
                case 4: DrawBlendshapeStep(); break;
                case 5: DrawReviewStep(); break;
            }
            EditorGUILayout.EndScrollView();
            DrawNavigation();
        }

        private void DrawStepHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (var index = 0; index < StepNames.Length; index++)
                {
                    using (new EditorGUI.DisabledScope(index > _step))
                    {
                        if (GUILayout.Toggle(_step == index, StepNames[index], EditorStyles.miniButton)) _step = index;
                    }
                }
            }
        }

        private void DrawSourceStep()
        {
            EditorGUILayout.LabelField("衣装Prefab", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("入力", _sourcePrefab, typeof(GameObject), false);
                EditorGUILayout.TextField("アセットパス", _analysis.AssetPath);
                EditorGUILayout.TextField("Dependency Hash", _analysis.DependencyHash);
            }
            _plan.OutputName = EditorGUILayout.TextField("出力オブジェクト名", _plan.OutputName);
            foreach (var error in _analysis.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (var warning in _analysis.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            EditorGUILayout.HelpBox(
                "解析と設定ではPrefabやSceneを変更しません。実際の変更は確認画面の「シーンに生成」を押したときだけ行われます。",
                MessageType.Info);
        }

        private void DrawPlacementStep()
        {
            EditorGUILayout.LabelField("配置先アバター", EditorStyles.boldLabel);
            if (_avatars.Count == 0)
            {
                EditorGUILayout.HelpBox("ロード済みSceneにAvatarDescriptorがありません。", MessageType.Error);
            }
            else
            {
                var labels = new[] { "<選択してください>" }
                    .Concat(_avatars.Select(GetAvatarLabel))
                    .ToArray();
                var selectedIndex = _selectedAvatar == null ? 0 : _avatars.IndexOf(_selectedAvatar) + 1;
                var nextIndex = EditorGUILayout.Popup("対象アバター", selectedIndex, labels);
                var nextAvatar = nextIndex == 0 ? null : _avatars[nextIndex - 1];
                if (_selectedAvatar != nextAvatar) SetSelectedAvatar(nextAvatar);
            }

            EditorGUI.BeginChangeCheck();
            var nextPlacement = (Transform)EditorGUILayout.ObjectField("配置Transform", _placement, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck())
            {
                _placement = nextPlacement;
                UpdateSceneReferences();
            }

            if (_selectedAvatar != null && _placement != null
                && _placement != _selectedAvatar.transform && !_placement.IsChildOf(_selectedAvatar.transform))
                EditorGUILayout.HelpBox("配置Transformは対象アバターの子孫を指定してください。", MessageType.Error);

            EditorGUILayout.HelpBox(
                "特定アバター向けの名前推定は行いません。衣装を置くTransformを明示してください。",
                MessageType.Info);
            DrawLocalReferenceError();
        }

        private void DrawMasterStep()
        {
            EditorGUILayout.LabelField("装着", EditorStyles.boldLabel);
            _plan.SetupMode = (OutfitSetupMode)EditorGUILayout.Popup("装着モード", (int)_plan.SetupMode, SetupModeLabels);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("メニューと全体トグル", EditorStyles.boldLabel);
            _plan.SubmenuLabel = EditorGUILayout.TextField("SubMenu表示名", _plan.SubmenuLabel);
            _plan.MasterToggleLabel = EditorGUILayout.TextField("全体トグル表示名", _plan.MasterToggleLabel);
            _plan.MasterDefaultOn = EditorGUILayout.Toggle("初期ON", _plan.MasterDefaultOn);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("衣装ON時に無効化するSceneオブジェクト", EditorStyles.boldLabel);
            DrawExclusionDropArea();

            for (var index = 0; index < _exclusionObjects.Count; index++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var next = (GameObject)EditorGUILayout.ObjectField(
                        "対象 " + (index + 1), _exclusionObjects[index], typeof(GameObject), true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _exclusionObjects[index] = next;
                        UpdateSceneReferences();
                    }
                    if (GUILayout.Button("削除", GUILayout.Width(52f)))
                    {
                        _exclusionObjects.RemoveAt(index);
                        UpdateSceneReferences();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (GUILayout.Button("対象を追加")) _exclusionObjects.Add(null);
            if (!string.IsNullOrEmpty(_exclusionDropMessage))
                EditorGUILayout.HelpBox(_exclusionDropMessage, MessageType.Info);
            EditorGUILayout.HelpBox(
                "全体トグルは「ON＝衣装を表示」です。上の対象は衣装ON時だけ非表示になります。Scene上の現在値は変更しません。",
                MessageType.Info);
            DrawLocalReferenceError();
        }

        private void DrawExclusionDropArea()
        {
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
            };
            var dropArea = GUILayoutUtility.GetRect(0f, 52f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Hierarchyから対象を複数選択し、ここへドラッグ＆ドロップ", style);

            var currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition)
                || (currentEvent.type != EventType.DragUpdated
                    && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            var droppedObjects = CollectDroppedSceneObjects(
                DragAndDrop.objectReferences,
                _exclusionObjects);
            DragAndDrop.visualMode = droppedObjects.Count > 0
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (droppedObjects.Count > 0)
                {
                    _exclusionObjects.AddRange(droppedObjects);
                    _exclusionDropMessage = droppedObjects.Count + "件の対象を追加しました。";
                    UpdateSceneReferences();
                }
                else
                {
                    _exclusionDropMessage = "追加できる新しいSceneオブジェクトがありませんでした。";
                }
            }

            currentEvent.Use();
        }

        internal static IReadOnlyList<GameObject> CollectDroppedSceneObjects(
            IEnumerable<UnityEngine.Object> droppedObjects,
            IEnumerable<GameObject> existingObjects)
        {
            var result = new List<GameObject>();
            var seenInstanceIds = new HashSet<int>();
            foreach (var existing in existingObjects ?? Enumerable.Empty<GameObject>())
            {
                if (existing != null) seenInstanceIds.Add(existing.GetInstanceID());
            }

            foreach (var droppedObject in droppedObjects ?? Enumerable.Empty<UnityEngine.Object>())
            {
                var gameObject = droppedObject as GameObject;
                if (gameObject == null && droppedObject is Component component)
                    gameObject = component.gameObject;

                if (gameObject == null
                    || EditorUtility.IsPersistent(gameObject)
                    || !gameObject.scene.IsValid()
                    || !gameObject.scene.isLoaded
                    || !seenInstanceIds.Add(gameObject.GetInstanceID()))
                {
                    continue;
                }

                result.Add(gameObject);
            }

            return result;
        }

        private void DrawPartsStep()
        {
            EditorGUILayout.LabelField("個別メニュー項目", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1つ以上のPrefab内GameObjectを1つのトグル項目へまとめ、対象ごとにメニューON時の表示／非表示を指定できます。",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                _showAllPrefabObjects = EditorGUILayout.ToggleLeft("Renderer以外も表示", _showAllPrefabObjects, GUILayout.Width(150f));
                if (GUILayout.Button("Renderer候補を1項目ずつ追加")) AddRendererCandidates();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Prefab内ターゲット", EditorStyles.miniBoldLabel);
            foreach (var target in _analysis.Targets)
            {
                if (target.TargetKey.IsRoot) continue;
                if (!_showAllPrefabObjects && !target.IsRendererCandidate) continue;
                var selected = _selectedPartTargets.Contains(target.TargetKey);
                var label = new string(' ', Mathf.Min(target.Depth, 12) * 2) + target.Name
                            + (target.IsRendererCandidate ? "  [Renderer]" : string.Empty)
                            + (target.ActiveSelf ? string.Empty : "  [初期OFF]");
                var next = EditorGUILayout.ToggleLeft(label, selected);
                if (next != selected)
                {
                    if (next) _selectedPartTargets.Add(target.TargetKey);
                    else _selectedPartTargets.Remove(target.TargetKey);
                }
            }
            using (new EditorGUI.DisabledScope(_selectedPartTargets.Count == 0))
            {
                if (GUILayout.Button("選択対象を1つのメニュー項目として追加")) AddSelectedPartGroup();
            }
            EditorGUILayout.Space(10f);
            DrawPartPlans();
        }

        private void DrawPartPlans()
        {
            if (_plan.PartToggles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "個別項目はありません。この場合、Prefabインスタンス上のMenu Groupと「メニュー」階層は生成されません。",
                    MessageType.None);
                return;
            }

            for (var index = 0; index < _plan.PartToggles.Count; index++)
            {
                var part = _plan.PartToggles[index];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        part.Label = EditorGUILayout.TextField("表示名", part.Label);
                        if (GUILayout.Button("削除", GUILayout.Width(52f)))
                        {
                            _plan.PartToggles.RemoveAt(index);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUILayout.LabelField("ターゲットごとのメニューON時の状態", EditorStyles.miniBoldLabel);
                    foreach (var targetKey in part.Targets)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var target = _analysis.FindTarget(targetKey);
                            EditorGUILayout.LabelField(target?.DisplayPath ?? "<未解決>");
                            EditorGUILayout.LabelField("ON時", GUILayout.Width(34f));
                            var activeWhenOn = part.GetTargetActiveWhenOn(targetKey);
                            var nextState = EditorGUILayout.Popup(
                                activeWhenOn ? 0 : 1,
                                TargetOnStateLabels,
                                GUILayout.Width(80f));
                            part.SetTargetActiveWhenOn(targetKey, nextState == 0);
                        }
                    }

                    var initialState = !part.InitialOn.HasValue ? 0 : part.InitialOn.Value ? 2 : 1;
                    var nextInitialState = EditorGUILayout.Popup(
                        "メニュー初期状態",
                        initialState,
                        PartInitialStateLabels);
                    part.InitialOn = nextInitialState == 0 ? (bool?)null : nextInitialState == 2;

                    if (!part.InitialOn.HasValue && part.TryGetEffectiveInitialOn(_analysis, out var inherited))
                    {
                        EditorGUILayout.LabelField(
                            "自動判定",
                            inherited ? "ON（Prefab状態とON時設定から判定）" : "OFF（Prefab状態とON時設定から判定）");
                    }
                    else if (!part.InitialOn.HasValue)
                    {
                        EditorGUILayout.HelpBox(
                            "Prefab状態とON時設定から初期状態を一意に決められません。初期OFFまたは初期ONを選択してください。",
                            MessageType.Error);
                    }
                }
            }
        }

        private void DrawBlendshapeStep()
        {
            UpdateSceneReferences();
            EditorGUILayout.LabelField("MA Blendshape Sync", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "衣装Rendererごとに、対象アバター内の同期元Rendererを1つ選びます。Remap Curveは編集せず、0→0・100→100の恒等変換で生成します。",
                MessageType.Info);

            if (_analysis.BlendshapeRenderers.Count == 0)
            {
                EditorGUILayout.HelpBox("衣装PrefabにSkinnedMeshRendererがありません。", MessageType.None);
                return;
            }

            var sourceCandidates = GetAvatarBlendshapeRenderers();
            if (sourceCandidates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "対象アバター内にBlendShapeを持つSkinnedMeshRendererがありません。同期設定を追加する場合は同期元を明示できません。",
                    MessageType.Warning);
            }
            else if (sourceCandidates.Count == 1)
            {
                EditorGUILayout.HelpBox(
                    "同期元候補が1件のため、新しい設定では「" + GetRendererLabel(sourceCandidates[0]) + "」を自動選択します。",
                    MessageType.Info);
            }

            foreach (var localRenderer in _analysis.BlendshapeRenderers)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(localRenderer.DisplayPath, EditorStyles.boldLabel);
                    DrawOutfitBlendshapeNames(localRenderer.BlendshapeNames);

                    if (localRenderer.HasExistingBlendshapeSync)
                    {
                        EditorGUILayout.HelpBox(
                            "既存のMA Blendshape Syncをそのまま保持します。このRendererへ新しい設定は追加できません。",
                            MessageType.Info);
                        continue;
                    }

                    var sync = _plan.BlendshapeSyncs.FirstOrDefault(
                        candidate => candidate.LocalRendererKey.Equals(localRenderer.TargetKey));
                    if (sync == null)
                    {
                        using (new EditorGUI.DisabledScope(localRenderer.BlendshapeNames.Count == 0))
                        {
                            if (GUILayout.Button("このRendererに同期設定を追加"))
                            {
                                AddBlendshapeSyncPlan(localRenderer, sourceCandidates);
                                GUIUtility.ExitGUI();
                            }
                        }
                        if (localRenderer.BlendshapeNames.Count == 0)
                            EditorGUILayout.HelpBox("同期先にできるBlendShapeがありません。", MessageType.None);
                        continue;
                    }

                    DrawBlendshapeSyncPlan(localRenderer, sync);
                }
            }

            DrawLocalReferenceError();
        }

        private static void DrawOutfitBlendshapeNames(IReadOnlyList<string> blendshapeNames)
        {
            EditorGUILayout.LabelField(
                "衣装BlendShape（" + blendshapeNames.Count + "件）",
                EditorStyles.miniBoldLabel);
            if (blendshapeNames.Count == 0) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(
                    string.Join(" / ", blendshapeNames),
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawBlendshapeSyncPlan(OutfitRendererInfo localRenderer, BlendshapeSyncPlan sync)
        {
            _blendshapeSourceRenderers.TryGetValue(sync.LocalRendererKey, out var sourceRenderer);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var nextSource = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                    "同期元Renderer", sourceRenderer, typeof(SkinnedMeshRenderer), true);
                if (EditorGUI.EndChangeCheck())
                {
                    if (nextSource == null) _blendshapeSourceRenderers.Remove(sync.LocalRendererKey);
                    else _blendshapeSourceRenderers[sync.LocalRendererKey] = nextSource;
                    sync.Mappings.Clear();
                    UpdateSceneReferences();
                }

                if (GUILayout.Button("設定を削除", GUILayout.Width(80f)))
                {
                    _plan.BlendshapeSyncs.Remove(sync);
                    _blendshapeSourceRenderers.Remove(sync.LocalRendererKey);
                    GUIUtility.ExitGUI();
                }
            }

            var sourceShapeNames = GetBlendshapeNames(sourceRenderer);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(sourceShapeNames.Count == 0))
                {
                    if (GUILayout.Button("同名BlendShapeを一括追加"))
                        AddSameNameBlendshapeMappings(sync, sourceShapeNames, localRenderer.BlendshapeNames);
                }
                if (GUILayout.Button("マッピングを追加"))
                    sync.Mappings.Add(new BlendshapeMappingPlan(string.Empty, string.Empty));
            }

            if (sync.Mappings.Count == 0)
            {
                EditorGUILayout.HelpBox("マッピングを1件以上追加してください。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("同期元BlendShape → 衣装BlendShape", EditorStyles.miniBoldLabel);
            for (var index = 0; index < sync.Mappings.Count; index++)
            {
                var mapping = sync.Mappings[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawBlendshapePopup(mapping, true, sourceShapeNames, GUILayout.MinWidth(150f));
                    EditorGUILayout.LabelField("→", GUILayout.Width(18f));
                    DrawBlendshapePopup(mapping, false, localRenderer.BlendshapeNames, GUILayout.MinWidth(150f));
                    if (GUILayout.Button("削除", GUILayout.Width(52f)))
                    {
                        sync.Mappings.RemoveAt(index);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void AddBlendshapeSyncPlan(
            OutfitRendererInfo localRenderer,
            IReadOnlyList<SkinnedMeshRenderer> sourceCandidates)
        {
            var sourceRenderer = sourceCandidates.Count == 1 ? sourceCandidates[0] : null;
            SceneObjectReference sourceReference = null;
            if (sourceRenderer != null)
            {
                _blendshapeSourceRenderers[localRenderer.TargetKey] = sourceRenderer;
                sourceReference = TryCreateSceneReference(
                    sourceRenderer.gameObject,
                    "BlendShape同期元 " + sourceRenderer.name);
            }
            _plan.BlendshapeSyncs.Add(new BlendshapeSyncPlan(localRenderer.TargetKey, sourceReference));
        }

        internal static void AddSameNameBlendshapeMappings(
            BlendshapeSyncPlan sync,
            IReadOnlyList<string> sourceShapeNames,
            IReadOnlyList<string> localShapeNames)
        {
            var usedSourceNames = new HashSet<string>(
                sync.Mappings.Select(mapping => mapping.SourceShape),
                StringComparer.Ordinal);
            var usedLocalNames = new HashSet<string>(
                sync.Mappings.Select(mapping => mapping.LocalShape),
                StringComparer.Ordinal);
            var localNames = new HashSet<string>(localShapeNames, StringComparer.Ordinal);

            foreach (var shapeName in sourceShapeNames
                         .Where(localNames.Contains)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                if (usedSourceNames.Contains(shapeName) || usedLocalNames.Contains(shapeName)) continue;
                sync.Mappings.Add(new BlendshapeMappingPlan(shapeName, shapeName));
                usedSourceNames.Add(shapeName);
                usedLocalNames.Add(shapeName);
            }
        }

        private static void DrawBlendshapePopup(
            BlendshapeMappingPlan mapping,
            bool source,
            IReadOnlyList<string> shapeNames,
            params GUILayoutOption[] options)
        {
            var current = source ? mapping.SourceShape : mapping.LocalShape;
            var names = shapeNames.Distinct(StringComparer.Ordinal).ToArray();
            var labels = new[] { "<選択してください>" }.Concat(names).ToArray();
            var currentIndex = Array.IndexOf(names, current);
            var selectedIndex = currentIndex < 0 ? 0 : currentIndex + 1;
            var nextIndex = EditorGUILayout.Popup(selectedIndex, labels, options);
            if (nextIndex == selectedIndex) return;

            var nextValue = nextIndex <= 0 ? string.Empty : names[nextIndex - 1];
            if (source) mapping.SourceShape = nextValue;
            else mapping.LocalShape = nextValue;
        }

        private IReadOnlyList<SkinnedMeshRenderer> GetAvatarBlendshapeRenderers()
        {
            if (_selectedAvatar == null) return Array.Empty<SkinnedMeshRenderer>();
            return _selectedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer != null
                                   && renderer.sharedMesh != null
                                   && renderer.sharedMesh.blendShapeCount > 0)
                .OrderBy(renderer => GetRendererLabel(renderer), StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<string> GetBlendshapeNames(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null) return Array.Empty<string>();
            return Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                .Select(renderer.sharedMesh.GetBlendShapeName)
                .ToArray();
        }

        private static string GetRendererLabel(SkinnedMeshRenderer renderer)
        {
            return renderer == null ? "<missing>" : GetHierarchyPath(renderer.transform);
        }

        private void DrawReviewStep()
        {
            UpdateSceneReferences();
            var validation = OutfitSetupValidator.Instance.Validate(_plan);
            EditorGUILayout.LabelField("生成予定", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("配置先", _plan.PlacementReference?.DisplayName ?? "<未指定>");
            EditorGUILayout.LabelField("Prefab", _analysis.AssetPath);
            EditorGUILayout.LabelField("Prefab接続", "維持（元Prefabインスタンスとして生成）");
            EditorGUILayout.LabelField("Scene保存", "行わない");
            EditorGUILayout.Space(4f);
            DrawHierarchyPreview();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("参照とOverride", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("全体ON", "衣装Prefabインスタンスを表示");
            EditorGUILayout.LabelField("基準状態", "衣装Prefabインスタンスを非表示（Prefab Overrideとして記録）");
            if (_exclusionObjects.Count > 0)
                EditorGUILayout.LabelField("排他対象", string.Join(", ", _exclusionObjects.Where(x => x != null).Select(x => x.name)));
            foreach (var part in _plan.PartToggles)
            {
                foreach (var targetKey in part.Targets)
                {
                    var target = _analysis.FindTarget(targetKey);
                    EditorGUILayout.LabelField(
                        part.Label + " / " + (target?.DisplayPath ?? "<未解決>"),
                        part.GetTargetActiveWhenOn(targetKey) ? "ON時に表示" : "ON時に非表示");
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("BlendShape Sync", EditorStyles.boldLabel);
            var existingSyncRenderers = _analysis.BlendshapeRenderers
                .Where(renderer => renderer.HasExistingBlendshapeSync)
                .ToArray();
            if (existingSyncRenderers.Length == 0 && _plan.BlendshapeSyncs.Count == 0)
            {
                EditorGUILayout.LabelField("設定", "なし");
            }
            foreach (var renderer in existingSyncRenderers)
            {
                EditorGUILayout.LabelField(renderer.DisplayPath, "既存MA Blendshape Syncを保持");
            }
            foreach (var sync in _plan.BlendshapeSyncs)
            {
                var localRenderer = _analysis.FindBlendshapeRenderer(sync.LocalRendererKey);
                EditorGUILayout.LabelField(
                    localRenderer?.DisplayPath ?? "<未解決>",
                    sync.SourceRendererReference?.DisplayName ?? "<同期元未指定>");
                foreach (var mapping in sync.Mappings)
                {
                    EditorGUILayout.LabelField(
                        "  " + EmptyFallback(mapping.SourceShape, "<同期元未指定>"),
                        "→ " + EmptyFallback(mapping.LocalShape, "<衣装側未指定>"));
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("検証", EditorStyles.boldLabel);
            DrawLocalReferenceError();
            foreach (var message in validation.Messages)
                EditorGUILayout.HelpBox("[" + message.Code + "] " + message.Message, ToMessageType(message.Severity));

            _plan.AllowDuplicate = EditorGUILayout.ToggleLeft("同一Prefabの重複配置を明示的に許可する", _plan.AllowDuplicate);
            UpdateSceneReferences();
            validation = OutfitSetupValidator.Instance.Validate(_plan);
            var environmentBlocked = EditorApplication.isPlayingOrWillChangePlaymode
                                     || PrefabStageUtility.GetCurrentPrefabStage() != null;
            var canGenerate = validation.IsValid && string.IsNullOrEmpty(_localReferenceError) && !environmentBlocked;
            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                if (GUILayout.Button("シーンに生成", GUILayout.Height(36f))) Generate();
            }
        }

        private void DrawHierarchyPreview()
        {
            var placementName = _placement != null ? _placement.name : "<配置先>";
            EditorGUILayout.LabelField(placementName);
            EditorGUILayout.LabelField("  └─ " + EmptyFallback(_plan.OutputName, "<出力名>"));
            EditorGUILayout.LabelField("       ├─ " + EmptyFallback(_plan.MasterToggleLabel, "<全体トグル>"));
            EditorGUILayout.LabelField("       └─ " + _analysis.RootName + "  [Prefab instance]");
            if (_plan.PartToggles.Count <= 0) return;
            EditorGUILayout.LabelField("            └─ メニュー");
            foreach (var part in _plan.PartToggles)
                EditorGUILayout.LabelField("                 └─ " + EmptyFallback(part.Label, "<個別項目>"));
        }

        private void DrawNavigation()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_step == 0))
                {
                    if (GUILayout.Button("戻る", GUILayout.Width(100f)))
                    {
                        _step--;
                        _scrollPosition = Vector2.zero;
                    }
                }
                GUILayout.FlexibleSpace();
                if (_step < StepNames.Length - 1)
                {
                    using (new EditorGUI.DisabledScope(!CanAdvanceFromCurrentStep()))
                    {
                        if (GUILayout.Button("次へ", GUILayout.Width(100f)))
                        {
                            _step++;
                            _scrollPosition = Vector2.zero;
                        }
                    }
                }
            }
        }

        private bool CanAdvanceFromCurrentStep()
        {
            switch (_step)
            {
                case 0:
                    return _analysis.IsValid && !string.IsNullOrWhiteSpace(_plan.OutputName);
                case 1:
                    return _selectedAvatar != null && _placement != null
                           && (_placement == _selectedAvatar.transform || _placement.IsChildOf(_selectedAvatar.transform))
                           && string.IsNullOrEmpty(_localReferenceError);
                case 2:
                    return !string.IsNullOrWhiteSpace(_plan.SubmenuLabel)
                           && !string.IsNullOrWhiteSpace(_plan.MasterToggleLabel)
                           && string.IsNullOrEmpty(_localReferenceError);
                case 3:
                    return _plan.PartToggles.All(part => !string.IsNullOrWhiteSpace(part.Label)
                        && part.Targets.Count > 0 && part.TryGetEffectiveInitialOn(_analysis, out _));
                case 4:
                    UpdateSceneReferences();
                    return string.IsNullOrEmpty(_localReferenceError)
                           && _plan.BlendshapeSyncs.All(sync => sync.SourceRendererReference != null
                               && sync.Mappings.Count > 0
                               && sync.Mappings.All(mapping => !string.IsNullOrWhiteSpace(mapping.SourceShape)
                                   && !string.IsNullOrWhiteSpace(mapping.LocalShape)));
                default:
                    return true;
            }
        }

        private void RefreshAvatars(bool selectAutomatic)
        {
            var previous = _selectedAvatar;
            _avatars.Clear();
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    _avatars.AddRange(root.GetComponentsInChildren<VRCAvatarDescriptor>(true));
            }
            _avatars.RemoveAll(avatar => avatar == null || EditorUtility.IsPersistent(avatar));
            _avatars.Sort((left, right) => string.CompareOrdinal(GetAvatarLabel(left), GetAvatarLabel(right)));
            if (previous != null && _avatars.Contains(previous))
            {
                _selectedAvatar = previous;
                return;
            }
            if (selectAutomatic && _avatars.Count == 1) SetSelectedAvatar(_avatars[0]);
            else
            {
                _selectedAvatar = null;
                _placement = null;
                UpdateSceneReferences();
            }
        }

        private void SetSelectedAvatar(VRCAvatarDescriptor avatar)
        {
            if (_selectedAvatar != avatar)
            {
                _plan?.BlendshapeSyncs.Clear();
                _blendshapeSourceRenderers.Clear();
            }
            _selectedAvatar = avatar;
            _placement = avatar != null ? avatar.transform : null;
            _exclusionObjects.Clear();
            UpdateSceneReferences();
        }

        private void UpdateSceneReferences()
        {
            _localReferenceError = null;
            _plan.AvatarReference = TryCreateSceneReference(_selectedAvatar != null ? _selectedAvatar.gameObject : null, "対象アバター");
            _plan.PlacementReference = TryCreateSceneReference(_placement != null ? _placement.gameObject : null, "配置Transform");
            _plan.ExclusionTargets.Clear();
            foreach (var exclusion in _exclusionObjects)
            {
                if (exclusion == null)
                {
                    SetLocalReferenceError("排他対象に未指定の行があります。");
                    continue;
                }
                var reference = TryCreateSceneReference(exclusion, "排他対象 " + exclusion.name);
                if (reference != null) _plan.ExclusionTargets.Add(reference);
            }

            foreach (var sync in _plan.BlendshapeSyncs)
            {
                if (!_blendshapeSourceRenderers.TryGetValue(sync.LocalRendererKey, out var sourceRenderer)
                    || sourceRenderer == null)
                {
                    sync.SourceRendererReference = null;
                    continue;
                }

                sync.SourceRendererReference = TryCreateSceneReference(
                    sourceRenderer.gameObject,
                    "BlendShape同期元 " + sourceRenderer.name);
            }
        }

        private SceneObjectReference TryCreateSceneReference(GameObject gameObject, string label)
        {
            if (gameObject == null) return null;
            try { return SceneObjectReference.Create(gameObject); }
            catch (Exception exception)
            {
                SetLocalReferenceError(label + "をGlobalObjectIdで保持できません: " + exception.Message);
                return null;
            }
        }

        private void SetLocalReferenceError(string message)
        {
            if (string.IsNullOrEmpty(_localReferenceError)) _localReferenceError = message;
        }

        private void DrawLocalReferenceError()
        {
            if (!string.IsNullOrEmpty(_localReferenceError)) EditorGUILayout.HelpBox(_localReferenceError, MessageType.Error);
        }

        private void AddRendererCandidates()
        {
            foreach (var candidate in _analysis.PartCandidates)
            {
                if (_plan.PartToggles.Any(part => part.Targets.Contains(candidate.TargetKey))) continue;
                _plan.PartToggles.Add(new PartTogglePlan(candidate.Name, new[] { candidate.TargetKey }));
            }
        }

        private void AddSelectedPartGroup()
        {
            var targets = _selectedPartTargets.Select(key => _analysis.FindTarget(key)).Where(x => x != null)
                .OrderBy(x => x.DisplayPath, StringComparer.Ordinal).ToArray();
            if (targets.Length == 0) return;
            _plan.PartToggles.Add(new PartTogglePlan(
                targets.Length == 1 ? targets[0].Name : "パーツ",
                targets.Select(target => target.TargetKey)));
            _selectedPartTargets.Clear();
        }

        private void Generate()
        {
            try
            {
                UpdateSceneReferences();
                var generatedRoot = new OutfitSceneGenerator().Generate(_plan);
                Selection.activeGameObject = generatedRoot;
                EditorGUIUtility.PingObject(generatedRoot);
                Close();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "衣装セットアップに失敗しました",
                    exception.Message + "\n\nシーンへの変更はロールバックされました。",
                    "閉じる");
            }
        }

        private static string GetAvatarLabel(VRCAvatarDescriptor descriptor)
        {
            return descriptor == null ? "<missing>" : descriptor.gameObject.scene.name + "/" + GetHierarchyPath(descriptor.transform);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static MessageType ToMessageType(ValidationSeverity severity)
        {
            switch (severity)
            {
                case ValidationSeverity.Error: return MessageType.Error;
                case ValidationSeverity.Warning: return MessageType.Warning;
                default: return MessageType.Info;
            }
        }

        private static string EmptyFallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
