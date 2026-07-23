using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal static class OutfitSetupMenu
    {
        private const string MenuPath = "Assets/Setup Outfit Component/衣装セットアップ...";

        [MenuItem(MenuPath, false, 2000)]
        private static void Open()
        {
            var sourcePrefab = GetSelectedPrefab();
            if (sourcePrefab == null)
            {
                EditorUtility.DisplayDialog("衣装セットアップ", "Project上のPrefabを1つ選択してください。", "閉じる");
                return;
            }

            OutfitSetupWindow.Open(sourcePrefab);
        }

        [MenuItem(MenuPath, true)]
        internal static bool ValidateOpen()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                   && PrefabStageUtility.GetCurrentPrefabStage() == null
                   && GetSelectedPrefab() != null;
        }

        internal static GameObject GetSelectedPrefab()
        {
            if (Selection.objects == null || Selection.objects.Length != 1) return null;
            if (!(Selection.activeObject is GameObject gameObject)) return null;

            var assetPath = AssetDatabase.GetAssetPath(gameObject);
            if (string.IsNullOrEmpty(assetPath)
                || !string.Equals(Path.GetExtension(assetPath), ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != gameObject) return null;

            var assetType = PrefabUtility.GetPrefabAssetType(gameObject);
            return assetType == PrefabAssetType.Regular || assetType == PrefabAssetType.Variant
                ? gameObject
                : null;
        }
    }
}
