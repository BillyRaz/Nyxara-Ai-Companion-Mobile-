// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using Nyxara.AICompanion.Face;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Nyxara.AICompanion.Expressions
{
    public class ExpressionLibraryManager : MonoBehaviour
    {
        [SerializeField] private string expressionLibraryPath = "Assets/Nyxara AI Studio/Generated/Expressions";
        [SerializeField] private SkinnedMeshRenderer targetFaceRenderer;
        [SerializeField] private List<SkinnedMeshRenderer> additionalFaceRenderers = new();
        [SerializeField] private bool expressionModeActive;

        private List<ExpressionPreset> _loadedPresets = new();
        private Dictionary<string, ExpressionPreset> _presetLookup = new();

        public event Action<ExpressionPreset> OnPresetSelected;
        public event Action<IReadOnlyList<ExpressionPreset>> OnLibraryUpdated;

        public IReadOnlyList<ExpressionPreset> LoadedPresets => _loadedPresets;
        public IReadOnlyList<SkinnedMeshRenderer> TargetFaceRenderers => GetAllFaceRenderers();
        public bool ExpressionModeActive => expressionModeActive;
        public string ExpressionLibraryPath => expressionLibraryPath;

        private void Awake()
        {
            expressionLibraryPath = NormalizeExpressionLibraryPath(expressionLibraryPath);
            LoadAllPresets();
        }

        private void OnValidate()
        {
            expressionLibraryPath = NormalizeExpressionLibraryPath(expressionLibraryPath);
        }

        public void LoadAllPresets()
        {
            _loadedPresets.Clear();
            _presetLookup.Clear();

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(expressionLibraryPath) && AssetDatabase.IsValidFolder(expressionLibraryPath))
            {
                var guids = AssetDatabase.FindAssets("t:ExpressionPreset", new[] { expressionLibraryPath });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var preset = AssetDatabase.LoadAssetAtPath<ExpressionPreset>(path);
                    if (preset != null)
                    {
                        _loadedPresets.Add(preset);
                        if (!string.IsNullOrWhiteSpace(preset.presetId))
                        {
                            _presetLookup[preset.presetId] = preset;
                        }
                    }
                }
            }
#endif

            _loadedPresets = _loadedPresets
                .OrderBy(p => p.category)
                .ThenBy(p => p.displayName)
                .ToList();

            OnLibraryUpdated?.Invoke(_loadedPresets);
        }

        public ExpressionPreset GetPreset(string presetId)
        {
            _presetLookup.TryGetValue(presetId, out var preset);
            return preset;
        }

        public void ApplyPreset(string presetId)
        {
            ApplyPreset(GetPreset(presetId));
        }

        public void ApplyPreset(ExpressionPreset preset)
        {
            if (preset == null || GetAllFaceRenderers().Count == 0)
            {
                return;
            }

            ResetToNeutral();
            ApplyExpressionWeights(preset.ToDictionary(), false);
            OnPresetSelected?.Invoke(preset);
        }

        public void ApplyExpressionWeights(IReadOnlyDictionary<string, float> blendshapeWeights, bool resetToNeutralFirst = true)
        {
            var renderers = GetAllFaceRenderers();
            if (renderers.Count == 0 || blendshapeWeights == null)
            {
                return;
            }

            if (resetToNeutralFirst)
            {
                ResetToNeutral();
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                foreach (var pair in blendshapeWeights)
                {
                    if (!expressionModeActive && ExpressionBuilderHelper.IsMouthRelatedBlendshape(pair.Key))
                    {
                        continue;
                    }

                    ApplyBlendshapeWeight(renderer, pair.Key, pair.Value);
                }
            }
        }

        public Dictionary<string, float> CaptureCurrentExpression()
        {
            var result = new Dictionary<string, float>();
            foreach (var renderer in GetAllFaceRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var mesh = renderer.sharedMesh;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    var weight = renderer.GetBlendShapeWeight(i);
                    if (weight > 0.01f)
                    {
                        var blendshapeName = mesh.GetBlendShapeName(i);
                        result[blendshapeName] = result.TryGetValue(blendshapeName, out var existingWeight)
                            ? Mathf.Max(existingWeight, weight)
                            : weight;
                    }
                }
            }

            return result;
        }

        public ExpressionPreset FindPresetByDisplayName(string displayName)
        {
            return _loadedPresets.FirstOrDefault(p => string.Equals(p.displayName, displayName, System.StringComparison.OrdinalIgnoreCase));
        }

        public void ResetToNeutral()
        {
            foreach (var renderer in GetAllFaceRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var mesh = renderer.sharedMesh;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    if (!expressionModeActive && ExpressionBuilderHelper.IsMouthRelatedBlendshape(mesh.GetBlendShapeName(i)))
                    {
                        continue;
                    }

                    renderer.SetBlendShapeWeight(i, 0f);
                }
            }
        }

        public List<ExpressionPreset> GetPresetsByCategory(ExpressionCategory category)
        {
            return _loadedPresets.Where(p => p.category == category).ToList();
        }

        public void SetExpressionMode(bool active)
        {
            expressionModeActive = active;
        }

        public void SetExpressionLibraryPath(string libraryPath, bool reload = true)
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                return;
            }

            expressionLibraryPath = NormalizeExpressionLibraryPath(libraryPath);
            if (reload)
            {
                LoadAllPresets();
            }
        }

#if UNITY_EDITOR
        public ExpressionPreset SavePreset(
            string presetName,
            ExpressionCategory category,
            string description,
            float transitionTimeInSeconds,
            IReadOnlyDictionary<string, float> blendshapeWeights)
        {
            if (string.IsNullOrWhiteSpace(presetName) || blendshapeWeights == null)
            {
                return null;
            }

            EnsureLibraryFolderExists();

            var existing = FindPresetByDisplayName(presetName);
            var preset = existing != null ? existing : ScriptableObject.CreateInstance<ExpressionPreset>();
            preset.presetId = string.IsNullOrWhiteSpace(preset.presetId) ? System.Guid.NewGuid().ToString() : preset.presetId;
            preset.displayName = presetName.Trim();
            preset.category = category;
            preset.description = description?.Trim() ?? string.Empty;
            preset.transitionTimeInSeconds = transitionTimeInSeconds;
            preset.blendshapeWeights = blendshapeWeights
                .Where(pair => pair.Value > 0.01f)
                .Select(pair => new BlendshapeWeight
                {
                    blendshapeName = pair.Key,
                    weight = pair.Value
                })
                .ToList();

            if (existing == null)
            {
                var assetName = SanitizeFileName(preset.displayName);
                var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{expressionLibraryPath}/{assetName}.asset");
                AssetDatabase.CreateAsset(preset, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(existing);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            LoadAllPresets();
            return FindPresetByDisplayName(presetName);
        }

        public bool DeletePreset(ExpressionPreset preset)
        {
            if (preset == null)
            {
                return false;
            }

            var assetPath = AssetDatabase.GetAssetPath(preset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            var deleted = AssetDatabase.DeleteAsset(assetPath);
            if (deleted)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                LoadAllPresets();
            }

            return deleted;
        }

        private void EnsureLibraryFolderExists()
        {
            if (AssetDatabase.IsValidFolder(expressionLibraryPath))
            {
                return;
            }

            var segments = expressionLibraryPath.Split('/');
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
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        }

        #endif

        private List<SkinnedMeshRenderer> GetAllFaceRenderers()
        {
            var renderers = new List<SkinnedMeshRenderer>();
            if (targetFaceRenderer != null)
            {
                renderers.Add(targetFaceRenderer);
            }

            foreach (var renderer in additionalFaceRenderers)
            {
                if (renderer != null && !renderers.Contains(renderer))
                {
                    renderers.Add(renderer);
                }
            }

            return renderers;
        }

        private static string NormalizeExpressionLibraryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                string.Equals(path, "Assets/Nyxara Ai Studio/AICompanionStudio/Expressions", StringComparison.Ordinal) ||
                string.Equals(path, "Assets/Nyxara AI Studio/AICompanionStudio/Expressions", StringComparison.Ordinal) ||
                string.Equals(path, "Assets/NyxaraAIStudio/Expressions", StringComparison.Ordinal) ||
                string.Equals(path, "Assets/AICompanionStudio/Expressions", StringComparison.Ordinal) ||
                !path.StartsWith("Assets/Nyxara AI Studio/", StringComparison.Ordinal))
            {
                return "Assets/Nyxara AI Studio/Generated/Expressions";
            }

            return path;
        }

        private static void ApplyBlendshapeWeight(SkinnedMeshRenderer renderer, string blendshapeName, float weight)
        {
            if (renderer == null || renderer.sharedMesh == null || string.IsNullOrWhiteSpace(blendshapeName))
            {
                return;
            }

            var appliedIndices = new HashSet<int>();
            foreach (var candidate in ArkItBlendshapeDriver.ResolveBlendshapeCandidates(blendshapeName))
            {
                var index = renderer.sharedMesh.GetBlendShapeIndex(candidate);
                if (index < 0 || !appliedIndices.Add(index))
                {
                    continue;
                }

                renderer.SetBlendShapeWeight(index, weight);
            }
        }
    }
}
