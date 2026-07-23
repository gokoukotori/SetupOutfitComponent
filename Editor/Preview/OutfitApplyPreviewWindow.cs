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
        private OutfitVisibilityPreviewFilter _filter;
        private PreviewSession _previewSession;
        private IDisposable _filterRegistration;
        private TargetAvatarVisibility _targetAvatarVisibility;
        private bool _previewOn;
        private string _error;
        private int _rebuildCount;
        private bool _isRebuilding;

        internal static OutfitApplyPreviewWindow ActiveWindowForTests => _activeWindow;
        internal int RebuildCountForTests => _rebuildCount;
        internal OutfitPreviewRenderMirror MirrorForTests => _mirror;
        internal OutfitVisibilityPreviewFilter FilterForTests => _filter;

        internal static void OpenOrUpdate(
            OutfitSetupWindow owner,
            OutfitPreviewRequest request)
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
            _activeWindow.ApplyRequest(request);
            _activeWindow.Focus();
        }

        internal static void UpdateIfOpen(
            OutfitSetupWindow owner,
            OutfitPreviewRequest request)
        {
            if (_activeWindow == null || _activeWindow._owner != owner) return;
            _activeWindow.ApplyRequest(request);
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
            GUILayout.BeginArea(new Rect(12f, 12f, 360f, 150f), EditorStyles.helpBox);
            GUILayout.Label("衣装の全体ON/OFF適用プレビュー", EditorStyles.boldLabel);
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

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("正面表示")) FramePreview(true);
                    if (GUILayout.Button("再構築") && _request != null) Rebuild(_request);
                }
            }

            EditorGUILayout.HelpBox(
                "表示と排他動作だけを確認します。MA装着処理、個別パーツ、BlendShape Sync、最終NDMFビルド結果は反映しません。",
                MessageType.Info);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void ApplyRequest(OutfitPreviewRequest request)
        {
            if (_request != null && _request.IsStructurallyEquivalentTo(request))
            {
                _request = request;
                SetPreviewOn(request.InitialOn);
                return;
            }

            Rebuild(request);
        }

        private void Rebuild(OutfitPreviewRequest request)
        {
            if (_isRebuilding) return;
            _isRebuilding = true;
            try
            {
                RebuildCore(request);
            }
            finally
            {
                _isRebuilding = false;
            }
        }

        private void RebuildCore(OutfitPreviewRequest request)
        {
            DisposePreviewResources();
            _request = request;
            _previewOn = request.InitialOn;
            _error = null;

            if (!OutfitPreviewRequest.TryCreate(
                    request.SourcePrefab,
                    request.AvatarRoot,
                    request.Placement,
                    request.Exclusions,
                    request.DependencyHash,
                    request.InitialOn,
                    out var refreshed,
                    out var error))
            {
                _request = null;
                _error = error;
                Repaint();
                return;
            }

            try
            {
                _request = refreshed;
                _mirror = new OutfitPreviewRenderMirror(refreshed);
                var exclusions = CollectExclusionRenderers(refreshed.Exclusions);
                _filter = new OutfitVisibilityPreviewFilter(
                    _mirror.OutfitRenderers,
                    exclusions,
                    _previewOn);

                _previewSession = PreviewSession.Current?.Fork(
                                      "Setup Outfit Component Apply Preview")
                                  ?? new PreviewSession();
                _targetAvatarVisibility = new TargetAvatarVisibility(refreshed.AvatarRoot);
                _previewSession.HiddenRenderers = _targetAvatarVisibility.GetHiddenRenderers;
                _filterRegistration = _previewSession.AddMutator(
                    new SequencePoint { DebugString = "Setup Outfit Component Apply Preview" },
                    _filter);
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
            _filter?.SetPreviewOn(previewOn);
            Repaint();
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
            _filterRegistration?.Dispose();
            _filterRegistration = null;
            _filter = null;
            _targetAvatarVisibility = null;

            _previewSession?.Dispose();
            _previewSession = null;

            _mirror?.Dispose();
            _mirror = null;
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
