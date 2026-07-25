using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEngine;

namespace Gokoukotori.SetupOutfitComponent.Editor
{
    internal enum OutfitSetupMode
    {
        AutomaticPreferExisting,
        AlwaysRunModularAvatarSetup,
        DoNotConfigureAttachment,
    }

    internal sealed class OutfitAnalysis
    {
        internal OutfitAnalysis(
            GameObject sourcePrefab,
            string assetGuid,
            string assetPath,
            string dependencyHash,
            string rootName,
            IEnumerable<PrefabTargetInfo> targets,
            IEnumerable<OutfitPartCandidate> partCandidates,
            IEnumerable<OutfitRendererInfo> blendshapeRenderers,
            IEnumerable<string> errors,
            IEnumerable<string> warnings)
            : this(
                sourcePrefab,
                assetGuid,
                assetPath,
                dependencyHash,
                rootName,
                targets,
                partCandidates,
                blendshapeRenderers,
                Enumerable.Empty<ExistingShapeChangerInfo>(),
                errors,
                warnings)
        {
        }

        internal OutfitAnalysis(
            GameObject sourcePrefab,
            string assetGuid,
            string assetPath,
            string dependencyHash,
            string rootName,
            IEnumerable<PrefabTargetInfo> targets,
            IEnumerable<OutfitPartCandidate> partCandidates,
            IEnumerable<OutfitRendererInfo> blendshapeRenderers,
            IEnumerable<ExistingShapeChangerInfo> existingShapeChangers,
            IEnumerable<string> errors,
            IEnumerable<string> warnings)
        {
            SourcePrefab = sourcePrefab;
            AssetGuid = assetGuid ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            DependencyHash = dependencyHash ?? string.Empty;
            RootName = rootName ?? string.Empty;
            Targets = (targets ?? Enumerable.Empty<PrefabTargetInfo>()).ToArray();
            var rendererOwners = Targets
                .Where(target => target.IsRendererCandidate)
                .ToList();
            if (SourcePrefab != null && SourcePrefab.GetComponent<Renderer>() != null)
            {
                rendererOwners.Insert(0, new PrefabTargetInfo(
                    PrefabTargetKey.FromTransform(
                        SourcePrefab.transform,
                        SourcePrefab.transform,
                        DependencyHash),
                    SourcePrefab.name,
                    SourcePrefab.name,
                    SourcePrefab.activeSelf,
                    true,
                    0));
            }

            RendererOwners = rendererOwners;
            PartCandidates = (partCandidates ?? Enumerable.Empty<OutfitPartCandidate>()).ToArray();
            BlendshapeRenderers = (blendshapeRenderers ?? Enumerable.Empty<OutfitRendererInfo>()).ToList();
            ExistingShapeChangers = (existingShapeChangers ?? Enumerable.Empty<ExistingShapeChangerInfo>()).ToArray();
            Errors = (errors ?? Enumerable.Empty<string>()).ToArray();
            Warnings = (warnings ?? Enumerable.Empty<string>()).ToArray();
        }

        internal GameObject SourcePrefab { get; }
        internal string AssetGuid { get; }
        internal string AssetPath { get; }
        internal string DependencyHash { get; }
        internal string RootName { get; }
        internal IReadOnlyList<PrefabTargetInfo> Targets { get; }
        internal IReadOnlyList<PrefabTargetInfo> RendererOwners { get; }
        internal IReadOnlyList<OutfitPartCandidate> PartCandidates { get; }
        internal IReadOnlyList<OutfitRendererInfo> BlendshapeRenderers { get; }
        internal IReadOnlyList<ExistingShapeChangerInfo> ExistingShapeChangers { get; }
        internal IReadOnlyList<string> Errors { get; }
        internal IReadOnlyList<string> Warnings { get; }
        internal bool IsValid => Errors.Count == 0;

        internal PrefabTargetInfo FindTarget(PrefabTargetKey key)
        {
            return Targets.FirstOrDefault(candidate => candidate.TargetKey.Equals(key));
        }

        internal PrefabTargetInfo FindRendererOwner(PrefabTargetKey key)
        {
            return RendererOwners.FirstOrDefault(candidate => candidate.TargetKey.Equals(key));
        }

        internal OutfitRendererInfo FindBlendshapeRenderer(PrefabTargetKey key)
        {
            return BlendshapeRenderers.FirstOrDefault(renderer => renderer.TargetKey.Equals(key));
        }
    }

    internal class PrefabTargetInfo
    {
        internal PrefabTargetInfo(
            PrefabTargetKey targetKey,
            string displayPath,
            string name,
            bool activeSelf,
            bool isRendererCandidate,
            int depth)
        {
            TargetKey = targetKey;
            DisplayPath = displayPath ?? string.Empty;
            Name = name ?? string.Empty;
            ActiveSelf = activeSelf;
            IsRendererCandidate = isRendererCandidate;
            Depth = depth;
        }

        internal PrefabTargetKey TargetKey { get; }
        internal string DisplayPath { get; }
        internal string Name { get; }
        internal bool ActiveSelf { get; }
        internal bool IsRendererCandidate { get; }
        internal int Depth { get; }
    }

    internal sealed class OutfitPartCandidate : PrefabTargetInfo
    {
        internal OutfitPartCandidate(
            PrefabTargetKey targetKey,
            string displayPath,
            string name,
            bool activeSelf,
            int depth)
            : base(targetKey, displayPath, name, activeSelf, true, depth)
        {
        }
    }

    internal sealed class OutfitRendererInfo
    {
        internal OutfitRendererInfo(
            PrefabTargetKey targetKey,
            string displayPath,
            string name,
            IEnumerable<string> blendshapeNames,
            bool hasExistingBlendshapeSync,
            bool hasExistingShapeChanger = false)
        {
            TargetKey = targetKey;
            DisplayPath = displayPath ?? string.Empty;
            Name = name ?? string.Empty;
            BlendshapeNames = (blendshapeNames ?? Enumerable.Empty<string>()).ToArray();
            HasExistingBlendshapeSync = hasExistingBlendshapeSync;
            HasExistingShapeChanger = hasExistingShapeChanger;
        }

        internal PrefabTargetKey TargetKey { get; }
        internal string DisplayPath { get; }
        internal string Name { get; }
        internal IReadOnlyList<string> BlendshapeNames { get; }
        internal bool HasExistingBlendshapeSync { get; }
        internal bool HasExistingShapeChanger { get; }
    }

    internal sealed class ExistingShapeChangerInfo
    {
        internal ExistingShapeChangerInfo(
            PrefabTargetKey ownerKey,
            string displayPath,
            float threshold,
            bool inverted,
            IEnumerable<ExistingShapeChangeInfo> shapes)
        {
            OwnerKey = ownerKey;
            DisplayPath = displayPath ?? string.Empty;
            Threshold = threshold;
            Inverted = inverted;
            Shapes = (shapes ?? Enumerable.Empty<ExistingShapeChangeInfo>()).ToArray();
        }

        internal PrefabTargetKey OwnerKey { get; }
        internal string DisplayPath { get; }
        internal float Threshold { get; }
        internal bool Inverted { get; }
        internal IReadOnlyList<ExistingShapeChangeInfo> Shapes { get; }
    }

    internal sealed class ExistingShapeChangeInfo
    {
        internal ExistingShapeChangeInfo(
            string targetPath,
            string shapeName,
            ShapeChangeType changeType,
            float value)
        {
            TargetPath = targetPath ?? string.Empty;
            ShapeName = shapeName ?? string.Empty;
            ChangeType = changeType;
            Value = value;
        }

        internal string TargetPath { get; }
        internal string ShapeName { get; }
        internal ShapeChangeType ChangeType { get; }
        internal float Value { get; }
    }

    internal sealed class BlendshapeMappingPlan
    {
        internal BlendshapeMappingPlan(string sourceShape, string localShape)
        {
            SourceShape = sourceShape ?? string.Empty;
            LocalShape = localShape ?? string.Empty;
        }

        internal string SourceShape { get; set; }
        internal string LocalShape { get; set; }
    }

    internal sealed class BlendshapeSyncPlan
    {
        internal BlendshapeSyncPlan(
            PrefabTargetKey localRendererKey,
            SceneObjectReference sourceRendererReference)
        {
            LocalRendererKey = localRendererKey;
            SourceRendererReference = sourceRendererReference;
        }

        internal PrefabTargetKey LocalRendererKey { get; }
        internal SceneObjectReference SourceRendererReference { get; set; }
        internal List<BlendshapeMappingPlan> Mappings { get; } = new List<BlendshapeMappingPlan>();
    }

    internal sealed class ShapeChangerSettingPlan
    {
        private ShapeChangerSettingPlan(
            PartTargetSource source,
            PrefabTargetKey prefabRendererKey,
            SceneObjectReference sceneRendererReference,
            string shapeName,
            float value)
        {
            Source = source;
            PrefabRendererKey = prefabRendererKey;
            SceneRendererReference = sceneRendererReference;
            ShapeName = shapeName ?? string.Empty;
            Value = value;
        }

        internal PartTargetSource Source { get; set; }
        internal PrefabTargetKey PrefabRendererKey { get; set; }
        internal SceneObjectReference SceneRendererReference { get; set; }
        internal string ShapeName { get; set; }
        internal float Value { get; set; }
        internal string StableRendererId => Source == PartTargetSource.OutfitPrefab
            ? "P:" + PrefabRendererKey
            : "S:" + (SceneRendererReference?.GlobalObjectId ?? string.Empty);

        internal static ShapeChangerSettingPlan ForPrefab(
            PrefabTargetKey prefabRendererKey,
            string shapeName,
            float value = 100f)
        {
            return new ShapeChangerSettingPlan(
                PartTargetSource.OutfitPrefab,
                prefabRendererKey,
                null,
                shapeName,
                value);
        }

        internal static ShapeChangerSettingPlan ForScene(
            SceneObjectReference sceneRendererReference,
            string shapeName,
            float value = 100f)
        {
            return new ShapeChangerSettingPlan(
                PartTargetSource.SceneObject,
                default,
                sceneRendererReference ?? throw new ArgumentNullException(nameof(sceneRendererReference)),
                shapeName,
                value);
        }
    }

    internal sealed class OutfitRendererShapeChangerPlan
    {
        internal OutfitRendererShapeChangerPlan(PrefabTargetKey ownerKey)
        {
            OwnerKey = ownerKey;
        }

        internal PrefabTargetKey OwnerKey { get; set; }
        internal List<ShapeChangerSettingPlan> ShapeChanges { get; } =
            new List<ShapeChangerSettingPlan>();
    }

    internal enum PartTargetSource
    {
        OutfitPrefab,
        SceneObject,
    }

    internal sealed class MasterSceneTargetPlan
    {
        internal MasterSceneTargetPlan(
            SceneObjectReference reference,
            bool activeWhenOn = false)
        {
            Reference = reference;
            ActiveWhenOn = activeWhenOn;
        }

        internal SceneObjectReference Reference { get; }
        internal bool ActiveWhenOn { get; set; }
        internal string StableId =>
            "S:" + (Reference?.GlobalObjectId ?? string.Empty);
    }

    internal sealed class PartToggleTargetPlan : IEquatable<PartToggleTargetPlan>
    {
        private PartToggleTargetPlan(
            PartTargetSource source,
            PrefabTargetKey prefabKey,
            SceneObjectReference sceneReference,
            bool activeWhenOn)
        {
            Source = source;
            PrefabKey = prefabKey;
            SceneReference = sceneReference;
            ActiveWhenOn = activeWhenOn;
        }

        internal PartTargetSource Source { get; }
        internal PrefabTargetKey PrefabKey { get; }
        internal SceneObjectReference SceneReference { get; }
        internal bool ActiveWhenOn { get; set; }
        internal string StableId => Source == PartTargetSource.OutfitPrefab
            ? "P:" + PrefabKey
            : "S:" + (SceneReference?.GlobalObjectId ?? string.Empty);

        internal static PartToggleTargetPlan ForPrefab(
            PrefabTargetKey prefabKey,
            bool activeWhenOn = false)
        {
            return new PartToggleTargetPlan(
                PartTargetSource.OutfitPrefab,
                prefabKey,
                null,
                activeWhenOn);
        }

        internal static PartToggleTargetPlan ForScene(
            SceneObjectReference sceneReference,
            bool activeWhenOn = true)
        {
            return new PartToggleTargetPlan(
                PartTargetSource.SceneObject,
                default,
                sceneReference ?? throw new ArgumentNullException(nameof(sceneReference)),
                activeWhenOn);
        }

        public bool Equals(PartToggleTargetPlan other)
        {
            return other != null
                   && string.Equals(StableId, other.StableId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as PartToggleTargetPlan);
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(StableId ?? string.Empty);
    }

    internal sealed class PartTogglePlan
    {
        internal PartTogglePlan(string label, IEnumerable<PrefabTargetKey> targets, bool? initialOn = null)
            : this(
                Guid.NewGuid().ToString("N"),
                label,
                (targets ?? Enumerable.Empty<PrefabTargetKey>())
                .Select(target => PartToggleTargetPlan.ForPrefab(target)),
                initialOn)
        {
        }

        internal PartTogglePlan(
            string itemId,
            string label,
            IEnumerable<PrefabTargetKey> targets,
            bool? initialOn = null)
            : this(
                itemId,
                label,
                (targets ?? Enumerable.Empty<PrefabTargetKey>())
                .Select(target => PartToggleTargetPlan.ForPrefab(target)),
                initialOn)
        {
        }

        internal PartTogglePlan(
            string label,
            IEnumerable<PartToggleTargetPlan> targets,
            bool? initialOn = null)
            : this(
                Guid.NewGuid().ToString("N"),
                label,
                targets,
                initialOn)
        {
        }

        internal PartTogglePlan(
            string itemId,
            string label,
            IEnumerable<PartToggleTargetPlan> targets,
            bool? initialOn = null)
        {
            ItemId = itemId ?? string.Empty;
            Label = label ?? string.Empty;
            Targets = (targets ?? Enumerable.Empty<PartToggleTargetPlan>()).ToList();
            InitialOn = initialOn;
        }

        internal string ItemId { get; }
        internal string Label { get; set; }
        internal List<PartToggleTargetPlan> Targets { get; }
        internal List<ShapeChangerSettingPlan> ShapeChanges { get; } =
            new List<ShapeChangerSettingPlan>();
        internal bool? InitialOn { get; set; }

        internal bool GetTargetActiveWhenOn(PrefabTargetKey target)
        {
            return Targets.FirstOrDefault(candidate =>
                       candidate.Source == PartTargetSource.OutfitPrefab
                       && candidate.PrefabKey.Equals(target))
                   ?.ActiveWhenOn == true;
        }

        internal void SetTargetActiveWhenOn(PrefabTargetKey target, bool activeWhenOn)
        {
            var candidate = Targets.FirstOrDefault(item =>
                item.Source == PartTargetSource.OutfitPrefab
                && item.PrefabKey.Equals(target));
            if (candidate != null) candidate.ActiveWhenOn = activeWhenOn;
        }

        internal bool TryGetEffectiveInitialOn(OutfitAnalysis analysis, out bool initialOn)
        {
            _ = analysis;
            initialOn = InitialOn ?? false;
            return true;
        }
    }

    internal sealed class OutfitSetupPlan
    {
        internal OutfitSetupPlan(OutfitAnalysis analysis)
        {
            Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            SourcePrefab = analysis.SourcePrefab;
            SourceAssetGuid = analysis.AssetGuid;
            SourceAssetPath = analysis.AssetPath;
            DependencyHash = analysis.DependencyHash;
            OutputName = analysis.RootName;
            SubmenuLabel = analysis.RootName;
            MasterToggleLabel = "ON";
        }

        internal OutfitAnalysis Analysis { get; }
        internal GameObject SourcePrefab { get; }
        internal string SourceAssetGuid { get; }
        internal string SourceAssetPath { get; }
        internal string DependencyHash { get; }
        internal SceneObjectReference AvatarReference { get; set; }
        internal SceneObjectReference PlacementReference { get; set; }
        internal string OutputName { get; set; }
        internal string SubmenuLabel { get; set; }
        internal string MasterToggleLabel { get; set; }
        internal bool MasterDefaultOn { get; set; }
        internal OutfitSetupMode SetupMode { get; set; } = OutfitSetupMode.AutomaticPreferExisting;
        internal List<MasterSceneTargetPlan> MasterSceneTargets { get; } =
            new List<MasterSceneTargetPlan>();
        internal List<PartTogglePlan> PartToggles { get; } = new List<PartTogglePlan>();
        internal List<BlendshapeSyncPlan> BlendshapeSyncs { get; } = new List<BlendshapeSyncPlan>();
        internal List<ShapeChangerSettingPlan> MasterShapeChanges { get; } =
            new List<ShapeChangerSettingPlan>();
        internal List<OutfitRendererShapeChangerPlan> OutfitRendererShapeChangers { get; } =
            new List<OutfitRendererShapeChangerPlan>();
        internal bool AllowDuplicate { get; set; }
    }

    internal enum ValidationSeverity
    {
        Info,
        Warning,
        Error,
    }

    internal sealed class ValidationMessage
    {
        internal ValidationMessage(string code, string message, ValidationSeverity severity)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Severity = severity;
        }

        internal string Code { get; }
        internal string Message { get; }
        internal ValidationSeverity Severity { get; }
    }

    internal sealed class ValidationResult
    {
        internal ValidationResult(IEnumerable<ValidationMessage> messages)
        {
            Messages = (messages ?? Enumerable.Empty<ValidationMessage>()).ToArray();
        }

        internal IReadOnlyList<ValidationMessage> Messages { get; }
        internal bool IsValid => Messages.All(message => message.Severity != ValidationSeverity.Error);
        internal IEnumerable<ValidationMessage> Errors =>
            Messages.Where(message => message.Severity == ValidationSeverity.Error);
    }

    internal class OutfitGenerationException : Exception
    {
        internal OutfitGenerationException(string message)
            : base(message)
        {
        }

        internal OutfitGenerationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class OutfitValidationException : OutfitGenerationException
    {
        internal OutfitValidationException(ValidationResult result)
            : base(string.Join("\n", result.Errors.Select(error => error.Message)))
        {
            Result = result;
        }

        internal ValidationResult Result { get; }
    }
}
