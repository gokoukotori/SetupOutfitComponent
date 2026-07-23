using System;
using UnityEditor;
using UnityEngine;

namespace Gokoukotori.SetupComponents.Editor
{
    internal sealed class SceneObjectReference : IEquatable<SceneObjectReference>
    {
        private SceneObjectReference(string globalObjectId, string displayName)
        {
            GlobalObjectId = globalObjectId;
            DisplayName = displayName;
        }

        internal string GlobalObjectId { get; }
        internal string DisplayName { get; }

        internal static SceneObjectReference Create(GameObject sceneObject)
        {
            if (sceneObject == null) throw new ArgumentNullException(nameof(sceneObject));
            if (EditorUtility.IsPersistent(sceneObject) || !sceneObject.scene.IsValid())
            {
                throw new ArgumentException("A loaded scene object is required.", nameof(sceneObject));
            }

            var globalId = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(sceneObject).ToString();
            var reference = new SceneObjectReference(globalId, GetHierarchyPath(sceneObject.transform));
            if (reference.Resolve() != sceneObject)
            {
                throw new InvalidOperationException(
                    "The scene object cannot be resolved by GlobalObjectId. Save the scene and try again.");
            }

            return reference;
        }

        internal GameObject Resolve()
        {
            if (string.IsNullOrEmpty(GlobalObjectId)
                || !UnityEditor.GlobalObjectId.TryParse(GlobalObjectId, out var parsed))
            {
                return null;
            }

            var resolved = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
            if (resolved is GameObject gameObject) return gameObject;
            if (resolved is Component component) return component.gameObject;
            return null;
        }

        public bool Equals(SceneObjectReference other)
        {
            return other != null && string.Equals(GlobalObjectId, other.GlobalObjectId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as SceneObjectReference);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(GlobalObjectId ?? string.Empty);
        public override string ToString() => DisplayName + " (" + GlobalObjectId + ")";

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
    }
}
