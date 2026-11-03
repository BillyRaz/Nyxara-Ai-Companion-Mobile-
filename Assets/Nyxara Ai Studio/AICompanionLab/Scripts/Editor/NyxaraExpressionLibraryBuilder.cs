// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nyxara.AICompanion.Expressions;
using UnityEditor;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    /// <summary>
    /// Materialises the code-defined <see cref="NyxaraExpressionCatalog"/> into
    /// real ExpressionPreset assets, and audits every preset against the actual
    /// blendshape inventory of the assigned face renderers.
    /// </summary>
    public static class NyxaraExpressionLibraryBuilder
    {
        private const string DefaultLibraryRoot = "Assets/Nyxara AI Studio/Generated/Expressions";

        [MenuItem("Nyxara AI/Expressions/Generate Expression Library Assets", false, 40)]
        private static void GenerateFromMenu()
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<ExpressionLibraryManager>();
            if (manager == null)
            {
                Debug.LogWarning(
                    "[Nyxara Expressions] No ExpressionLibraryManager found in the open scene. Open the Studio Window and use its Generate Library button instead.");
                return;
            }

            GenerateLibrary(manager, false);
        }

        [MenuItem("Nyxara AI/Expressions/Audit Face Expressions", false, 41)]
        private static void AuditFromMenu()
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<ExpressionLibraryManager>();
            Audit(manager);
        }

        public static int GenerateLibrary(ExpressionLibraryManager manager, bool overwriteExisting)
        {
            if (manager == null)
            {
                manager = UnityEngine.Object.FindFirstObjectByType<ExpressionLibraryManager>();
            }

            if (manager == null)
            {
                Debug.LogWarning("[Nyxara Expressions] Cannot generate the library without an ExpressionLibraryManager.");
                return 0;
            }

            var root = string.IsNullOrWhiteSpace(manager.ExpressionLibraryPath)
                ? DefaultLibraryRoot
                : manager.ExpressionLibraryPath;

            EnsureFolderPath(root);

            var generated = 0;
            var skipped = 0;
            var existingAssets = FindPersistedPresets(root);

            foreach (var builtIn in NyxaraExpressionCatalog.GetPresets())
            {
                if (builtIn == null)
                {
                    continue;
                }

                var existing = FindExistingPreset(existingAssets, builtIn);
                if (existing != null && !overwriteExisting)
                {
                    skipped++;
                    continue;
                }

                if (existing == null)
                {
                    var categoryFolder = ResolveCategoryFolder(root, builtIn.category);
                    EnsureFolderPath(categoryFolder);
                    existing = ScriptableObject.CreateInstance<ExpressionPreset>();
                    var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        $"{categoryFolder}/{SanitizeFileName(builtIn.displayName)}.asset");
                    AssetDatabase.CreateAsset(existing, assetPath);
                    existingAssets.Add(existing);
                }

                CopyPreset(builtIn, existing);
                EditorUtility.SetDirty(existing);
                generated++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            manager.LoadAllPresets();
            SyncPresetLibraryList(manager);

            var report = ExpressionLibraryAudit.Audit(manager.LoadedPresets, manager.TargetFaceRenderers);
            Debug.Log(
                $"[Nyxara Expressions] Generated/updated {generated} preset asset(s), skipped {skipped} existing. " +
                $"Library now has {manager.LoadedPresets.Count} presets ({report.supportedCount} supported, " +
                $"{report.partialCount} partial, {report.unsupportedCount} unsupported on the primary face).");
            return generated;
        }

        public static ExpressionLibraryAuditReport Audit(ExpressionLibraryManager manager, bool log = true)
        {
            if (manager == null)
            {
                manager = UnityEngine.Object.FindFirstObjectByType<ExpressionLibraryManager>();
            }

            if (manager == null)
            {
                Debug.LogWarning("[Nyxara Expressions] Cannot audit without an ExpressionLibraryManager.");
                return null;
            }

            var report = ExpressionLibraryAudit.Audit(manager.LoadedPresets, manager.TargetFaceRenderers);
            if (log && report != null)
            {
                Debug.Log(report.ToSummary());
            }

            return report;
        }

        /// <summary>
        /// Mirrors the persisted assets into the manager's serialized list so
        /// player builds can load user overrides without the editor AssetDatabase.
        /// </summary>
        public static void SyncPresetLibraryList(ExpressionLibraryManager manager)
        {
            if (manager == null)
            {
                return;
            }

            var assets = FindPersistedPresets(manager.ExpressionLibraryPath)
                .Where(preset => preset != null)
                .Distinct()
                .ToList();

            var serializedObject = new SerializedObject(manager);
            var property = serializedObject.FindProperty("presetLibrary");
            if (property == null || !property.isArray)
            {
                return;
            }

            property.arraySize = assets.Count;
            for (var i = 0; i < assets.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = assets[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        public static string ResolveCategoryFolder(string root, ExpressionCategory category)
        {
            var segment = category switch
            {
                ExpressionCategory.Emotion => "Core Emotions",
                ExpressionCategory.Personality => "Personality",
                ExpressionCategory.EyeAction => "Eye Actions",
                ExpressionCategory.MouthAction => "Mouth & Jaw",
                ExpressionCategory.Speech => "Speech & Visemes",
                _ => category.ToString()
            };

            return $"{root}/{segment}";
        }

        private static List<ExpressionPreset> FindPersistedPresets(string root)
        {
            var result = new List<ExpressionPreset>();
            if (string.IsNullOrWhiteSpace(root) || !AssetDatabase.IsValidFolder(root))
            {
                return result;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:ExpressionPreset", new[] { root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var preset = AssetDatabase.LoadAssetAtPath<ExpressionPreset>(path);
                if (preset != null)
                {
                    result.Add(preset);
                }
            }

            return result;
        }

        private static ExpressionPreset FindExistingPreset(
            IEnumerable<ExpressionPreset> existingAssets,
            ExpressionPreset builtIn)
        {
            if (builtIn == null)
            {
                return null;
            }

            foreach (var candidate in existingAssets)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(builtIn.presetId) &&
                    string.Equals(candidate.presetId, builtIn.presetId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (!string.IsNullOrWhiteSpace(builtIn.displayName) &&
                    string.Equals(candidate.displayName, builtIn.displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void CopyPreset(ExpressionPreset source, ExpressionPreset destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            destination.name = source.displayName;
            destination.hideFlags = HideFlags.None;
            destination.presetId = source.presetId;
            destination.displayName = source.displayName;
            destination.description = source.description;
            destination.category = source.category;
            destination.transitionTimeInSeconds = source.transitionTimeInSeconds;
            destination.holdDuration = source.holdDuration;
            destination.autoReturnToNeutral = source.autoReturnToNeutral;
            destination.preserveIndependentChannels = source.preserveIndependentChannels;
            if (source.thumbnail != null || destination.thumbnail == null)
            {
                destination.thumbnail = source.thumbnail;
            }

            destination.blendshapeWeights = (source.blendshapeWeights ?? new List<BlendshapeWeight>())
                .Where(weight => weight != null)
                .Select(weight => new BlendshapeWeight
                {
                    blendshapeName = weight.blendshapeName,
                    weight = weight.weight
                })
                .ToList();
        }

        private static void EnsureFolderPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var segments = assetPath.Split('/');
            var currentPath = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var nextPath = $"{currentPath}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[i]);
                }

                currentPath = nextPath;
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Expression";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        }
    }
}
#endif
