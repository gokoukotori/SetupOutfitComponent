using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitApplyPreviewWindow : SceneView
    {
        internal static readonly Quaternion FrontViewRotation =
            Quaternion.Euler(0f, 180f, 0f);

        private static OutfitApplyPreviewWindow _activeWindow;

        private OutfitSetupWindow _owner;
        private OutfitPreviewRequest _request;
        private OutfitPreviewRenderMirror _mirror;
        private OutfitVisibilityPreviewFilter _exclusionFilter;
        private OutfitPartVisibilityPreviewFilter _partFilter;
        private PreviewSession _previewSession;
        private IDisposable _exclusionFilterRegistration;
        private IDisposable _partFilterRegistration;
        private TargetAvatarVisibility _targetAvatarVisibility;
        private readonly Dictionary<string, bool> _partStates =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private Vector2 _partScrollPosition;
        private bool _previewOn;
        private string _error;
        private int _rebuildCount;
        private bool _isRebuilding;

        internal static OutfitApplyPreviewWindow ActiveWindowForTests => _activeWindow;
        internal int RebuildCountForTests => _rebuildCount;
        internal OutfitPreviewRenderMirror MirrorForTests => _mirror;
        internal OutfitVisibilityPreviewFilter FilterForTests => _exclusionFilter;
        internal OutfitPartVisibilityPreviewFilter PartFilterForTests => _partFilter;
        internal IReadOnlyDictionary<string, bool> PartStatesForTests => _partStates;
        internal bool PreviewOnForTests => _previewOn;

        internal void SetPreviewOnForTests(bool previewOn) => SetPreviewOn(previewOn);
        internal void SetPartPreviewOnForTests(string key, bool previewOn) =>
            SetPartPreviewOn(key, previewOn);
        internal void ResetPartStatesForTests() => ResetPartStates(_request);

        internal static void OpenOrUpdate(
            OutfitSetupWindow owner,
            OutfitPreviewRequest request,
            bool forceOutfitOn = false)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (_activeWindow == null)
            {
                _activeWindow = CreateWindow<OutfitApplyPreviewWindow>();
                _activeWindow.minSize = new Vector2(640f, 480f);
                _activeWindow.titleContent = new GUIContent("衣装適用プレビュー");
                _activeWindow.Show();
            }

            _activeWindow._owner = owner;
            _activeWindow.ApplyRequest(request, true, forceOutfitOn);
            _activeWindow.Focus();
        }

        internal static void UpdateIfOpen(
            OutfitSetupWindow owner,
            OutfitPreviewRequest request)
        {
            if (_activeWindow == null || _activeWindow._owner != owner) return;
            _activeWindow.ApplyRequest(request, false, false);
        }

        internal static void SetErrorIfOpen(OutfitSetupWindow owner, string error)
        {
            if (_activeWindow == null || _activeWindow._owner != owner) return;
            _activeWindow.DisposePreviewResources();
            _activeWindow._request = null;
            _activeWindow._error = error;
            _activeWindow.Repaint();
        }

        internal static void CloseForOwner(OutfitSetupWindow owner)
        {
            if (_activeWindow == null || _activeWindow._owner != owner) return;
            _activeWindow.Close();
        }

        public override void OnEnable()
        {
            base.OnEnable();
            titleContent = new GUIContent("衣装適用プレビュー");
            drawGizmos = false;
            sceneViewState.alwaysRefresh = true;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private new void OnDestroy()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            DisposePreviewResources();
            if (_activeWindow == this) _activeWindow = null;
            _owner = null;
        }

        protected override void OnSceneGUI()
        {
            base.OnSceneGUI();

            if (Event.current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if ((Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp)
                && Event.current.keyCode == KeyCode.Delete)
            {
                Event.current.Use();
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode
                || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorApplication.delayCall += CloseIfAlive;
                return;
            }

            Handles.BeginGUI();
            var overlayHeight = Mathf.Min(
                Mathf.Max(position.height - 24f, 150f),
                _request != null && _request.Parts.Length > 0 ? 440f : 190f);
            GUILayout.BeginArea(new Rect(12f, 12f, 380f, overlayHeight), EditorStyles.helpBox);
            GUILayout.Label("衣装の表示適用プレビュー", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }
            else
            {
                var nextOn = GUILayout.Toggle(
                    _previewOn,
                    _previewOn ? "衣装ON（排他対象を非表示）" : "衣装OFF",
                    "Button",
                    GUILayout.Height(28f));
                if (nextOn != _previewOn) SetPreviewOn(nextOn);

                DrawPartControls();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("正面表示")) FramePreview(true);
                    if (GUILayout.Button("再構築") && _request != null) Rebuild(_request);
                }
            }

            EditorGUILayout.HelpBox(
                "表示・排他・個別パーツの視覚状態だけを確認します。MA装着処理、BlendShape Sync、最終NDMFビルド結果は反映しません。",
                MessageType.Info);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void DrawPartControls()
        {
            if (_request == null || _request.Parts.Length == 0) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("個別メニュー項目", EditorStyles.miniBoldLabel);
            _partScrollPosition = EditorGUILayout.BeginScrollView(
                _partScrollPosition,
                GUILayout.MinHeight(54f),
                GUILayout.MaxHeight(180f));
            foreach (var part in _request.Parts)
            {
                var selected = _partStates.TryGetValue(part.Key, out var partOn) && partOn;
                var label = string.IsNullOrWhiteSpace(part.Label) ? "<個別項目>" : part.Label;
                var next = GUILayout.Toggle(
                    selected,
                    label + (selected ? "：メニューON" : "：メニューOFF"),
                    "Button");
                if (next != selected) SetPartPreviewOn(part.Key, next);
                if (!part.InitialResolved)
                {
                    EditorGUILayout.HelpBox(
                        label + "の初期状態は未確定です。プレビュー上はOFFから開始しています。",
                        MessageType.Warning);
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(_request.Parts.Length == 0))
            {
                if (GUILayout.Button("個別項目を初期状態に戻す")) ResetPartStates(_request);
            }

            foreach (var warning in _partFilter?.Warnings ?? Array.Empty<string>())
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }

        private void ApplyRequest(
            OutfitPreviewRequest request,
            bool explicitOpen,
            bool forceOutfitOn)
        {
            if (_request != null
                && _mirror != null
                && _previewSession != null
                && _partFilter != null
                && _request.IsMirrorStructureEquivalentTo(request))
            {
                try
                {
                    var previous = _request;
                    var exclusionsChanged = !previous.HasEquivalentExclusionRendererSetTo(request);
                    var partRulesChanged = !HaveEquivalentPartRules(previous.Parts, request.Parts);
                    var partEnableBoundaryChanged =
                        HasPartTargets(previous.Parts) != HasPartTargets(request.Parts);

                    ReconcilePartStates(previous, request, explicitOpen);
                    _request = request;

                    if (explicitOpen)
                        _previewOn = forceOutfitOn || request.InitialOn;
                    else if (previous.InitialOn != request.InitialOn)
                        _previewOn = request.InitialOn;

                    if (exclusionsChanged) RebuildExclusionFilter();
                    if (partEnableBoundaryChanged)
                    {
                        RebuildPartFilter();
                    }
                    else if (partRulesChanged)
                    {
                        _partFilter.UpdateRules(
                            request.SourcePrefab,
                            request.DependencyHash,
                            _mirror.SourceToMirror,
                            request.Parts,
                            _previewOn,
                            _partStates);
                    }
                    else
                    {
                        _partFilter.SetPreviewState(_previewOn, _partStates);
                    }

                    _exclusionFilter?.SetPreviewOn(_previewOn);
                    _error = null;
                }
                catch (Exception exception)
                {
                    DisposePreviewResources();
                    _request = null;
                    _error = "プレビューを更新できませんでした: " + exception.Message;
                    Debug.LogException(exception);
                }

                Repaint();
                return;
            }

            Rebuild(request, forceOutfitOn && explicitOpen);
        }

        private void Rebuild(OutfitPreviewRequest request, bool forceOutfitOn = false)
        {
            if (_isRebuilding) return;
            _isRebuilding = true;
            try
            {
                RebuildCore(request, forceOutfitOn);
            }
            finally
            {
                _isRebuilding = false;
            }
        }

        private void RebuildCore(OutfitPreviewRequest request, bool forceOutfitOn)
        {
            DisposePreviewResources();
            _request = request;
            _previewOn = forceOutfitOn || request.InitialOn;
            ResetPartStates(request, false);
            _error = null;

            if (!OutfitPreviewRequest.TryCreate(
                    request.SourcePrefab,
                    request.AvatarRoot,
                    request.Placement,
                    request.Exclusions,
                    request.DependencyHash,
                    request.InitialOn,
                    out _,
                    out var error))
            {
                _request = null;
                _error = error;
                Repaint();
                return;
            }

            try
            {
                _mirror = new OutfitPreviewRenderMirror(request);
                _previewSession = PreviewSession.Current?.Fork(
                                      "Setup Outfit Component Apply Preview")
                                  ?? new PreviewSession();
                _targetAvatarVisibility = new TargetAvatarVisibility(request.AvatarRoot);
                _previewSession.HiddenRenderers = _targetAvatarVisibility.GetHiddenRenderers;
                RebuildExclusionFilter();
                RebuildPartFilter();
                _previewSession.OverrideCamera(camera);
                _rebuildCount++;
                FramePreview(false);
                Repaint();
            }
            catch (Exception exception)
            {
                DisposePreviewResources();
                _error = "プレビューを構築できませんでした: " + exception.Message;
                Debug.LogException(exception);
                Repaint();
            }
        }

        private static Renderer[] CollectExclusionRenderers(
            IEnumerable<GameObject> exclusions)
        {
            return (exclusions ?? Enumerable.Empty<GameObject>())
                .Where(exclusion => exclusion != null)
                .SelectMany(exclusion => exclusion.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer is MeshRenderer or SkinnedMeshRenderer)
                .Distinct()
                .OrderBy(renderer => renderer.GetInstanceID())
                .ToArray();
        }

        private void SetPreviewOn(bool previewOn)
        {
            _previewOn = previewOn;
            _exclusionFilter?.SetPreviewOn(previewOn);
            _partFilter?.SetPreviewState(previewOn, _partStates);
            Repaint();
        }

        private void SetPartPreviewOn(string key, bool previewOn)
        {
            _partStates[key] = previewOn;
            _partFilter?.SetPreviewState(_previewOn, _partStates);
            Repaint();
        }

        private void ResetPartStates(OutfitPreviewRequest request, bool repaint = true)
        {
            _partStates.Clear();
            if (request != null)
            {
                foreach (var part in request.Parts)
                    _partStates[part.Key] = part.InitialResolved && part.InitialOn;
            }

            _partFilter?.SetPreviewState(_previewOn, _partStates);
            if (repaint) Repaint();
        }

        private void ReconcilePartStates(
            OutfitPreviewRequest previous,
            OutfitPreviewRequest next,
            bool explicitOpen)
        {
            if (explicitOpen)
            {
                ResetPartStates(next, false);
                return;
            }

            var previousParts = previous.Parts.ToDictionary(part => part.Key, StringComparer.Ordinal);
            var reconciled = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var part in next.Parts)
            {
                if (!previousParts.TryGetValue(part.Key, out var oldPart)
                    || !_partStates.TryGetValue(part.Key, out var current))
                {
                    reconciled[part.Key] = part.InitialResolved && part.InitialOn;
                    continue;
                }

                var targetBehaviorChanged = !oldPart.Targets.SequenceEqual(part.Targets);
                var initialChanged = oldPart.InitialResolved != part.InitialResolved
                                     || oldPart.InitialOn != part.InitialOn;
                reconciled[part.Key] = initialChanged && !targetBehaviorChanged
                    ? part.InitialResolved && part.InitialOn
                    : current;
            }

            _partStates.Clear();
            foreach (var pair in reconciled) _partStates[pair.Key] = pair.Value;
        }

        private void RebuildExclusionFilter()
        {
            _exclusionFilterRegistration?.Dispose();
            _exclusionFilterRegistration = null;
            _exclusionFilter = null;
            if (_previewSession == null || _request == null) return;

            _exclusionFilter = new OutfitVisibilityPreviewFilter(
                CollectExclusionRenderers(_request.Exclusions),
                _previewOn);
            _exclusionFilterRegistration = _previewSession.AddMutator(
                new SequencePoint { DebugString = "Setup Outfit Component Exclusion Preview" },
                _exclusionFilter);
        }

        private void RebuildPartFilter()
        {
            _partFilterRegistration?.Dispose();
            _partFilterRegistration = null;
            _partFilter = null;
            if (_previewSession == null || _request == null || _mirror == null) return;

            _partFilter = new OutfitPartVisibilityPreviewFilter(
                _mirror.OutfitRenderers,
                _request.SourcePrefab,
                _request.DependencyHash,
                _mirror.SourceToMirror,
                _request.Parts,
                _previewOn,
                _partStates);
            _partFilterRegistration = _previewSession.AddMutator(
                new SequencePoint { DebugString = "Setup Outfit Component Part Preview" },
                _partFilter);
        }

        private static bool HaveEquivalentPartRules(
            IEnumerable<OutfitPartPreviewSnapshot> left,
            IEnumerable<OutfitPartPreviewSnapshot> right)
        {
            var leftArray = left.ToArray();
            var rightArray = right.ToArray();
            if (leftArray.Length != rightArray.Length) return false;
            for (var index = 0; index < leftArray.Length; index++)
            {
                if (!string.Equals(leftArray[index].Key, rightArray[index].Key, StringComparison.Ordinal)
                    || !leftArray[index].Targets.SequenceEqual(rightArray[index].Targets))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasPartTargets(IEnumerable<OutfitPartPreviewSnapshot> parts)
        {
            return parts.Any(part => part.Targets.Length > 0);
        }

        private void FramePreview(bool resetRotation)
        {
            if (_request == null) return;
            var bounds = CalculatePreviewBounds(_request.AvatarRoot, _mirror);
            pivot = bounds.center;
            size = Mathf.Max(bounds.extents.magnitude, 0.5f);
            if (resetRotation) rotation = FrontViewRotation;
            Repaint();
        }

        private static Bounds CalculatePreviewBounds(
            GameObject avatarRoot,
            OutfitPreviewRenderMirror mirror)
        {
            var initialized = false;
            var bounds = new Bounds(avatarRoot.transform.position, Vector3.one);
            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true)
                         .Concat(mirror?.OutfitRenderers ?? Array.Empty<Renderer>()))
            {
                if (renderer == null) continue;
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private void DisposePreviewResources()
        {
            _partFilterRegistration?.Dispose();
            _partFilterRegistration = null;
            _partFilter = null;

            _exclusionFilterRegistration?.Dispose();
            _exclusionFilterRegistration = null;
            _exclusionFilter = null;
            _targetAvatarVisibility = null;

            _previewSession?.Dispose();
            _previewSession = null;

            _mirror?.Dispose();
            _mirror = null;
            _partStates.Clear();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode
                || state == PlayModeStateChange.EnteredPlayMode)
            {
                Close();
            }
        }

        private void OnBeforeAssemblyReload()
        {
            Close();
        }

        private void CloseIfAlive()
        {
            if (this != null) Close();
        }

        private sealed class TargetAvatarVisibility
        {
            private readonly PublishedValue<GameObject> _targetAvatar;

            internal TargetAvatarVisibility(GameObject targetAvatar)
            {
                _targetAvatar = new PublishedValue<GameObject>(
                    targetAvatar,
                    "SetupOutfitComponent/PreviewAvatar");
            }

            internal ImmutableHashSet<Renderer> GetHiddenRenderers(ComputeContext context)
            {
                var target = context.Observe(
                    _targetAvatar,
                    avatar => avatar,
                    (left, right) => left == right);
                return context.GetAvatarRoots()
                    .Where(root => root != target)
                    .SelectMany(root => context.GetComponentsInChildren<Renderer>(root, true))
                    .ToImmutableHashSet();
            }
        }
    }
}
