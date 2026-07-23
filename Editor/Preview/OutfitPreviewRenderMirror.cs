using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal sealed class OutfitPreviewRenderMirror : IDisposable
    {
        private readonly Dictionary<Transform, Transform> _sourceToMirror =
            new Dictionary<Transform, Transform>();
        private readonly List<Renderer> _outfitRenderers = new List<Renderer>();
        private Scene _previewScene;
        private GameObject _previewRoot;
        private bool _disposed;

        internal OutfitPreviewRenderMirror(OutfitPreviewRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            _previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                Build(request);
                BuildCount++;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static int BuildCount { get; private set; }
        internal Scene PreviewScene => _previewScene;
        internal GameObject PreviewRoot => _previewRoot;
        internal IReadOnlyList<Renderer> OutfitRenderers => _outfitRenderers;
        internal IReadOnlyDictionary<Transform, Transform> SourceToMirror => _sourceToMirror;

        internal static void ResetBuildCountForTests()
        {
            BuildCount = 0;
        }

        internal Bounds CalculateBounds()
        {
            var initialized = false;
            var bounds = new Bounds(Vector3.zero, Vector3.one);
            foreach (var renderer in _outfitRenderers)
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

        private void Build(OutfitPreviewRequest request)
        {
            _previewRoot = CreateObject("__SetupOutfitPreview__");

            var placementMirror = BuildPlacementHierarchy(
                request.AvatarRoot.transform,
                request.Placement,
                _previewRoot.transform);
            var outfitRoot = CloneTransformHierarchy(
                request.SourcePrefab.transform,
                placementMirror,
                true);
            outfitRoot.gameObject.SetActive(true);
            CloneRenderers(request.SourcePrefab.transform);
        }

        private Transform BuildPlacementHierarchy(
            Transform avatarRoot,
            Transform placement,
            Transform previewParent)
        {
            var chain = new Stack<Transform>();
            var current = placement;
            while (current != null)
            {
                chain.Push(current);
                if (current == avatarRoot) break;
                current = current.parent;
            }

            if (chain.Count == 0 || chain.Peek() != avatarRoot)
                throw new InvalidOperationException("配置先を対象アバター内で解決できませんでした。");

            Transform parent = previewParent;
            var isAvatarRoot = true;
            while (chain.Count > 0)
            {
                var source = chain.Pop();
                var mirror = CreateObject(source.name).transform;
                mirror.SetParent(parent, false);
                if (isAvatarRoot)
                {
                    mirror.SetPositionAndRotation(source.position, source.rotation);
                    mirror.localScale = source.lossyScale;
                    isAvatarRoot = false;
                }
                else
                {
                    CopyLocalTransform(source, mirror);
                }

                mirror.gameObject.SetActive(source.gameObject.activeSelf);
                parent = mirror;
            }

            return parent;
        }

        private Transform CloneTransformHierarchy(
            Transform source,
            Transform parent,
            bool isOutfitRoot)
        {
            var mirror = CreateObject(source.name).transform;
            mirror.SetParent(parent, false);
            CopyLocalTransform(source, mirror);
            mirror.gameObject.SetActive(isOutfitRoot || source.gameObject.activeSelf);
            _sourceToMirror[source] = mirror;

            foreach (Transform child in source)
                CloneTransformHierarchy(child, mirror, false);

            return mirror;
        }

        private void CloneRenderers(Transform sourceRoot)
        {
            foreach (var sourceTransform in sourceRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!_sourceToMirror.TryGetValue(sourceTransform, out var mirrorTransform)) continue;

                var sourceMeshFilter = sourceTransform.GetComponent<MeshFilter>();
                var sourceMeshRenderer = sourceTransform.GetComponent<MeshRenderer>();
                if (sourceMeshRenderer != null
                    && sourceMeshFilter != null
                    && sourceMeshFilter.sharedMesh != null)
                {
                    var mirrorFilter = mirrorTransform.gameObject.AddComponent<MeshFilter>();
                    EditorUtility.CopySerialized(sourceMeshFilter, mirrorFilter);

                    var mirrorRenderer = mirrorTransform.gameObject.AddComponent<MeshRenderer>();
                    EditorUtility.CopySerialized(sourceMeshRenderer, mirrorRenderer);
                    RemapProbeAnchor(sourceMeshRenderer, mirrorRenderer);
                    _outfitRenderers.Add(mirrorRenderer);
                }

                var sourceSkinnedRenderer = sourceTransform.GetComponent<SkinnedMeshRenderer>();
                if (sourceSkinnedRenderer == null || sourceSkinnedRenderer.sharedMesh == null) continue;

                var mirrorSkinnedRenderer = mirrorTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                EditorUtility.CopySerialized(sourceSkinnedRenderer, mirrorSkinnedRenderer);
                mirrorSkinnedRenderer.bones = sourceSkinnedRenderer.bones
                    .Select(RemapTransform)
                    .ToArray();
                mirrorSkinnedRenderer.rootBone = RemapTransform(sourceSkinnedRenderer.rootBone);
                RemapProbeAnchor(sourceSkinnedRenderer, mirrorSkinnedRenderer);
                for (var index = 0; index < sourceSkinnedRenderer.sharedMesh.blendShapeCount; index++)
                {
                    mirrorSkinnedRenderer.SetBlendShapeWeight(
                        index,
                        sourceSkinnedRenderer.GetBlendShapeWeight(index));
                }

                _outfitRenderers.Add(mirrorSkinnedRenderer);
            }
        }

        private Transform RemapTransform(Transform source)
        {
            if (source == null) return null;
            return _sourceToMirror.TryGetValue(source, out var mirror) ? mirror : null;
        }

        private void RemapProbeAnchor(Renderer source, Renderer mirror)
        {
            if (source.probeAnchor == null) return;
            mirror.probeAnchor = RemapTransform(source.probeAnchor);
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (gameObject.scene != _previewScene)
                SceneManager.MoveGameObjectToScene(gameObject, _previewScene);
            return gameObject;
        }

        private static void CopyLocalTransform(Transform source, Transform mirror)
        {
            mirror.localPosition = source.localPosition;
            mirror.localRotation = source.localRotation;
            mirror.localScale = source.localScale;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _outfitRenderers.Clear();
            _sourceToMirror.Clear();
            _previewRoot = null;

            if (_previewScene.IsValid())
                EditorSceneManager.ClosePreviewScene(_previewScene);
            _previewScene = default;
        }
    }
}
