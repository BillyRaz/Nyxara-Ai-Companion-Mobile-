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
    /// <summary>
    /// Central expression driver for Nyxara.
    ///
    /// The manager keeps one base expression plus any number of optional overlay
    /// expressions. Shapes shared by several layers are merged with Mathf.Max so
    /// no layer can produce conflicting writes. Built-in catalog entries are
    /// merged into the persisted library and persisted assets always win.
    ///
    /// Blink and gaze stay independent because emotion/personality presets opt
    /// into <see cref="ExpressionPreset.preserveIndependentChannels"/>. Mouth
    /// shapes stay reserved for lip sync while Expression Mode is off.
    /// </summary>
    public class ExpressionLibraryManager : MonoBehaviour
    {
        [Serializable]
        private sealed class OverlayEntry
        {
            public ExpressionPreset preset;
            [Range(0f, 1f)] public float intensity = 1f;
        }

        [Header("Library")]
        [SerializeField] private string expressionLibraryPath = "Assets/Nyxara AI Studio/Generated/Expressions";

        [Tooltip("Persisted presets are serialized here so player builds can load user overrides. The code catalog still supplies built-in presets at runtime.")]
        [SerializeField] private List<ExpressionPreset> presetLibrary = new();

        [Header("Face Renderers")]
        [SerializeField] private SkinnedMeshRenderer targetFaceRenderer;
        [SerializeField] private List<SkinnedMeshRenderer> additionalFaceRenderers = new();

        [Header("Driving")]
        [SerializeField] private bool expressionModeActive;
        [SerializeField, Range(0f, 1f)] private float expressionIntensity = 1f;
        [Tooltip("0 uses each preset's transition time. A positive value overrides every preset for live tuning.")]
        [SerializeField, Range(0f, 1f)] private float transitionDurationOverride;

        private readonly List<ExpressionPreset> _loadedPresets = new();
        private readonly Dictionary<string, ExpressionPreset> _presetLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ExpressionPreset> _builtInPresets = new();
        private readonly List<OverlayEntry> _overlays = new();
        private readonly Dictionary<string, float> _currentWeights = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _targetWeights = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _managedShapes = new(StringComparer.OrdinalIgnoreCase);

        private ExpressionPreset _currentPreset;
        private bool _isTransitioning;
        private float _transitionElapsed;
        private float _transitionDuration;

        public event Action<ExpressionPreset> OnPresetSelected;
        public event Action<IReadOnlyList<ExpressionPreset>> OnLibraryUpdated;
        public event Action OnExpressionStateChanged;

        public IReadOnlyList<ExpressionPreset> LoadedPresets => _loadedPresets;
        public IReadOnlyList<SkinnedMeshRenderer> TargetFaceRenderers => GetAllFaceRenderers();
        public bool ExpressionModeActive => expressionModeActive;
        public string ExpressionLibraryPath => expressionLibraryPath;
        public ExpressionPreset CurrentPreset => _currentPreset;
        public bool IsTransitioning => _isTransitioning;

        public IReadOnlyList<ExpressionPreset> ActiveOverlays =>
            _overlays.Where(entry => entry.preset != null).Select(entry => entry.preset).ToList();

        public float ExpressionIntensity
        {
            get => expressionIntensity;
            set
            {
                var clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(expressionIntensity, clamped))
                {
                    return;
                }

                expressionIntensity = clamped;
                if (_currentPreset != null)
                {
                    RebuildTargetsAndApply();
                    OnExpressionStateChanged?.Invoke();
                }
            }
        }

        public float TransitionDurationOverride
        {
            get => transitionDurationOverride;
            set
            {
                var clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(transitionDurationOverride, clamped))
                {
                    return;
                }

                transitionDurationOverride = clamped;
                OnExpressionStateChanged?.Invoke();
            }
        }

        private void Awake()
        {
            expressionLibraryPath = NormalizeExpressionLibraryPath(expressionLibraryPath);
            LoadAllPresets();
        }

        private void OnValidate()
        {
            expressionLibraryPath = NormalizeExpressionLibraryPath(expressionLibraryPath);
            expressionIntensity = Mathf.Clamp01(expressionIntensity);
            transitionDurationOverride = Mathf.Clamp01(transitionDurationOverride);
        }

        private void LateUpdate()
        {
            if (!_isTransitioning)
            {
                return;
            }

            // The editor window applies previews immediately. Transitions only run
            // while the game is playing so scene editing stays deterministic.
            if (!Application.isPlaying)
            {
                ApplyImmediately();
                return;
            }

            var renderers = GetAllFaceRenderers();
            if (renderers.Count == 0)
            {
                _isTransitioning = false;
                return;
            }

            _transitionElapsed += Time.deltaTime;
            var t = _transitionDuration <= 0.0001f
                ? 1f
                : Mathf.Clamp01(_transitionElapsed / _transitionDuration);
            var eased = Mathf.SmoothStep(0f, 1f, t);

            foreach (var shape in CollectTransitionShapes())
            {
                var from = _currentWeights.TryGetValue(shape, out var current) ? current : 0f;
                var to = _targetWeights.TryGetValue(shape, out var target) ? target : 0f;
                var value = Mathf.Lerp(from, to, eased);
                WriteShape(renderers, shape, value);
                _currentWeights[shape] = value;
            }

            if (t >= 1f)
            {
                ApplyImmediately();
            }
        }

        public void LoadAllPresets()
        {
            _loadedPresets.Clear();
            _presetLookup.Clear();

            // Serialized assets first: these are the user's overrides and must win.
            foreach (var preset in presetLibrary)
            {
                AddPreset(preset);
            }

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(expressionLibraryPath) && AssetDatabase.IsValidFolder(expressionLibraryPath))
            {
                var guids = AssetDatabase.FindAssets("t:ExpressionPreset", new[] { expressionLibraryPath });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    AddPreset(AssetDatabase.LoadAssetAtPath<ExpressionPreset>(path));
                }
            }
#endif

            EnsureBuiltInPresets();
            foreach (var builtIn in _builtInPresets)
            {
                if (builtIn == null)
                {
                    continue;
                }

                var key = GetPresetKey(builtIn);
                if (!string.IsNullOrWhiteSpace(key) && _presetLookup.ContainsKey(key))
                {
                    continue;
                }

                if (_loadedPresets.Any(preset =>
                        preset != null &&
                        string.Equals(preset.displayName, builtIn.displayName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                AddPreset(builtIn);
            }

            var sorted = _loadedPresets
                .Where(preset => preset != null)
                .OrderBy(preset => ExpressionCategoryUtility.GetSortOrder(preset.category))
                .ThenBy(preset => preset.displayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _loadedPresets.Clear();
            _loadedPresets.AddRange(sorted);

            OnLibraryUpdated?.Invoke(_loadedPresets);
        }

        public ExpressionPreset GetPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return null;
            }

            return _presetLookup.TryGetValue(presetId, out var preset)
                ? preset
                : FindPresetByDisplayName(presetId);
        }

        public void ApplyPreset(string presetId)
        {
            ApplyPreset(GetPreset(presetId));
        }

        public void ApplyPreset(string presetId, float intensity)
        {
            ApplyPreset(GetPreset(presetId), intensity);
        }

        public void ApplyPreset(ExpressionPreset preset)
        {
            ApplyPreset(preset, expressionIntensity);
        }

        public void ApplyPreset(ExpressionPreset preset, float intensity)
        {
            if (preset == null)
            {
                ResetToNeutral();
                return;
            }

            expressionIntensity = Mathf.Clamp01(intensity);
            _currentPreset = preset;

            if (GetAllFaceRenderers().Count > 0)
            {
                RebuildTargetsAndApply();
            }

            OnPresetSelected?.Invoke(preset);
            OnExpressionStateChanged?.Invoke();
        }

        public void ApplyBasePreset(ExpressionPreset preset, float intensity = -1f)
        {
            ApplyPreset(preset, intensity < 0f ? expressionIntensity : intensity);
        }

        public bool AddOverlayPreset(ExpressionPreset preset, float intensity = 1f, bool replaceExistingIntensity = true)
        {
            if (preset == null)
            {
                return false;
            }

            var clamped = Mathf.Clamp01(intensity);
            var existing = _overlays.FirstOrDefault(entry => PresetsMatch(entry.preset, preset));
            if (existing != null)
            {
                if (replaceExistingIntensity)
                {
                    existing.intensity = clamped;
                }
            }
            else
            {
                _overlays.Add(new OverlayEntry { preset = preset, intensity = clamped });
            }

            RebuildTargetsAndApply();
            OnExpressionStateChanged?.Invoke();
            return true;
        }

        public bool RemoveOverlayPreset(ExpressionPreset preset)
        {
            var removed = _overlays.RemoveAll(entry => PresetsMatch(entry.preset, preset));
            if (removed <= 0)
            {
                return false;
            }

            RebuildTargetsAndApply();
            OnExpressionStateChanged?.Invoke();
            return true;
        }

        public bool ToggleOverlayPreset(ExpressionPreset preset, float intensity = 1f)
        {
            if (preset == null)
            {
                return false;
            }

            if (IsOverlayActive(preset))
            {
                RemoveOverlayPreset(preset);
                return false;
            }

            AddOverlayPreset(preset, intensity);
            return true;
        }

        public bool IsOverlayActive(ExpressionPreset preset)
        {
            return _overlays.Any(entry => PresetsMatch(entry.preset, preset));
        }

        public float GetOverlayIntensity(ExpressionPreset preset)
        {
            var entry = _overlays.FirstOrDefault(candidate => PresetsMatch(candidate.preset, preset));
            return entry != null ? entry.intensity : 0f;
        }

        public void SetOverlayIntensity(ExpressionPreset preset, float intensity)
        {
            var entry = _overlays.FirstOrDefault(candidate => PresetsMatch(candidate.preset, preset));
            if (entry == null)
            {
                return;
            }

            entry.intensity = Mathf.Clamp01(intensity);
            RebuildTargetsAndApply();
            OnExpressionStateChanged?.Invoke();
        }

        public void ClearOverlays()
        {
            if (_overlays.Count == 0)
            {
                return;
            }

            _overlays.Clear();
            RebuildTargetsAndApply();
            OnExpressionStateChanged?.Invoke();
        }

        /// <summary>
        /// Legacy builder/preview path. Applies an explicit shape dictionary
        /// immediately and clears the preset layers so it cannot be overwritten by
        /// a pending transition.
        /// </summary>
        public void ApplyExpressionWeights(IReadOnlyDictionary<string, float> blendshapeWeights, bool resetToNeutralFirst = true)
        {
            if (blendshapeWeights == null)
            {
                return;
            }

            if (resetToNeutralFirst)
            {
                ResetToNeutral();
            }

            _currentPreset = null;
            _overlays.Clear();
            _targetWeights.Clear();

            foreach (var pair in blendshapeWeights)
            {
                if (!expressionModeActive && ExpressionBuilderHelper.IsMouthRelatedBlendshape(pair.Key))
                {
                    continue;
                }

                var canonical = ArkItBlendshapeDriver.GetCanonicalBlendshapeName(pair.Key);
                if (string.IsNullOrWhiteSpace(canonical) || pair.Value <= 0.01f)
                {
                    continue;
                }

                var weight = Mathf.Clamp(pair.Value, 0f, 100f);
                if (_targetWeights.TryGetValue(canonical, out var existing))
                {
                    _targetWeights[canonical] = Mathf.Max(existing, weight);
                }
                else
                {
                    _targetWeights[canonical] = weight;
                }
            }

            _isTransitioning = false;
            ApplyImmediately();
            OnExpressionStateChanged?.Invoke();
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
                    if (weight <= 0.01f)
                    {
                        continue;
                    }

                    var blendshapeName = ArkItBlendshapeDriver.GetCanonicalBlendshapeName(mesh.GetBlendShapeName(i));
                    if (string.IsNullOrWhiteSpace(blendshapeName))
                    {
                        continue;
                    }

                    result[blendshapeName] = result.TryGetValue(blendshapeName, out var existingWeight)
                        ? Mathf.Max(existingWeight, weight)
                        : weight;
                }
            }

            return result;
        }

        public ExpressionPreset FindPresetByDisplayName(string displayName)
        {
            return _loadedPresets.FirstOrDefault(preset =>
                string.Equals(preset.displayName, displayName, StringComparison.OrdinalIgnoreCase));
        }

        public void ResetToNeutral()
        {
            _currentPreset = null;
            _overlays.Clear();
            _targetWeights.Clear();
            _currentWeights.Clear();
            _managedShapes.Clear();
            _isTransitioning = false;

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

            OnExpressionStateChanged?.Invoke();
        }

        public List<ExpressionPreset> GetPresetsByCategory(ExpressionCategory category)
        {
            return _loadedPresets.Where(preset => preset.category == category).ToList();
        }

        public void SetExpressionMode(bool active)
        {
            if (expressionModeActive == active)
            {
                return;
            }

            expressionModeActive = active;
            if (_currentPreset != null || _overlays.Count > 0)
            {
                RebuildTargetsAndApply();
            }
            else if (!active)
            {
                ReleaseManagedMouthShapes();
            }

            OnExpressionStateChanged?.Invoke();
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

        /// <summary>Rebuilds the built-in catalog after a domain reload or code change.</summary>
        public void ReloadCatalog()
        {
            _builtInPresets.Clear();
            NyxaraExpressionCatalog.InvalidateCache();
            LoadAllPresets();
        }

        public void ReapplyCurrentExpression()
        {
            if (_currentPreset != null || _overlays.Count > 0)
            {
                RebuildTargetsAndApply();
            }
        }

        private void RebuildTargetsAndApply()
        {
            _targetWeights.Clear();

            if (_currentPreset != null)
            {
                AddPresetWeights(
                    _currentPreset,
                    expressionIntensity,
                    preserveIndependentChannels: _currentPreset.preserveIndependentChannels);
            }

            foreach (var overlay in _overlays)
            {
                if (overlay.preset != null)
                {
                    AddPresetWeights(overlay.preset, overlay.intensity, preserveIndependentChannels: false);
                }
            }

            if (GetAllFaceRenderers().Count == 0)
            {
                return;
            }

            var duration = ResolveTransitionDuration();
            if (!Application.isPlaying || duration <= 0.001f)
            {
                ApplyImmediately();
                return;
            }

            SyncCurrentWeights();
            _transitionElapsed = 0f;
            _transitionDuration = duration;
            _isTransitioning = true;
        }

        private void AddPresetWeights(ExpressionPreset preset, float intensity, bool preserveIndependentChannels)
        {
            if (preset == null || preset.blendshapeWeights == null)
            {
                return;
            }

            var scale = Mathf.Clamp01(intensity);
            foreach (var weight in preset.blendshapeWeights)
            {
                if (weight == null || weight.weight <= 0.01f || string.IsNullOrWhiteSpace(weight.blendshapeName))
                {
                    continue;
                }

                var canonical = ArkItBlendshapeDriver.GetCanonicalBlendshapeName(weight.blendshapeName);
                if (string.IsNullOrWhiteSpace(canonical))
                {
                    continue;
                }

                if (preserveIndependentChannels && ExpressionLibraryAudit.IsIndependentChannel(canonical))
                {
                    continue;
                }

                if (!expressionModeActive && ExpressionBuilderHelper.IsMouthRelatedBlendshape(canonical))
                {
                    continue;
                }

                var scaled = Mathf.Clamp(weight.weight, 0f, 100f) * scale;
                if (scaled <= 0.01f)
                {
                    continue;
                }

                if (_targetWeights.TryGetValue(canonical, out var existing))
                {
                    _targetWeights[canonical] = Mathf.Max(existing, scaled);
                }
                else
                {
                    _targetWeights[canonical] = scaled;
                }
            }
        }

        private void SyncCurrentWeights()
        {
            var renderers = GetAllFaceRenderers();
            foreach (var shape in CollectTransitionShapes())
            {
                _currentWeights[shape] = ReadShapeWeight(renderers, shape);
            }
        }

        private HashSet<string> CollectTransitionShapes()
        {
            var shapes = new HashSet<string>(_targetWeights.Keys, StringComparer.OrdinalIgnoreCase);
            shapes.UnionWith(_managedShapes);
            shapes.UnionWith(_currentWeights.Keys);
            return shapes;
        }

        private void ApplyImmediately()
        {
            var renderers = GetAllFaceRenderers();
            if (renderers.Count == 0)
            {
                _isTransitioning = false;
                return;
            }

            foreach (var shape in CollectTransitionShapes())
            {
                var weight = _targetWeights.TryGetValue(shape, out var target) ? target : 0f;
                WriteShape(renderers, shape, weight);
                _currentWeights[shape] = weight;
            }

            _managedShapes.Clear();
            _managedShapes.UnionWith(_targetWeights.Keys);
            _isTransitioning = false;
        }

        private void ReleaseManagedMouthShapes()
        {
            var renderers = GetAllFaceRenderers();
            var mouthShapes = _managedShapes
                .Where(ExpressionBuilderHelper.IsMouthRelatedBlendshape)
                .ToList();

            foreach (var shape in mouthShapes)
            {
                WriteShape(renderers, shape, 0f);
                _currentWeights[shape] = 0f;
                _managedShapes.Remove(shape);
            }
        }

        private float ResolveTransitionDuration()
        {
            if (transitionDurationOverride > 0.001f)
            {
                return Mathf.Clamp(transitionDurationOverride, 0.01f, 2f);
            }

            var duration = 0f;
            if (_currentPreset != null)
            {
                duration = Mathf.Max(duration, _currentPreset.transitionTimeInSeconds);
            }

            foreach (var overlay in _overlays)
            {
                if (overlay.preset != null)
                {
                    duration = Mathf.Max(duration, overlay.preset.transitionTimeInSeconds);
                }
            }

            if (duration <= 0.001f)
            {
                duration = 0.15f;
            }

            return Mathf.Clamp(duration, 0.01f, 2f);
        }

        private void EnsureBuiltInPresets()
        {
            _builtInPresets.RemoveAll(preset => preset == null);
            var catalogPresets = NyxaraExpressionCatalog.GetPresets();
            if (_builtInPresets.Count == catalogPresets.Count)
            {
                return;
            }

            _builtInPresets.Clear();
            foreach (var preset in catalogPresets)
            {
                if (preset != null)
                {
                    _builtInPresets.Add(preset);
                }
            }
        }

        private void AddPreset(ExpressionPreset preset)
        {
            if (preset == null || _loadedPresets.Contains(preset))
            {
                return;
            }

            var key = GetPresetKey(preset);
            if (!string.IsNullOrWhiteSpace(key) && _presetLookup.ContainsKey(key))
            {
                return;
            }

            _loadedPresets.Add(preset);
            if (!string.IsNullOrWhiteSpace(key))
            {
                _presetLookup[key] = preset;
            }
        }

        private static string GetPresetKey(ExpressionPreset preset)
        {
            if (preset == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preset.presetId))
            {
                return preset.presetId;
            }

            return !string.IsNullOrWhiteSpace(preset.displayName)
                ? $"name:{preset.displayName}"
                : null;
        }

        private static bool PresetsMatch(ExpressionPreset left, ExpressionPreset right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left == right)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(left.presetId) && !string.IsNullOrWhiteSpace(right.presetId))
            {
                return string.Equals(left.presetId, right.presetId, StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(left.displayName) &&
                   string.Equals(left.displayName, right.displayName, StringComparison.OrdinalIgnoreCase);
        }

        private static float ReadShapeWeight(IReadOnlyList<SkinnedMeshRenderer> renderers, string shape)
        {
            var weight = 0f;
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var index = ArkItBlendshapeDriver.FindBlendshapeIndex(renderer.sharedMesh, shape);
                if (index >= 0)
                {
                    weight = Mathf.Max(weight, renderer.GetBlendShapeWeight(index));
                }
            }

            return weight;
        }

        private static void WriteShape(IReadOnlyList<SkinnedMeshRenderer> renderers, string shape, float weight)
        {
            foreach (var renderer in renderers)
            {
                ApplyBlendshapeWeight(renderer, shape, weight);
            }
        }

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
                var index = ArkItBlendshapeDriver.FindBlendshapeIndex(renderer.sharedMesh, candidate);
                if (index < 0 || !appliedIndices.Add(index))
                {
                    continue;
                }

                renderer.SetBlendShapeWeight(index, weight);
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
            var existingPath = existing != null ? AssetDatabase.GetAssetPath(existing) : string.Empty;
            var createNewAsset = string.IsNullOrEmpty(existingPath);

            ExpressionPreset preset;
            if (createNewAsset)
            {
                preset = ScriptableObject.CreateInstance<ExpressionPreset>();
                if (existing != null)
                {
                    // The user is overriding a code-defined preset. Reuse its id so
                    // the persisted asset wins on the next library merge.
                    preset.presetId = existing.presetId;
                    preset.holdDuration = existing.holdDuration;
                    preset.autoReturnToNeutral = existing.autoReturnToNeutral;
                    preset.preserveIndependentChannels = existing.preserveIndependentChannels;
                    preset.thumbnail = existing.thumbnail;
                }
                else
                {
                    preset.presetId = Guid.NewGuid().ToString();
                }
            }
            else
            {
                preset = existing;
            }

            preset.displayName = presetName.Trim();
            preset.category = category;
            preset.description = description?.Trim() ?? string.Empty;
            preset.transitionTimeInSeconds = Mathf.Clamp(transitionTimeInSeconds, 0.01f, 2f);

            var canonicalWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in blendshapeWeights)
            {
                if (pair.Value <= 0.01f)
                {
                    continue;
                }

                var canonical = ArkItBlendshapeDriver.GetCanonicalBlendshapeName(pair.Key);
                if (string.IsNullOrWhiteSpace(canonical))
                {
                    continue;
                }

                var weight = Mathf.Clamp(pair.Value, 0f, 100f);
                canonicalWeights[canonical] = canonicalWeights.TryGetValue(canonical, out var existingWeight)
                    ? Mathf.Max(existingWeight, weight)
                    : weight;
            }

            preset.blendshapeWeights = canonicalWeights
                .Select(pair => new BlendshapeWeight
                {
                    blendshapeName = pair.Key,
                    weight = pair.Value
                })
                .ToList();

            if (createNewAsset)
            {
                var assetName = SanitizeFileName(preset.displayName);
                var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{expressionLibraryPath}/{assetName}.asset");
                AssetDatabase.CreateAsset(preset, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(preset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            LoadAllPresets();
            return FindPresetByDisplayName(preset.displayName) ?? preset;
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
                Debug.LogWarning(
                    $"[Nyxara Expressions] '{preset.displayName}' is a built-in catalog preset. Delete its generated asset instead of the in-memory instance.");
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
    }
}
