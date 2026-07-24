using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

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
        private OutfitVisibilityPreviewFilter _sceneVisibilityFilter;
        private OutfitPartVisibilityPreviewFilter _partFilter;
        private PreviewSession _previewSession;
        private IDisposable _sceneVisibilityFilterRegistration;
        private IDisposable _partFilterRegistration;
        private TargetAvatarVisibility _targetAvatarVisibility;
        private readonly Dictionary<string, PartToggleTargetPlan> _highlightTargets =
            new Dictionary<string, PartToggleTargetPlan>(StringComparer.Ordinal);
        private readonly OutfitPreviewHighlightCache _highlightCache =
            new OutfitPreviewHighlightCache();
        private readonly Dictionary<string, bool> _partStates =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private Vector2 _partScrollPosition;
        private bool _previewOn;
        private string _error;
        private int _rebuildCount;
        private bool _isRebuilding;

        private static readonly Color HighlightColor =
            new Color(1f, 0.65f, 0.12f, 1f);
        private static readonly SequencePoint SceneVisibilityPreviewSequencePoint =
            new SequencePoint { DebugString = "Setup Outfit Component Scene Visibility Preview" };
        private static readonly SequencePoint PartPreviewSequencePoint =
            new SequencePoint { DebugString = "Setup Outfit Component Part Preview" };

        internal static OutfitApplyPreviewWindow ActiveWindowForTests => _activeWindow;
        internal int RebuildCountForTests => _rebuildCount;
        internal OutfitPreviewRenderMirror MirrorForTests => _mirror;
        internal OutfitVisibilityPreviewFilter SceneVisibilityFilterForTests =>
            _sceneVisibilityFilter;
        internal OutfitVisibilityPreviewFilter FilterForTests => _sceneVisibilityFilter;
        internal OutfitPartVisibilityPreviewFilter PartFilterForTests => _partFilter;
        internal OutfitPreviewHighlightCache HighlightCacheForTests => _highlightCache;
        internal IReadOnlyList<Renderer> HighlightedVisibleRenderersForTests =>
            GetHighlightedVisibleRenderers();
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

            if (_activeWindow._owner != null && _activeWindow._owner != owner)
                _activeWindow.ClearHighlightedTargets();

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

        internal static void UpdateHighlightedTargetsIfOpen(
            OutfitSetupWindow owner,
            IEnumerable<PartToggleTargetPlan> targets)
        {
            if (_activeWindow == null || _activeWindow._owner != owner) return;
            _activeWindow.UpdateHighlightedTargets(targets);
        }

        internal static void RefreshHighlightedTargetsIfOpen(OutfitSetupWindow owner)
        {
            if (_activeWindow == null || _activeWindow._owner != owner) return;
            _activeWindow._highlightCache.InvalidateResolution();
            _activeWindow.ResolveHighlightedTargets();
            _activeWindow.Repaint();
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

            DrawHighlightedTargets();

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
                    _previewOn ? "衣装ON（Scene表示設定を適用）" : "衣装OFF",
                    "Button",
                    GUILayout.Height(28f));
                if (nextOn != _previewOn) SetPreviewOn(nextOn);

                DrawPartControls();

                foreach (var warning in _highlightCache.Warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("正面表示")) FramePreview(true);
                    if (GUILayout.Button("再構築") && _request != null) Rebuild(_request);
                }
            }

            EditorGUILayout.HelpBox(
                "Scene表示・個別パーツの視覚状態だけを確認します。MA装着処理、BlendShape Sync、最終NDMFビルド結果は反映しません。",
                MessageType.Info);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void DrawPartControls()
        {
            if (_request == null) return;

            if (_request.Parts.Length > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("個別メニュー項目", EditorStyles.miniBoldLabel);
                _partScrollPosition = EditorGUILayout.BeginScrollView(
                    _partScrollPosition,
                    GUILayout.MinHeight(54f),
                    GUILayout.MaxHeight(180f));
                for (var partIndex = 0; partIndex < _request.Parts.Length; partIndex++)
                {
                    var part = _request.Parts[partIndex];
                    var selected = _partStates.TryGetValue(part.ItemId, out var partOn)
                                   && partOn;
                    var label = string.IsNullOrWhiteSpace(part.Label)
                        ? "<個別項目>"
                        : part.Label;
                    label = $"{partIndex + 1}. {label}";
                    var next = GUILayout.Toggle(
                        selected,
                        label + (selected ? "：メニューON" : "：メニューOFF"),
                        "Button");
                    if (next != selected) SetPartPreviewOn(part.ItemId, next);
                    if (!part.InitialResolved)
                    {
                        EditorGUILayout.HelpBox(
                            label + "の初期状態は未確定です。プレビュー上はOFFから開始しています。",
                            MessageType.Warning);
                    }
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("個別項目を初期状態に戻す"))
                    ResetPartStates(_request);

                foreach (var warning in _partFilter?.Warnings ?? Array.Empty<string>())
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            foreach (var warning in _sceneVisibilityFilter?.Warnings
                                    ?? Array.Empty<string>())
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }
        private void ApplyRequest(
            OutfitPreviewRequest request,
            bool explicitOpen,
            bool forceOutfitOn)
        {
            if (_request != null
                && _mirror != null
                && _previewSession != null
                && _sceneVisibilityFilter != null
                && _partFilter != null
                && _request.IsMirrorStructureEquivalentTo(request))
            {
                try
                {
                    var previous = _request;
                    var prefabPartRulesChanged = !HaveEquivalentPartRules(
                        previous.Parts,
                        request.Parts,
                        PartTargetSource.OutfitPrefab);

                    var prefabEnableBoundaryChanged =
                        HasPartTargets(previous.Parts, PartTargetSource.OutfitPrefab)
                        != HasPartTargets(request.Parts, PartTargetSource.OutfitPrefab);
                    var sceneRendererSetChanged =
                        !_sceneVisibilityFilter.HasEquivalentRendererSet(
                            request.MasterSceneTargets,
                            request.Parts);

                    ReconcilePartStates(previous, request, explicitOpen);
                    _request = request;

                    if (explicitOpen)
                        _previewOn = forceOutfitOn || request.InitialOn;
                    else if (previous.InitialOn != request.InitialOn)
                        _previewOn = request.InitialOn;

                    if (prefabEnableBoundaryChanged)
                    {
                        RebuildPartFilter();
                    }
                    else if (prefabPartRulesChanged)
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

                    if (sceneRendererSetChanged)
                    {
                        RebuildSceneVisibilityFilter();
                    }
                    else
                    {
                        _sceneVisibilityFilter.UpdateRules(
                            request.MasterSceneTargets,
                            request.Parts,
                            _previewOn,
                            _partStates);
                    }

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
                    request.MasterSceneTargets,
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
                RebuildSceneVisibilityFilter();
                RebuildPartFilter();
                ResolveHighlightedTargets();
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

        private void SetPreviewOn(bool previewOn)
        {
            _previewOn = previewOn;
            _sceneVisibilityFilter?.SetPreviewState(previewOn, _partStates);
            _partFilter?.SetPreviewState(previewOn, _partStates);
            Repaint();
        }

        private void SetPartPreviewOn(string key, bool previewOn)
        {
            _partStates[key] = previewOn;
            _sceneVisibilityFilter?.SetPreviewState(_previewOn, _partStates);
            _partFilter?.SetPreviewState(_previewOn, _partStates);
            Repaint();
        }

        private void UpdateHighlightedTargets(IEnumerable<PartToggleTargetPlan> targets)
        {
            var nextTargets = (targets ?? Enumerable.Empty<PartToggleTargetPlan>())
                .Where(target => target != null)
                .GroupBy(target => target.StableId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);
            if (_highlightTargets.Count == nextTargets.Count
                && _highlightTargets.Keys.All(nextTargets.ContainsKey))
            {
                return;
            }

            _highlightTargets.Clear();
            foreach (var pair in nextTargets) _highlightTargets[pair.Key] = pair.Value;
            ResolveHighlightedTargets();
            Repaint();
        }

        private void ClearHighlightedTargets()
        {
            _highlightTargets.Clear();
            _highlightCache.Clear();
        }

        private void ResolveHighlightedTargets()
        {
            if (_request == null || _mirror == null)
            {
                _highlightCache.Clear();
                return;
            }

            _highlightCache.Update(
                _request.SourcePrefab,
                _request.DependencyHash,
                _mirror.SourceToMirror,
                _highlightTargets.Values);
        }

        private IReadOnlyList<Renderer> GetHighlightedVisibleRenderers()
        {
            if (_partFilter == null || _sceneVisibilityFilter == null)
                return Array.Empty<Renderer>();
            return _highlightCache.Bindings
                .Where(IsHighlightBindingVisible)
                .Select(binding => binding.Renderer)
                .ToList();
        }

        private bool IsHighlightBindingVisible(OutfitPreviewHighlightBinding binding)
        {
            if (binding.Source == PartTargetSource.OutfitPrefab)
                return _partFilter.IsRendererVisible(binding.Renderer);
            if (_sceneVisibilityFilter.ControlsRenderer(binding.Renderer))
                return _sceneVisibilityFilter.IsRendererVisible(binding.Renderer);
            return !_previewOn
                   && binding.Renderer != null
                   && binding.Renderer.enabled
                   && binding.Renderer.gameObject.activeInHierarchy;
        }

        private void DrawHighlightedTargets()
        {
            if (_partFilter == null
                || _sceneVisibilityFilter == null
                || _highlightCache.Bindings.Count == 0)
            {
                return;
            }

            var previousColor = Handles.color;
            var previousMatrix = Handles.matrix;
            var previousZTest = Handles.zTest;
            try
            {
                Handles.color = HighlightColor;
                Handles.matrix = Matrix4x4.identity;
                Handles.zTest = CompareFunction.LessEqual;
                foreach (var binding in _highlightCache.Bindings)
                {
                    var renderer = binding.Renderer;
                    if (!IsHighlightBindingVisible(binding)) continue;
                    var bounds = renderer.bounds;
                    var padding = Mathf.Max(bounds.size.magnitude * 0.02f, 0.01f);
                    bounds.Expand(padding);
                    Handles.DrawWireCube(bounds.center, bounds.size);
                    Handles.Label(
                        bounds.center + Vector3.up * bounds.extents.y,
                        binding.DisplayPath,
                        EditorStyles.whiteMiniLabel);
                }
            }
            finally
            {
                Handles.color = previousColor;
                Handles.matrix = previousMatrix;
                Handles.zTest = previousZTest;
            }
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
            _sceneVisibilityFilter?.SetPreviewState(_previewOn, _partStates);
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

        private void RebuildSceneVisibilityFilter()
        {
            _sceneVisibilityFilterRegistration?.Dispose();
            _sceneVisibilityFilterRegistration = null;
            _sceneVisibilityFilter = null;
            if (_previewSession == null || _request == null) return;

            _sceneVisibilityFilter = new OutfitVisibilityPreviewFilter(
                _request.MasterSceneTargets,
                _request.Parts,
                _previewOn,
                _partStates);
            _sceneVisibilityFilterRegistration = _previewSession.AddMutator(
                SceneVisibilityPreviewSequencePoint,
                _sceneVisibilityFilter);
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
                PartPreviewSequencePoint,
                _partFilter);
        }

        private static bool HaveEquivalentPartRules(
            IEnumerable<OutfitPartPreviewSnapshot> left,
            IEnumerable<OutfitPartPreviewSnapshot> right,
            PartTargetSource source)
        {
            var leftParts = left
                .Select(part => new
                {
                    part.ItemId,
                    Targets = part.Targets
                        .Where(target => target.Source == source)
                        .OrderBy(target => target.StableId, StringComparer.Ordinal)
                        .ToArray()
                })
                .Where(part => part.Targets.Length > 0)
                .ToArray();
            var rightParts = right
                .Select(part => new
                {
                    part.ItemId,
                    Targets = part.Targets
                        .Where(target => target.Source == source)
                        .OrderBy(target => target.StableId, StringComparer.Ordinal)
                        .ToArray()
                })
                .Where(part => part.Targets.Length > 0)
                .ToArray();
            if (leftParts.Length != rightParts.Length) return false;
            for (var index = 0; index < leftParts.Length; index++)
            {
                if (!string.Equals(
                        leftParts[index].ItemId,
                        rightParts[index].ItemId,
                        StringComparison.Ordinal)
                    || !leftParts[index].Targets.SequenceEqual(rightParts[index].Targets))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasPartTargets(
            IEnumerable<OutfitPartPreviewSnapshot> parts,
            PartTargetSource source)
        {
            return parts.Any(part => part.Targets.Any(target => target.Source == source));
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

            _sceneVisibilityFilterRegistration?.Dispose();
            _sceneVisibilityFilterRegistration = null;
            _sceneVisibilityFilter = null;


            _targetAvatarVisibility = null;

            _previewSession?.Dispose();
            _previewSession = null;

            _mirror?.Dispose();
            _mirror = null;
            _partStates.Clear();
            _highlightCache.Clear();
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

    internal readonly struct OutfitPreviewHighlightBinding
    {
        internal PartTargetSource Source { get; }
        internal Renderer Renderer { get; }
        internal string DisplayPath { get; }

        internal OutfitPreviewHighlightBinding(
            PartTargetSource source,
            Renderer renderer,
            string displayPath)
        {
            Source = source;
            Renderer = renderer;
            DisplayPath = displayPath ?? string.Empty;
        }
    }

    internal sealed class OutfitPreviewHighlightCache
    {
        private GameObject _sourcePrefab;
        private string _dependencyHash = string.Empty;
        private IReadOnlyDictionary<Transform, Transform> _sourceToMirror;
        private string[] _targetIds = Array.Empty<string>();
        private OutfitPreviewHighlightBinding[] _bindings =
            Array.Empty<OutfitPreviewHighlightBinding>();
        private string[] _warnings = Array.Empty<string>();

        internal IReadOnlyList<OutfitPreviewHighlightBinding> Bindings => _bindings;
        internal IReadOnlyList<string> Warnings => _warnings;
        internal int ResolveCount { get; private set; }

        internal void Update(
            GameObject sourcePrefab,
            string dependencyHash,
            IReadOnlyDictionary<Transform, Transform> sourceToMirror,
            IEnumerable<PartToggleTargetPlan> targets)
        {
            var normalizedTargets = (targets ?? Enumerable.Empty<PartToggleTargetPlan>())
                .Where(target => target != null)
                .GroupBy(target => target.StableId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(target => target.StableId, StringComparer.Ordinal)
                .ToArray();
            var normalizedTargetIds = normalizedTargets
                .Select(target => target.StableId)
                .ToArray();
            dependencyHash ??= string.Empty;
            if (_sourcePrefab == sourcePrefab
                && string.Equals(_dependencyHash, dependencyHash, StringComparison.Ordinal)
                && ReferenceEquals(_sourceToMirror, sourceToMirror)
                && _targetIds.SequenceEqual(normalizedTargetIds))
            {
                return;
            }

            _sourcePrefab = sourcePrefab;
            _dependencyHash = dependencyHash;
            _sourceToMirror = sourceToMirror;
            _targetIds = normalizedTargetIds;
            ResolveCount++;

            var bindings = new List<OutfitPreviewHighlightBinding>();
            var warnings = new List<string>();
            if (sourcePrefab == null || sourceToMirror == null)
            {
                if (normalizedTargets.Length != 0)
                    warnings.Add("強調対象をPrefabまたはプレビューMirror上で解決できませんでした。");
                _bindings = bindings.ToArray();
                _warnings = warnings.ToArray();
                return;
            }

            var mirrorToSource = sourceToMirror.ToDictionary(
                pair => pair.Value,
                pair => pair.Key);
            var seenRenderers = new HashSet<Renderer>();
            foreach (var target in normalizedTargets)
            {
                if (target.Source == PartTargetSource.SceneObject)
                {
                    ResolveSceneTarget(target, bindings, warnings, seenRenderers);
                    continue;
                }

                var targetKey = target.PrefabKey;
                var sourceTarget = targetKey.Resolve(sourcePrefab, dependencyHash);
                if (sourceTarget == null)
                {
                    warnings.Add($"強調対象「{targetKey}」をPrefab上で解決できませんでした。");
                    continue;
                }

                var targetPath = GetRelativePath(
                    sourcePrefab.transform,
                    sourceTarget.transform);
                if (!sourceToMirror.TryGetValue(sourceTarget.transform, out var mirrorTarget)
                    || mirrorTarget == null)
                {
                    warnings.Add(
                        $"強調対象「{targetPath}」をプレビューMirror上で解決できませんでした。");
                    continue;
                }

                var rendererFound = false;
                foreach (var renderer in mirrorTarget.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                    rendererFound = true;
                    if (!seenRenderers.Add(renderer)) continue;
                    var displayPath = mirrorToSource.TryGetValue(
                        renderer.transform,
                        out var sourceTransform)
                        ? GetRelativePath(sourcePrefab.transform, sourceTransform)
                        : targetPath;
                    bindings.Add(new OutfitPreviewHighlightBinding(
                        PartTargetSource.OutfitPrefab,
                        renderer,
                        displayPath));
                }

                if (!rendererFound)
                {
                    warnings.Add(
                        $"強調対象「{targetPath}」の配下にプレビュー対応Rendererがありません。");
                }
            }

            _bindings = bindings.ToArray();
            _warnings = warnings
                .Distinct(StringComparer.Ordinal)
                .OrderBy(warning => warning, StringComparer.Ordinal)
                .ToArray();
        }

        internal void Clear()
        {
            _sourcePrefab = null;
            _dependencyHash = string.Empty;
            _sourceToMirror = null;
            _targetIds = Array.Empty<string>();
            _bindings = Array.Empty<OutfitPreviewHighlightBinding>();
            _warnings = Array.Empty<string>();
        }

        internal void InvalidateResolution()
        {
            _targetIds = Array.Empty<string>();
        }

        private static void ResolveSceneTarget(
            PartToggleTargetPlan target,
            ICollection<OutfitPreviewHighlightBinding> bindings,
            ICollection<string> warnings,
            ISet<Renderer> seenRenderers)
        {
            var sceneObject = target.SceneReference?.Resolve();
            if (sceneObject != null
                && sceneObject.scene.IsValid()
                && sceneObject.scene.isLoaded)
            {
                var rendererFound = false;
                foreach (var renderer in sceneObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                    rendererFound = true;
                    if (!seenRenderers.Add(renderer)) continue;
                    bindings.Add(new OutfitPreviewHighlightBinding(
                        PartTargetSource.SceneObject,
                        renderer,
                        GetHierarchyPath(renderer.transform)));
                }

                if (!rendererFound)
                {
                    warnings.Add(
                        $"強調対象「{target.SceneReference.DisplayName}」の配下にプレビュー対応Rendererがありません。");
                }
                return;
            }

            warnings.Add(
                $"強調対象「{target.SceneReference?.DisplayName ?? target.StableId}」をScene上で解決できませんでした。");
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return "<未解決>";
            if (root == target) return root.name;

            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", names) : target.name;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return "<未解決>";
            var names = new Stack<string>();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }
            return string.Join("/", names);
        }
    }
}
