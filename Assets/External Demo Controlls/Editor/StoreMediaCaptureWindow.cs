#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nyxara.ExternalTools.Editor
{
    public class StoreMediaCaptureWindow : EditorWindow
    {
        private enum ResolutionPreset
        {
            HD1080,
            UHD4K,
            Custom
        }

        private const int HdWidth = 1920;
        private const int HdHeight = 1080;
        private const int FourKWidth = 3840;
        private const int FourKHeight = 2160;

        private Camera _targetCamera;
        private ResolutionPreset _resolutionPreset = ResolutionPreset.HD1080;
        private int _customWidth = HdWidth;
        private int _customHeight = HdHeight;
        private int _recordFrameRate = 30;
        private float _recordDurationSeconds = 10f;
        private string _outputFolder = string.Empty;
        private bool _isRecording;
        private int _recordFrameIndex;
        private int _recordMaxFrames;
        private double _recordNextCaptureTime;
        private string _recordSessionFolder = string.Empty;
        private Vector2 _scrollPosition;

        [MenuItem("Tools/Store Media Capture", false, 250)]
        public static void ShowWindow()
        {
            var window = GetWindow<StoreMediaCaptureWindow>("Store Media Capture");
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            _outputFolder = GetDefaultOutputFolder();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _isRecording = false;
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("Store Media Capture", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Standalone editor-only capture tool for store screenshots and promo video frames. It uses a selected in-scene camera and stays out of builds.", MessageType.Info);

            EditorGUILayout.Space(8f);
            _targetCamera = (Camera)EditorGUILayout.ObjectField("In-Game Camera", _targetCamera, typeof(Camera), true);
            _resolutionPreset = (ResolutionPreset)EditorGUILayout.EnumPopup("Resolution", _resolutionPreset);

            if (_resolutionPreset == ResolutionPreset.Custom)
            {
                _customWidth = Mathf.Max(16, EditorGUILayout.IntField("Custom Width", _customWidth));
                _customHeight = Mathf.Max(16, EditorGUILayout.IntField("Custom Height", _customHeight));
            }
            else
            {
                var (presetWidth, presetHeight) = GetPresetResolution(_resolutionPreset);
                EditorGUILayout.LabelField("Output Size", $"{presetWidth} x {presetHeight}");
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Output Folder", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(_outputFolder, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Choose Folder"))
            {
                var selected = EditorUtility.OpenFolderPanel("Select Capture Output Folder", _outputFolder, string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _outputFolder = selected;
                }
            }

            if (GUILayout.Button("Open Folder"))
            {
                EnsureOutputFolderExists();
                EditorUtility.RevealInFinder(_outputFolder);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Snapshots", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = ResolveCamera() != null;
            if (GUILayout.Button("Snapshot HD"))
            {
                CaptureSnapshot(HdWidth, HdHeight, "HD");
            }

            if (GUILayout.Button("Snapshot 4K"))
            {
                CaptureSnapshot(FourKWidth, FourKHeight, "4K");
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Snapshot Current Setup"))
            {
                var (width, height) = GetResolution(_resolutionPreset);
                CaptureSnapshot(width, height, $"{width}x{height}");
            }
            GUI.enabled = true;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Recording", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("Recording saves a PNG frame sequence from the selected camera. This is ideal for clean promo-video capture and can be assembled into a video externally.", MessageType.None);
            _recordFrameRate = Mathf.Clamp(EditorGUILayout.IntField("FPS", _recordFrameRate), 1, 120);
            _recordDurationSeconds = Mathf.Clamp(EditorGUILayout.FloatField("Duration (Seconds)", _recordDurationSeconds), 0.5f, 600f);

            var (recordWidth, recordHeight) = GetResolution(_resolutionPreset);
            EditorGUILayout.LabelField("Record Size", $"{recordWidth} x {recordHeight}");
            EditorGUILayout.LabelField("Record Status", _isRecording ? $"Recording frame {_recordFrameIndex}/{_recordMaxFrames}" : "Idle");
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode is recommended for recording live in-game movement and camera animation.", MessageType.Info);
            }

            GUI.enabled = ResolveCamera() != null;
            if (!_isRecording)
            {
                if (GUILayout.Button("Start Record"))
                {
                    StartRecording(recordWidth, recordHeight);
                }
            }
            else if (GUILayout.Button("Stop Record"))
            {
                StopRecording();
            }
            GUI.enabled = true;

            if (ResolveCamera() == null)
            {
                EditorGUILayout.HelpBox("Assign an in-scene camera, or tag your gameplay camera as MainCamera so the tool can find it automatically.", MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnEditorUpdate()
        {
            if (!_isRecording)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < _recordNextCaptureTime)
            {
                return;
            }

            var camera = ResolveCamera();
            if (camera == null)
            {
                Debug.LogWarning("[Store Media Capture] Recording stopped because no camera is available.");
                StopRecording();
                return;
            }

            var (width, height) = GetResolution(_resolutionPreset);
            var filePath = Path.Combine(_recordSessionFolder, $"frame_{_recordFrameIndex:D05}.png");
            CaptureCameraToFile(camera, width, height, filePath);
            _recordFrameIndex++;

            if (_recordFrameIndex >= _recordMaxFrames)
            {
                StopRecording();
                return;
            }

            _recordNextCaptureTime += 1d / _recordFrameRate;
            Repaint();
        }

        private void CaptureSnapshot(int width, int height, string label)
        {
            var camera = ResolveCamera();
            if (camera == null)
            {
                Debug.LogWarning("[Store Media Capture] Snapshot skipped because no camera is assigned.");
                return;
            }

            EnsureOutputFolderExists();
            var fileName = $"store_shot_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(_outputFolder, fileName);
            CaptureCameraToFile(camera, width, height, filePath);
            Debug.Log($"[Store Media Capture] Saved snapshot: {filePath}");
            EditorUtility.RevealInFinder(filePath);
        }

        private void StartRecording(int width, int height)
        {
            var camera = ResolveCamera();
            if (camera == null)
            {
                Debug.LogWarning("[Store Media Capture] Recording skipped because no camera is assigned.");
                return;
            }

            EnsureOutputFolderExists();
            _recordSessionFolder = Path.Combine(_outputFolder, $"record_{width}x{height}_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(_recordSessionFolder);
            _recordFrameIndex = 0;
            _recordMaxFrames = Mathf.Max(1, Mathf.CeilToInt(_recordDurationSeconds * _recordFrameRate));
            _recordNextCaptureTime = EditorApplication.timeSinceStartup;
            _isRecording = true;
            Debug.Log($"[Store Media Capture] Recording started: {_recordSessionFolder}");
        }

        private void StopRecording()
        {
            if (_isRecording)
            {
                Debug.Log($"[Store Media Capture] Recording finished: {_recordSessionFolder}");
            }

            _isRecording = false;
            _recordFrameIndex = 0;
            _recordMaxFrames = 0;
            _recordNextCaptureTime = 0d;
            Repaint();
        }

        private Camera ResolveCamera()
        {
            if (_targetCamera != null)
            {
                return _targetCamera;
            }

            return Camera.main;
        }

        private void EnsureOutputFolderExists()
        {
            if (string.IsNullOrWhiteSpace(_outputFolder))
            {
                _outputFolder = GetDefaultOutputFolder();
            }

            Directory.CreateDirectory(_outputFolder);
        }

        private static string GetDefaultOutputFolder()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "External", "StoreMediaCaptures");
        }

        private static (int width, int height) GetPresetResolution(ResolutionPreset preset)
        {
            return preset switch
            {
                ResolutionPreset.UHD4K => (FourKWidth, FourKHeight),
                _ => (HdWidth, HdHeight)
            };
        }

        private (int width, int height) GetResolution(ResolutionPreset preset)
        {
            return preset == ResolutionPreset.Custom
                ? (Mathf.Max(16, _customWidth), Mathf.Max(16, _customHeight))
                : GetPresetResolution(preset);
        }

        private static void CaptureCameraToFile(Camera camera, int width, int height, string filePath)
        {
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(filePath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                DestroyImmediate(renderTexture);
                DestroyImmediate(texture);
            }
        }
    }
}
#endif
