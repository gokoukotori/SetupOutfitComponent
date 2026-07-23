using System;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.modular_avatar.core.editor;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal interface IOutfitSetupAdapter
    {
        void Configure(GameObject outfitRoot, GameObject avatarRoot, OutfitSetupMode mode);
    }

    internal sealed class ModularAvatarOutfitSetupAdapter : IOutfitSetupAdapter
    {
        internal static readonly ModularAvatarOutfitSetupAdapter Instance = new ModularAvatarOutfitSetupAdapter();

        public void Configure(GameObject outfitRoot, GameObject avatarRoot, OutfitSetupMode mode)
        {
            if (outfitRoot == null) throw new ArgumentNullException(nameof(outfitRoot));
            if (avatarRoot == null) throw new ArgumentNullException(nameof(avatarRoot));

            switch (mode)
            {
                case OutfitSetupMode.DoNotConfigureAttachment:
                    return;
                case OutfitSetupMode.AutomaticPreferExisting:
                    if (HasValidExistingMergeArmature(outfitRoot, avatarRoot)) return;
                    break;
                case OutfitSetupMode.AlwaysRunModularAvatarSetup:
                    break;
                default:
                    throw new OutfitGenerationException("装着モードが不正です。");
            }

            SetupOutfit.SetupOutfitUI(outfitRoot);
            ValidateSetupResult(outfitRoot, avatarRoot);
        }

        internal static bool HasValidExistingMergeArmature(GameObject outfitRoot, GameObject avatarRoot)
        {
            var merges = outfitRoot.GetComponentsInChildren<ModularAvatarMergeArmature>(true)
                .Where(merge => merge != null && merge.enabled)
                .ToArray();
            return merges.Length > 0 && merges.All(merge => IsValidMerge(merge, avatarRoot));
        }

        internal static void ValidateSetupResult(GameObject outfitRoot, GameObject avatarRoot)
        {
            var merges = outfitRoot.GetComponentsInChildren<ModularAvatarMergeArmature>(true)
                .Where(merge => merge != null && merge.enabled)
                .ToArray();
            if (merges.Length == 0)
            {
                throw new OutfitGenerationException(
                    "Modular Avatar標準セットアップ後に有効なMA Merge Armatureが生成されませんでした。");
            }

            if (merges.Any(merge => !IsValidMerge(merge, avatarRoot)))
            {
                throw new OutfitGenerationException(
                    "MA Merge ArmatureのmergeTargetを対象アバター内に解決できませんでした。");
            }

            var meshSettings = outfitRoot.GetComponent<ModularAvatarMeshSettings>();
            if (meshSettings == null)
            {
                throw new OutfitGenerationException(
                    "Modular Avatar標準セットアップ後にMA Mesh Settingsが生成されませんでした。");
            }
        }

        private static bool IsValidMerge(ModularAvatarMergeArmature merge, GameObject avatarRoot)
        {
            if (merge.mergeTarget == null) return false;
            var target = merge.mergeTarget.Get(merge);
            return target != null
                   && (target == avatarRoot || target.transform.IsChildOf(avatarRoot.transform));
        }
    }
}
