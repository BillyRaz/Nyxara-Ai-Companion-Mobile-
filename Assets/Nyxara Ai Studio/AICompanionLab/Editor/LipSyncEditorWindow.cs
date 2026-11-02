// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System.Collections.Generic;
using Nyxara.AICompanion.LipSync;
using UnityEditor;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    public class LipSyncEditorWindow : EditorWindow
    {
        private const string DefaultGeneratedFolder = "Assets/Nyxara AI Studio/Generated";

        private SkinnedMeshRenderer _targetRenderer;
        private LipSyncData _lipSyncData;
        private Vector2 _scrollPosition;
        private string _testPhrase = "Hello, how are you today?";
        private readonly Dictionary<int, float> _previewWeights = new();
        private Dictionary<int, float> _targetPreviewWeights = new();
        private double _lastPreviewUpdateTime;

        [MenuItem("Nyxara AI/Editors/Lip Sync Editor", false, 21)]
        public static void ShowWindow()
        {
            var window = GetWindow<LipSyncEditorWindow>("Nyxara AI Lip Sync Editor");
            window.minSize = new Vector2(760, 640);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += UpdatePreview;
            _lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Lip Sync Configuration", EditorStyles.boldLabel);
            _targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Face Renderer", _targetRenderer, typeof(SkinnedMeshRenderer), true);
            _lipSyncData = (LipSyncData)EditorGUILayout.ObjectField("Lip Sync Data", _lipSyncData, typeof(LipSyncData), false);

            if (_lipSyncData == null)
            {
                if (GUILayout.Button("Create New Lip Sync Data"))
                {
                    CreateLipSyncData();
                }

                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Viseme to Blendshape Mapping", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(320));
            for (var i = 0; i < _lipSyncData.visemeMappings.Count; i++)
            {
                var mapping = _lipSyncData.visemeMappings[i];
                EditorGUILayout.BeginHorizontal("box");
                mapping.viseme = (Viseme)EditorGUILayout.EnumPopup(mapping.viseme, GUILayout.Width(100));
                mapping.blendshapeName = EditorGUILayout.TextField(mapping.blendshapeName, GUILayout.MinWidth(260), GUILayout.ExpandWidth(true));
                mapping.intensity = EditorGUILayout.Slider(mapping.intensity, 0f, 100f, GUILayout.Width(170));
                mapping.jawOpenContribution = EditorGUILayout.Slider(mapping.jawOpenContribution, 0f, 1f, GUILayout.Width(170));

                if (GUILayout.Button("Test", GUILayout.Width(50)))
                {
                    TestViseme(mapping);
                }

                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    _lipSyncData.visemeMappings.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Add Viseme Mapping"))
            {
                _lipSyncData.visemeMappings.Add(new VisemeMapping { viseme = Viseme.sil, blendshapeName = "", intensity = 0f });
                EditorUtility.SetDirty(_lipSyncData);
            }

            EditorGUILayout.HelpBox("Blendshape(s) supports multiple names separated by commas. Example: mouthSmileLeft, mouthSmileRight", MessageType.None);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Animation Settings", EditorStyles.boldLabel);
            _lipSyncData.smoothTime = EditorGUILayout.Slider("Smooth Time", _lipSyncData.smoothTime, 0.01f, 0.3f);
            _lipSyncData.jawOpenMultiplier = EditorGUILayout.Slider("Jaw Open Multiplier", _lipSyncData.jawOpenMultiplier, 0f, 1f);
            _lipSyncData.responseStart = EditorGUILayout.Slider("Response Start", _lipSyncData.responseStart, 0f, 0.95f);
            _lipSyncData.responseEnd = EditorGUILayout.Slider("Response End", _lipSyncData.responseEnd, Mathf.Max(_lipSyncData.responseStart + 0.01f, 0.05f), 1f);
            _lipSyncData.responseFalloff = EditorGUILayout.Slider("Response Falloff", _lipSyncData.responseFalloff, 0.25f, 3f);
            _lipSyncData.responseSmoothing = EditorGUILayout.Slider("Response Smoothing", _lipSyncData.responseSmoothing, 1f, 25f);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);
            _testPhrase = EditorGUILayout.TextField("Test Phrase", _testPhrase);
            if (GUILayout.Button("Test Lip Sync"))
            {
                TestLipSync();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(_lipSyncData);
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateLipSyncData()
        {
            EnsureFolderPathExists(DefaultGeneratedFolder);
            var path = EditorUtility.SaveFilePanelInProject("Create Lip Sync Data", "LipSyncData.asset", "asset", "Choose location", DefaultGeneratedFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            path = NormalizeOutputPath(path, DefaultGeneratedFolder);

            var data = CreateInstance<LipSyncData>();
            data.visemeMappings = new List<VisemeMapping>
            {
                new() { viseme = Viseme.AA, blendshapeName = "mouthAH", intensity = 80f },
                new() { viseme = Viseme.IY, blendshapeName = "mouthSmile", intensity = 70f },
                new() { viseme = Viseme.UH, blendshapeName = "mouthFunnel", intensity = 75f },
                new() { viseme = Viseme.OW, blendshapeName = "mouthO", intensity = 80f },
                new() { viseme = Viseme.EH, blendshapeName = "mouthDimple", intensity = 65f },
                new() { viseme = Viseme.FV, blendshapeName = "mouthPress", intensity = 50f },
                new() { viseme = Viseme.M, blendshapeName = "mouthClose", intensity = 60f },
                new() { viseme = Viseme.sil, blendshapeName = "mouthRest", intensity = 0f }
            };

            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            _lipSyncData = data;
            EditorGUIUtility.PingObject(data);
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
                selectedPath.StartsWith("Assets/Nyxara AI Studio/", System.StringComparison.Ordinal))
            {
                return selectedPath;
            }

            return AssetDatabase.GenerateUniqueAssetPath($"{fallbackFolder}/{System.IO.Path.GetFileName(selectedPath)}");
        }

        private void TestViseme(VisemeMapping mapping)
        {
            if (_targetRenderer == null || _targetRenderer.sharedMesh == null)
            {
                Debug.LogWarning("No face renderer selected");
                return;
            }

            _targetPreviewWeights = new Dictionary<int, float>();
            foreach (var blendshapeName in mapping.EnumerateBlendshapeNames())
            {
                var index = _targetRenderer.sharedMesh.GetBlendShapeIndex(blendshapeName);
                if (index >= 0)
                {
                    _targetPreviewWeights[index] = mapping.intensity;
                }
            }
        }

        private async void TestLipSync()
        {
            var controller = FindFirstObjectByType<VisemeLipSyncController>();
            if (controller != null)
            {
                await controller.SpeakWithLipSync(_testPhrase);
            }
            else
            {
                Debug.LogWarning("No VisemeLipSyncController found in scene");
            }
        }

        private void UpdatePreview()
        {
            if (_targetRenderer == null || _targetRenderer.sharedMesh == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var deltaTime = Mathf.Max(0.001f, (float)(now - _lastPreviewUpdateTime));
            _lastPreviewUpdateTime = now;
            var smoothing = _lipSyncData != null ? Mathf.Max(0.01f, _lipSyncData.smoothTime) : 0.08f;
            var lerpFactor = 1f - Mathf.Exp(-deltaTime / smoothing);

            var changed = false;
            for (var i = 0; i < _targetRenderer.sharedMesh.blendShapeCount; i++)
            {
                var targetWeight = _targetPreviewWeights.TryGetValue(i, out var foundWeight) ? foundWeight : 0f;
                var currentWeight = _previewWeights.TryGetValue(i, out var cachedWeight) ? cachedWeight : _targetRenderer.GetBlendShapeWeight(i);
                var nextWeight = Mathf.Lerp(currentWeight, targetWeight, lerpFactor);

                if (Mathf.Abs(nextWeight - currentWeight) > 0.01f)
                {
                    _targetRenderer.SetBlendShapeWeight(i, nextWeight);
                    _previewWeights[i] = nextWeight;
                    changed = true;
                }
                else if (!_previewWeights.ContainsKey(i))
                {
                    _previewWeights[i] = nextWeight;
                }
            }

            if (changed)
            {
                Repaint();
            }
        }
    }
}
#endif
