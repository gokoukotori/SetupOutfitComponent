using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitSetupWindow : EditorWindow
    {
        private static readonly string[] StepNames =
        {
            "1. 衣装Prefab", "2. 配置先", "3. 装着と全体動作", "4. 個別パーツ",
            "5. BlendShape Sync", "6. Shape Changer", "7. 確認",
        };

        private static readonly string[] SetupModeLabels =
        {
            "自動（既存を優先）", "MA標準セットアップを実行", "装着処理を行わない",
        };

        private static readonly string[] PartInitialStateLabels =
        {
            "自動（OFF）", "初期OFF", "初期ON",
        };

        private static readonly string[] TargetOnStateLabels =
        {
            "表示", "非表示",
        };

        private const string PartDragDataKey =
            "Gokoukotori.SetupOutfitComponent.PartToggleItemId";

        private OutfitAnalysis _analysis;
        private OutfitSetupPlan _plan;
        private GameObject _sourcePrefab;
        private int _step;
        private Vector2 _scrollPosition;
        private readonly List<VRCAvatarDescriptor> _avatars = new List<VRCAvatarDescriptor>();
        private VRCAvatarDescriptor _selectedAvatar;
        private Transform _placement;
        private readonly List<MasterSceneTargetRow> _sceneTargetRows = new List<MasterSceneTargetRow>();
        private readonly Dictionary<string, PartToggleTargetPlan> _selectedPartTargets =
            new Dictionary<string, PartToggleTargetPlan>(StringComparer.Ordinal);
        private readonly Dictionary<int, SceneObjectReference> _sceneTargetReferencesByInstanceId =
            new Dictionary<int, SceneObjectReference>();
        private readonly Dictionary<PrefabTargetKey, SkinnedMeshRenderer> _blendshapeSourceRenderers =
            new Dictionary<PrefabTargetKey, SkinnedMeshRenderer>();
        private readonly Dictionary<ShapeChangerSettingPlan, SkinnedMeshRenderer> _shapeChangerSceneRenderers =
            new Dictionary<ShapeChangerSettingPlan, SkinnedMeshRenderer>();
        private IReadOnlyList<OutfitRendererInfo> _shapeChangerPrefabRenderers =
            Array.Empty<OutfitRendererInfo>();
        private readonly Dictionary<PrefabTargetKey, string> _shapeChangerOwnerRendererTypes =
            new Dictionary<PrefabTargetKey, string>();
        private readonly BlendshapeUiCache _blendshapeUiCache = new BlendshapeUiCache();
        private readonly SceneReferenceRefreshGate _sceneReferenceRefreshGate =
            new SceneReferenceRefreshGate();
        private readonly ReviewValidationCache _reviewValidationCache =
            new ReviewValidationCache();
        private bool _showAllPrefabObjects;
        private string _localReferenceError;
        private string _sceneTargetDropMessage;
        private string _pendingPartDragItemId;

        internal sealed class MasterSceneTargetRow
        {
            internal MasterSceneTargetRow(GameObject target, bool activeWhenOn)
            {
                Target = target;
                ActiveWhenOn = activeWhenOn;
            }

            internal GameObject Target { get; set; }
            internal bool ActiveWhenOn { get; set; }
        }
        internal BlendshapeUiCache BlendshapeUiCacheForTests => _blendshapeUiCache;

        internal static void Open(GameObject sourcePrefab)
        {
            var window = GetWindow<OutfitSetupWindow>(true, "衣装セットアップ", true);
            window.minSize = new Vector2(640f, 520f);
            window.Initialize(sourcePrefab);
            window.Show();
        }

        internal void Initialize(GameObject sourcePrefab)
        {
            OutfitApplyPreviewWindow.CloseForOwner(this);
            _sourcePrefab = sourcePrefab;
            _analysis = OutfitAnalyzer.Analyze(sourcePrefab);
            _plan = new OutfitSetupPlan(_analysis);
            _blendshapeUiCache.SetAnalysis(_analysis);
            _shapeChangerPrefabRenderers = _analysis.BlendshapeRenderers
                .Where(renderer => renderer.BlendshapeNames.Count > 0)
                .ToArray();
            _shapeChangerOwnerRendererTypes.Clear();
            foreach (var owner in _analysis.RendererOwners)
            {
                var ownerObject = owner.TargetKey.Resolve(sourcePrefab, _analysis.DependencyHash);
                var renderer = ownerObject != null ? ownerObject.GetComponent<Renderer>() : null;
                _shapeChangerOwnerRendererTypes[owner.TargetKey] =
                    renderer != null ? renderer.GetType().Name : "<Renderer未解決>";
            }
            _step = 0;
            _scrollPosition = Vector2.zero;
            _selectedPartTargets.Clear();
            _sceneTargetReferencesByInstanceId.Clear();
            _blendshapeSourceRenderers.Clear();
            _shapeChangerSceneRenderers.Clear();
            _sceneTargetRows.Clear();
            _localReferenceError = null;
            _sceneTargetDropMessage = null;
            _sceneReferenceRefreshGate.Invalidate();
            InvalidateReviewValidation();
            RefreshAvatars(true);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("衣装セットアップ");
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            OutfitApplyPreviewWindow.CloseForOwner(this);
        }

        private void OnHierarchyChange()
        {
            HandleHierarchyChange();
        }

        internal void HandleHierarchyChange()
        {
            _blendshapeUiCache.InvalidateAvatar();
            if (_plan == null) return;
            RefreshAvatars(false);
            RefreshSceneReferencesAfterChange();
            UpdateApplyPreviewIfOpen();
            OutfitApplyPreviewWindow.RefreshHighlightedTargetsIfOpen(this);
            Repaint();
        }

        private void OnProjectChange()
        {
            HandleProjectChange();
        }

        internal void HandleProjectChange()
        {
            _blendshapeUiCache.InvalidateProject();
            InvalidateReviewValidation();
            UpdateApplyPreviewIfOpen();
            Repaint();
        }

        private void OnSceneSaved(Scene scene)
        {
            if (_plan == null) return;
            _blendshapeUiCache.InvalidateAvatar();
            RefreshSceneReferencesAfterChange();
            UpdateApplyPreviewIfOpen();
            Repaint();
        }

        private void OnGUI()
        {
            if (_sourcePrefab == null || _analysis == null || _plan == null)
            {
                EditorGUILayout.HelpBox(
                    "Project上の衣装Prefabを右クリックし、Assets/Setup Outfit Component/衣装セットアップ... から開いてください。",
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
                case 5: DrawShapeChangerStep(); break;
                case 6: DrawReviewStep(); break;
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
                RefreshSceneReferencesAfterChange();
                UpdateApplyPreviewIfOpen();
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
            EditorGUI.BeginChangeCheck();
            _plan.MasterDefaultOn = EditorGUILayout.Toggle("初期ON", _plan.MasterDefaultOn);
            if (EditorGUI.EndChangeCheck()) UpdateApplyPreviewIfOpen();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("衣装ON時のScene表示設定", EditorStyles.boldLabel);
            DrawSceneTargetDropArea();

            for (var index = 0; index < _sceneTargetRows.Count; index++)
            {
                var row = _sceneTargetRows[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var nextTarget = (GameObject)EditorGUILayout.ObjectField(
                        "対象 " + (index + 1), row.Target, typeof(GameObject), true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        row.Target = nextTarget;
                        RefreshSceneReferencesAfterChange();
                        UpdateApplyPreviewIfOpen();
                    }

                    EditorGUILayout.LabelField("ON時", GUILayout.Width(34f));
                    var nextState = EditorGUILayout.Popup(
                        row.ActiveWhenOn ? 0 : 1,
                        TargetOnStateLabels,
                        GUILayout.Width(80f));
                    var nextActiveWhenOn = nextState == 0;
                    if (nextActiveWhenOn != row.ActiveWhenOn)
                    {
                        row.ActiveWhenOn = nextActiveWhenOn;
                        RefreshSceneReferencesAfterChange();
                        UpdateApplyPreviewIfOpen();
                    }

                    if (GUILayout.Button("削除", GUILayout.Width(52f)))
                    {
                        _sceneTargetRows.RemoveAt(index);
                        RefreshSceneReferencesAfterChange();
                        UpdateApplyPreviewIfOpen();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (GUILayout.Button("対象を追加"))
            {
                _sceneTargetRows.Add(new MasterSceneTargetRow(null, false));
                RefreshSceneReferencesAfterChange();
                UpdateApplyPreviewIfOpen();
            }
            if (!string.IsNullOrEmpty(_sceneTargetDropMessage))
                EditorGUILayout.HelpBox(_sceneTargetDropMessage, MessageType.Info);
            EditorGUILayout.HelpBox(
                "全体トグルは「ON＝衣装を表示」です。Scene対象は衣装ON時だけ指定した表示／非表示へ切り替わり、全体OFF時は元Sceneの状態へ戻ります。Scene上の現在値は変更しません。",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(!CanAttemptApplyPreview()))
            {
                if (GUILayout.Button("適用プレビューを開く", GUILayout.Height(30f)))
                    OpenApplyPreview();
            }
            EditorGUILayout.HelpBox(
                "専用SceneViewで衣装ON/OFF、Scene表示設定、個別パーツの見え方を確認します。MA装着処理、BlendShape Sync、Armature統合結果は反映しません。",
                MessageType.None);
            DrawLocalReferenceError();
        }

        private void DrawSceneTargetDropArea()
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

            var droppedRows = CreateDroppedMasterSceneTargetRows(
                DragAndDrop.objectReferences,
                _sceneTargetRows.Select(row => row.Target));
            DragAndDrop.visualMode = droppedRows.Count > 0
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (droppedRows.Count > 0)
                {
                    _sceneTargetRows.AddRange(droppedRows);
                    _sceneTargetDropMessage = droppedRows.Count + "件のScene対象を非表示設定で追加しました。";
                    RefreshSceneReferencesAfterChange();
                    UpdateApplyPreviewIfOpen();
                }
                else
                {
                    _sceneTargetDropMessage = "追加できる新しいSceneオブジェクトがありませんでした。";
                }
            }

            currentEvent.Use();
        }

        internal static IReadOnlyList<MasterSceneTargetRow> CreateDroppedMasterSceneTargetRows(
            IEnumerable<UnityEngine.Object> droppedObjects,
            IEnumerable<GameObject> existingObjects)
        {
            return CollectDroppedSceneObjects(droppedObjects, existingObjects)
                .Select(target => new MasterSceneTargetRow(target, false))
                .ToArray();
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
                "Prefab内GameObjectとステップ3のScene対象を1つのトグル項目へまとめ、対象ごとにメニューON時の表示／非表示を指定できます。同じ対象を複数項目へ追加した場合は、メニューで下側にあるON項目を優先します。",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(!CanAttemptApplyPreview()))
            {
                if (GUILayout.Button("個別パーツプレビューを開く", GUILayout.Height(30f)))
                    OpenApplyPreview();
            }
            EditorGUILayout.HelpBox(
                "プレビュー内のメニューON／OFFは見え方確認用の一時状態です。この画面の初期状態設定には書き戻しません。",
                MessageType.None);
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
                var selection = PartToggleTargetPlan.ForPrefab(target.TargetKey);
                var selected = _selectedPartTargets.ContainsKey(selection.StableId);
                var label = new string(' ', Mathf.Min(target.Depth, 12) * 2) + target.Name
                            + (target.IsRendererCandidate ? "  [Renderer]" : string.Empty)
                            + (target.ActiveSelf ? string.Empty : "  [初期OFF]");
                var next = EditorGUILayout.ToggleLeft(label, selected);
                if (next != selected)
                {
                    if (next) _selectedPartTargets[selection.StableId] = selection;
                    else _selectedPartTargets.Remove(selection.StableId);
                    NotifyHighlightedPartTargetsChanged();
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("ステップ3のScene対象", EditorStyles.miniBoldLabel);
            if (_sceneTargetRows.All(row => row.Target == null))
            {
                EditorGUILayout.HelpBox(
                    "ステップ3でScene表示対象を指定すると候補に表示されます。",
                    MessageType.None);
            }
            foreach (var row in _sceneTargetRows.Where(row => row.Target != null))
            {
                var sceneTarget = row.Target;
                if (!_sceneTargetReferencesByInstanceId.TryGetValue(
                        sceneTarget.GetInstanceID(),
                        out var sceneReference))
                {
                    EditorGUILayout.LabelField(sceneTarget.name + "  [Scene/参照未解決]");
                    continue;
                }

                var selection = PartToggleTargetPlan.ForScene(sceneReference);
                var selected = _selectedPartTargets.ContainsKey(selection.StableId);
                var label = sceneReference.DisplayName
                            + (row.ActiveWhenOn
                                ? "  [Scene/全体ON時に表示]"
                                : "  [Scene/全体ON時に非表示]")
                            + (sceneTarget.activeSelf ? string.Empty : "  [現在OFF]");
                var next = EditorGUILayout.ToggleLeft(label, selected);
                if (next == selected) continue;
                if (next) _selectedPartTargets[selection.StableId] = selection;
                else _selectedPartTargets.Remove(selection.StableId);
                NotifyHighlightedPartTargetsChanged();
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

            EditorGUILayout.HelpBox(
                "上から下の順にメニューへ生成します。同じ対象を共有する項目が同時にONの場合は、下側の項目を優先します。▲／▼または左端のハンドルのドラッグで並べ替えられます。",
                MessageType.None);
            for (var partIndex = 0; partIndex < _plan.PartToggles.Count; partIndex++)
            {
                DrawPartReorderDropZone(partIndex);
                var part = _plan.PartToggles[partIndex];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawPartDragHandle(part);
                        using (new EditorGUI.DisabledScope(partIndex <= 0))
                        {
                            if (GUILayout.Button("▲", GUILayout.Width(28f)))
                            {
                                MovePartToggle(_plan.PartToggles, part.ItemId, partIndex - 1);
                                OnPartOrderChanged();
                                GUIUtility.ExitGUI();
                            }
                        }
                        using (new EditorGUI.DisabledScope(partIndex >= _plan.PartToggles.Count - 1))
                        {
                            if (GUILayout.Button("▼", GUILayout.Width(28f)))
                            {
                                MovePartToggle(_plan.PartToggles, part.ItemId, partIndex + 1);
                                OnPartOrderChanged();
                                GUIUtility.ExitGUI();
                            }
                        }
                        EditorGUI.BeginChangeCheck();
                        var nextLabel = EditorGUILayout.TextField("表示名", part.Label);
                        if (EditorGUI.EndChangeCheck())
                        {
                            part.Label = nextLabel;
                            UpdateApplyPreviewIfOpen();
                            InvalidateReviewValidation();
                        }
                        if (GUILayout.Button("削除", GUILayout.Width(52f)))
                        {
                            foreach (var shapeChange in part.ShapeChanges)
                                _shapeChangerSceneRenderers.Remove(shapeChange);
                            _plan.PartToggles.Remove(part);
                            UpdateApplyPreviewIfOpen();
                            InvalidateReviewValidation();
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUILayout.LabelField("ターゲットごとのメニューON時の状態", EditorStyles.miniBoldLabel);
                    for (var targetIndex = 0; targetIndex < part.Targets.Count; targetIndex++)
                    {
                        var targetPlan = part.Targets[targetIndex];
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(GetPartTargetDisplayName(targetPlan));
                            EditorGUILayout.LabelField("ON時", GUILayout.Width(34f));
                            var activeWhenOn = targetPlan?.ActiveWhenOn == true;
                            var nextState = EditorGUILayout.Popup(
                                activeWhenOn ? 0 : 1,
                                TargetOnStateLabels,
                                GUILayout.Width(80f));
                            var nextActiveWhenOn = nextState == 0;
                            if (targetPlan != null && nextActiveWhenOn != activeWhenOn)
                            {
                                targetPlan.ActiveWhenOn = nextActiveWhenOn;
                                UpdateApplyPreviewIfOpen();
                                InvalidateReviewValidation();
                            }
                            if (GUILayout.Button("削除", GUILayout.Width(52f)))
                            {
                                part.Targets.RemoveAt(targetIndex);
                                UpdateApplyPreviewIfOpen();
                                InvalidateReviewValidation();
                                GUIUtility.ExitGUI();
                            }
                        }
                    }

                    var initialState = !part.InitialOn.HasValue ? 0 : part.InitialOn.Value ? 2 : 1;
                    var nextInitialState = EditorGUILayout.Popup(
                        "メニュー初期状態",
                        initialState,
                        PartInitialStateLabels);
                    if (nextInitialState != initialState)
                    {
                        part.InitialOn = nextInitialState == 0 ? (bool?)null : nextInitialState == 2;
                        UpdateApplyPreviewIfOpen();
                        InvalidateReviewValidation();
                    }
                }
            }
            DrawPartReorderDropZone(_plan.PartToggles.Count);
        }

        private void DrawPartDragHandle(PartTogglePlan part)
        {
            var handleContent = new GUIContent("≡", "ドラッグしてメニュー順を変更");
            var handleRect = GUILayoutUtility.GetRect(
                handleContent,
                EditorStyles.miniButton,
                GUILayout.Width(28f));
            GUI.Label(handleRect, handleContent, EditorStyles.miniButton);

            var currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown
                && currentEvent.button == 0
                && handleRect.Contains(currentEvent.mousePosition))
            {
                _pendingPartDragItemId = part.ItemId;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag
                && string.Equals(_pendingPartDragItemId, part.ItemId, StringComparison.Ordinal))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(PartDragDataKey, part.ItemId);
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.StartDrag("個別メニュー項目を並べ替え");
                _pendingPartDragItemId = null;
                currentEvent.Use();
                return;
            }

            if (currentEvent.rawType == EventType.MouseUp
                && string.Equals(_pendingPartDragItemId, part.ItemId, StringComparison.Ordinal))
            {
                _pendingPartDragItemId = null;
            }
        }

        private void DrawPartReorderDropZone(int insertionIndex)
        {
            var dropRect = GUILayoutUtility.GetRect(0f, 8f, GUILayout.ExpandWidth(true));
            var draggedItemId = DragAndDrop.GetGenericData(PartDragDataKey) as string;
            if (string.IsNullOrEmpty(draggedItemId)) return;

            var currentEvent = Event.current;
            if (!dropRect.Contains(currentEvent.mousePosition)
                || (currentEvent.type != EventType.DragUpdated
                    && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            EditorGUI.DrawRect(dropRect, new Color(0.2f, 0.55f, 1f, 0.65f));
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var moved = MovePartToggleAtInsertionIndex(
                    _plan.PartToggles,
                    draggedItemId,
                    insertionIndex);
                DragAndDrop.SetGenericData(PartDragDataKey, null);
                _pendingPartDragItemId = null;
                if (moved)
                {
                    OnPartOrderChanged();
                    currentEvent.Use();
                    GUIUtility.ExitGUI();
                }
            }

            currentEvent.Use();
        }

        private void OnPartOrderChanged()
        {
            UpdateApplyPreviewIfOpen();
            InvalidateReviewValidation();
            Repaint();
        }

        internal static bool MovePartToggle(
            IList<PartTogglePlan> parts,
            string itemId,
            int targetIndex)
        {
            if (parts == null || string.IsNullOrEmpty(itemId) || parts.Count == 0)
                return false;

            var sourceIndex = FindPartToggleIndex(parts, itemId);
            if (sourceIndex < 0) return false;
            var normalizedTargetIndex = Mathf.Clamp(targetIndex, 0, parts.Count - 1);
            if (sourceIndex == normalizedTargetIndex) return false;

            var part = parts[sourceIndex];
            parts.RemoveAt(sourceIndex);
            parts.Insert(normalizedTargetIndex, part);
            return true;
        }

        internal static bool MovePartToggleAtInsertionIndex(
            IList<PartTogglePlan> parts,
            string itemId,
            int insertionIndex)
        {
            if (parts == null || string.IsNullOrEmpty(itemId)) return false;

            var sourceIndex = FindPartToggleIndex(parts, itemId);
            if (sourceIndex < 0) return false;
            var normalizedInsertionIndex = Mathf.Clamp(insertionIndex, 0, parts.Count);
            var targetIndex = normalizedInsertionIndex > sourceIndex
                ? normalizedInsertionIndex - 1
                : normalizedInsertionIndex;
            return MovePartToggle(parts, itemId, targetIndex);
        }

        private static int FindPartToggleIndex(IList<PartTogglePlan> parts, string itemId)
        {
            for (var index = 0; index < parts.Count; index++)
            {
                if (string.Equals(parts[index]?.ItemId, itemId, StringComparison.Ordinal))
                    return index;
            }

            return -1;
        }
        private void DrawBlendshapeStep()
        {
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
                    DrawOutfitBlendshapeNames(localRenderer);

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

        private void DrawOutfitBlendshapeNames(OutfitRendererInfo renderer)
        {
            if (renderer == null)
            {
                EditorGUILayout.HelpBox("衣装Rendererを解決できません。", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField(
                "衣装BlendShape（" + renderer.BlendshapeNames.Count + "件）",
                EditorStyles.miniBoldLabel);
            if (renderer.BlendshapeNames.Count == 0) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(
                    _blendshapeUiCache.GetLocalDisplayText(renderer),
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
                    RefreshSceneReferencesAfterChange();
                }

                if (GUILayout.Button("設定を削除", GUILayout.Width(80f)))
                {
                    _plan.BlendshapeSyncs.Remove(sync);
                    _blendshapeSourceRenderers.Remove(sync.LocalRendererKey);
                    InvalidateReviewValidation();
                    GUIUtility.ExitGUI();
                }
            }

            var sourceOptions = _blendshapeUiCache.GetSourceOptions(sourceRenderer);
            var localOptions = _blendshapeUiCache.GetLocalOptions(localRenderer);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(sourceOptions.Names.Count == 0))
                {
                    if (GUILayout.Button("同名BlendShapeを一括追加"))
                    {
                        AddSameNameBlendshapeMappings(sync, sourceOptions.Names, localRenderer.BlendshapeNames);
                        InvalidateReviewValidation();
                    }
                }
                if (GUILayout.Button("マッピングを追加"))
                {
                    sync.Mappings.Add(new BlendshapeMappingPlan(string.Empty, string.Empty));
                    InvalidateReviewValidation();
                }
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
                    DrawBlendshapePopup(mapping, true, sourceOptions, GUILayout.MinWidth(150f));
                    EditorGUILayout.LabelField("→", GUILayout.Width(18f));
                    DrawBlendshapePopup(mapping, false, localOptions, GUILayout.MinWidth(150f));
                    if (GUILayout.Button("削除", GUILayout.Width(52f)))
                    {
                        sync.Mappings.RemoveAt(index);
                        InvalidateReviewValidation();
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
            if (sourceRenderer != null)
                _blendshapeSourceRenderers[localRenderer.TargetKey] = sourceRenderer;
            _plan.BlendshapeSyncs.Add(new BlendshapeSyncPlan(localRenderer.TargetKey, null));
            RefreshSceneReferencesAfterChange();
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

        private void DrawBlendshapePopup(
            BlendshapeMappingPlan mapping,
            bool source,
            BlendshapeOptionSet shapeOptions,
            params GUILayoutOption[] layoutOptions)
        {
            var current = source ? mapping.SourceShape : mapping.LocalShape;
            var content = new GUIContent(
                string.IsNullOrEmpty(current) ? "<選択してください>" : current);
            var rect = EditorGUILayout.GetControlRect(
                false,
                EditorGUIUtility.singleLineHeight,
                EditorStyles.popup,
                layoutOptions);
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Keyboard, EditorStyles.popup)) return;

            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("<選択してください>"),
                string.IsNullOrEmpty(current),
                () => SetBlendshapeMapping(mapping, source, string.Empty));
            foreach (var shapeName in shapeOptions.Names)
            {
                var capturedName = shapeName;
                menu.AddItem(
                    new GUIContent(capturedName),
                    string.Equals(current, capturedName, StringComparison.Ordinal),
                    () => SetBlendshapeMapping(mapping, source, capturedName));
            }

            menu.DropDown(rect);
        }

        private void SetBlendshapeMapping(
            BlendshapeMappingPlan mapping,
            bool source,
            string value)
        {
            if (source) mapping.SourceShape = value;
            else mapping.LocalShape = value;
            InvalidateReviewValidation();
            Repaint();
        }

        private IReadOnlyList<SkinnedMeshRenderer> GetAvatarBlendshapeRenderers()
        {
            return _blendshapeUiCache.GetAvatarRenderers(_selectedAvatar);
        }

        private string GetRendererLabel(SkinnedMeshRenderer renderer)
        {
            return _blendshapeUiCache.GetRendererLabel(renderer);
        }

        private void DrawShapeChangerStep()
        {
            EditorGUILayout.LabelField("MA Shape Changer Set", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "衣装全体ON、衣装Renderer GameObjectの表示状態、または個別メニュー項目のON状態に連動して、アバター側／衣装側のBlendShapeを0～100の値へ設定します。Delete、Threshold編集、Inverted編集には対応しません。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!CanAttemptApplyPreview()))
            {
                if (GUILayout.Button("Shape Changerプレビューを開く", GUILayout.Height(30f)))
                    OpenApplyPreview();
            }
            EditorGUILayout.HelpBox(
                "専用プレビューは新規に設定したSetだけを反映します。衣装Renderer表示連動はGameObjectのactiveSelfと祖先状態を使用します。既存／外部Shape Changer、BlendShape Sync伝播、Animator、Delete、最終NDMF競合は再現しません。",
                MessageType.None);
            EditorGUILayout.HelpBox(
                "Shape操作対象RendererまたはBlendShape名が未指定の行は、専用プレビューから除外します。完成済みの設定と衣装表示は引き続きプレビューできますが、未指定行はステップ7への移動と生成ではエラーになります。",
                MessageType.Warning);

            DrawExistingShapeChangers();

            var sceneRenderers = GetAvatarBlendshapeRenderers();
            DrawOutfitRendererShapeChangerOwners(sceneRenderers, _shapeChangerPrefabRenderers);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("メニュー状態に連動", EditorStyles.boldLabel);
            DrawShapeChangerOwner(
                "衣装全体ON",
                "全体トグルがONの間に適用します。",
                _plan.MasterShapeChanges,
                sceneRenderers,
                _shapeChangerPrefabRenderers);

            for (var partIndex = 0; partIndex < _plan.PartToggles.Count; partIndex++)
            {
                var part = _plan.PartToggles[partIndex];
                DrawShapeChangerOwner(
                    (partIndex + 1) + ". " + EmptyFallback(part.Label, "<個別項目>"),
                    "メニューON時に適用します。同じShapeを複数項目が操作する場合は下側を優先します。",
                    part.ShapeChanges,
                    sceneRenderers,
                    _shapeChangerPrefabRenderers);
            }

            DrawLocalReferenceError();
        }

        private void DrawOutfitRendererShapeChangerOwners(
            IReadOnlyList<SkinnedMeshRenderer> sceneRenderers,
            IReadOnlyList<OutfitRendererInfo> prefabRenderers)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("衣装Renderer表示連動", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "選択した衣装Renderer GameObjectへMA Shape Changerを追加します。GameObject自身と祖先がアクティブな間だけSetが有効になり、非アクティブになるとこの設定の寄与を解放します。次の有効な所有者、または元のBlendShape Weightが使用されます。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Renderer.enabledはShape Changerの有効条件ではありません。GameObjectのactiveSelfと祖先状態が有効条件です。Renderer.enabled=falseはプレビュー上の描画だけを抑制します。",
                MessageType.Warning);

            using (new EditorGUI.DisabledScope(_analysis.RendererOwners.Count == 0))
            {
                if (GUILayout.Button("衣装Renderer所有者を追加"))
                    ShowOutfitRendererShapeChangerOwnerMenu();
            }

            if (_plan.OutfitRendererShapeChangers.Count == 0)
            {
                EditorGUILayout.LabelField("表示連動設定", "なし");
                return;
            }

            foreach (var owner in EnumerateOutfitRendererShapeChangerOwnersInHierarchy().ToArray())
            {
                DrawOutfitRendererShapeChangerOwner(owner, sceneRenderers, prefabRenderers);
            }
        }

        private void DrawOutfitRendererShapeChangerOwner(
            OutfitRendererShapeChangerPlan owner,
            IReadOnlyList<SkinnedMeshRenderer> sceneRenderers,
            IReadOnlyList<OutfitRendererInfo> prefabRenderers)
        {
            var ownerInfo = _analysis.FindRendererOwner(owner.OwnerKey);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        ownerInfo?.DisplayPath ?? "<付与先未解決>",
                        EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("削除", GUILayout.Width(52f)))
                    {
                        foreach (var setting in owner.ShapeChanges)
                            _shapeChangerSceneRenderers.Remove(setting);
                        _plan.OutfitRendererShapeChangers.Remove(owner);
                        ShapeChangerPlanChanged();
                        GUIUtility.ExitGUI();
                    }
                }

                DrawOutfitRendererShapeChangerOwnerPopup(owner, ownerInfo);
                ownerInfo = _analysis.FindRendererOwner(owner.OwnerKey);
                EditorGUILayout.LabelField(
                    "Prefab相対パス",
                    ownerInfo?.DisplayPath ?? "<付与先未解決>");
                EditorGUILayout.LabelField(
                    "基準activeSelf",
                    ownerInfo == null ? "<未解決>" : ownerInfo.ActiveSelf ? "ON" : "OFF");
                EditorGUILayout.LabelField(
                    "Renderer種別",
                    GetShapeChangerOwnerRendererType(owner.OwnerKey));
                EditorGUILayout.LabelField(
                    "関連するStep 4項目",
                    GetRelatedPartToggleDescription(owner.OwnerKey));
                EditorGUILayout.LabelField(
                    "有効条件",
                    "付与先GameObjectとすべての祖先がアクティブ",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField("Shape対象", EditorStyles.miniBoldLabel);
                DrawShapeChangerSettings(
                    owner.ShapeChanges,
                    sceneRenderers,
                    prefabRenderers);
                if (owner.ShapeChanges.Count == 0)
                    EditorGUILayout.HelpBox("Shape対象を1件以上追加してください。", MessageType.Warning);
            }
        }

        private void ShowOutfitRendererShapeChangerOwnerMenu()
        {
            var menu = new GenericMenu();
            foreach (var candidate in _analysis.RendererOwners)
            {
                var captured = candidate;
                var alreadyUsed = _plan.OutfitRendererShapeChangers.Any(owner =>
                    owner.OwnerKey.Equals(captured.TargetKey));
                var label = GetShapeChangerOwnerMenuLabel(captured);
                if (alreadyUsed)
                {
                    menu.AddDisabledItem(new GUIContent(label), true);
                    continue;
                }

                menu.AddItem(new GUIContent(label), false, () =>
                {
                    _plan.OutfitRendererShapeChangers.Add(
                        new OutfitRendererShapeChangerPlan(captured.TargetKey));
                    ShapeChangerPlanChanged();
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void DrawOutfitRendererShapeChangerOwnerPopup(
            OutfitRendererShapeChangerPlan owner,
            PrefabTargetInfo currentOwner)
        {
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("付与先"));
            var content = new GUIContent(
                currentOwner != null
                    ? GetShapeChangerOwnerMenuLabel(currentOwner)
                    : "<付与先未解決>");
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Keyboard, EditorStyles.popup))
                return;

            var menu = new GenericMenu();
            foreach (var candidate in _analysis.RendererOwners)
            {
                var captured = candidate;
                var usedByOther = _plan.OutfitRendererShapeChangers.Any(other =>
                    !ReferenceEquals(other, owner)
                    && other.OwnerKey.Equals(captured.TargetKey));
                var selected = owner.OwnerKey.Equals(captured.TargetKey);
                var label = GetShapeChangerOwnerMenuLabel(captured);
                if (usedByOther)
                {
                    menu.AddDisabledItem(new GUIContent(label), false);
                    continue;
                }

                menu.AddItem(new GUIContent(label), selected, () =>
                {
                    owner.OwnerKey = captured.TargetKey;
                    ShapeChangerPlanChanged();
                    Repaint();
                });
            }
            menu.DropDown(rect);
        }

        private string GetShapeChangerOwnerMenuLabel(PrefabTargetInfo owner)
        {
            if (owner == null) return "<付与先未解決>";
            var rendererType = GetShapeChangerOwnerRendererType(owner.TargetKey);
            var baseState = owner.ActiveSelf ? string.Empty : " [初期OFF]";
            return owner.DisplayPath + " [" + rendererType + "]" + baseState;
        }

        private string GetShapeChangerOwnerRendererType(PrefabTargetKey ownerKey)
        {
            return _shapeChangerOwnerRendererTypes.TryGetValue(ownerKey, out var rendererType)
                ? rendererType
                : "<Renderer未解決>";
        }

        private string GetRelatedPartToggleDescription(PrefabTargetKey ownerKey)
        {
            var relatedItems = new List<string>();
            for (var partIndex = 0; partIndex < _plan.PartToggles.Count; partIndex++)
            {
                var part = _plan.PartToggles[partIndex];
                var relatedTargets = part.Targets
                    .Where(target => target != null
                                     && target.Source == PartTargetSource.OutfitPrefab
                                     && (target.PrefabKey.Equals(ownerKey)
                                         || target.PrefabKey.IsAncestorOf(ownerKey)))
                    .ToArray();
                if (relatedTargets.Length == 0) continue;
                var states = string.Join(", ", relatedTargets.Select(target =>
                    target.ActiveWhenOn ? "ON時表示" : "ON時非表示"));
                relatedItems.Add(
                    (partIndex + 1) + ". "
                    + EmptyFallback(part.Label, "<個別項目>")
                    + " (" + states + ")");
            }

            return relatedItems.Count == 0
                ? "なし（PrefabのactiveSelfと祖先状態に従う）"
                : string.Join(" / ", relatedItems);
        }

        private IEnumerable<OutfitRendererShapeChangerPlan>
            EnumerateOutfitRendererShapeChangerOwnersInHierarchy()
        {
            if (_plan == null || _analysis == null) yield break;
            foreach (var candidate in _analysis.RendererOwners)
            {
                var owner = _plan.OutfitRendererShapeChangers.FirstOrDefault(item =>
                    item != null && item.OwnerKey.Equals(candidate.TargetKey));
                if (owner != null) yield return owner;
            }

            foreach (var owner in _plan.OutfitRendererShapeChangers)
            {
                if (owner != null && _analysis.FindRendererOwner(owner.OwnerKey) == null)
                    yield return owner;
            }
        }

        private void DrawExistingShapeChangers()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("入力Prefabの既存Shape Changer", EditorStyles.boldLabel);
            if (_analysis.ExistingShapeChangers.Count == 0)
            {
                EditorGUILayout.LabelField("設定", "なし");
                return;
            }

            EditorGUILayout.HelpBox(
                "既存コンポーネントは変更せず、そのまま保持します。新規設定との競合は警告として表示し、最終結果はMAのHierarchy順に依存します。Prefab解析時に解決できないAvatar相対パスは表示だけを行います。",
                MessageType.Info);
            foreach (var existing in _analysis.ExistingShapeChangers)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(existing.DisplayPath, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Threshold", existing.Threshold.ToString("0.#####"));
                    EditorGUILayout.LabelField("Inverted", existing.Inverted ? "true" : "false");
                    if (existing.Shapes.Count == 0)
                    {
                        EditorGUILayout.LabelField("Shape", "なし");
                        continue;
                    }

                    foreach (var shape in existing.Shapes)
                    {
                        var targetPath = EmptyFallback(shape.TargetPath, "<参照未解決>");
                        EditorGUILayout.LabelField(
                            targetPath + " / " + EmptyFallback(shape.ShapeName, "<Shape未指定>"),
                            shape.ChangeType + "  " + shape.Value.ToString("0.###"));
                    }
                }
            }
        }

        private void DrawShapeChangerOwner(
            string title,
            string description,
            IList<ShapeChangerSettingPlan> settings,
            IReadOnlyList<SkinnedMeshRenderer> sceneRenderers,
            IReadOnlyList<OutfitRendererInfo> prefabRenderers)
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
                DrawShapeChangerSettings(settings, sceneRenderers, prefabRenderers);
            }
        }

        private void DrawShapeChangerSettings(
            IList<ShapeChangerSettingPlan> settings,
            IReadOnlyList<SkinnedMeshRenderer> sceneRenderers,
            IReadOnlyList<OutfitRendererInfo> prefabRenderers)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(sceneRenderers.Count == 0))
                {
                    if (GUILayout.Button("アバターShape対象を追加"))
                        ShowSceneShapeChangerRendererMenu(settings, sceneRenderers);
                }
                using (new EditorGUI.DisabledScope(prefabRenderers.Count == 0))
                {
                    if (GUILayout.Button("衣装Shape対象を追加"))
                        ShowPrefabShapeChangerRendererMenu(settings, prefabRenderers);
                }
            }

            if (settings.Count == 0)
            {
                EditorGUILayout.LabelField("新規設定", "なし");
                return;
            }

            for (var index = 0; index < settings.Count; index++)
                DrawShapeChangerSetting(settings, index);
        }

        private void DrawShapeChangerSetting(
            IList<ShapeChangerSettingPlan> settings,
            int index)
        {
            var setting = settings[index];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        setting.Source == PartTargetSource.SceneObject
                            ? "Shape対象：アバター側"
                            : "Shape対象：衣装Prefab側",
                        EditorStyles.miniBoldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("削除", GUILayout.Width(52f)))
                    {
                        _shapeChangerSceneRenderers.Remove(setting);
                        settings.RemoveAt(index);
                        ShapeChangerPlanChanged();
                        GUIUtility.ExitGUI();
                    }
                }

                BlendshapeOptionSet options;
                if (setting.Source == PartTargetSource.SceneObject)
                {
                    _shapeChangerSceneRenderers.TryGetValue(setting, out var renderer);
                    EditorGUI.BeginChangeCheck();
                    var nextRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                        "Shape対象Renderer", renderer, typeof(SkinnedMeshRenderer), true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (nextRenderer == null)
                        {
                            _shapeChangerSceneRenderers.Remove(setting);
                            setting.SceneRendererReference = null;
                        }
                        else
                        {
                            _shapeChangerSceneRenderers[setting] = nextRenderer;
                            setting.SceneRendererReference = TryCreateSceneReference(
                                nextRenderer.gameObject,
                                "Shape Changer対象 " + nextRenderer.name);
                        }
                        setting.ShapeName = string.Empty;
                        RefreshSceneReferencesAfterChange();
                        ShapeChangerPlanChanged();
                    }
                    options = _blendshapeUiCache.GetSourceOptions(nextRenderer);
                    EditorGUILayout.LabelField("BlendShape候補", options.Names.Count + "件");
                }
                else
                {
                    var rendererInfo = _analysis.FindBlendshapeRenderer(setting.PrefabRendererKey);
                    DrawPrefabShapeChangerRendererPopup(setting, rendererInfo);
                    rendererInfo = _analysis.FindBlendshapeRenderer(setting.PrefabRendererKey);
                    options = _blendshapeUiCache.GetLocalOptions(rendererInfo);
                    DrawOutfitBlendshapeNames(rendererInfo);
                }

                DrawShapeChangerShapePopup(setting, options);
                EditorGUI.BeginChangeCheck();
                var nextValue = EditorGUILayout.Slider("Set値", setting.Value, 0f, 100f);
                if (EditorGUI.EndChangeCheck())
                {
                    setting.Value = nextValue;
                    ShapeChangerPlanChanged();
                }

                if (string.IsNullOrWhiteSpace(setting.ShapeName))
                    EditorGUILayout.HelpBox("BlendShapeを選択してください。", MessageType.Warning);
            }
        }

        private void DrawPrefabShapeChangerRendererPopup(
            ShapeChangerSettingPlan setting,
            OutfitRendererInfo currentRenderer)
        {
            var content = new GUIContent(currentRenderer?.DisplayPath ?? "<Renderer未指定>");
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("Shape対象Renderer"));
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Keyboard, EditorStyles.popup)) return;

            var menu = new GenericMenu();
            foreach (var renderer in _shapeChangerPrefabRenderers)
            {
                var captured = renderer;
                menu.AddItem(
                    new GUIContent(captured.DisplayPath),
                    captured.TargetKey.Equals(setting.PrefabRendererKey),
                    () =>
                    {
                        setting.PrefabRendererKey = captured.TargetKey;
                        setting.ShapeName = string.Empty;
                        ShapeChangerPlanChanged();
                        Repaint();
                    });
            }
            menu.DropDown(rect);
        }

        private void DrawShapeChangerShapePopup(
            ShapeChangerSettingPlan setting,
            BlendshapeOptionSet options)
        {
            var current = setting.ShapeName;
            var content = new GUIContent(
                string.IsNullOrEmpty(current) ? "<選択してください>" : current);
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent("BlendShape"));
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Keyboard, EditorStyles.popup)) return;

            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("<選択してください>"),
                string.IsNullOrEmpty(current),
                () => SetShapeChangerShape(setting, string.Empty));
            foreach (var shapeName in options.Names)
            {
                var captured = shapeName;
                menu.AddItem(
                    new GUIContent(captured),
                    string.Equals(current, captured, StringComparison.Ordinal),
                    () => SetShapeChangerShape(setting, captured));
            }
            menu.DropDown(rect);
        }

        private void SetShapeChangerShape(ShapeChangerSettingPlan setting, string shapeName)
        {
            setting.ShapeName = shapeName ?? string.Empty;
            ShapeChangerPlanChanged();
            Repaint();
        }

        private void ShowSceneShapeChangerRendererMenu(
            IList<ShapeChangerSettingPlan> settings,
            IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            var menu = new GenericMenu();
            foreach (var renderer in renderers)
            {
                var captured = renderer;
                menu.AddItem(new GUIContent(GetRendererLabel(captured)), false, () =>
                {
                    var reference = TryCreateSceneReference(
                        captured.gameObject,
                        "Shape Changer対象 " + captured.name);
                    if (reference == null) return;
                    var setting = ShapeChangerSettingPlan.ForScene(reference, string.Empty);
                    settings.Add(setting);
                    _shapeChangerSceneRenderers[setting] = captured;
                    ShapeChangerPlanChanged();
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void ShowPrefabShapeChangerRendererMenu(
            IList<ShapeChangerSettingPlan> settings,
            IReadOnlyList<OutfitRendererInfo> renderers)
        {
            var menu = new GenericMenu();
            foreach (var renderer in renderers)
            {
                var captured = renderer;
                menu.AddItem(new GUIContent(captured.DisplayPath), false, () =>
                {
                    settings.Add(ShapeChangerSettingPlan.ForPrefab(captured.TargetKey, string.Empty));
                    ShapeChangerPlanChanged();
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void ShapeChangerPlanChanged()
        {
            InvalidateReviewValidation();
            UpdateApplyPreviewIfOpen();
        }

        private string GetShapeChangerRendererDisplayName(ShapeChangerSettingPlan setting)
        {
            if (setting == null) return "<未解決>";
            if (setting.Source == PartTargetSource.OutfitPrefab)
                return _analysis.FindBlendshapeRenderer(setting.PrefabRendererKey)?.DisplayPath
                       ?? "<衣装Renderer未解決>";
            return setting.SceneRendererReference?.DisplayName ?? "<アバターRenderer未解決>";
        }

        private void DrawReviewStep()
        {
            EditorGUILayout.LabelField("生成予定", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("配置先", _plan.PlacementReference?.DisplayName ?? "<未指定>");
            EditorGUILayout.LabelField("Prefab", _analysis.AssetPath);
            EditorGUILayout.LabelField("Prefab接続", "維持（元Prefabインスタンスとして生成）");
            EditorGUILayout.LabelField("Scene保存", "行わない");
            EditorGUILayout.LabelField(
                "個別項目の競合解決",
                "同じ対象では、メニューで最も下にあるON項目を適用");
            EditorGUILayout.LabelField(
                "表示／生成順",
                "確認一覧と生成Hierarchyは上→下。下側ほど優先");
            EditorGUILayout.LabelField(
                "全項目OFF",
                "Prefab対象は元のactiveSelf、Scene対象はステップ3の表示設定");
            EditorGUILayout.Space(4f);
            DrawHierarchyPreview();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("参照とOverride", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("全体ON", "衣装Prefabインスタンスを表示");
            EditorGUILayout.LabelField("基準状態", "衣装Prefabインスタンスを非表示（Prefab Overrideとして記録）");
            if (_sceneTargetRows.Count > 0)
            {
                EditorGUILayout.LabelField("Scene表示設定", EditorStyles.miniBoldLabel);
                foreach (var row in _sceneTargetRows)
                {
                    EditorGUILayout.LabelField(
                        row.Target != null ? row.Target.name : "<未指定>",
                        row.ActiveWhenOn ? "衣装ON時に表示" : "衣装ON時に非表示");
                }
            }
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("個別項目（メニュー順・下側優先）", EditorStyles.miniBoldLabel);
            for (var partIndex = 0; partIndex < _plan.PartToggles.Count; partIndex++)
            {
                var part = _plan.PartToggles[partIndex];
                EditorGUILayout.LabelField(
                    EmptyFallback(part.Label, "<個別項目>"),
                    (partIndex + 1) + "番目");
                foreach (var target in part.Targets)
                {
                    EditorGUILayout.LabelField(
                        "  " + GetPartTargetDisplayName(target),
                        target?.ActiveWhenOn == true ? "ON時に表示" : "ON時に非表示");
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
            EditorGUILayout.LabelField("Shape Changer Set", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("生成設定", "Set / Inverted=false / Threshold=0.01");
            EditorGUILayout.LabelField(
                "競合解決",
                "全体 → 衣装Renderer付与先（Prefab Hierarchy順） → 個別項目（メニュー順）");
            EditorGUILayout.LabelField(
                "有効所有者の優先",
                "Hierarchyで後ろの有効な所有者を優先。非アクティブな所有者はSet寄与を解放");
            if (_analysis.ExistingShapeChangers.Count == 0
                && _plan.MasterShapeChanges.Count == 0
                && _plan.OutfitRendererShapeChangers.All(owner =>
                    owner == null || owner.ShapeChanges.Count == 0)
                && _plan.PartToggles.All(part => part.ShapeChanges.Count == 0))
            {
                EditorGUILayout.LabelField("設定", "なし");
            }
            foreach (var existing in _analysis.ExistingShapeChangers)
            {
                EditorGUILayout.LabelField(existing.DisplayPath, "既存MA Shape Changerを保持");
                foreach (var shape in existing.Shapes)
                {
                    EditorGUILayout.LabelField(
                        "  " + EmptyFallback(shape.TargetPath, "<参照未解決>")
                        + " / " + EmptyFallback(shape.ShapeName, "<Shape未指定>"),
                        shape.ChangeType + "  " + shape.Value.ToString("0.###"));
                }
            }
            DrawShapeChangerReviewOwner("衣装全体ON", _plan.MasterShapeChanges);
            foreach (var owner in EnumerateOutfitRendererShapeChangerOwnersInHierarchy())
            {
                var ownerInfo = _analysis.FindRendererOwner(owner.OwnerKey);
                var ownerPath = ownerInfo?.DisplayPath ?? "<付与先未解決>";
                EditorGUILayout.LabelField(
                    "衣装Renderer表示連動: " + ownerPath,
                    "Prefab Override: Added Component");
                EditorGUILayout.LabelField(
                    "  基準activeSelf / Renderer",
                    (ownerInfo == null ? "<未解決>" : ownerInfo.ActiveSelf ? "ON" : "OFF")
                    + " / " + GetShapeChangerOwnerRendererType(owner.OwnerKey));
                EditorGUILayout.LabelField(
                    "  関連するStep 4項目",
                    GetRelatedPartToggleDescription(owner.OwnerKey));
                DrawShapeChangerReviewOwner("  Shape対象", owner.ShapeChanges);
            }
            for (var partIndex = 0; partIndex < _plan.PartToggles.Count; partIndex++)
            {
                var part = _plan.PartToggles[partIndex];
                DrawShapeChangerReviewOwner(
                    (partIndex + 1) + ". " + EmptyFallback(part.Label, "<個別項目>"),
                    part.ShapeChanges);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("検証", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var allowDuplicate = EditorGUILayout.ToggleLeft(
                "同一Prefabの重複配置を明示的に許可する",
                _plan.AllowDuplicate);
            if (EditorGUI.EndChangeCheck())
            {
                _plan.AllowDuplicate = allowDuplicate;
                InvalidateReviewValidation();
            }

            var validation = GetReviewValidation();
            DrawLocalReferenceError();
            foreach (var message in validation.Messages)
                EditorGUILayout.HelpBox("[" + message.Code + "] " + message.Message, ToMessageType(message.Severity));

            var environmentBlocked = EditorApplication.isPlayingOrWillChangePlaymode
                                     || PrefabStageUtility.GetCurrentPrefabStage() != null;
            var canGenerate = validation.IsValid && string.IsNullOrEmpty(_localReferenceError) && !environmentBlocked;
            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                if (GUILayout.Button("シーンに生成", GUILayout.Height(36f))) Generate();
            }
        }

        private void DrawShapeChangerReviewOwner(
            string ownerLabel,
            IEnumerable<ShapeChangerSettingPlan> settings)
        {
            var settingArray = (settings ?? Enumerable.Empty<ShapeChangerSettingPlan>()).ToArray();
            if (settingArray.Length == 0) return;
            EditorGUILayout.LabelField(ownerLabel, EditorStyles.miniBoldLabel);
            foreach (var setting in settingArray)
            {
                EditorGUILayout.LabelField(
                    "  [" + GetShapeChangerTargetSourceLabel(setting) + "] "
                    + GetShapeChangerRendererDisplayName(setting)
                    + " / " + EmptyFallback(setting.ShapeName, "<Shape未指定>"),
                    "Set " + setting.Value.ToString("0.###"));
            }
        }

        private static string GetShapeChangerTargetSourceLabel(ShapeChangerSettingPlan setting)
        {
            return setting?.Source == PartTargetSource.SceneObject
                ? "アバター"
                : "衣装Prefab";
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
            for (var partIndex = 0; partIndex < _plan.PartToggles.Count; partIndex++)
            {
                var part = _plan.PartToggles[partIndex];
                var branch = partIndex == _plan.PartToggles.Count - 1 ? "└─ " : "├─ ";
                EditorGUILayout.LabelField(
                    "                 " + branch + EmptyFallback(part.Label, "<個別項目>"));
            }
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
                            if (_step == 5) PrepareReviewValidation();
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
                        && part.Targets.Count > 0);
                case 4:
                    return string.IsNullOrEmpty(_localReferenceError)
                           && _plan.BlendshapeSyncs.All(sync => sync.SourceRendererReference != null
                               && sync.Mappings.Count > 0
                               && sync.Mappings.All(mapping => !string.IsNullOrWhiteSpace(mapping.SourceShape)
                                   && !string.IsNullOrWhiteSpace(mapping.LocalShape)));
                case 5:
                    return string.IsNullOrEmpty(_localReferenceError)
                           && _plan.OutfitRendererShapeChangers.All(owner =>
                               owner != null
                               && _analysis.FindRendererOwner(owner.OwnerKey) != null
                               && owner.ShapeChanges.Count > 0)
                           && EnumerateShapeChangerSettings().All(IsShapeChangerSettingComplete);
                default:
                    return true;
            }
        }

        private IEnumerable<ShapeChangerSettingPlan> EnumerateShapeChangerSettings()
        {
            if (_plan == null) return Enumerable.Empty<ShapeChangerSettingPlan>();
            return _plan.MasterShapeChanges
                .Concat(_plan.OutfitRendererShapeChangers
                    .Where(owner => owner != null)
                    .SelectMany(owner => owner.ShapeChanges))
                .Concat(_plan.PartToggles.SelectMany(part => part.ShapeChanges));
        }

        private static bool IsShapeChangerSettingComplete(ShapeChangerSettingPlan setting)
        {
            if (setting == null
                || string.IsNullOrWhiteSpace(setting.ShapeName)
                || float.IsNaN(setting.Value)
                || float.IsInfinity(setting.Value)
                || setting.Value < 0f
                || setting.Value > 100f)
            {
                return false;
            }

            return setting.Source == PartTargetSource.OutfitPrefab
                   || setting.SceneRendererReference != null;
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
                RemoveSceneShapeChangerSettings();
                _selectedAvatar = null;
                _placement = null;
                RefreshSceneReferencesAfterChange();
            }
        }

        private void SetSelectedAvatar(VRCAvatarDescriptor avatar)
        {
            if (_selectedAvatar != avatar)
            {
                _plan?.BlendshapeSyncs.Clear();
                _blendshapeSourceRenderers.Clear();
                RemoveSceneShapeChangerSettings();
                _blendshapeUiCache.InvalidateAvatar();
            }
            _selectedAvatar = avatar;
            _placement = avatar != null ? avatar.transform : null;
            _sceneTargetRows.Clear();
            RefreshSceneReferencesAfterChange();
            UpdateApplyPreviewIfOpen();
        }

        private bool CanAttemptApplyPreview()
        {
            return _sourcePrefab != null
                   && _analysis != null
                   && _selectedAvatar != null
                   && _placement != null
                   && !EditorApplication.isPlayingOrWillChangePlaymode
                   && PrefabStageUtility.GetCurrentPrefabStage() == null;
        }

        private bool TryCreateApplyPreviewRequest(
            out OutfitPreviewRequest request,
            out string error)
        {
            EnsureSceneReferencesCurrent();
            if (!TryValidateMasterSceneTargetsForPreview(
                    _sceneTargetRows,
                    _plan.MasterSceneTargets,
                    _localReferenceError,
                    out error))
            {
                request = null;
                return false;
            }

            return OutfitPreviewRequest.TryCreate(
                _sourcePrefab,
                _selectedAvatar != null ? _selectedAvatar.gameObject : null,
                _placement,
                _plan.MasterSceneTargets,
                _analysis != null ? _analysis.DependencyHash : null,
                _plan != null && _plan.MasterDefaultOn,
                _analysis,
                _plan != null ? _plan.PartToggles : null,
                _plan != null ? _plan.MasterShapeChanges : null,
                _plan != null ? _plan.OutfitRendererShapeChangers : null,
                out request,
                out error);
        }

        internal static bool TryValidateMasterSceneTargetsForPreview(
            IEnumerable<MasterSceneTargetRow> rows,
            IEnumerable<MasterSceneTargetPlan> configuredTargets,
            string localReferenceError,
            out string error)
        {
            if (!string.IsNullOrEmpty(localReferenceError))
            {
                error = localReferenceError;
                return false;
            }

            var rowArray = (rows ?? Enumerable.Empty<MasterSceneTargetRow>()).ToArray();
            var targetArray = (configuredTargets ?? Enumerable.Empty<MasterSceneTargetPlan>())
                .ToArray();
            var validTargetCount = targetArray.Count(target => target?.Reference != null);
            if (rowArray.Any(row => row?.Target == null)
                || validTargetCount != rowArray.Length
                || validTargetCount != targetArray.Length)
            {
                error = "Scene対象の参照をすべて解決できません。対象を再指定してください。";
                return false;
            }

            error = null;
            return true;
        }

        private void OpenApplyPreview()
        {
            if (TryCreateApplyPreviewRequest(out var request, out var error))
            {
                OutfitApplyPreviewWindow.OpenOrUpdate(this, request);
                NotifyHighlightedPartTargetsChanged();
                return;
            }

            EditorUtility.DisplayDialog("適用プレビューを開けません", error, "閉じる");
        }

        private void UpdateApplyPreviewIfOpen()
        {
            if (TryCreateApplyPreviewRequest(out var request, out var error))
                OutfitApplyPreviewWindow.UpdateIfOpen(this, request);
            else
                OutfitApplyPreviewWindow.SetErrorIfOpen(this, error);
        }

        private void NotifyHighlightedPartTargetsChanged()
        {
            OutfitApplyPreviewWindow.UpdateHighlightedTargetsIfOpen(
                this,
                _selectedPartTargets.Values);
        }

        private void RefreshSceneReferencesAfterChange()
        {
            _sceneReferenceRefreshGate.Invalidate();
            EnsureSceneReferencesCurrent();
        }

        private void EnsureSceneReferencesCurrent()
        {
            _sceneReferenceRefreshGate.EnsureCurrent(UpdateSceneReferences);
        }

        private void UpdateSceneReferences()
        {
            _localReferenceError = null;
            _plan.AvatarReference = TryCreateSceneReference(_selectedAvatar != null ? _selectedAvatar.gameObject : null, "対象アバター");
            _plan.PlacementReference = TryCreateSceneReference(_placement != null ? _placement.gameObject : null, "配置Transform");
            _plan.MasterSceneTargets.Clear();
            _sceneTargetReferencesByInstanceId.Clear();
            foreach (var row in _sceneTargetRows)
            {
                if (row.Target == null)
                {
                    SetLocalReferenceError("Scene対象に未指定の行があります。");
                    continue;
                }
                var reference = TryCreateSceneReference(row.Target, "Scene対象 " + row.Target.name);
                if (reference != null)
                {
                    _plan.MasterSceneTargets.Add(
                        new MasterSceneTargetPlan(reference, row.ActiveWhenOn));
                    _sceneTargetReferencesByInstanceId[row.Target.GetInstanceID()] = reference;
                }
            }
            PruneSelectedSceneTargets();

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

            foreach (var setting in EnumerateShapeChangerSettings()
                         .Where(candidate => candidate != null
                                             && candidate.Source == PartTargetSource.SceneObject))
            {
                if (!_shapeChangerSceneRenderers.TryGetValue(setting, out var renderer)
                    || renderer == null)
                {
                    setting.SceneRendererReference = null;
                    continue;
                }

                setting.SceneRendererReference = TryCreateSceneReference(
                    renderer.gameObject,
                    "Shape Changer対象 " + renderer.name);
            }

            InvalidateReviewValidation();
        }

        private void RemoveSceneShapeChangerSettings()
        {
            if (_plan == null) return;
            _plan.MasterShapeChanges.RemoveAll(
                setting => setting?.Source == PartTargetSource.SceneObject);
            foreach (var part in _plan.PartToggles)
            {
                part.ShapeChanges.RemoveAll(
                    setting => setting?.Source == PartTargetSource.SceneObject);
            }
            foreach (var owner in _plan.OutfitRendererShapeChangers)
            {
                if (owner == null) continue;
                owner.ShapeChanges.RemoveAll(
                    setting => setting?.Source == PartTargetSource.SceneObject);
            }
            _shapeChangerSceneRenderers.Clear();
        }

        private void PrepareReviewValidation()
        {
            EnsureSceneReferencesCurrent();
            _reviewValidationCache.Invalidate();
            _reviewValidationCache.Get(() => OutfitSetupValidator.Instance.Validate(_plan));
        }

        private ValidationResult GetReviewValidation()
        {
            return _reviewValidationCache.Get(() => OutfitSetupValidator.Instance.Validate(_plan));
        }

        private void InvalidateReviewValidation()
        {
            _reviewValidationCache.Invalidate();
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
            var added = false;
            foreach (var candidate in _analysis.PartCandidates)
            {
                var part = new PartTogglePlan(candidate.Name, new[] { candidate.TargetKey });
                _plan.PartToggles.Add(part);
                added = true;
            }
            if (added)
            {
                UpdateApplyPreviewIfOpen();
                InvalidateReviewValidation();
            }
        }

        private void AddSelectedPartGroup()
        {
            var targets = _selectedPartTargets.Values
                .Where(target => target != null)
                .OrderBy(target => target.StableId, StringComparer.Ordinal)
                .Select(ClonePartTarget)
                .ToArray();
            if (targets.Length == 0) return;
            var part = new PartTogglePlan(
                targets.Length == 1 ? GetPartTargetShortName(targets[0]) : "パーツ",
                targets);
            _plan.PartToggles.Add(part);
            _selectedPartTargets.Clear();
            UpdateApplyPreviewIfOpen();
            InvalidateReviewValidation();
            NotifyHighlightedPartTargetsChanged();
        }


        private void PruneSelectedSceneTargets()
        {
            var currentSceneIds = new HashSet<string>(
                _plan.MasterSceneTargets
                    .Where(target => target?.Reference != null)
                    .Select(target => target.Reference.GlobalObjectId),
                StringComparer.Ordinal);
            var removed = _selectedPartTargets
                .Where(pair => pair.Value.Source == PartTargetSource.SceneObject
                               && (pair.Value.SceneReference == null
                                   || !currentSceneIds.Contains(
                                       pair.Value.SceneReference.GlobalObjectId)))
                .Select(pair => pair.Key)
                .ToArray();
            if (removed.Length == 0) return;
            foreach (var stableId in removed) _selectedPartTargets.Remove(stableId);
            NotifyHighlightedPartTargetsChanged();
        }

        private PartToggleTargetPlan ClonePartTarget(PartToggleTargetPlan target)
        {
            return target.Source == PartTargetSource.OutfitPrefab
                ? PartToggleTargetPlan.ForPrefab(target.PrefabKey, target.ActiveWhenOn)
                : PartToggleTargetPlan.ForScene(target.SceneReference, target.ActiveWhenOn);
        }

        private string GetPartTargetDisplayName(PartToggleTargetPlan target)
        {
            if (target == null) return "<未解決>";
            if (target.Source == PartTargetSource.OutfitPrefab)
                return _analysis.FindTarget(target.PrefabKey)?.DisplayPath ?? "<Prefab対象未解決>";

            if (target.Source != PartTargetSource.SceneObject
                || target.SceneReference == null)
            {
                return "<Scene対象未解決>";
            }

            var stillConfigured = _plan.MasterSceneTargets.Any(configuredTarget =>
                configuredTarget?.Reference != null
                && string.Equals(
                    configuredTarget.Reference.GlobalObjectId,
                    target.SceneReference.GlobalObjectId,
                    StringComparison.Ordinal));
            return target.SceneReference.DisplayName
                   + (stillConfigured ? "  [Scene]" : "  [Scene/ステップ3から削除済み]");
        }

        private string GetPartTargetShortName(PartToggleTargetPlan target)
        {
            if (target.Source == PartTargetSource.OutfitPrefab)
                return _analysis.FindTarget(target.PrefabKey)?.Name ?? "パーツ";

            var displayName = target.SceneReference?.DisplayName;
            if (string.IsNullOrEmpty(displayName)) return "パーツ";
            var separator = displayName.LastIndexOf('/');
            return separator >= 0 ? displayName.Substring(separator + 1) : displayName;
        }

        private void Generate()
        {
            try
            {
                EnsureSceneReferencesCurrent();
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
