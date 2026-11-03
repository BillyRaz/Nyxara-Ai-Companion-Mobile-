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
    public partial class NyxaraCompanionStudioWindow
    {
        private Vector2 _expressionLibraryScroll;
        private string _expressionLibrarySearch = string.Empty;
        private ExpressionCategory _expressionLibraryCategory = ExpressionCategory.Emotion;
        private ExpressionLibraryAuditReport _expressionLibraryAudit;
        private bool _expressionLibraryAuditDirty = true;
        private bool _expressionLibraryShowAudit;
        private bool _expressionLibraryShowPersistedOnly;

        private string _expressionLibrarySaveName = "New Expression";
        private string _expressionLibrarySaveDescription = string.Empty;
        private ExpressionCategory _expressionLibrarySaveCategory = ExpressionCategory.Emotion;
        private float _expressionLibrarySaveTransition = 0.15f;

        private Queue<ExpressionPreset> _expressionTestQueue;
        private ExpressionLibraryManager _expressionTestManager;
        private double _expressionTestNextAt;
        private float _expressionTestHoldSeconds = 1.2f;
        private bool _expressionTestRunning;

        private void DrawExpressionLibraryPanel(
            GameObject studioRoot,
            ExpressionLibraryManager expressionLibrary,
            ArkItBlendshapeDriver faceDriver)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Nyxara Expression Library", EditorStyles.boldLabel);

            if (expressionLibrary == null)
            {
                EditorGUILayout.HelpBox(
                    "No ExpressionLibraryManager is assigned on the studio root. Build the studio or add the component to expose the library controls.",
                    MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            if (expressionLibrary.LoadedPresets.Count == 0)
            {
                expressionLibrary.LoadAllPresets();
                _expressionLibraryAuditDirty = true;
            }

            var audit = EnsureLibraryAudit(expressionLibrary, false);
            EditorGUILayout.LabelField(
                "Library",
                $"{expressionLibrary.LoadedPresets.Count} presets  |  {audit?.supportedCount ?? 0} supported  |  " +
                $"{audit?.partialCount ?? 0} partial  |  {audit?.unsupportedCount ?? 0} unsupported");
            EditorGUILayout.LabelField(
                "Renderers",
                string.Join(", ", expressionLibrary.TargetFaceRenderers.Select(renderer => renderer != null ? renderer.name : "<Missing>")));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Library"))
            {
                expressionLibrary.LoadAllPresets();
                _expressionLibraryAuditDirty = true;
                Repaint();
            }

            if (GUILayout.Button("Generate Assets"))
            {
                var generated = NyxaraExpressionLibraryBuilder.GenerateLibrary(expressionLibrary, false);
                _expressionLibraryAuditDirty = true;
                if (generated == 0)
                {
                    Debug.Log("[Nyxara Expressions] Every catalog preset already has a persisted asset.");
                }

                Repaint();
            }

            if (GUILayout.Button("Rebuild Assets (Overwrite)"))
            {
                if (EditorUtility.DisplayDialog(
                        "Rebuild Expression Assets",
                        "Regenerate every catalog preset asset from code, overwriting edits made to matching asset files. Presets outside the catalog are untouched.",
                        "Rebuild",
                        "Cancel"))
                {
                    NyxaraExpressionLibraryBuilder.GenerateLibrary(expressionLibrary, true);
                    _expressionLibraryAuditDirty = true;
                    Repaint();
                }
            }

            if (GUILayout.Button("Audit"))
            {
                _expressionLibraryAudit = NyxaraExpressionLibraryBuilder.Audit(expressionLibrary);
                _expressionLibraryShowAudit = true;
                _expressionLibraryAuditDirty = false;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();

            DrawExpressionLibraryState(studioRoot, expressionLibrary);
            DrawExpressionLibraryFilters(expressionLibrary);

            EditorGUILayout.Space(6f);
            DrawExpressionLibraryPresetList(expressionLibrary);
            DrawExpressionLibrarySelectedDetails(studioRoot, expressionLibrary);
            DrawExpressionLibraryTestControls(expressionLibrary);
            DrawExpressionLibrarySaveControls(expressionLibrary);
            DrawExpressionLibraryAuditPanel(expressionLibrary);

            EditorGUILayout.EndVertical();
        }

        private void DrawExpressionLibraryState(GameObject studioRoot, ExpressionLibraryManager expressionLibrary)
        {
            EditorGUILayout.Space(4f);
            var intensity = EditorGUILayout.Slider("Intensity", expressionLibrary.ExpressionIntensity, 0f, 1f);
            if (!Mathf.Approximately(intensity, expressionLibrary.ExpressionIntensity))
            {
                expressionLibrary.ExpressionIntensity = intensity;
            }

            var transition = EditorGUILayout.Slider(
                new GUIContent("Transition Override", "0 uses the preset's own transition time. Positive values override every preset while tuning."),
                expressionLibrary.TransitionDurationOverride,
                0f,
                1f);
            if (!Mathf.Approximately(transition, expressionLibrary.TransitionDurationOverride))
            {
                expressionLibrary.TransitionDurationOverride = transition;
            }

            var baseName = expressionLibrary.CurrentPreset != null
                ? expressionLibrary.CurrentPreset.displayName
                : "None";
            EditorGUILayout.LabelField("Base Expression", baseName);

            var overlays = expressionLibrary.ActiveOverlays;
            if (overlays.Count == 0)
            {
                EditorGUILayout.LabelField("Overlays", "None");
            }
            else
            {
                EditorGUILayout.LabelField("Overlays", $"{overlays.Count} active");
                foreach (var overlay in overlays.ToList())
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  {overlay.displayName}", GUILayout.Width(180f));
                    var overlayIntensity = expressionLibrary.GetOverlayIntensity(overlay);
                    var newOverlayIntensity = EditorGUILayout.Slider(overlayIntensity, 0f, 1f, GUILayout.Width(160f));
                    if (!Mathf.Approximately(overlayIntensity, newOverlayIntensity))
                    {
                        expressionLibrary.SetOverlayIntensity(overlay, newOverlayIntensity);
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                    {
                        expressionLibrary.RemoveOverlayPreset(overlay);
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            if (!expressionLibrary.ExpressionModeActive)
            {
                EditorGUILayout.HelpBox(
                    "Expression Mode is OFF: mouth and jaw shapes are reserved for lip sync. Eye and upper-face presets work normally. Enable Expression Mode to preview mouth actions.",
                    MessageType.Info);
                if (GUILayout.Button("Enable Expression Mode For Mouth Actions"))
                {
                    EnsureExpressionModeForEditing(studioRoot);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = expressionLibrary.CurrentPreset != null || overlays.Count > 0 || expressionLibrary.ExpressionModeActive;
            if (GUILayout.Button("Reset Face"))
            {
                expressionLibrary.ResetToNeutral();
                _expressionLibraryAuditDirty = true;
            }

            GUI.enabled = overlays.Count > 0;
            if (GUILayout.Button("Clear Overlays"))
            {
                expressionLibrary.ClearOverlays();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawExpressionLibraryFilters(ExpressionLibraryManager expressionLibrary)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            _expressionLibrarySearch = EditorGUILayout.TextField(
                new GUIContent("Search", "Filter by name or description."),
                _expressionLibrarySearch);

            _expressionLibraryShowPersistedOnly = EditorGUILayout.ToggleLeft(
                new GUIContent("Assets only", "Hide the read-only catalog entries that have not been generated as assets yet."),
                _expressionLibraryShowPersistedOnly,
                GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();

            var categories = ExpressionCategoryUtility.LibraryOrder;
            var labels = categories.Select(ExpressionCategoryUtility.GetDisplayName).ToArray();
            var currentIndex = Mathf.Max(0, Array.IndexOf(categories, _expressionLibraryCategory));
            var newIndex = GUILayout.Toolbar(currentIndex, labels);
            if (newIndex >= 0 && newIndex < categories.Length)
            {
                _expressionLibraryCategory = categories[newIndex];
            }

            if (GUILayout.Button("Test Entire Category", GUILayout.Height(22f)))
            {
                var presets = FilterLibraryPresets(expressionLibrary).ToList();
                if (presets.Count == 0)
                {
                    Debug.LogWarning("[Nyxara Expressions] No presets match the current category/search filter.");
                }
                else
                {
                    StartExpressionLibraryTest(expressionLibrary, presets);
                }
            }

            if (!expressionLibrary.ExpressionModeActive &&
                (_expressionLibraryCategory == ExpressionCategory.MouthAction ||
                 _expressionLibraryCategory == ExpressionCategory.Speech))
            {
                EditorGUILayout.HelpBox(
                    "Expression Mode is OFF, so lip sync owns the mouth. These presets will not visibly drive mouth/jaw shapes until you enable Expression Mode in the panel above.",
                    MessageType.Info);
            }
            else if (_expressionLibraryCategory == ExpressionCategory.EyeAction)
            {
                EditorGUILayout.HelpBox(
                    "Eye actions are designed as overlays. Use the Overlay button to combine them with an emotion while blinking and gaze remain independent.",
                    MessageType.None);
            }
        }

        private void DrawExpressionLibraryPresetList(ExpressionLibraryManager expressionLibrary)
        {
            var presets = FilterLibraryPresets(expressionLibrary).ToList();
            if (presets.Count == 0)
            {
                EditorGUILayout.HelpBox("No presets match this category and search.", MessageType.None);
                return;
            }

            _expressionLibraryScroll = EditorGUILayout.BeginScrollView(
                _expressionLibraryScroll,
                GUILayout.MinHeight(200f),
                GUILayout.MaxHeight(340f));

            foreach (var preset in presets)
            {
                var presetAudit = _expressionLibraryAudit?.Find(preset);
                var supportColor = GetSupportColor(presetAudit?.support ?? ExpressionSupportLevel.Supported);
                var isSelected = _selectedExpressionPreset == preset ||
                                 (_selectedExpressionPreset != null &&
                                  !string.IsNullOrWhiteSpace(preset.presetId) &&
                                  string.Equals(_selectedExpressionPreset.presetId, preset.presetId, StringComparison.OrdinalIgnoreCase));
                var overlayActive = expressionLibrary.IsOverlayActive(preset);
                var label = new GUIContent(
                    preset.displayName,
                    BuildPresetTooltip(preset, presetAudit));

                EditorGUILayout.BeginHorizontal();
                var previousColor = GUI.contentColor;
                GUI.contentColor = isSelected ? Color.white : supportColor;
                if (GUILayout.Button(
                        label,
                        isSelected ? EditorStyles.toolbarButton : EditorStyles.miniButtonLeft,
                        GUILayout.Width(220f),
                        GUILayout.Height(22f)))
                {
                    _selectedExpressionPreset = preset;
                    expressionLibrary.ApplyPreset(preset);
                }

                GUI.contentColor = previousColor;

                var supportTag = presetAudit == null
                    ? string.Empty
                    : presetAudit.IsSupported
                        ? "OK"
                        : presetAudit.IsPartial
                            ? "Partial"
                            : "Missing";
                GUILayout.Label(supportTag, GUILayout.Width(52f));

                if (GUILayout.Button(overlayActive ? "Remove" : "Overlay", GUILayout.Width(70f)))
                {
                    if (overlayActive)
                    {
                        expressionLibrary.RemoveOverlayPreset(preset);
                    }
                    else
                    {
                        expressionLibrary.AddOverlayPreset(preset);
                    }

                    _selectedExpressionPreset = preset;
                    Repaint();
                }

                if (GUILayout.Button("Test", GUILayout.Width(52f)))
                {
                    StartExpressionLibraryTest(expressionLibrary, new[] { preset });
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawExpressionLibrarySelectedDetails(
            GameObject studioRoot,
            ExpressionLibraryManager expressionLibrary)
        {
            if (_selectedExpressionPreset == null)
            {
                return;
            }

            var preset = _selectedExpressionPreset;
            var presetAudit = _expressionLibraryAudit?.Find(preset);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(preset.displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{ExpressionCategoryUtility.GetDisplayName(preset.category)}  |  " +
                $"{(preset.isBuiltIn ? "Catalog" : "Asset")}  |  {preset.blendshapeWeights.Count} shapes");

            if (!string.IsNullOrWhiteSpace(preset.description))
            {
                EditorGUILayout.HelpBox(preset.description, MessageType.None);
            }

            if (presetAudit != null && presetAudit.missingShapesOnPrimary.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Missing on the primary face renderer: {string.Join(", ", presetAudit.missingShapesOnPrimary)}",
                    MessageType.Warning);
            }

            if (presetAudit?.requiresMouthChannels == true && !expressionLibrary.ExpressionModeActive)
            {
                EditorGUILayout.HelpBox(
                    "This preset uses mouth/jaw shapes. None of those shapes are driven while lip sync owns the mouth; enable Expression Mode to preview the full preset.",
                    MessageType.Info);
                if (GUILayout.Button("Enable Expression Mode And Apply"))
                {
                    EnsureExpressionModeForEditing(studioRoot);
                    expressionLibrary.ApplyPreset(preset);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply As Base"))
            {
                expressionLibrary.ApplyPreset(preset);
            }

            if (GUILayout.Button("Add As Overlay"))
            {
                expressionLibrary.AddOverlayPreset(preset);
            }

            if (GUILayout.Button("Remove Overlay"))
            {
                expressionLibrary.RemoveOverlayPreset(preset);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawExpressionLibraryTestControls(ExpressionLibraryManager expressionLibrary)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Preview & Test", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _expressionTestHoldSeconds = EditorGUILayout.Slider(
                new GUIContent("Hold", "Seconds each preset is held while testing."),
                _expressionTestHoldSeconds,
                0.25f,
                5f,
                GUILayout.Width(240f));

            GUI.enabled = _selectedExpressionPreset != null && !_expressionTestRunning;
            if (GUILayout.Button("Test Selected"))
            {
                StartExpressionLibraryTest(expressionLibrary, new[] { _selectedExpressionPreset });
            }

            GUI.enabled = !_expressionTestRunning;
            if (GUILayout.Button("Test Filtered List"))
            {
                StartExpressionLibraryTest(expressionLibrary, FilterLibraryPresets(expressionLibrary));
            }

            GUI.enabled = _expressionTestRunning;
            if (GUILayout.Button("Stop Test"))
            {
                StopExpressionLibraryTest(true);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_expressionTestRunning)
            {
                EditorGUILayout.HelpBox($"Testing {_expressionTestQueue?.Count ?? 0} remaining preset(s).", MessageType.None);
            }
        }

        private void DrawExpressionLibrarySaveControls(ExpressionLibraryManager expressionLibrary)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Save Current Face", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _expressionLibrarySaveName = EditorGUILayout.TextField("Name", _expressionLibrarySaveName);
            _expressionLibrarySaveCategory = (ExpressionCategory)EditorGUILayout.EnumPopup(
                _expressionLibrarySaveCategory,
                GUILayout.Width(150f));
            EditorGUILayout.EndHorizontal();

            _expressionLibrarySaveDescription = EditorGUILayout.TextField("Description", _expressionLibrarySaveDescription);
            _expressionLibrarySaveTransition = EditorGUILayout.Slider(
                "Transition",
                _expressionLibrarySaveTransition,
                0.05f,
                1f);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrWhiteSpace(_expressionLibrarySaveName);
            if (GUILayout.Button("Save Current Face To Library"))
            {
                var weights = expressionLibrary.CaptureCurrentExpression();
                if (weights.Count == 0)
                {
                    EditorUtility.DisplayDialog(
                        "Nothing To Save",
                        "The face has no active blendshape weights. Pose the face first, then save.",
                        "OK");
                }
                else
                {
                    var preset = expressionLibrary.SavePreset(
                        _expressionLibrarySaveName,
                        _expressionLibrarySaveCategory,
                        _expressionLibrarySaveDescription,
                        _expressionLibrarySaveTransition,
                        weights);

                    if (preset != null)
                    {
                        _selectedExpressionPreset = preset;
                        _expressionLibraryAuditDirty = true;
                        NyxaraExpressionLibraryBuilder.SyncPresetLibraryList(expressionLibrary);
                        EditorGUIUtility.PingObject(preset);
                        Repaint();
                    }
                }
            }

            GUI.enabled = true;
            if (GUILayout.Button("Reset Face"))
            {
                expressionLibrary.ResetToNeutral();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawExpressionLibraryAuditPanel(ExpressionLibraryManager expressionLibrary)
        {
            EditorGUILayout.Space(6f);
            _expressionLibraryShowAudit = EditorGUILayout.Foldout(
                _expressionLibraryShowAudit,
                "Renderers & Unsupported Expressions",
                true);
            if (!_expressionLibraryShowAudit)
            {
                return;
            }

            if (_expressionLibraryAuditDirty || _expressionLibraryAudit == null)
            {
                _expressionLibraryAudit = ExpressionLibraryAudit.Audit(
                    expressionLibrary.LoadedPresets,
                    expressionLibrary.TargetFaceRenderers);
                _expressionLibraryAuditDirty = false;
            }

            var audit = _expressionLibraryAudit;
            if (audit == null)
            {
                return;
            }

            EditorGUILayout.LabelField(
                "Summary",
                $"{audit.PresetCount} presets, {audit.supportedCount} supported, " +
                $"{audit.partialCount} partial, {audit.unsupportedCount} unsupported");

            foreach (var renderer in audit.renderers)
            {
                EditorGUILayout.LabelField(
                    $"{renderer.rendererName}",
                    $"{renderer.blendshapeCount} shapes, {renderer.supportedShapeCount}/{renderer.requiredShapeCount} required" +
                    (renderer.missingShapes.Count > 0
                        ? $" (missing: {string.Join(", ", renderer.missingShapes)})"
                        : string.Empty),
                    EditorStyles.wordWrappedLabel);
            }

            var problemPresets = audit.PartialPresets.Concat(audit.UnsupportedPresets).ToList();
            if (problemPresets.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Every preset is fully supported by the primary face renderer.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("Unsupported / partial presets", EditorStyles.miniBoldLabel);
                foreach (var presetAudit in problemPresets)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        $"{presetAudit.support}: {presetAudit.preset?.displayName}",
                        EditorStyles.wordWrappedLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(60f)))
                    {
                        _selectedExpressionPreset = presetAudit.preset;
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Audit Report"))
            {
                EditorGUIUtility.systemCopyBuffer = audit.ToSummary();
                Debug.Log(audit.ToSummary());
            }

            if (GUILayout.Button("Log Audit Report"))
            {
                NyxaraExpressionLibraryBuilder.Audit(expressionLibrary);
            }

            EditorGUILayout.EndHorizontal();
        }

        private ExpressionLibraryAuditReport EnsureLibraryAudit(
            ExpressionLibraryManager expressionLibrary,
            bool force)
        {
            if (force ||
                _expressionLibraryAuditDirty ||
                _expressionLibraryAudit == null ||
                _expressionLibraryAudit.PresetCount != expressionLibrary.LoadedPresets.Count)
            {
                _expressionLibraryAudit = ExpressionLibraryAudit.Audit(
                    expressionLibrary.LoadedPresets,
                    expressionLibrary.TargetFaceRenderers);
                _expressionLibraryAuditDirty = false;
            }

            return _expressionLibraryAudit;
        }

        private IEnumerable<ExpressionPreset> FilterLibraryPresets(ExpressionLibraryManager expressionLibrary)
        {
            var presets = expressionLibrary.LoadedPresets.AsEnumerable();
            presets = presets.Where(preset => preset != null && preset.category == _expressionLibraryCategory);

            if (_expressionLibraryShowPersistedOnly)
            {
#if UNITY_EDITOR
                presets = presets.Where(preset => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(preset)));
#else
                presets = presets.Where(preset => !preset.isBuiltIn);
#endif
            }

            if (!string.IsNullOrWhiteSpace(_expressionLibrarySearch))
            {
                presets = presets.Where(preset =>
                    (!string.IsNullOrWhiteSpace(preset.displayName) &&
                     preset.displayName.Contains(_expressionLibrarySearch, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(preset.description) &&
                     preset.description.Contains(_expressionLibrarySearch, StringComparison.OrdinalIgnoreCase)));
            }

            return presets;
        }

        private static string BuildPresetTooltip(ExpressionPreset preset, ExpressionPresetAudit audit)
        {
            if (preset == null)
            {
                return string.Empty;
            }

            var tooltip = !string.IsNullOrWhiteSpace(preset.description)
                ? preset.description
                : preset.displayName;

            if (audit == null)
            {
                return tooltip;
            }

            tooltip += $"\n\nSupport: {audit.support}";
            if (audit.missingShapesOnPrimary.Count > 0)
            {
                tooltip += $"\nMissing: {string.Join(", ", audit.missingShapesOnPrimary)}";
            }

            return tooltip;
        }

        private static Color GetSupportColor(ExpressionSupportLevel level)
        {
            return level switch
            {
                ExpressionSupportLevel.Partial => new Color(0.95f, 0.76f, 0.30f),
                ExpressionSupportLevel.Unsupported => new Color(0.95f, 0.38f, 0.38f),
                _ => new Color(0.62f, 0.90f, 0.68f)
            };
        }

        private void StartExpressionLibraryTest(
            ExpressionLibraryManager expressionLibrary,
            IEnumerable<ExpressionPreset> presets)
        {
            var queue = new Queue<ExpressionPreset>((presets ?? Enumerable.Empty<ExpressionPreset>())
                .Where(preset => preset != null));
            if (queue.Count == 0)
            {
                return;
            }

            StopExpressionLibraryTest(false);
            _expressionTestManager = expressionLibrary;
            _expressionTestQueue = queue;
            _expressionTestNextAt = 0d;
            _expressionTestRunning = true;
            EditorApplication.update -= TickExpressionLibraryTest;
            EditorApplication.update += TickExpressionLibraryTest;
            TickExpressionLibraryTest();
        }

        private void TickExpressionLibraryTest()
        {
            if (!_expressionTestRunning)
            {
                EditorApplication.update -= TickExpressionLibraryTest;
                return;
            }

            if (_expressionTestManager == null || _expressionTestQueue == null)
            {
                StopExpressionLibraryTest(true);
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < _expressionTestNextAt)
            {
                Repaint();
                return;
            }

            if (_expressionTestQueue.Count == 0)
            {
                StopExpressionLibraryTest(true);
                return;
            }

            var preset = _expressionTestQueue.Dequeue();
            _selectedExpressionPreset = preset;
            _expressionTestManager.ApplyPreset(preset);
            var hold = Mathf.Max(0.25f, _expressionTestHoldSeconds);
            _expressionTestNextAt = now + hold;
            Repaint();
        }

        private void StopExpressionLibraryTest(bool resetToNeutral)
        {
            _expressionTestRunning = false;
            EditorApplication.update -= TickExpressionLibraryTest;
            _expressionTestQueue?.Clear();
            _expressionTestQueue = null;

            if (resetToNeutral && _expressionTestManager != null)
            {
                _expressionTestManager.ResetToNeutral();
            }

            _expressionTestManager = null;
            Repaint();
        }
    }
}
#endif
