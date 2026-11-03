// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nyxara.AICompanion.Face;
using UnityEngine;

namespace Nyxara.AICompanion.Expressions
{
    public enum ExpressionSupportLevel
    {
        Supported,
        Partial,
        Unsupported
    }

    public sealed class ExpressionRendererAudit
    {
        public string rendererName;
        public int blendshapeCount;
        public int requiredShapeCount;
        public int supportedShapeCount;
        public ExpressionSupportLevel support;
        public IReadOnlyList<string> missingShapes;
        public IReadOnlyList<string> availableCanonicalShapes;
    }

    public sealed class ExpressionPresetAudit
    {
        public ExpressionPreset preset;
        public ExpressionSupportLevel support;
        public IReadOnlyList<string> requiredShapes;
        public IReadOnlyList<string> missingShapesOnPrimary;
        public IReadOnlyList<ExpressionRendererAudit> renderers;
        public bool requiresMouthChannels;
        public bool requiresEyeChannels;
        public bool preservesIndependentChannels;

        public bool IsSupported => support == ExpressionSupportLevel.Supported;
        public bool IsPartial => support == ExpressionSupportLevel.Partial;
        public bool IsUnsupported => support == ExpressionSupportLevel.Unsupported;

        public override string ToString()
        {
            var missing = missingShapesOnPrimary != null && missingShapesOnPrimary.Count > 0
                ? $" missing: {string.Join(", ", missingShapesOnPrimary)}"
                : string.Empty;
            return $"{support} {preset?.displayName}{missing}";
        }
    }

    public sealed class ExpressionLibraryAuditReport
    {
        public IReadOnlyList<ExpressionPresetAudit> presets;
        public IReadOnlyList<ExpressionRendererAudit> renderers;
        public int supportedCount;
        public int partialCount;
        public int unsupportedCount;

        public int PresetCount => presets?.Count ?? 0;

        public IEnumerable<ExpressionPresetAudit> SupportedPresets =>
            (presets ?? Array.Empty<ExpressionPresetAudit>()).Where(audit => audit.IsSupported);

        public IEnumerable<ExpressionPresetAudit> PartialPresets =>
            (presets ?? Array.Empty<ExpressionPresetAudit>()).Where(audit => audit.IsPartial);

        public IEnumerable<ExpressionPresetAudit> UnsupportedPresets =>
            (presets ?? Array.Empty<ExpressionPresetAudit>()).Where(audit => audit.IsUnsupported);

        public ExpressionPresetAudit Find(ExpressionPreset preset)
        {
            if (preset == null || presets == null)
            {
                return null;
            }

            return presets.FirstOrDefault(audit =>
                audit.preset == preset ||
                (!string.IsNullOrWhiteSpace(preset.presetId) &&
                 string.Equals(audit.preset?.presetId, preset.presetId, StringComparison.OrdinalIgnoreCase)));
        }

        public string ToSummary()
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                $"[Nyxara Expression Audit] Presets={PresetCount} Supported={supportedCount} Partial={partialCount} Unsupported={unsupportedCount}");

            if (renderers != null && renderers.Count > 0)
            {
                builder.AppendLine($"Renderers={renderers.Count}");
                foreach (var renderer in renderers)
                {
                    builder.AppendLine(
                        $"  - {renderer.rendererName}: {renderer.blendshapeCount} shapes, " +
                        $"{renderer.supportedShapeCount}/{renderer.requiredShapeCount} required shapes present" +
                        (renderer.missingShapes.Count > 0 ? $" (missing: {string.Join(", ", renderer.missingShapes)})" : string.Empty));
                }
            }
            else
            {
                builder.AppendLine("Renderers=0 (no face renderer assigned)");
            }

            var partial = PartialPresets.ToList();
            if (partial.Count > 0)
            {
                builder.AppendLine("Partially supported on the primary renderer:");
                foreach (var audit in partial)
                {
                    builder.AppendLine(
                        $"  - {audit.preset?.displayName} [{ExpressionCategoryUtility.GetDisplayName(audit.preset?.category ?? ExpressionCategory.Custom)}]: " +
                        $"missing {string.Join(", ", audit.missingShapesOnPrimary)}");
                }
            }

            var unsupported = UnsupportedPresets.ToList();
            if (unsupported.Count > 0)
            {
                builder.AppendLine("Unsupported on the primary renderer:");
                foreach (var audit in unsupported)
                {
                    builder.AppendLine(
                        $"  - {audit.preset?.displayName}: missing {string.Join(", ", audit.missingShapesOnPrimary)}");
                }
            }

            if (partial.Count == 0 && unsupported.Count == 0)
            {
                builder.AppendLine("All presets are fully supported by the primary face renderer.");
            }

            return builder.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Audits the code-defined and persisted presets against the real blendshape
    /// inventory of every participating face renderer. Shape matching goes through
    /// <see cref="ArkItBlendshapeDriver.FindBlendshapeIndex"/> so prefixed FBX names
    /// are resolved without assuming identical indices between meshes.
    /// </summary>
    public static class ExpressionLibraryAudit
    {
        public static ExpressionLibraryAuditReport Audit(
            IEnumerable<ExpressionPreset> presets,
            IEnumerable<SkinnedMeshRenderer> faceRenderers)
        {
            var presetList = (presets ?? Array.Empty<ExpressionPreset>())
                .Where(preset => preset != null)
                .ToList();
            var rendererList = (faceRenderers ?? Array.Empty<SkinnedMeshRenderer>())
                .Where(renderer => renderer != null && renderer.sharedMesh != null)
                .Distinct()
                .ToList();

            var rendererInventories = rendererList
                .Select(renderer => new
                {
                    renderer,
                    name = renderer.name,
                    shapes = BuildCanonicalInventory(renderer.sharedMesh)
                })
                .ToList();

            var report = new ExpressionLibraryAuditReport
            {
                presets = new List<ExpressionPresetAudit>(),
                renderers = rendererInventories.Select(item => new ExpressionRendererAudit
                {
                    rendererName = item.name,
                    blendshapeCount = item.renderer.sharedMesh.blendShapeCount,
                    availableCanonicalShapes = item.shapes.ToList()
                }).ToList()
            };

            var presetAudits = new List<ExpressionPresetAudit>();

            foreach (var preset in presetList)
            {
                var requiredShapes = preset.blendshapeWeights
                    .Where(weight => weight != null && weight.weight > 0.01f && !string.IsNullOrWhiteSpace(weight.blendshapeName))
                    .Select(weight => ArkItBlendshapeDriver.GetCanonicalBlendshapeName(weight.blendshapeName))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var presetAudit = new ExpressionPresetAudit
                {
                    preset = preset,
                    requiredShapes = requiredShapes,
                    requiresMouthChannels = requiredShapes.Any(ExpressionBuilderHelper.IsMouthRelatedBlendshape),
                    requiresEyeChannels = requiredShapes.Any(ExpressionBuilderHelper.IsEyeRelatedBlendshape),
                    preservesIndependentChannels = preset.preserveIndependentChannels
                };

                var rendererAudits = new List<ExpressionRendererAudit>();
                foreach (var item in rendererInventories)
                {
                    var missing = requiredShapes
                        .Where(shape => !item.shapes.Contains(shape))
                        .ToList();
                    var supported = requiredShapes.Count - missing.Count;
                    var rendererAudit = new ExpressionRendererAudit
                    {
                        rendererName = item.name,
                        blendshapeCount = item.renderer.sharedMesh.blendShapeCount,
                        requiredShapeCount = requiredShapes.Count,
                        supportedShapeCount = supported,
                        missingShapes = missing,
                        availableCanonicalShapes = item.shapes.ToList(),
                        support = ResolveSupport(requiredShapes.Count, supported)
                    };

                    rendererAudits.Add(rendererAudit);
                    if (presetAudit.missingShapesOnPrimary == null)
                    {
                        presetAudit.missingShapesOnPrimary = missing;
                        presetAudit.support = rendererAudit.support;
                    }
                }

                presetAudit.renderers = rendererAudits;

                if (presetAudit.missingShapesOnPrimary == null)
                {
                    // No renderer configured: the preset cannot be applied anywhere.
                    presetAudit.missingShapesOnPrimary = requiredShapes;
                    presetAudit.support = requiredShapes.Count == 0
                        ? ExpressionSupportLevel.Supported
                        : ExpressionSupportLevel.Unsupported;
                }

                presetAudits.Add(presetAudit);
            }

            report.presets = presetAudits;
            report.supportedCount = report.presets.Count(audit => audit.IsSupported);
            report.partialCount = report.presets.Count(audit => audit.IsPartial);
            report.unsupportedCount = report.presets.Count(audit => audit.IsUnsupported);
            return report;
        }

        public static bool IsIndependentChannel(string blendshapeName)
        {
            var canonical = ArkItBlendshapeDriver.GetCanonicalBlendshapeName(blendshapeName);
            return canonical.StartsWith("eyeBlink", StringComparison.OrdinalIgnoreCase) ||
                   canonical.StartsWith("eyeLook", StringComparison.OrdinalIgnoreCase);
        }

        public static HashSet<string> BuildCanonicalInventory(Mesh mesh)
        {
            var inventory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mesh == null)
            {
                return inventory;
            }

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var canonical = ArkItBlendshapeDriver.GetCanonicalBlendshapeName(mesh.GetBlendShapeName(i));
                if (!string.IsNullOrWhiteSpace(canonical))
                {
                    inventory.Add(canonical);
                }
            }

            return inventory;
        }

        private static ExpressionSupportLevel ResolveSupport(int requiredCount, int supportedCount)
        {
            if (requiredCount <= 0)
            {
                return ExpressionSupportLevel.Supported;
            }

            if (supportedCount >= requiredCount)
            {
                return ExpressionSupportLevel.Supported;
            }

            return supportedCount > 0
                ? ExpressionSupportLevel.Partial
                : ExpressionSupportLevel.Unsupported;
        }
    }
}
