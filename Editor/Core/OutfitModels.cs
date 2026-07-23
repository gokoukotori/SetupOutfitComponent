using System;
using System.Collections.Generic;
using System.Linq;
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
        {
            SourcePrefab = sourcePrefab;
            AssetGuid = assetGuid ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            DependencyHash = dependencyHash ?? string.Empty;
            RootName = rootName ?? string.Empty;
            Targets = (targets ?? Enumerable.Empty<PrefabTargetInfo>()).ToArray();
            PartCandidates = (partCandidates ?? Enumerable.Empty<OutfitPartCandidate>()).ToArray();
            BlendshapeRenderers = (blendshapeRenderers ?? Enumerable.Empty<OutfitRendererInfo>()).ToList();
            Errors = (errors ?? Enumerable.Empty<string>()).ToArray();
            Warnings = (warnings ?? Enumerable.Empty<string>()).ToArray();
        }

        internal GameObject SourcePrefab { get; }
        internal string AssetGuid { get; }
        internal string AssetPath { get; }
        internal string DependencyHash { get; }
        internal string RootName { get; }
        internal IReadOnlyList<PrefabTargetInfo> Targets { get; }
        internal IReadOnlyList<OutfitPartCandidate> PartCandidates { get; }
        internal IReadOnlyList<OutfitRendererInfo> BlendshapeRenderers { get; }
        internal IReadOnlyList<string> Errors { get; }
        internal IReadOnlyList<string> Warnings { get; }
        internal bool IsValid => Errors.Count == 0;

        internal PrefabTargetInfo FindTarget(PrefabTargetKey key)
        {
            return Targets.FirstOrDefault(candidate => candidate.TargetKey.Equals(key));
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
            bool hasExistingBlendshapeSync)
        {
            TargetKey = targetKey;
            DisplayPath = displayPath ?? string.Empty;
            Name = name ?? string.Empty;
            BlendshapeNames = (blendshapeNames ?? Enumerable.Empty<string>()).ToArray();
            HasExistingBlendshapeSync = hasExistingBlendshapeSync;
        }

        internal PrefabTargetKey TargetKey { get; }
        internal string DisplayPath { get; }
        internal string Name { get; }
        internal IReadOnlyList<string> BlendshapeNames { get; }
        internal bool HasExistingBlendshapeSync { get; }
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

    internal sealed class PartTogglePlan
    {
        private readonly Dictionary<PrefabTargetKey, bool> _activeWhenOn =
            new Dictionary<PrefabTargetKey, bool>();

        internal PartTogglePlan(string label, IEnumerable<PrefabTargetKey> targets, bool? initialOn = null)
        {
            Label = label ?? string.Empty;
            Targets = (targets ?? Enumerable.Empty<PrefabTargetKey>()).ToList();
            InitialOn = initialOn;
            foreach (var target in Targets) _activeWhenOn[target] = false;
        }

        internal string Label { get; set; }
        internal List<PrefabTargetKey> Targets { get; }
        internal bool? InitialOn { get; set; }

        internal bool GetTargetActiveWhenOn(PrefabTargetKey target)
        {
            return _activeWhenOn.TryGetValue(target, out var activeWhenOn) && activeWhenOn;
        }

        internal void SetTargetActiveWhenOn(PrefabTargetKey target, bool activeWhenOn)
        {
            _activeWhenOn[target] = activeWhenOn;
        }

        internal bool TryGetEffectiveInitialOn(OutfitAnalysis analysis, out bool initialOn)
        {
            if (InitialOn.HasValue)
            {
                initialOn = InitialOn.Value;
                return true;
            }

            var targetStates = Targets
                .Select(targetKey => new
                {
                    Target = analysis.FindTarget(targetKey),
                    ActiveWhenOn = GetTargetActiveWhenOn(targetKey),
                })
                .Where(binding => binding.Target != null)
                .Select(binding => binding.Target.ActiveSelf == binding.ActiveWhenOn)
                .Distinct()
                .ToArray();

            if (targetStates.Length == 1)
            {
                initialOn = targetStates[0];
                return true;
            }

            initialOn = false;
            return false;
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
        internal List<SceneObjectReference> ExclusionTargets { get; } = new List<SceneObjectReference>();
        internal List<PartTogglePlan> PartToggles { get; } = new List<PartTogglePlan>();
        internal List<BlendshapeSyncPlan> BlendshapeSyncs { get; } = new List<BlendshapeSyncPlan>();
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
