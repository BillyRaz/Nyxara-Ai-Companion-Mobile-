// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Nyxara.AICompanion.Expressions;
using Nyxara.AICompanion.Face;
using UnityEditor;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    public class ExpressionEditorWindow : EditorWindow
    {
        private const string DefaultExpressionFolder = "Assets/Nyxara AI Studio/Generated/Expressions";

        private enum EditorTab
        {
            ExpressionEditor,
            LibraryBrowser,
            BlendShapeMapper
        }

        [Serializable]
        private class FloatClipboardData
        {
            public List<string> keys = new();
            public List<float> values = new();
        }

        private EditorTab _currentTab = EditorTab.ExpressionEditor;
        private Vector2 _scrollPosition;

        private SkinnedMeshRenderer _targetRenderer;
        private readonly List<string> _blendshapeNames = new();
        private readonly Dictionary<string, float> _currentWeights = new();
        private string _newPresetName = "New Expression";
        private ExpressionCategory _newPresetCategory = ExpressionCategory.Emotion;
        private string _newPresetDescription = "";
        private float _previewTransitionSpeed = 0.15f;

        private ExpressionLibraryManager _libraryManager;
        private string _searchFilter = "";
        private bool _useCategoryFilter;
        private ExpressionCategory _categoryFilter = ExpressionCategory.Emotion;
        private ExpressionPreset _selectedPreset;

        private readonly Dictionary<string, string> _signalToBlendshapeMap = new();
        private readonly Dictionary<string, float> _signalWeightMap = new();
        private readonly List<string> _availableSignals = new() { "smile", "eyebrow_raise", "head_tilt", "suspicious_look", "shy_smile", "amused_smirk", "concerned", "bold_stare" };

        [MenuItem("Nyxara AI/Editors/Expression Editor", false, 20)]
        public static void ShowWindow()
        {
            var window = GetWindow<ExpressionEditorWindow>("Nyxara AI Expression Editor");
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        private void OnEnable()
        {
            if (_targetRenderer == null)
            {
                _targetRenderer = FindFirstObjectByType<SkinnedMeshRenderer>();
            }

            if (_targetRenderer != null)
            {
                RefreshBlendshapeList();
            }

            _libraryManager = FindFirstObjectByType<ExpressionLibraryManager>();
            LoadSignalMappings();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawTabs();
            EditorGUILayout.Space(10);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            switch (_currentTab)
            {
                case EditorTab.ExpressionEditor:
                    DrawExpressionEditorTab();
                    break;
                case EditorTab.LibraryBrowser:
                    DrawLibraryBrowserTab();
                    break;
                case EditorTab.BlendShapeMapper:
                    DrawBlendShapeMapperTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Nyxara Expression Studio", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Target:", GUILayout.Width(45));
            var newRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(_targetRenderer, typeof(SkinnedMeshRenderer), true, GUILayout.Width(180));
            if (newRenderer != _targetRenderer)
            {
                _targetRenderer = newRenderer;
                if (_targetRenderer != null)
                {
                    RefreshBlendshapeList();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Expression Editor", EditorStyles.miniButtonLeft))
            {
                _currentTab = EditorTab.ExpressionEditor;
            }

            if (GUILayout.Button("Library Browser", EditorStyles.miniButtonMid))
            {
                _currentTab = EditorTab.LibraryBrowser;
            }

            if (GUILayout.Button("BlendShape Mapper", EditorStyles.miniButtonRight))
            {
                _currentTab = EditorTab.BlendShapeMapper;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawExpressionEditorTab()
        {
            if (_targetRenderer == null)
            {
                EditorGUILayout.HelpBox("No SkinnedMeshRenderer selected. Please assign a face renderer.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Expression Designer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Adjust sliders to create facial expressions and save them to the library.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset All", GUILayout.Width(100)))
            {
                ResetAllWeights();
            }

            if (GUILayout.Button("Copy Current", GUILayout.Width(100)))
            {
                CopyCurrentWeights();
            }

            if (GUILayout.Button("Paste", GUILayout.Width(80)))
            {
                PasteWeights();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            foreach (var name in _blendshapeNames)
            {
                var currentWeight = _currentWeights.TryGetValue(name, out var value) ? value : 0f;
                var newWeight = EditorGUILayout.Slider(name, currentWeight, 0f, 100f);
                if (Math.Abs(newWeight - currentWeight) > 0.01f)
                {
                    _currentWeights[name] = newWeight;
                    ApplyWeight(name, newWeight);
                }
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Save to Library", EditorStyles.boldLabel);
            _newPresetName = EditorGUILayout.TextField("Preset Name", _newPresetName);
            _newPresetCategory = (ExpressionCategory)EditorGUILayout.EnumPopup("Category", _newPresetCategory);
            _newPresetDescription = EditorGUILayout.TextField("Description", _newPresetDescription);
            _previewTransitionSpeed = EditorGUILayout.Slider("Transition Speed (s)", _previewTransitionSpeed, 0.05f, 1f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to Library", GUILayout.Height(30)))
            {
                SaveCurrentAsPreset();
            }

            if (GUILayout.Button("Save & Replace Selected", GUILayout.Height(30)))
            {
                SaveAndReplacePreset();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawLibraryBrowserTab()
        {
            if (_libraryManager == null)
            {
                EditorGUILayout.HelpBox("No ExpressionLibraryManager found in scene. Please add one to your AI companion root.", MessageType.Warning);
                if (GUILayout.Button("Create Library Manager"))
                {
                    CreateLibraryManager();
                }

                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Expression Library", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _searchFilter = EditorGUILayout.TextField("Search:", _searchFilter);
            _useCategoryFilter = EditorGUILayout.ToggleLeft("Filter Category", _useCategoryFilter, GUILayout.Width(110));
            if (_useCategoryFilter)
            {
                _categoryFilter = (ExpressionCategory)EditorGUILayout.EnumPopup(_categoryFilter, GUILayout.Width(110));
            }

            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                _searchFilter = "";
                _useCategoryFilter = false;
            }

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Refresh Library", GUILayout.Width(120)))
            {
                _libraryManager.LoadAllPresets();
            }

            EditorGUILayout.Space(10);
            var filteredPresets = FilterPresets(_libraryManager.LoadedPresets);
            if (filteredPresets.Count == 0)
            {
                EditorGUILayout.HelpBox("No expressions found. Create one in the Expression Editor tab.", MessageType.Info);
            }
            else
            {
                DrawPresetGrid(filteredPresets);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBlendShapeMapperTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Signal to BlendShape Mapping", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Map expression signals to specific blendshapes for automatic facial response.", MessageType.Info);

            if (_targetRenderer == null)
            {
                EditorGUILayout.HelpBox("Select a face renderer to see available blendshapes.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var blendshapeOptions = _blendshapeNames.ToArray();
            foreach (var signal in _availableSignals)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(signal, GUILayout.Width(150));
                if (!_signalToBlendshapeMap.ContainsKey(signal))
                {
                    _signalToBlendshapeMap[signal] = "";
                }

                if (!_signalWeightMap.ContainsKey(signal))
                {
                    _signalWeightMap[signal] = 50f;
                }

                var selectedIndex = Array.IndexOf(blendshapeOptions, _signalToBlendshapeMap[signal]);
                var newIndex = EditorGUILayout.Popup(selectedIndex < 0 ? 0 : selectedIndex, blendshapeOptions, GUILayout.Width(220));
                if (blendshapeOptions.Length > 0 && newIndex >= 0 && newIndex < blendshapeOptions.Length)
                {
                    _signalToBlendshapeMap[signal] = blendshapeOptions[newIndex];
                }

                _signalWeightMap[signal] = EditorGUILayout.Slider(_signalWeightMap[signal], 0f, 100f, GUILayout.Width(160));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Save Mappings to ExpressionSignalRouter"))
            {
                SaveSignalMappings();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPresetGrid(List<ExpressionPreset> presets)
        {
            const int columns = 3;
            var rows = Mathf.CeilToInt(presets.Count / (float)columns);
            for (var row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (var col = 0; col < columns; col++)
                {
                    var index = row * columns + col;
                    if (index >= presets.Count)
                    {
                        break;
                    }

                    DrawPresetCard(presets[index]);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
            }
        }

        private void DrawPresetCard(ExpressionPreset preset)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(180), GUILayout.Height(120));
            if (preset.thumbnail != null)
            {
                GUILayout.Box(preset.thumbnail, GUILayout.Width(160), GUILayout.Height(60));
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(160, 60);
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
                GUI.Label(rect, preset.displayName, EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.LabelField(preset.displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(preset.category.ToString(), EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                _libraryManager.ApplyPreset(preset);
                _selectedPreset = preset;
            }

            if (GUILayout.Button("Edit"))
            {
                LoadPresetIntoEditor(preset);
                _selectedPreset = preset;
                _currentTab = EditorTab.ExpressionEditor;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private List<ExpressionPreset> FilterPresets(IReadOnlyList<ExpressionPreset> presets)
        {
            var filtered = presets.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                filtered = filtered.Where(p =>
                    (!string.IsNullOrWhiteSpace(p.displayName) && p.displayName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(p.description) && p.description.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)));
            }

            if (_useCategoryFilter)
            {
                filtered = filtered.Where(p => p.category == _categoryFilter);
            }

            return filtered.ToList();
        }

        private void RefreshBlendshapeList()
        {
            if (_targetRenderer == null || _targetRenderer.sharedMesh == null)
            {
                return;
            }

            _blendshapeNames.Clear();
            _currentWeights.Clear();
            var mesh = _targetRenderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                _blendshapeNames.Add(name);
                _currentWeights[name] = _targetRenderer.GetBlendShapeWeight(i);
            }
        }

        private void ResetAllWeights()
        {
            foreach (var name in _blendshapeNames)
            {
                _currentWeights[name] = 0f;
                ApplyWeight(name, 0f);
            }
        }

        private void ApplyWeight(string name, float weight)
        {
            if (_targetRenderer == null || _targetRenderer.sharedMesh == null)
            {
                return;
            }

            var index = _targetRenderer.sharedMesh.GetBlendShapeIndex(name);
            if (index >= 0)
            {
                _targetRenderer.SetBlendShapeWeight(index, weight);
            }
        }

        private void CopyCurrentWeights()
        {
            var data = new FloatClipboardData();
            foreach (var pair in _currentWeights)
            {
                data.keys.Add(pair.Key);
                data.values.Add(pair.Value);
            }

            EditorGUIUtility.systemCopyBuffer = JsonUtility.ToJson(data);
            EditorUtility.DisplayDialog("Copied", "Current expression weights copied to clipboard.", "OK");
        }

        private void PasteWeights()
        {
            try
            {
                var data = JsonUtility.FromJson<FloatClipboardData>(EditorGUIUtility.systemCopyBuffer);
                if (data?.keys == null || data.values == null)
                {
                    return;
                }

                for (var i = 0; i < Mathf.Min(data.keys.Count, data.values.Count); i++)
                {
                    var key = data.keys[i];
                    if (_currentWeights.ContainsKey(key))
                    {
                        _currentWeights[key] = data.values[i];
                        ApplyWeight(key, data.values[i]);
                    }
                }
            }
            catch
            {
                EditorUtility.DisplayDialog("Paste Failed", "Clipboard does not contain valid expression data.", "OK");
            }
        }

        private void SaveCurrentAsPreset()
        {
            if (string.IsNullOrWhiteSpace(_newPresetName))
            {
                EditorUtility.DisplayDialog("Error", "Please enter a preset name.", "OK");
                return;
            }

            var preset = CreateInstance<ExpressionPreset>();
            preset.presetId = Guid.NewGuid().ToString();
            preset.displayName = _newPresetName;
            preset.category = _newPresetCategory;
            preset.description = _newPresetDescription;
            preset.transitionTimeInSeconds = _previewTransitionSpeed;
            preset.blendshapeWeights = _currentWeights
                .Where(kvp => kvp.Value > 0.01f)
                .Select(kvp => new BlendshapeWeight { blendshapeName = kvp.Key, weight = kvp.Value })
                .ToList();

            EnsureFolderPathExists(DefaultExpressionFolder);
            var path = EditorUtility.SaveFilePanelInProject("Save Expression Preset", $"{_newPresetName}.asset", "asset", "Choose location", DefaultExpressionFolder);
            if (!string.IsNullOrEmpty(path))
            {
                path = NormalizeOutputPath(path, DefaultExpressionFolder);
                AssetDatabase.CreateAsset(preset, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                _libraryManager?.LoadAllPresets();
            }
        }

        private static void EnsureFolderPathExists(string assetPath)
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

        private static string NormalizeOutputPath(string selectedPath, string fallbackFolder)
        {
            if (string.IsNullOrWhiteSpace(selectedPath) ||
                selectedPath.StartsWith("Assets/Nyxara AI Studio/", StringComparison.Ordinal))
            {
                return selectedPath;
            }

            return AssetDatabase.GenerateUniqueAssetPath($"{fallbackFolder}/{System.IO.Path.GetFileName(selectedPath)}");
        }

        private void SaveAndReplacePreset()
        {
            if (_selectedPreset == null)
            {
                EditorUtility.DisplayDialog("Info", "Select a preset in Library Browser first, then use Save & Replace Selected.", "OK");
                return;
            }

            _selectedPreset.displayName = _newPresetName;
            _selectedPreset.category = _newPresetCategory;
            _selectedPreset.description = _newPresetDescription;
            _selectedPreset.transitionTimeInSeconds = _previewTransitionSpeed;
            _selectedPreset.blendshapeWeights = _currentWeights
                .Where(kvp => kvp.Value > 0.01f)
                .Select(kvp => new BlendshapeWeight { blendshapeName = kvp.Key, weight = kvp.Value })
                .ToList();

            EditorUtility.SetDirty(_selectedPreset);
            AssetDatabase.SaveAssets();
            _libraryManager?.LoadAllPresets();
        }

        private void LoadPresetIntoEditor(ExpressionPreset preset)
        {
            _newPresetName = preset.displayName;
            _newPresetCategory = preset.category;
            _newPresetDescription = preset.description;
            ResetAllWeights();
            foreach (var weight in preset.blendshapeWeights)
            {
                _currentWeights[weight.blendshapeName] = weight.weight;
                ApplyWeight(weight.blendshapeName, weight.weight);
            }
        }

        private void CreateLibraryManager()
        {
            var go = new GameObject("ExpressionLibraryManager");
            _libraryManager = go.AddComponent<ExpressionLibraryManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create Expression Library Manager");
            Selection.activeGameObject = go;
        }

        private void LoadSignalMappings()
        {
            var router = FindFirstObjectByType<ExpressionSignalRouter>();
            if (router == null)
            {
                return;
            }

            _signalToBlendshapeMap.Clear();
            _signalWeightMap.Clear();
            foreach (var mapping in router.signalMappings)
            {
                _signalToBlendshapeMap[mapping.signalName] = mapping.blendshapeName;
                _signalWeightMap[mapping.signalName] = mapping.weight;
            }
        }

        private void SaveSignalMappings()
        {
            var router = FindFirstObjectByType<ExpressionSignalRouter>();
            if (router == null)
            {
                EditorUtility.DisplayDialog("Error", "No ExpressionSignalRouter found in scene.", "OK");
                return;
            }

            Undo.RecordObject(router, "Save Signal Mappings");
            router.signalMappings.Clear();
            foreach (var signal in _availableSignals)
            {
                if (_signalToBlendshapeMap.TryGetValue(signal, out var blendshapeName) && !string.IsNullOrWhiteSpace(blendshapeName))
                {
                    router.signalMappings.Add(new ExpressionSignalRouter.SignalMapping
                    {
                        signalName = signal,
                        blendshapeName = blendshapeName,
                        weight = _signalWeightMap.TryGetValue(signal, out var weight) ? weight : 50f
                    });
                }
            }

            router.RefreshMappings();
            EditorUtility.SetDirty(router);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
