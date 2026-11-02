// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nyxara.AICompanion.Configuration;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Data;
using Nyxara.AICompanion.Diagnostics;
using Nyxara.AICompanion.Expressions;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.LipSync;
using Nyxara.AICompanion.Runtime;
using Nyxara.AICompanion.Speech;
using Nyxara.AICompanion.UI;
using Nyxara.AICompanion.Studio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    public partial class NyxaraCompanionStudioWindow : EditorWindow
    {
        private const string DefaultStudioRootFolder = "Assets/Nyxara AI Studio";
        private const string DefaultGeneratedFolder = DefaultStudioRootFolder + "/Generated";
        private const string DefaultConfigPath = DefaultGeneratedFolder + "/AICompanionStudioConfig.asset";
        private const string LegacyRootConfigPath = "Assets/NyxaraAIStudio/Generated/NyxaraAIStudioConfig.asset";
        private const string LegacyDefaultConfigPath = "Assets/AICompanionStudio/Generated/AICompanionStudioConfig.asset";
        private const string StudioTabPrefsKey = "NyxaraStudio.CurrentTab";
        private const string DiagnosticsTabPrefsKey = "NyxaraStudio.DiagnosticsTab";
        private const string LipResponseStartPrefsKey = "NyxaraStudio.LipResponseStart";
        private const string LipResponseEndPrefsKey = "NyxaraStudio.LipResponseEnd";
        private const string LipResponseFalloffPrefsKey = "NyxaraStudio.LipResponseFalloff";
        private const string LipResponseSmoothingPrefsKey = "NyxaraStudio.LipResponseSmoothing";
        private const string LipSliderStartPrefsPrefix = "NyxaraStudio.LipSliderStart.";
        private const string LipSliderEndPrefsPrefix = "NyxaraStudio.LipSliderEnd.";
        private const string AutoBlinkEnabledPrefsKey = "NyxaraStudio.AutoBlinkEnabled";
        private const string AutoBlinkIntervalMinPrefsKey = "NyxaraStudio.AutoBlinkIntervalMin";
        private const string AutoBlinkIntervalMaxPrefsKey = "NyxaraStudio.AutoBlinkIntervalMax";
        private const string AutoBlinkCloseAmountPrefsKey = "NyxaraStudio.AutoBlinkCloseAmount";
        private const string AutoBlinkCloseDurationPrefsKey = "NyxaraStudio.AutoBlinkCloseDuration";
        private const string AutoBlinkOpenDurationPrefsKey = "NyxaraStudio.AutoBlinkOpenDuration";
        private const string AutoBlinkDoubleChancePrefsKey = "NyxaraStudio.AutoBlinkDoubleChance";
        private const string AutoBlinkDoubleGapMinPrefsKey = "NyxaraStudio.AutoBlinkDoubleGapMin";
        private const string AutoBlinkDoubleGapMaxPrefsKey = "NyxaraStudio.AutoBlinkDoubleGapMax";
        private enum StudioTab
        {
            Studio,
            Status,
            Expression,
            Profile,
            LipsAndExpression,
            Memory,
            Diagnostics
        }

        private enum DiagnosticsTab
        {
            SystemScan,
            Runtime,
            Logs
        }

        private struct RendererEntry
        {
            public string Path;
            public string Label;
        }

        private struct LogEntry
        {
            public string Timestamp;
            public string Message;
            public LogType Type;
        }

        private readonly struct LipControlDefinition
        {
            public readonly string Key;
            public readonly string Label;
            public readonly float Max;
            public readonly string[] Blendshapes;

            public LipControlDefinition(string key, string label, float max, params string[] blendshapes)
            {
                Key = key;
                Label = label;
                Max = max;
                Blendshapes = blendshapes;
            }
        }

        private readonly struct StudioStatusItem
        {
            public readonly string Label;
            public readonly bool IsReady;
            public readonly string ReadyText;
            public readonly string MissingText;
            public readonly string MissingGuidance;

            public StudioStatusItem(string label, bool isReady, string readyText, string missingText, string missingGuidance)
            {
                Label = label;
                IsReady = isReady;
                ReadyText = readyText;
                MissingText = missingText;
                MissingGuidance = missingGuidance;
            }
        }

        private Vector2 _scrollPosition;
        private AICompanionStudioConfig _config;
        private StudioTab _currentTab;
        private DiagnosticsTab _diagnosticsTab;
        private RuntimeDiagnosticsMonitor _runtimeMonitor;
        private SystemDiagnosticsReport _lastScanReport;
        private readonly List<LogEntry> _logEntries = new();
        private string _logFilter = string.Empty;
        private bool _showErrors = true;
        private bool _showWarnings = true;
        private bool _showInfo = true;
        private double _lastRuntimeRepaint;
        private SkinnedMeshRenderer _expressionRenderer;
        private LipSyncData _lipSyncData;
        private ExpressionPreset _selectedExpressionPreset;
        private bool _expressionModeEnabled;
        private string _lipSyncTestLine = "Testing lip sync. Jaw, mouth, tongue, and voice should all respond together.";
        private string _llmTestPrompt = "Give me a short in-character greeting and one sentence about how you're feeling.";
        private string _llmTestReply = string.Empty;
        private string _runtimeReplyText = string.Empty;
        private string _fullSystemTestPrompt = "Hello Nyxara. Please greet me briefly, say how you are feeling, and end with a short invitation to continue talking.";
        private string _fullSystemTestStatus = string.Empty;
        private string _microphoneTranscript = string.Empty;
        private bool _memoryStartFreshConversation = true;
        private bool _memoryKeepPersonalityButWipeMemory = true;
        private Vector2 _memoryEventsScroll;
        private Vector2 _memoryRelationshipScroll;
        private AudioClip _testingVoiceClip;
        private string _profileJson = string.Empty;
        private string _runtimeJson = string.Empty;
        private float _lipResponseStart = 0f;
        private float _lipResponseEnd = 1f;
        private float _lipResponseFalloff = 1.35f;
        private float _lipResponseSmoothing = 12f;
        private bool _autoBlinkEnabled;
        private float _autoBlinkIntervalMin = 2.5f;
        private float _autoBlinkIntervalMax = 6.5f;
        private float _autoBlinkCloseAmount = 85f;
        private float _autoBlinkCloseDuration = 0.07f;
        private float _autoBlinkOpenDuration = 0.11f;
        private float _autoBlinkDoubleChance = 0.35f;
        private float _autoBlinkDoubleGapMin = 0.09f;
        private float _autoBlinkDoubleGapMax = 0.18f;
        private float _autoBlinkNextTriggerTime;
        private float _autoBlinkNextGap = 0.12f;
        private float _autoBlinkLastRandomRoll;
        private int _autoBlinkRemainingClosures;
        private double _autoBlinkPhaseStartedAt;
        private double _autoBlinkLastUpdateTime;
        private bool _autoBlinkClosingPhase;
        private float _autoBlinkCurrentWeight;
        private readonly Dictionary<string, float> _lipTargetValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _lipAppliedValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _lipSliderStartValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _lipSliderEndValues = new(StringComparer.OrdinalIgnoreCase);
        private string _builderPresetName = "New ARKit Expression";
        private string _builderDescription = string.Empty;
        private ExpressionCategory _builderCategory = ExpressionCategory.Emotion;
        private float _builderTransitionTime = 0.15f;
        private readonly Dictionary<string, string> _builderBlendshapeMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _builderWeights = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _builderBlendshapeOptions = new();
        private static readonly LipControlDefinition[] LipControls =
        {
            new("jaw_open", "Jaw Open", 27.9f, "jawOpen"),
            new("mouth_close", "Mouth Close", 36.7f, "mouthClose"),
            new("mouth_funnel", "Mouth Funnel", 59.4f, "mouthFunnel"),
            new("mouth_pucker", "Mouth Pucker", 80f, "mouthPucker"),
            new("mouth_smile_pair", "Smile Left/Right", 40f, "mouthSmileLeft", "mouthSmileRight"),
            new("mouth_frown_pair", "Frown Left/Right", 50f, "mouthFrownLeft", "mouthFrownRight"),
            new("mouth_dimple_pair", "Dimple Left/Right", 35f, "mouthDimpleLeft", "mouthDimpleRight"),
            new("mouth_stretch_pair", "Stretch Left/Right", 35f, "mouthStretchLeft", "mouthStretchRight"),
            new("mouth_roll_pair", "Roll Lower/Upper", 30f, "mouthRollLower", "mouthRollUpper"),
            new("mouth_shrug_pair", "Shrug Lower/Upper", 50f, "mouthShrugLower", "mouthShrugUpper")
        };

        [MenuItem("Nyxara AI/Studio", false, 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<NyxaraCompanionStudioWindow>("Nyxara AI Studio");
            window.minSize = new Vector2(620f, 760f);
            window.Show();
        }

        [MenuItem("Nyxara AI/Editors/Memory", false, 31)]
        public static void ShowMemoryEditor()
        {
            var window = GetWindow<NyxaraCompanionStudioWindow>("Nyxara AI Studio");
            window.minSize = new Vector2(620f, 760f);
            window._currentTab = StudioTab.Memory;
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            _config = LoadOrCreateConfig();
            ApplyDefaultPathsIfEmpty(_config);
            NormalizeOptionalDependencyState(_config);
            ResetWindowState(false);
            LoadPersistedLipMixerSettings();
            Application.logMessageReceived += OnLogMessageReceived;
            EditorApplication.update += OnEditorUpdate;
            FindRuntimeMonitor();
        }

        private void OnDisable()
        {
            PersistLipMixerSettings();
            ResetAutoBlinkPreview(ResolveStudioRootFromContext() != null ? ResolveStudioRootFromContext().GetComponent<ArkItBlendshapeDriver>() : null);
            Application.logMessageReceived -= OnLogMessageReceived;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (_currentTab == StudioTab.LipsAndExpression && EditorApplication.isPlaying)
            {
                UpdateTestingLipPreview();
            }

            if (_currentTab != StudioTab.Diagnostics || _diagnosticsTab != DiagnosticsTab.Runtime || !EditorApplication.isPlaying)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _lastRuntimeRepaint > 0.5d)
            {
                _lastRuntimeRepaint = EditorApplication.timeSinceStartup;
                FindRuntimeMonitor();
                Repaint();
            }
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            _logEntries.Insert(0, new LogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Message = condition,
                Type = type
            });

            while (_logEntries.Count > 500)
            {
                _logEntries.RemoveAt(_logEntries.Count - 1);
            }
        }

        private void OnGUI()
        {
            _config = (AICompanionStudioConfig)EditorGUILayout.ObjectField("Studio Config", _config, typeof(AICompanionStudioConfig), false);
            if (_config == null)
            {
                if (GUILayout.Button("Create Studio Config"))
                {
                    _config = LoadOrCreateConfig();
                }

                return;
            }

            DrawMainTabs();
            DrawQuickTools();
            EditorGUILayout.Space(8f);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                switch (_currentTab)
                {
                    case StudioTab.Studio:
                        DrawStudioTab();
                        break;
                    case StudioTab.Status:
                        DrawStatusTab();
                        break;
                    case StudioTab.Expression:
                        DrawExpressionTab();
                        break;
                    case StudioTab.Profile:
                        DrawProfileTab();
                        break;
                    case StudioTab.LipsAndExpression:
                        DrawTestingTab();
                        break;
                    case StudioTab.Memory:
                        DrawMemoryTab();
                        break;
                    case StudioTab.Diagnostics:
                        DrawDiagnosticsTab();
                        break;
                }
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                EditorGUILayout.HelpBox($"Nyxara Studio hit an editor error: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(_config);
            }

            PersistWindowSelection();
        }

        private void DrawMainTabs()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Studio", EditorStyles.miniButtonLeft)) _currentTab = StudioTab.Studio;
            if (GUILayout.Button("Status", EditorStyles.miniButtonMid)) _currentTab = StudioTab.Status;
            if (GUILayout.Button("Expression", EditorStyles.miniButtonMid)) _currentTab = StudioTab.Expression;
            if (GUILayout.Button("Profile", EditorStyles.miniButtonMid)) _currentTab = StudioTab.Profile;
            if (GUILayout.Button("Lips & Expression", EditorStyles.miniButtonMid)) _currentTab = StudioTab.LipsAndExpression;
            if (GUILayout.Button("Diagnostics", EditorStyles.miniButtonRight)) _currentTab = StudioTab.Diagnostics;
            GUILayout.EndHorizontal();
        }

        private void DrawQuickTools()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Studio Tools", GUILayout.Width(82f));
            if (GUILayout.Button("Setup Wizard", EditorStyles.toolbarButton)) NyxaraSetupWizardWindow.ShowWindow();
            if (GUILayout.Button("Status", EditorStyles.toolbarButton)) _currentTab = StudioTab.Status;
            if (GUILayout.Button("Memory", EditorStyles.toolbarButton)) _currentTab = StudioTab.Memory;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawStudioTab()
        {
            DrawOverviewSection();
            EditorGUILayout.Space(8f);
            DrawStatusPanel();
            EditorGUILayout.Space(8f);
            DrawMicrophoneSection();
            EditorGUILayout.Space(8f);
            DrawSourceSection();
            EditorGUILayout.Space(8f);
            DrawPathSection();
            EditorGUILayout.Space(8f);
            DrawStudioRigSection();
            EditorGUILayout.Space(8f);
            DrawOptionsSection();
            EditorGUILayout.Space(8f);
            DrawBuildSection();
        }

        private void DrawStatusTab()
        {
            var studioRoot = ResolveStudioRootFromContext();
            var llmAgent = FindOptionalComponent(studioRoot, "LLMAgent");
            var expressionLibrary = studioRoot != null ? studioRoot.GetComponent<ExpressionLibraryManager>() : null;
            var faceDriver = studioRoot != null ? studioRoot.GetComponent<ArkItBlendshapeDriver>() : null;

            EditorGUILayout.BeginVertical("box");
            try
            {
                EditorGUILayout.LabelField("Status And Runtime Tuning", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Use this tab to inspect the live LLM runtime, set the desired values in Studio Config, and push them into the current root.", MessageType.Info);
                DrawStatusPanel();
                EditorGUILayout.Space(8f);
                DrawFaceProfilePanel(studioRoot, expressionLibrary, faceDriver);
                EditorGUILayout.Space(8f);
                DrawModelPathConfigEditor(studioRoot);
                EditorGUILayout.Space(8f);
                DrawLlmRuntimeConfigEditor(llmAgent, studioRoot);
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawProfileTab()
        {
            var studioRoot = ResolveStudioRootFromContext();
            var brain = studioRoot != null ? studioRoot.GetComponent<NyxaraCompanionBrain>() : null;
            EnsureTestingJsonLoaded(brain);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Profile Studio", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Author structured character identity, behavior, response rules, runtime defaults, and expression trigger routing here. The raw runtime JSON tools are still available as the advanced layer below.", MessageType.Info);
            DrawStructuredProfileStudio(brain, studioRoot);
            EditorGUILayout.EndVertical();
        }

        private void DrawMemoryTab()
        {
            var studioRoot = ResolveStudioRootFromContext();
            var brain = studioRoot != null ? studioRoot.GetComponent<NyxaraCompanionBrain>() : FindFirstObjectByType<NyxaraCompanionBrain>();
            var memoryController = brain != null ? brain.MemoryController : FindFirstObjectByType<RecentMemoryController>();
            var characterId = brain?.CharacterProfile?.identity?.characterName;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Memory", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use this tab to inspect and reset Nyxara's working memory, saved event memory, relationship memory, and current conversation state without touching the character profile.", MessageType.Info);

            if (brain == null || memoryController == null)
            {
                EditorGUILayout.HelpBox("No live NyxaraCompanionBrain or RecentMemoryController was found in the active scene.", MessageType.Warning);
                if (GUILayout.Button("Open Runtime Diagnostics"))
                {
                    _currentTab = StudioTab.Diagnostics;
                    _diagnosticsTab = DiagnosticsTab.Runtime;
                }

                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Current Memory Status", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Working Memory Entries: {memoryController.GetMemoryCount()}");
            EditorGUILayout.LabelField($"Saved Event Memory Entries: {memoryController.GetSavedEventCount()}");
            EditorGUILayout.LabelField($"Saved Relationship Memory Entries: {memoryController.GetSavedRelationshipEventCount()}");
            EditorGUILayout.LabelField("Storage Summary");
            EditorGUILayout.LabelField(memoryController.GetMemoryStorageSummary(), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Live Runtime State", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(brain.LastRuntimeStateSummary, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            var eventsPath = memoryController.GetEventsFilePath(characterId);
            var relationshipPath = memoryController.GetRelationshipFilePath(characterId);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Memory Files", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("Event Memory File");
            EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(eventsPath) ? "<unavailable>" : eventsPath, EditorStyles.textField, GUILayout.Height(34f));
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrWhiteSpace(eventsPath);
            if (GUILayout.Button("Reveal Event File"))
            {
                EditorUtility.RevealInFinder(eventsPath);
            }

            if (GUILayout.Button("Copy Event Path"))
            {
                EditorGUIUtility.systemCopyBuffer = eventsPath;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Relationship Memory File");
            EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(relationshipPath) ? "<unavailable>" : relationshipPath, EditorStyles.textField, GUILayout.Height(34f));
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrWhiteSpace(relationshipPath);
            if (GUILayout.Button("Reveal Relationship File"))
            {
                EditorUtility.RevealInFinder(relationshipPath);
            }

            if (GUILayout.Button("Copy Relationship Path"))
            {
                EditorGUIUtility.systemCopyBuffer = relationshipPath;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Reset Options", EditorStyles.miniBoldLabel);
            _memoryStartFreshConversation = EditorGUILayout.Toggle("Start Fresh Conversation", _memoryStartFreshConversation);
            _memoryKeepPersonalityButWipeMemory = EditorGUILayout.Toggle("Keep Personality But Wipe Memory", _memoryKeepPersonalityButWipeMemory);
            EditorGUILayout.HelpBox(
                _memoryKeepPersonalityButWipeMemory
                    ? "Memory will be wiped, but Nyxara keeps the current live relationship/mood state. The character profile always stays intact."
                    : "Memory will be wiped and Nyxara's live relationship/runtime state will return to profile defaults when a fresh conversation reset is applied.",
                MessageType.None);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Memory Controls", EditorStyles.miniBoldLabel);

            if (GUILayout.Button("Reset All Memory", GUILayout.Height(30f)))
            {
                memoryController.ResetAllMemory(characterId);
                if (_memoryStartFreshConversation)
                {
                    brain.ResetConversationState(_memoryKeepPersonalityButWipeMemory);
                }

                Debug.Log("[Nyxara Memory] Reset all memory requested from Studio Memory tab.");
            }

            if (GUILayout.Button("Reset Relationship Only", GUILayout.Height(28f)))
            {
                memoryController.ResetRelationshipMemory(characterId);
                brain.ResetRelationshipStateToDefaults();
                Debug.Log("[Nyxara Memory] Reset relationship memory requested from Studio Memory tab.");
            }

            if (GUILayout.Button("Reset Saved Event Memory Only", GUILayout.Height(28f)))
            {
                memoryController.ResetSavedEventMemory(characterId);
                Debug.Log("[Nyxara Memory] Reset saved event memory requested from Studio Memory tab.");
            }

            if (GUILayout.Button("Reset Session Memory", GUILayout.Height(28f)))
            {
                memoryController.ResetSessionMemory();
                if (_memoryStartFreshConversation)
                {
                    brain.ResetConversationState(_memoryKeepPersonalityButWipeMemory);
                }

                Debug.Log("[Nyxara Memory] Reset session memory requested from Studio Memory tab.");
            }

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Open Runtime Diagnostics", GUILayout.Height(24f)))
            {
                _currentTab = StudioTab.Diagnostics;
                _diagnosticsTab = DiagnosticsTab.Runtime;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Saved Event Preview", EditorStyles.miniBoldLabel);
            _memoryEventsScroll = EditorGUILayout.BeginScrollView(_memoryEventsScroll, GUILayout.MinHeight(180f));
            EditorGUILayout.SelectableLabel(memoryController.BuildSavedEventPreview(characterId), EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Relationship Memory Preview", EditorStyles.miniBoldLabel);
            _memoryRelationshipScroll = EditorGUILayout.BeginScrollView(_memoryRelationshipScroll, GUILayout.MinHeight(180f));
            EditorGUILayout.SelectableLabel(memoryController.BuildRelationshipPreview(characterId), EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawOverviewSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Studio Overview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Build a portrait-style studio scene with camera focus, face lighting, local AI systems, and prefab output from one place.", MessageType.Info);
            EditorGUILayout.LabelField("Root folder", _config.rootFolder);
            EditorGUILayout.LabelField("Prefab output", _config.prefabFolder);
            EditorGUILayout.LabelField("Companion output", _config.companionPrefabFolder);
            EditorGUILayout.LabelField("Source character", _config.sourceCharacterPrefab != null ? _config.sourceCharacterPrefab.name : "Missing");
            EditorGUILayout.LabelField("Profile asset", _config.characterProfile != null ? AssetDatabase.GetAssetPath(_config.characterProfile) : "Will auto-create if enabled");
            EditorGUILayout.EndVertical();
        }

        private void DrawStatusPanel()
        {
            var studioRoot = ResolveStudioRootFromContext();
            var llmAgent = FindOptionalComponent(studioRoot, "LLMAgent");
            var whisperInput = FindFirstObjectByType<WhisperMicrophoneInput>();
            var ttsService = FindFirstObjectByType<PiperTtsService>();
            var integrationSnapshot = NyxaraIntegrationValidator.CaptureSnapshot(_config);

            var resolvedModelPath = ResolveModelStatusPath(_config.llmModelPath);
            var modelFound = !string.IsNullOrWhiteSpace(resolvedModelPath) && File.Exists(resolvedModelPath);
            var llmConnected = HasLiveLlm(llmAgent);
            var llmDetected = integrationSnapshot.LlmPackageDetected || integrationSnapshot.LlmTypeAvailable;
            var llmReady = llmConnected || (llmDetected && modelFound);
            var llmStatusLabel = llmConnected
                ? "Connected"
                : llmDetected
                    ? modelFound
                        ? "Detected + Model"
                        : integrationSnapshot.LlmDefineEnabled
                            ? "Detected"
                            : "Detected (Validate)"
                    : modelFound
                        ? "Model Only"
                        : "Missing";

            var resolvedWhisperPath = ResolveAbsoluteOrProjectPath(_config.whisperModelRelativePath);
            var whisperModelFound = !string.IsNullOrWhiteSpace(resolvedWhisperPath) && File.Exists(resolvedWhisperPath);
            var sttConnected = whisperInput != null && whisperInput.IsWhisperAvailable;
            var whisperDetected = integrationSnapshot.WhisperPackageDetected || integrationSnapshot.WhisperTypeAvailable;
            var sttReady = sttConnected || (whisperDetected && whisperModelFound);
            var sttStatusLabel = sttConnected
                ? "Connected"
                : whisperDetected
                    ? whisperModelFound
                        ? "Detected + Model"
                        : integrationSnapshot.WhisperDefineEnabled
                            ? "Detected"
                            : "Detected (Validate)"
                    : whisperModelFound
                        ? "Model Only"
                        : "Missing";

            var ttsStatus = ttsService != null
                ? ttsService.AvailabilityStatus
                : PiperTtsService.EvaluateAvailabilityStatus(_config.ttsEnabled, _config.piperExecutablePath, _config.piperVoicePath);
            var ttsReady = ttsStatus != PiperTtsAvailabilityStatus.InvalidPath;
            var ttsStatusLabel = ttsService != null && ttsService.HasInProcessVoice
                ? "Ready (In-Process)"
                : PiperTtsService.GetStatusLabel(ttsStatus);

            var statuses = new List<StudioStatusItem>
            {
                new("LLM", llmReady, llmStatusLabel, llmStatusLabel, modelFound
                    ? llmDetected
                        ? "LLMUnity is present and the model path is valid. Use the Setup Wizard validator if the current scene still needs binding repair."
                        : "LLM model path is valid, but the LLMUnity package is not detected yet."
                    : llmDetected
                        ? "LLMUnity is present. Set or validate a GGUF model path, then bind it to the current scene."
                        : "Set a valid local GGUF model path and install LLMUnity, then build or validate the studio root."),
                new("STT", sttReady, sttStatusLabel, sttStatusLabel, whisperModelFound
                    ? whisperDetected
                        ? "Whisper is present and the model path is valid. Use the Setup Wizard validator if the current scene still needs binding repair."
                        : "Whisper model path is valid, but whisper.unity is not detected yet."
                    : whisperDetected
                        ? "whisper.unity is present. Set or validate a Whisper model path, then bind it to the current scene."
                        : "Set a valid Whisper model path and install whisper.unity, then build or validate the studio root."),
                new("TTS", ttsReady, ttsStatusLabel, ttsStatusLabel, PiperTtsService.GetStatusGuidance(ttsStatus)),
                new("Model", modelFound, "Found", "Missing", "Put the GGUF model in StreamingAssets/Models or point the Studio Config to the correct file.")
            };

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status Panel", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Quick connection check after you build the studio. If something is missing, use the guidance below before moving on.", MessageType.Info);

            foreach (var status in statuses)
            {
                DrawStatusRow(status);
            }

            var missingStatuses = statuses.Where(status => !status.IsReady).ToList();
            if (missingStatuses.Count > 0)
            {
                EditorGUILayout.Space(6f);
                foreach (var missing in missingStatuses)
                {
                    var messageType = string.Equals(missing.Label, "TTS", StringComparison.OrdinalIgnoreCase) ? MessageType.Info : MessageType.Warning;
                    EditorGUILayout.HelpBox($"{missing.Label}: {missing.MissingGuidance}", messageType);
                }
            }

            DrawLiveLlmRuntimeStatus(llmAgent);

            EditorGUILayout.EndVertical();
        }

        private static void DrawLiveLlmRuntimeStatus(Component llmAgent)
        {
            if (!TryGetLlmRuntimeValues(llmAgent, out var contextSize, out var numThreads, out var numPredict, out var cachePrompt))
            {
                return;
            }
            var isFastConfig = contextSize <= 4096 && numPredict <= 96;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Live LLM Runtime", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Context Size", contextSize.ToString());
            EditorGUILayout.LabelField("Num Predict", numPredict.ToString());
            EditorGUILayout.LabelField("Threads", numThreads.ToString());
            EditorGUILayout.LabelField("Prompt Cache", cachePrompt ? "On" : "Off");

            if (!isFastConfig)
            {
                EditorGUILayout.HelpBox(
                    $"The live root is still using a heavier LLM config (context={contextSize}, numPredict={numPredict}). Run Apply Rig To Selected Studio Root or rebuild to push the faster runtime settings.",
                    MessageType.Warning);
            }
        }

        private void DrawModelPathConfigEditor(GameObject studioRoot)
        {
            var llmAgent = FindOptionalComponent(studioRoot, "LLMAgent");
            var whisperInput = FindFirstObjectByType<WhisperMicrophoneInput>();
            var ttsService = FindFirstObjectByType<PiperTtsService>();

            EditorGUILayout.LabelField("Model And Service Paths", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Set the local LLM and Whisper paths here. Voice Output is optional, so you can leave Piper disabled until you want spoken replies.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _config.llmModelPath = EditorGUILayout.TextField("LLM Model", _config.llmModelPath);
            _config.whisperModelRelativePath = EditorGUILayout.TextField("Whisper Model", _config.whisperModelRelativePath);
            _config.ttsEnabled = EditorGUILayout.Toggle("Enable Voice Output", _config.ttsEnabled);
            _config.piperExecutablePath = EditorGUILayout.TextField("Piper Executable", _config.piperExecutablePath);
            _config.piperVoicePath = EditorGUILayout.TextField("Piper Voice Model", _config.piperVoicePath);
            if (EditorGUI.EndChangeCheck())
            {
                SaveConfigAndSyncLiveServices();
            }

            DrawPathStatus("LLM Model", ResolveModelStatusPath(_config.llmModelPath), false);
            DrawPathStatus("Whisper Model", (_config.whisperModelRelativePath ?? string.Empty).Replace('\\', '/'), true);
            var ttsStatus = PiperTtsService.EvaluateAvailabilityStatus(_config.ttsEnabled, _config.piperExecutablePath, _config.piperVoicePath);
            EditorGUILayout.LabelField("Voice Output Status", PiperTtsService.GetStatusLabel(ttsStatus));
            DrawPathStatus("Piper Executable", (_config.piperExecutablePath ?? string.Empty).Replace('\\', '/'), true);
            DrawPathStatus("Piper Voice", (_config.piperVoicePath ?? string.Empty).Replace('\\', '/'), true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse LLM"))
            {
                var selected = EditorUtility.OpenFilePanel("Select GGUF Model", GetSafeBrowseDirectory(_config.llmModelPath, "Models"), "gguf");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _config.llmModelPath = MakeStreamingAssetsRelative(selected);
                    SaveConfigAndSyncLiveServices();
                }
            }

            if (GUILayout.Button("Browse Whisper"))
            {
                var selected = EditorUtility.OpenFilePanel("Select Whisper Model", GetSafeBrowseDirectory(_config.whisperModelRelativePath, "Speech"), "bin");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _config.whisperModelRelativePath = MakeStreamingAssetsRelative(selected);
                    SaveConfigAndSyncLiveServices();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse Piper Exe"))
            {
                var selected = EditorUtility.OpenFilePanel("Select Piper Executable", GetSafeBrowseDirectory(_config.piperExecutablePath, "Speech"), string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _config.piperExecutablePath = MakeStreamingAssetsRelative(selected);
                    _config.ttsEnabled = true;
                    SaveConfigAndSyncLiveServices();
                }
            }

            if (GUILayout.Button("Browse Piper Voice"))
            {
                var selected = EditorUtility.OpenFilePanel("Select Piper Voice", GetSafeBrowseDirectory(_config.piperVoicePath, Path.Combine("Speech", "PiperVoices")), "onnx");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _config.piperVoicePath = MakeStreamingAssetsRelative(selected);
                    _config.ttsEnabled = true;
                    SaveConfigAndSyncLiveServices();
                }
            }

            GUI.enabled = llmAgent != null || whisperInput != null || ttsService != null;
            if (GUILayout.Button("Use Live Paths"))
            {
                if (TryGetLlmModelPath(llmAgent, out var liveLlmPath))
                {
                    _config.llmModelPath = liveLlmPath;
                }

                if (whisperInput != null && whisperInput.IsWhisperAvailable && whisperInput.HasAssignedWhisperManager && !string.IsNullOrWhiteSpace(whisperInput.ConfiguredModelPath))
                {
                    _config.whisperModelRelativePath = whisperInput.ConfiguredModelPath;
                }

                if (ttsService != null)
                {
                    _config.ttsEnabled = ttsService.TtsEnabled;
                    if (TryMakeStreamingAssetsRelative(ttsService.PiperExecutablePath, out var runtimePiperExecutablePath))
                    {
                        _config.piperExecutablePath = runtimePiperExecutablePath;
                    }

                    if (TryMakeStreamingAssetsRelative(ttsService.VoiceModelPath, out var runtimePiperVoicePath))
                    {
                        _config.piperVoicePath = runtimePiperVoicePath;
                    }
                }

                SaveConfigAndSyncLiveServices();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLlmRuntimeConfigEditor(Component llmAgent, GameObject studioRoot)
        {
            EditorGUILayout.LabelField("Desired LLM Runtime", EditorStyles.boldLabel);
            _config.llmContextSize = EditorGUILayout.IntField("Context Size", _config.llmContextSize);
            _config.llmNumPredict = EditorGUILayout.IntField("Num Predict", _config.llmNumPredict);
            _config.llmNumThreads = EditorGUILayout.IntField("Threads", _config.llmNumThreads);
            _config.llmCachePrompt = EditorGUILayout.Toggle("Prompt Cache", _config.llmCachePrompt);
            _config.llmTemperature = EditorGUILayout.Slider("Temperature", _config.llmTemperature, 0f, 1f);
            _config.llmTopP = EditorGUILayout.Slider("Top P", _config.llmTopP, 0f, 1f);
            _config.llmTopK = EditorGUILayout.IntField("Top K", _config.llmTopK);
            _config.llmMinP = EditorGUILayout.Slider("Min P", _config.llmMinP, 0f, 1f);
            _config.llmRepeatPenalty = EditorGUILayout.Slider("Repeat Penalty", _config.llmRepeatPenalty, 1f, 1.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Recommended Fast"))
            {
                _config.llmContextSize = 4096;
                _config.llmNumPredict = 64;
                _config.llmNumThreads = -1;
                _config.llmCachePrompt = true;
                _config.llmTemperature = 0.2f;
                _config.llmTopP = 0.85f;
                _config.llmTopK = 30;
                _config.llmMinP = 0.08f;
                _config.llmRepeatPenalty = 1.05f;
                EditorUtility.SetDirty(_config);
            }

            GUI.enabled = TryGetLlmRuntimeValues(llmAgent, out var liveContextSize, out var liveNumThreads, out var liveNumPredict, out var liveCachePrompt);
            if (GUILayout.Button("Use Live Values"))
            {
                _config.llmContextSize = liveContextSize;
                _config.llmNumPredict = liveNumPredict;
                _config.llmNumThreads = liveNumThreads;
                _config.llmCachePrompt = liveCachePrompt;
                _config.llmTemperature = GetFloatField(llmAgent, "temperature", _config.llmTemperature);
                _config.llmTopP = GetFloatField(llmAgent, "topP", _config.llmTopP);
                _config.llmTopK = GetIntField(llmAgent, "topK", _config.llmTopK);
                _config.llmMinP = GetFloatField(llmAgent, "minP", _config.llmMinP);
                _config.llmRepeatPenalty = GetFloatField(llmAgent, "repeatPenalty", _config.llmRepeatPenalty);
                EditorUtility.SetDirty(_config);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = studioRoot != null;
            if (GUILayout.Button("Apply Config To Live Root"))
            {
                NyxaraCompanionStudioBuilder.ApplyStudioRigToExistingRoot(_config, studioRoot);
                PerformSystemScan();
            }

            if (GUILayout.Button("Rebuild With Config"))
            {
                NyxaraCompanionStudioBuilder.BuildStudioRoot(_config);
                PerformSystemScan();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMicrophoneSection()
        {
            var whisperInput = FindFirstObjectByType<WhisperMicrophoneInput>();
            var brain = FindFirstObjectByType<NyxaraCompanionBrain>();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Voice Chat", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use this in Play Mode to talk to Nyxara through the scene microphone path. Start recording, speak, then stop to transcribe and optionally auto-reply through the brain.", MessageType.Info);
            EditorGUILayout.LabelField("Microphone Input", whisperInput != null ? whisperInput.name : "Missing in scene");
            EditorGUILayout.LabelField("Brain", brain != null ? brain.name : "Missing in scene");
              EditorGUILayout.LabelField("Recording", whisperInput != null && whisperInput.IsRecording ? "Active" : "Idle");
              if (whisperInput != null)
              {
                  EditorGUILayout.LabelField("Resolved Device", whisperInput.ActiveMicrophoneDevice ?? "<default>");
                  EditorGUILayout.LabelField("Live Capture Device", whisperInput.ActiveRecordingDevice);
                  EditorGUILayout.LabelField("Mic Route", whisperInput.LastResolvedMicrophoneRoute);
                  EditorGUILayout.LabelField("Mic Diagnostic", whisperInput.LastMicrophoneDiagnostic);
                  EditorGUILayout.LabelField("Audio Stats", $"{whisperInput.LastRecordingDurationSeconds:0.00}s | RMS {whisperInput.LastRecordingRms:0.0000} | Peak {whisperInput.LastRecordingPeak:0.0000} | Gain {whisperInput.LastAppliedPreampGain:0.00}x");
                  EditorGUILayout.LabelField("Speech Gate", $"RMS >= {whisperInput.MinRmsForSpeech:0.0000}, Peak >= {whisperInput.MinPeakForSpeech:0.0000}, PreGain {whisperInput.PreTranscriptionGain:0.00}x");
                  EditorGUILayout.LabelField("Transcript Filters", $"Music {(whisperInput.RejectMusicLikeTranscripts ? "ON" : "OFF")} | Bracket Tags {(whisperInput.RejectBracketedNonSpeechTags ? "ON" : "OFF")}");
                  EditorGUILayout.LabelField("Capture Mode", whisperInput.CaptureModeLabel);
                  EditorGUILayout.LabelField("Failure Mode", whisperInput.LastCaptureFailureMode);
                  EditorGUILayout.LabelField("Forwarding", whisperInput.LastForwardingDecision);
              }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = EditorApplication.isPlaying && whisperInput != null && !whisperInput.IsRecording;
            if (GUILayout.Button("Start Mic"))
            {
                Debug.Log("[Nyxara Test][Mic] Starting microphone recording.");
                whisperInput.StartRecording();
            }

            GUI.enabled = EditorApplication.isPlaying && whisperInput != null && whisperInput.IsRecording;
            if (GUILayout.Button("Stop Mic And Send"))
            {
                StopMicrophoneAndSend(whisperInput);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_microphoneTranscript))
            {
                EditorGUILayout.LabelField("Last Transcript", EditorStyles.miniBoldLabel);
                EditorGUILayout.TextArea(_microphoneTranscript, GUILayout.MinHeight(48f));
            }

            if (whisperInput != null && !string.IsNullOrWhiteSpace(whisperInput.LastRawTranscript))
            {
                EditorGUILayout.LabelField("Last Raw STT", EditorStyles.miniBoldLabel);
                EditorGUILayout.TextArea(whisperInput.LastRawTranscript, GUILayout.MinHeight(48f));
            }

            if (whisperInput != null && !string.IsNullOrWhiteSpace(whisperInput.LastNormalizedTranscript))
            {
                EditorGUILayout.LabelField("Last Normalized STT", EditorStyles.miniBoldLabel);
                EditorGUILayout.TextArea(whisperInput.LastNormalizedTranscript, GUILayout.MinHeight(48f));
            }

            if (whisperInput != null && !string.IsNullOrWhiteSpace(whisperInput.LastRejectedTranscriptReason))
            {
                EditorGUILayout.HelpBox($"Rejected Transcript Reason: {whisperInput.LastRejectedTranscriptReason}", MessageType.Info);
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use the microphone conversation controls.", MessageType.None);
            }
            else if (whisperInput == null)
            {
                EditorGUILayout.HelpBox("No WhisperMicrophoneInput found in the scene. Build or apply the studio rig first.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawStatusRow(StudioStatusItem status)
        {
            var icon = status.IsReady ? "OK" : "X";
            var value = status.IsReady ? status.ReadyText : status.MissingText;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(status.Label, GUILayout.Width(70f));
            EditorGUILayout.LabelField(icon, GUILayout.Width(28f));
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Character Source", EditorStyles.boldLabel);
            _config.characterName = EditorGUILayout.TextField("Character Name", _config.characterName);
            _config.studioRootName = EditorGUILayout.TextField("Studio Root Name", _config.studioRootName);
            _config.sourceCharacterPrefab = (GameObject)EditorGUILayout.ObjectField("Source Character", _config.sourceCharacterPrefab, typeof(GameObject), false);
            _config.sourceIsExistingRootPrefab = EditorGUILayout.Toggle("Source Is Existing Root", _config.sourceIsExistingRootPrefab);
            DrawFaceRendererSelector();
            _config.playerTransform = (Transform)EditorGUILayout.ObjectField("Player Transform", _config.playerTransform, typeof(Transform), true);
            _config.characterProfile = (Nyxara.AICompanion.Data.CharacterProfileData)EditorGUILayout.ObjectField("Character Profile", _config.characterProfile, typeof(Nyxara.AICompanion.Data.CharacterProfileData), false);
            EditorGUILayout.EndVertical();
        }

        private void DrawFaceRendererSelector()
        {
            if (_config.sourceCharacterPrefab == null)
            {
                EditorGUILayout.HelpBox("Assign a source character prefab or FBX first, then choose the face renderer from its hierarchy.", MessageType.Info);
                return;
            }

            var renderers = GetRendererEntries(_config.sourceCharacterPrefab);
            if (renderers.Count == 0)
            {
                EditorGUILayout.HelpBox("No SkinnedMeshRenderer was found under the selected source character.", MessageType.Warning);
                return;
            }

            var selectedIndex = Mathf.Max(0, renderers.FindIndex(entry => entry.Path == _config.preferredFaceRendererPath));
            var labels = renderers.Select(entry => entry.Label).ToArray();
            var newIndex = EditorGUILayout.Popup("Preferred Face Renderer", selectedIndex, labels);
            _config.preferredFaceRendererPath = renderers[newIndex].Path;
            EditorGUILayout.LabelField("Renderer Path", _config.preferredFaceRendererPath, EditorStyles.miniLabel);
        }

        private void DrawPathSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("AI Stack Paths", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _config.llmModelPath = EditorGUILayout.TextField("LLM Model Path", _config.llmModelPath);
            _config.whisperModelRelativePath = EditorGUILayout.TextField("Whisper Model", _config.whisperModelRelativePath);
            if (EditorGUI.EndChangeCheck())
            {
                SaveConfigAndSyncLiveServices();
            }

            EditorGUILayout.Space(6f);
            DrawPathStatus("LLM", ResolveModelStatusPath(_config.llmModelPath), false);
            DrawPathStatus("Whisper", _config.whisperModelRelativePath, true);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Voice Output (Optional)", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            _config.ttsEnabled = EditorGUILayout.Toggle("Enable Voice Output", _config.ttsEnabled);
            _config.piperExecutablePath = EditorGUILayout.TextField("Piper Executable", _config.piperExecutablePath);
            _config.piperVoicePath = EditorGUILayout.TextField("Piper Voice Model", _config.piperVoicePath);
            if (EditorGUI.EndChangeCheck())
            {
                SaveConfigAndSyncLiveServices();
            }

            var ttsStatus = PiperTtsService.EvaluateAvailabilityStatus(_config.ttsEnabled, _config.piperExecutablePath, _config.piperVoicePath);
            var inProcessTts = NyxaraIntegrationValidator.HasInProcessPiperAssets();
            EditorGUILayout.LabelField("Status", inProcessTts ? "Ready (In-Process)" : PiperTtsService.GetStatusLabel(ttsStatus));
            DrawPathStatus("Piper EXE (legacy, optional)", _config.piperExecutablePath, true);
            DrawPathStatus("Piper Voice", _config.piperVoicePath, true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse Piper Executable"))
            {
                var selected = EditorUtility.OpenFilePanel("Select Piper Executable", Path.Combine(Application.streamingAssetsPath, "Speech"), string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _config.piperExecutablePath = MakeStreamingAssetsRelative(selected);
                    _config.ttsEnabled = true;
                    SaveConfigAndSyncLiveServices();
                }
            }

            if (GUILayout.Button("Browse Voice Model"))
            {
                var selected = EditorUtility.OpenFilePanel("Select Piper Voice Model", Path.Combine(Application.streamingAssetsPath, "Speech", "PiperVoices"), "onnx");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    _config.piperVoicePath = MakeStreamingAssetsRelative(selected);
                    _config.ttsEnabled = true;
                    SaveConfigAndSyncLiveServices();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Voice output runs in-process with the bundled voice at Assets/Speech/Piper/clean100.onnx, so no piper.exe is required. The legacy executable field is only used by the optional editor-only fallback.", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void DrawStudioRigSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Studio Rig", EditorStyles.boldLabel);
            _config.createStudioEnvironment = EditorGUILayout.Toggle("Create Studio Environment", _config.createStudioEnvironment);
            _config.createStudioCamera = EditorGUILayout.Toggle("Create Studio Camera", _config.createStudioCamera);
            _config.createStudioLights = EditorGUILayout.Toggle("Create Studio Lights", _config.createStudioLights);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Character Transform", EditorStyles.miniBoldLabel);
            _config.characterLocalPosition = EditorGUILayout.Vector3Field("Character Position", _config.characterLocalPosition);
            _config.characterLocalEuler = EditorGUILayout.Vector3Field("Character Rotation", _config.characterLocalEuler);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Camera Framing", EditorStyles.miniBoldLabel);
            _config.focusHeightOffset = EditorGUILayout.FloatField("Focus Height", _config.focusHeightOffset);
            _config.cameraPivotOffset = EditorGUILayout.Vector3Field("Camera Pivot Offset", _config.cameraPivotOffset);
            _config.cameraDistance = EditorGUILayout.Slider("Camera Distance", _config.cameraDistance, 0.6f, 3f);
            _config.cameraHeight = EditorGUILayout.Slider("Camera Height", _config.cameraHeight, 0.8f, 2.4f);
            _config.cameraYaw = EditorGUILayout.Slider("Camera Yaw", _config.cameraYaw, -35f, 35f);
            _config.cameraFieldOfView = EditorGUILayout.Slider("Camera FOV", _config.cameraFieldOfView, 15f, 60f);
            EditorGUILayout.HelpBox("Use Camera Pivot Offset to nudge the look target without changing the character transform. A small negative Y value usually removes extra empty head space.", MessageType.None);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Lighting", EditorStyles.miniBoldLabel);
            _config.keyLightIntensity = EditorGUILayout.Slider("Key Light", _config.keyLightIntensity, 0f, 4f);
            _config.fillLightIntensity = EditorGUILayout.Slider("Fill Light", _config.fillLightIntensity, 0f, 4f);
            _config.rimLightIntensity = EditorGUILayout.Slider("Rim Light", _config.rimLightIntensity, 0f, 4f);
            _config.studioBackgroundColor = EditorGUILayout.ColorField("Background", _config.studioBackgroundColor);
            EditorGUILayout.EndVertical();
        }

        private void DrawOptionsSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);
            _config.createSceneInstance = EditorGUILayout.Toggle("Create Scene Instance", _config.createSceneInstance);
            _config.saveRootPrefab = EditorGUILayout.Toggle("Save Root Prefab", _config.saveRootPrefab);
            _config.createProfileIfMissing = EditorGUILayout.Toggle("Create Profile If Missing", _config.createProfileIfMissing);
            _config.autoAttachBootstrap = EditorGUILayout.Toggle("Auto Attach Bootstrap", _config.autoAttachBootstrap);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Folders", EditorStyles.miniBoldLabel);
            _config.rootFolder = EditorGUILayout.TextField("Root Folder", _config.rootFolder);
            _config.prefabFolder = EditorGUILayout.TextField("Prefab Folder", _config.prefabFolder);
            _config.companionPrefabFolder = EditorGUILayout.TextField("Companion Folder", _config.companionPrefabFolder);
            _config.profileFolder = EditorGUILayout.TextField("Profile Folder", _config.profileFolder);
            _config.generatedFolder = EditorGUILayout.TextField("Generated Folder", _config.generatedFolder);
            _config.expressionFolder = EditorGUILayout.TextField("Expression Folder", _config.expressionFolder);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Runtime Input", EditorStyles.miniBoldLabel);
            _config.enableRuntimeConversationOverlay = EditorGUILayout.Toggle("Enable Runtime Voice Overlay", _config.enableRuntimeConversationOverlay);
            _config.showRuntimeConversationOverlay = EditorGUILayout.Toggle("Show Runtime Overlay", _config.showRuntimeConversationOverlay);
            _config.enableSmartVoiceCapture = EditorGUILayout.Toggle("Enable Smart Voice Capture", _config.enableSmartVoiceCapture);
            _config.runtimeMicHoldKey = (KeyCode)EditorGUILayout.EnumPopup("Mic Hold Key", _config.runtimeMicHoldKey);
            _config.runtimePromptPopupKey = (KeyCode)EditorGUILayout.EnumPopup("Prompt Popup Key", _config.runtimePromptPopupKey);
            EditorGUILayout.EndVertical();
        }

        private void DrawBuildSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            if (GUILayout.Button("Ensure Structure", GUILayout.Height(30f)))
            {
                NyxaraCompanionStudioBuilder.EnsureFolderStructure(_config);
                if (_config.createProfileIfMissing)
                {
                    NyxaraCompanionStudioBuilder.EnsureCharacterProfile(_config);
                }
            }

            GUI.enabled = _config.sourceCharacterPrefab != null;
            if (GUILayout.Button("Build Studio", GUILayout.Height(38f)))
            {
                NyxaraCompanionStudioBuilder.BuildStudioRoot(_config);
            }

            GUI.enabled = true;
            var selectedRoot = ResolveStudioRootFromContext();
            var canApplyToScene = selectedRoot != null;
            GUI.enabled = canApplyToScene;
            if (GUILayout.Button("Apply Rig To Selected Studio Root"))
            {
                NyxaraCompanionStudioBuilder.ApplyStudioRigToExistingRoot(_config, selectedRoot);
            }

            if (GUILayout.Button("Face Character To Studio Camera"))
            {
                NyxaraCompanionStudioBuilder.FaceCharacterTowardStudioCamera(selectedRoot);
            }

            if (GUILayout.Button("Finalize Companion Root Prefab", GUILayout.Height(32f)))
            {
                NyxaraCompanionStudioBuilder.FinalizeCompanionRoot(_config, selectedRoot);
            }

            GUI.enabled = true;
            if (GUILayout.Button("Reset Studio", GUILayout.Height(30f)))
            {
                ResetStudioWorkspace();
            }

            if (GUILayout.Button("Run System Scan"))
            {
                PerformSystemScan();
                _currentTab = StudioTab.Diagnostics;
                _diagnosticsTab = DiagnosticsTab.SystemScan;
            }

            EditorGUILayout.HelpBox("Apply Rig now preserves the character's current facing so manual face alignment is not overwritten. Use Face Character To Studio Camera only when you explicitly want the model turned toward the studio camera.", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void SaveConfigAndSyncLiveServices()
        {
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            SyncConfigToLiveComponents();
        }

        private void SyncConfigToLiveComponents()
        {
            if (_config == null)
            {
                return;
            }

            var studioRoot = ResolveStudioRootFromContext();
            var llm = FindOptionalComponent(studioRoot, "LLM");
            if (llm != null)
            {
                AssignStringFieldToComponent(llm, "model", _config.llmModelPath);
                EditorUtility.SetDirty(llm);
            }

            var ttsService = studioRoot != null
                ? studioRoot.GetComponentInChildren<PiperTtsService>(true)
                : FindFirstObjectByType<PiperTtsService>();
            if (ttsService != null)
            {
                NyxaraIntegrationValidator.BindInProcessPiperAssets(ttsService);
                ttsService.TtsEnabled = _config.ttsEnabled;
                ttsService.PiperExecutablePath = _config.piperExecutablePath;
                ttsService.VoiceModelPath = _config.piperVoicePath;
                EditorUtility.SetDirty(ttsService);
            }

            var phonemeExtractor = studioRoot != null
                ? studioRoot.GetComponent<PiperTTSPhonemeExtractor>()
                : FindFirstObjectByType<PiperTTSPhonemeExtractor>();
            if (phonemeExtractor != null)
            {
                AssignStringFieldToComponent(phonemeExtractor, "piperExecutablePath", _config.piperExecutablePath);
                AssignStringFieldToComponent(phonemeExtractor, "voiceModelPath", _config.piperVoicePath);
                EditorUtility.SetDirty(phonemeExtractor);
            }

            if (llm != null || ttsService != null || phonemeExtractor != null)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }

        private static void AssignStringFieldToComponent(Component component, string fieldName, string value)
        {
            if (component == null)
            {
                return;
            }

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null || property.propertyType != SerializedPropertyType.String)
            {
                return;
            }

            property.stringValue = value ?? string.Empty;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Component FindOptionalComponent(GameObject root, string typeName)
        {
            if (root != null)
            {
                var local = root.GetComponentsInChildren<Component>(true).FirstOrDefault(component => component != null && component.GetType().Name == typeName);
                if (local != null)
                {
                    return local;
                }
            }

            return FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().Name == typeName);
        }

        private static bool HasLiveLlm(Component llmAgent)
        {
            return TryGetObjectReference(llmAgent, "llm") != null || TryGetObjectReference(llmAgent, "_llm") != null;
        }

        private static bool TryGetLlmModelPath(Component llmAgent, out string modelPath)
        {
            modelPath = string.Empty;
            var llm = TryGetObjectReference(llmAgent, "llm") ?? TryGetObjectReference(llmAgent, "_llm");
            if (llm == null)
            {
                return false;
            }

            var serializedObject = new SerializedObject(llm);
            var property = serializedObject.FindProperty("model");
            if (property == null || string.IsNullOrWhiteSpace(property.stringValue))
            {
                return false;
            }

            modelPath = property.stringValue;
            return true;
        }

        private static bool TryGetLlmRuntimeValues(Component llmAgent, out int contextSize, out int numThreads, out int numPredict, out bool cachePrompt)
        {
            contextSize = 0;
            numThreads = 0;
            numPredict = 0;
            cachePrompt = false;

            var llm = TryGetObjectReference(llmAgent, "llm") ?? TryGetObjectReference(llmAgent, "_llm");
            if (llm == null)
            {
                return false;
            }

            var llmSerialized = new SerializedObject(llm);
            var contextProperty = llmSerialized.FindProperty("contextSize") ?? llmSerialized.FindProperty("_contextSize");
            var threadProperty = llmSerialized.FindProperty("numThreads") ?? llmSerialized.FindProperty("_numThreads");
            var agentSerialized = new SerializedObject(llmAgent);
            var predictProperty = agentSerialized.FindProperty("numPredict");
            var cacheProperty = agentSerialized.FindProperty("cachePrompt");
            if (contextProperty == null || threadProperty == null || predictProperty == null || cacheProperty == null)
            {
                return false;
            }

            contextSize = contextProperty.intValue;
            numThreads = threadProperty.intValue;
            numPredict = predictProperty.intValue;
            cachePrompt = cacheProperty.boolValue;
            return true;
        }

        private static UnityEngine.Object TryGetObjectReference(Component component, string fieldName)
        {
            if (component == null)
            {
                return null;
            }

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            return property != null ? property.objectReferenceValue : null;
        }

        private static float GetFloatField(Component component, string fieldName, float fallbackValue)
        {
            if (component == null)
            {
                return fallbackValue;
            }

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            return property != null ? property.floatValue : fallbackValue;
        }

        private static int GetIntField(Component component, string fieldName, int fallbackValue)
        {
            if (component == null)
            {
                return fallbackValue;
            }

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            return property != null ? property.intValue : fallbackValue;
        }

        private void DrawExpressionTab()
        {
            var studioRoot = ResolveStudioRootFromContext();
            SyncTabContextFromStudioRoot(studioRoot);
            var expressionLibrary = studioRoot != null ? studioRoot.GetComponent<ExpressionLibraryManager>() : null;
            var faceDriver = studioRoot != null ? studioRoot.GetComponent<ArkItBlendshapeDriver>() : null;
            SyncExpressionLibraryToDetectedProfile(expressionLibrary, faceDriver, false);
            EnsureBuilderState(faceDriver, expressionLibrary);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Expression Tools", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Studio Root", studioRoot != null ? studioRoot.name : "Missing");
            var newExpressionRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Face Renderer", _expressionRenderer, typeof(SkinnedMeshRenderer), true);
            if (newExpressionRenderer != _expressionRenderer)
            {
                _expressionRenderer = newExpressionRenderer;
                _builderBlendshapeMap.Clear();
                _builderWeights.Clear();
                RefreshBuilderBlendshapeOptions();
                AutoDetectBuilderBlendshapes();
                PullWeightsFromFaceDriver(faceDriver);
            }
            EditorGUILayout.LabelField("Library Manager", expressionLibrary != null ? expressionLibrary.name : "Missing in scene");
            EditorGUILayout.LabelField("Library Path", expressionLibrary != null ? expressionLibrary.ExpressionLibraryPath : "Missing in scene");
            EditorGUILayout.LabelField("Loaded Presets", expressionLibrary != null ? expressionLibrary.LoadedPresets.Count.ToString() : "0");
            _selectedExpressionPreset = (ExpressionPreset)EditorGUILayout.ObjectField("Selected Preset", _selectedExpressionPreset, typeof(ExpressionPreset), false);
            var newExpressionMode = EditorGUILayout.ToggleLeft("Expression Mode (full face control, including mouth)", _expressionModeEnabled);
            if (newExpressionMode != _expressionModeEnabled)
            {
                _expressionModeEnabled = newExpressionMode;
                ApplyExpressionModeToScene(studioRoot);
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = expressionLibrary != null;
            if (GUILayout.Button("Refresh Library"))
            {
                expressionLibrary.LoadAllPresets();
                if (_selectedExpressionPreset == null && expressionLibrary.LoadedPresets.Count > 0)
                {
                    _selectedExpressionPreset = expressionLibrary.LoadedPresets[0];
                }
            }

            GUI.enabled = expressionLibrary != null && _selectedExpressionPreset != null;
            if (GUILayout.Button("Apply Selected Preset"))
            {
                EnsureExpressionModeForEditing(studioRoot);
                expressionLibrary.ApplyPreset(_selectedExpressionPreset);
            }

            GUI.enabled = expressionLibrary != null;
            if (GUILayout.Button("Reset Face"))
            {
                EnsureExpressionModeForEditing(studioRoot);
                expressionLibrary.ResetToNeutral();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(_expressionModeEnabled
                ? "Expression Mode is ON. The expression tools own the full face, including mouth blendshapes."
                : "Expression Mode is OFF. Mouth-related blendshapes are reserved for lip sync, while expressions drive the rest of the face.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Detailed Expression Editor"))
            {
                ExpressionEditorWindow.ShowWindow();
            }

            if (GUILayout.Button("Log Face Blendshape Report"))
            {
                LogFaceBlendshapeReport(studioRoot, expressionLibrary);
            }

            if (GUILayout.Button("Log Live Face Driver Targets"))
            {
                LogLiveFaceDriverTargets(studioRoot, expressionLibrary, faceDriver);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8f);
            DrawFaceProfilePanel(studioRoot, expressionLibrary, faceDriver);
            EditorGUILayout.Space(8f);
            DrawArkitExpressionBuilder(expressionLibrary, faceDriver);
        }

        private void DrawTestingTab()
        {
            var studioRoot = ResolveStudioRootFromContext();
            SyncTabContextFromStudioRoot(studioRoot);
            var lipSyncController = studioRoot != null ? studioRoot.GetComponent<VisemeLipSyncController>() : null;
            var brain = studioRoot != null ? studioRoot.GetComponent<NyxaraCompanionBrain>() : null;
            var faceDriver = studioRoot != null ? studioRoot.GetComponent<ArkItBlendshapeDriver>() : null;
            var ttsService = FindFirstObjectByType<PiperTtsService>();
            var whisperInput = FindFirstObjectByType<WhisperMicrophoneInput>();
            SyncRuntimeReplyText(brain);
            EnsureTestingAssetsLoaded();
            EnsureTestingJsonLoaded(brain);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Lips And Expression", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Studio Root", studioRoot != null ? studioRoot.name : "Missing");
            _expressionRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Face Renderer", _expressionRenderer, typeof(SkinnedMeshRenderer), true);
            _lipSyncData = (LipSyncData)EditorGUILayout.ObjectField("Lip Sync Data", _lipSyncData, typeof(LipSyncData), false);
            EditorGUILayout.LabelField("Lip Sync Controller", lipSyncController != null ? lipSyncController.name : "Missing in scene");
            EditorGUILayout.LabelField("Brain", brain != null ? brain.name : "Missing in scene");
            EditorGUILayout.HelpBox("Use this tab to tune mouth motion, preview blinking, run voice playback checks, and test the full expression-to-speech flow from one place.", MessageType.Info);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("TTS Test", EditorStyles.miniBoldLabel);
            _testingVoiceClip = (AudioClip)EditorGUILayout.ObjectField("Imported Voice Clip", _testingVoiceClip, typeof(AudioClip), false);
            GUI.enabled = EditorApplication.isPlaying && ttsService != null && _testingVoiceClip != null;
            if (GUILayout.Button("Play Imported Voice Test"))
            {
                RunVoiceClipTest(ttsService);
            }
            GUI.enabled = true;

            EditorGUILayout.Space(8f);
            DrawLipSyncRuntimeSettings(lipSyncController);

            EditorGUILayout.Space(8f);
            DrawLipTestingSection(faceDriver);

            EditorGUILayout.Space(8f);
            DrawAutoBlinkTestingSection(faceDriver);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Lip Sync Test", EditorStyles.miniBoldLabel);
            _lipSyncTestLine = EditorGUILayout.TextField("Test Line", _lipSyncTestLine);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = EditorApplication.isPlaying && ttsService != null;
            if (GUILayout.Button("Run Lip Sync Test"))
            {
                RunLipSyncTest(ttsService);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Full System Test", EditorStyles.miniBoldLabel);
            _fullSystemTestPrompt = EditorGUILayout.TextField("Runthrough Prompt", _fullSystemTestPrompt);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = EditorApplication.isPlaying && brain != null && !brain.IsBusy;
            if (GUILayout.Button("Run Full System Test", GUILayout.Height(28f)))
            {
                RunFullSystemTest(brain, ttsService, lipSyncController, whisperInput);
            }

            GUI.enabled = !string.IsNullOrWhiteSpace(_fullSystemTestStatus);
            if (GUILayout.Button("Copy", GUILayout.Height(28f), GUILayout.Width(90f)))
            {
                EditorGUIUtility.systemCopyBuffer = _fullSystemTestStatus;
                Debug.Log("[Nyxara Test][Full] Copied full system test output to clipboard.");
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_fullSystemTestStatus))
            {
                EditorGUILayout.HelpBox(_fullSystemTestStatus, MessageType.None);
            }

            EditorGUILayout.Space(8f);
            DrawRuntimeReplyOutputSection(brain, ttsService);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to run the LLM test, imported voice playback, and full lip-sync test.", MessageType.None);
            }
            else if (ttsService == null)
            {
                EditorGUILayout.HelpBox("No PiperTtsService found in the active scene.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private async void RunLipSyncTest(PiperTtsService ttsService)
        {
            if (ttsService == null || string.IsNullOrWhiteSpace(_lipSyncTestLine))
            {
                return;
            }

            try
            {
                EnsureRuntimeLipSyncProfile(ResolveStudioRootFromContext(), false);
                EnsureRuntimeMouthControl(ResolveStudioRootFromContext());
                Debug.Log($"[Nyxara Test][LipSync] Starting lip sync test: {_lipSyncTestLine}");
                var clip = await ttsService.SpeakAsync(_lipSyncTestLine);
                if (clip == null)
                {
                    Debug.LogWarning($"[Nyxara Test][LipSync] No clip was generated. State={ttsService.InitializationState}, Error={ttsService.InitializationError ?? "none"}");
                }
                else
                {
                    var voiceName = ttsService.VoiceModel != null ? ttsService.VoiceModel.name : "unknown";
                    Debug.Log($"[Nyxara Test][LipSync] Clip generated: {clip.length:F2}s at {clip.frequency}Hz using '{voiceName}'. State={ttsService.InitializationState}.");
                }
                Debug.Log("[Nyxara Test][LipSync] Lip sync test request completed.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async void RunLlmTest(NyxaraCompanionBrain brain)
        {
            if (brain == null || string.IsNullOrWhiteSpace(_llmTestPrompt))
            {
                return;
            }

            _llmTestReply = "Running...";
            Repaint();

            try
            {
                Debug.Log($"[Nyxara Test][LLM] Sending prompt: {_llmTestPrompt}");
                _llmTestReply = await brain.ReplyToAsync(_llmTestPrompt);
                Debug.Log($"[Nyxara Test][LLM] Reply: {_llmTestReply}");
            }
            catch (Exception ex)
            {
                _llmTestReply = $"LLM test failed: {ex.Message}";
                Debug.LogException(ex);
            }

            Repaint();
        }

        private void RunVoiceClipTest(PiperTtsService ttsService)
        {
            if (ttsService == null || _testingVoiceClip == null)
            {
                return;
            }

            var audioSource = ttsService.AudioSource != null ? ttsService.AudioSource : ttsService.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                Debug.LogWarning("No AudioSource found for the imported voice clip test.");
                return;
            }

            audioSource.Stop();
            audioSource.clip = _testingVoiceClip;
            audioSource.Play();
            Debug.Log($"[Nyxara Test][TTS] Playing imported voice clip: {_testingVoiceClip.name}");

            EnsureRuntimeLipSyncProfile(ResolveStudioRootFromContext(), false);
            EnsureRuntimeMouthControl(ResolveStudioRootFromContext());
            if (ttsService.LipSyncController != null && !string.IsNullOrWhiteSpace(_lipSyncTestLine))
            {
                _ = ttsService.LipSyncController.SpeakWithLipSync(_lipSyncTestLine, _testingVoiceClip.length);
                Debug.Log($"[Nyxara Test][LipSync] Driving imported clip with test phrase timing: {_lipSyncTestLine}");
            }

            if (ttsService.FaceDriver != null)
            {
                ttsService.FaceDriver.SetSpeaking(true);
                ttsService.StartCoroutine(ttsService.FaceDriver.ReleaseSpeakingWhenSourceStops(audioSource));
            }
        }

        private async void StopMicrophoneAndSend(WhisperMicrophoneInput whisperInput)
        {
            if (whisperInput == null)
            {
                return;
            }

            try
            {
                Debug.Log("[Nyxara Test][Mic] Stopping microphone recording and starting transcription.");
                _microphoneTranscript = await whisperInput.StopRecordingAndTranscribeAsync();
                Debug.Log($"[Nyxara Test][Mic] Transcript: {_microphoneTranscript}");
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private async void RunFullSystemTest(
            NyxaraCompanionBrain brain,
            PiperTtsService ttsService,
            VisemeLipSyncController lipSyncController,
            WhisperMicrophoneInput whisperInput)
        {
            if (brain == null || string.IsNullOrWhiteSpace(_fullSystemTestPrompt))
            {
                return;
            }

            var stageMessages = new List<string>();
            void UpdateStatus(string message)
            {
                stageMessages.Add(message);
                _fullSystemTestStatus = string.Join("\n", stageMessages);
                Debug.Log($"[Nyxara Test][Full] {message}");
                Repaint();
            }

            UpdateStatus("Starting full system test...");
            UpdateStatus(brain.IsLlmAvailable ? "LLM: ready" : "LLM: missing agent or integration disabled");
            UpdateStatus(ttsService != null && ttsService.IsConfigured ? "TTS: ready" : "TTS: missing or not configured");
            UpdateStatus(lipSyncController != null ? "Lip Sync: ready" : "Lip Sync: missing");
            UpdateStatus(whisperInput != null && whisperInput.IsWhisperAvailable
                ? "STT: configured for runtime"
                : "STT: not fully configured for runtime");

            if (!brain.IsLlmAvailable)
            {
                UpdateStatus("Aborted: LLMUnity is not installed or no LLMAgent is assigned to the brain.");
                return;
            }

            try
            {
                PerformSystemScan();
                UpdateStatus("System scan: completed and logged to Unity console.");
                EnsureRuntimeLipSyncProfile(ResolveStudioRootFromContext(), false);
                EnsureRuntimeMouthControl(ResolveStudioRootFromContext());
                UpdateStatus("Sending internal diagnostic prompt through NyxaraCompanionBrain...");
                var reply = await brain.ReplyToSystemAsync(_fullSystemTestPrompt);
                _llmTestReply = reply;
                UpdateStatus(string.IsNullOrWhiteSpace(reply) ? "LLM reply: empty" : $"LLM reply: {reply}");

                if (ttsService == null || !ttsService.IsConfigured)
                {
                    UpdateStatus("TTS verification skipped because Piper is missing or not configured.");
                    return;
                }

                UpdateStatus("Waiting for TTS/lip sync playback to begin...");
                var playbackStarted = false;
                var audioSource = ttsService.AudioSource != null ? ttsService.AudioSource : ttsService.GetComponent<AudioSource>();
                for (var i = 0; i < 120; i++)
                {
                    if ((audioSource != null && audioSource.isPlaying) || (lipSyncController != null && lipSyncController.IsSpeaking))
                    {
                        playbackStarted = true;
                        break;
                    }

                    await System.Threading.Tasks.Task.Delay(50);
                }

                UpdateStatus(playbackStarted
                    ? "Playback: started successfully"
                    : "Playback: did not visibly start within the expected time");

                if (playbackStarted && audioSource != null)
                {
                    UpdateStatus("Waiting for playback to finish...");
                    while (audioSource.isPlaying)
                    {
                        await System.Threading.Tasks.Task.Delay(100);
                    }
                    UpdateStatus("Playback: finished");
                }

                UpdateStatus("Full system test completed.");
                Debug.Log($"[Nyxara Test][Full] Final summary:{Environment.NewLine}{_fullSystemTestStatus}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Full system test failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private void DrawCompanionBioSection()
        {
            EditorGUILayout.LabelField("Companion Bio / Profile JSON", EditorStyles.miniBoldLabel);
            var profile = _config != null ? _config.characterProfile : null;
            EditorGUILayout.LabelField("Profile Asset", profile != null ? AssetDatabase.GetAssetPath(profile) : "Missing");

            if (profile != null)
            {
                profile.backgroundSummary = EditorGUILayout.TextField("Companion Bio", profile.backgroundSummary);
                profile.speechStyle = EditorGUILayout.TextField("Speech Style", profile.speechStyle);
                EditorUtility.SetDirty(profile);
            }

            _profileJson = EditorGUILayout.TextArea(_profileJson, GUILayout.MinHeight(120f));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Profile JSON"))
            {
                RefreshProfileJson();
            }

            GUI.enabled = profile != null;
            if (GUILayout.Button("Save Profile JSON"))
            {
                SaveProfileJson();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPromptSenderSection(NyxaraCompanionBrain brain)
        {
            EditorGUILayout.LabelField("Prompt Sender", EditorStyles.miniBoldLabel);
            _llmTestPrompt = EditorGUILayout.TextField("Prompt", _llmTestPrompt);
            GUI.enabled = EditorApplication.isPlaying && brain != null && !brain.IsBusy;
            if (GUILayout.Button("Run LLM Test"))
            {
                RunLlmTest(brain);
            }

            GUI.enabled = true;
            if (!string.IsNullOrWhiteSpace(_llmTestReply))
            {
                EditorGUILayout.TextArea(_llmTestReply, GUILayout.MinHeight(60f));
            }
        }

        private void DrawLipTestingSection(ArkItBlendshapeDriver faceDriver)
        {
            EditorGUILayout.LabelField("Live Lip Mixer", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("These live testing sliders are smooth, mirrored, capped, and predictable. You can now widen each mouth control's working start/end range here while still keeping zero available to fully relax the face.", MessageType.None);
            var settingsChanged = false;
            var responseStart = EditorGUILayout.Slider("Response Start", _lipResponseStart, 0f, 0.95f);
            var responseEnd = EditorGUILayout.Slider("Response End", _lipResponseEnd, Mathf.Max(responseStart + 0.01f, 0.05f), 1f);
            var responseFalloff = EditorGUILayout.Slider("Response Falloff", _lipResponseFalloff, 0.25f, 3f);
            var responseSmoothing = EditorGUILayout.Slider("Response Smoothing", _lipResponseSmoothing, 1f, 25f);
            settingsChanged |= ApplyIfChanged(ref _lipResponseStart, responseStart);
            settingsChanged |= ApplyIfChanged(ref _lipResponseEnd, responseEnd);
            settingsChanged |= ApplyIfChanged(ref _lipResponseFalloff, responseFalloff);
            settingsChanged |= ApplyIfChanged(ref _lipResponseSmoothing, responseSmoothing);

            foreach (var control in LipControls)
            {
                if (!_lipTargetValues.ContainsKey(control.Key))
                {
                    _lipTargetValues[control.Key] = 0f;
                }

                if (!_lipSliderStartValues.ContainsKey(control.Key))
                {
                    _lipSliderStartValues[control.Key] = 0f;
                }

                if (!_lipSliderEndValues.ContainsKey(control.Key))
                {
                    _lipSliderEndValues[control.Key] = control.Max;
                }

                var rangeStart = Mathf.Clamp(_lipSliderStartValues[control.Key], 0f, 100f);
                var rangeEnd = Mathf.Clamp(_lipSliderEndValues[control.Key], Mathf.Max(rangeStart + 0.1f, 0.1f), 100f);
                EditorGUILayout.LabelField(control.Label, EditorStyles.miniBoldLabel);
                EditorGUILayout.MinMaxSlider("Working Range", ref rangeStart, ref rangeEnd, 0f, 100f);
                EditorGUILayout.BeginHorizontal();
                rangeStart = EditorGUILayout.FloatField("Start", rangeStart);
                rangeEnd = EditorGUILayout.FloatField("End", rangeEnd);
                EditorGUILayout.EndHorizontal();
                rangeStart = Mathf.Clamp(rangeStart, 0f, 100f);
                rangeEnd = Mathf.Clamp(Mathf.Max(rangeStart + 0.1f, rangeEnd), 0.1f, 100f);
                settingsChanged |= ApplyLipSliderRange(control.Key, rangeStart, rangeEnd);

                var currentValue = _lipTargetValues[control.Key];
                var sliderMax = Mathf.Max(0.1f, rangeEnd);
                var newValue = EditorGUILayout.Slider(
                    $"Value (default {control.Max:0.0}, range {rangeStart:0.0}-{rangeEnd:0.0})",
                    Mathf.Clamp(currentValue, 0f, sliderMax),
                    0f,
                    sliderMax);
                if (Math.Abs(newValue - currentValue) > 0.001f)
                {
                    _lipTargetValues[control.Key] = newValue;
                    Debug.Log($"[Nyxara Test][LipMixer] {control.Label} target set to {newValue:0.0} within range {rangeStart:0.0}-{rangeEnd:0.0}.");
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = faceDriver != null;
            if (GUILayout.Button("Reset Lip Mixer"))
            {
                foreach (var control in LipControls)
                {
                    _lipTargetValues[control.Key] = 0f;
                    foreach (var blendshape in control.Blendshapes)
                    {
                        faceDriver.TrySetBlendshapeWeight(blendshape, 0f);
                        _lipAppliedValues[blendshape] = 0f;
                    }
                }

                Debug.Log("[Nyxara Test][LipMixer] Reset all live lip mixer values.");
            }

            GUI.enabled = true;
            if (GUILayout.Button("Reset Lip Mixer Ranges"))
            {
                foreach (var control in LipControls)
                {
                    _lipSliderStartValues[control.Key] = 0f;
                    _lipSliderEndValues[control.Key] = control.Max;
                }

                PersistLipMixerSettings();
                Debug.Log("[Nyxara Test][LipMixer] Reset all lip mixer ranges to their defaults.");
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (settingsChanged)
            {
                PersistLipMixerSettings();
            }
        }

        private void DrawLipSyncRuntimeSettings(VisemeLipSyncController lipSyncController)
        {
            EditorGUILayout.LabelField("Lip Sync Runtime Setup", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("These are the live mouth-control values the lip sync controller uses during speech. Their ranges are expanded beyond 1.0 so you can push stronger mouth motion directly from the Test tab.", MessageType.None);

            if (lipSyncController == null)
            {
                EditorGUILayout.HelpBox("No VisemeLipSyncController found in the active Studio Root.", MessageType.Warning);
                return;
            }

            var serializedObject = new SerializedObject(lipSyncController);
            var changed = false;
            changed |= DrawSerializedFloatSlider(serializedObject, "mouthOpenAmount", "Mouth Open Amount", 0f, 3f);
            changed |= DrawSerializedFloatSlider(serializedObject, "visemeIntensityScale", "Viseme Intensity Scale", 0f, 3f);
            changed |= DrawSerializedFloatSlider(serializedObject, "lowerLipDropAmount", "Lower Lip Drop Amount", 0f, 3f);
            changed |= DrawSerializedFloatSlider(serializedObject, "upperLipRaiseAmount", "Upper Lip Raise Amount", 0f, 3f);
            changed |= DrawSerializedFloatSlider(serializedObject, "mouthStretchAmount", "Mouth Stretch Amount", 0f, 3f);
            changed |= DrawSerializedFloatSlider(serializedObject, "releaseDuration", "Release Duration", 0.01f, 1f);

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(lipSyncController);
                EditorUtility.SetDirty(lipSyncController);
            }
        }

        private void DrawAutoBlinkTestingSection(ArkItBlendshapeDriver faceDriver)
        {
            EditorGUILayout.LabelField("Auto Blink Test", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("Turn this on in Play Mode to preview natural eye blinking. It can randomly trigger a second quick blink, and the window shows the current random roll plus time until the next blink.", MessageType.None);

            var changed = false;
            var enabled = EditorGUILayout.Toggle("Auto Blink", _autoBlinkEnabled);
            changed |= ApplyIfChanged(ref _autoBlinkEnabled, enabled);
            changed |= ApplyIfChanged(ref _autoBlinkIntervalMin, EditorGUILayout.Slider("Min Interval", _autoBlinkIntervalMin, 0.5f, 10f));
            changed |= ApplyIfChanged(ref _autoBlinkIntervalMax, EditorGUILayout.Slider("Max Interval", _autoBlinkIntervalMax, Mathf.Max(_autoBlinkIntervalMin + 0.1f, 0.6f), 12f));
            changed |= ApplyIfChanged(ref _autoBlinkCloseAmount, EditorGUILayout.Slider("Blink Close Amount", _autoBlinkCloseAmount, 10f, 100f));
            changed |= ApplyIfChanged(ref _autoBlinkCloseDuration, EditorGUILayout.Slider("Close Time", _autoBlinkCloseDuration, 0.02f, 0.25f));
            changed |= ApplyIfChanged(ref _autoBlinkOpenDuration, EditorGUILayout.Slider("Open Time", _autoBlinkOpenDuration, 0.03f, 0.35f));
            changed |= ApplyIfChanged(ref _autoBlinkDoubleChance, EditorGUILayout.Slider("Double Blink Chance", _autoBlinkDoubleChance, 0f, 1f));
            changed |= ApplyIfChanged(ref _autoBlinkDoubleGapMin, EditorGUILayout.Slider("Double Blink Gap Min", _autoBlinkDoubleGapMin, 0.03f, 0.35f));
            changed |= ApplyIfChanged(ref _autoBlinkDoubleGapMax, EditorGUILayout.Slider("Double Blink Gap Max", _autoBlinkDoubleGapMax, Mathf.Max(_autoBlinkDoubleGapMin + 0.01f, 0.04f), 0.45f));

            var timeUntilNextBlink = Mathf.Max(0f, _autoBlinkNextTriggerTime - (float)EditorApplication.timeSinceStartup);
            EditorGUILayout.LabelField("Random Roll", _autoBlinkLastRandomRoll.ToString("0.00"));
            EditorGUILayout.LabelField("Next Blink In", $"{timeUntilNextBlink:0.00}s");
            EditorGUILayout.LabelField("Current Blink Weight", $"{_autoBlinkCurrentWeight:0.0}");

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = faceDriver != null && EditorApplication.isPlaying;
            if (GUILayout.Button("Blink Now"))
            {
                StartBlinkSequence();
            }

            if (GUILayout.Button("Reset Blink"))
            {
                ResetAutoBlinkPreview(faceDriver);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (changed)
            {
                PersistLipMixerSettings();
                if (!_autoBlinkEnabled)
                {
                    ResetAutoBlinkPreview(faceDriver);
                }
                else
                {
                    ScheduleNextBlink(true);
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to preview the random blinking test.", MessageType.Info);
            }
            else if (faceDriver == null)
            {
                EditorGUILayout.HelpBox("No ArkItBlendshapeDriver found in the active Studio Root.", MessageType.Warning);
            }
        }

        private static bool DrawSerializedFloatSlider(SerializedObject serializedObject, string propertyName, string label, float min, float max)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return false;
            }

            var currentValue = property.floatValue;
            var newValue = EditorGUILayout.Slider(label, currentValue, min, max);
            if (Math.Abs(newValue - currentValue) <= 0.0001f)
            {
                return false;
            }

            property.floatValue = newValue;
            return true;
        }

        private void DrawRuntimeJsonSection(NyxaraCompanionBrain brain)
        {
            EditorGUILayout.LabelField("Runtime JSON Editor", EditorStyles.miniBoldLabel);
            _runtimeJson = EditorGUILayout.TextArea(_runtimeJson, GUILayout.MinHeight(120f));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Runtime JSON"))
            {
                RefreshRuntimeJson(brain);
            }

            GUI.enabled = brain != null && brain.RuntimeState != null;
            if (GUILayout.Button("Apply Runtime JSON"))
            {
                ApplyRuntimeJson(brain);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureTestingJsonLoaded(NyxaraCompanionBrain brain)
        {
            if (string.IsNullOrWhiteSpace(_profileJson))
            {
                RefreshProfileJson();
            }

            if (string.IsNullOrWhiteSpace(_runtimeJson))
            {
                RefreshRuntimeJson(brain);
            }
        }

        private void RefreshProfileJson()
        {
            _profileJson = _config != null && _config.characterProfile != null
                ? EditorJsonUtility.ToJson(_config.characterProfile, true)
                : string.Empty;
        }

        private void SaveProfileJson()
        {
            if (_config?.characterProfile == null || string.IsNullOrWhiteSpace(_profileJson))
            {
                return;
            }

            try
            {
                EditorJsonUtility.FromJsonOverwrite(_profileJson, _config.characterProfile);
                EditorUtility.SetDirty(_config.characterProfile);
                AssetDatabase.SaveAssets();
                RefreshProfileJson();
                Debug.Log("[Nyxara Test][Profile] Profile JSON saved.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void RefreshRuntimeJson(NyxaraCompanionBrain brain)
        {
            _runtimeJson = brain != null && brain.RuntimeState != null
                ? JsonUtility.ToJson(brain.RuntimeState, true)
                : string.Empty;
        }

        private void ApplyRuntimeJson(NyxaraCompanionBrain brain)
        {
            if (brain?.RuntimeState == null || string.IsNullOrWhiteSpace(_runtimeJson))
            {
                return;
            }

            try
            {
                JsonUtility.FromJsonOverwrite(_runtimeJson, brain.RuntimeState);
                RefreshRuntimeJson(brain);
                Debug.Log("[Nyxara Test][Runtime] Runtime JSON applied.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void UpdateTestingLipPreview()
        {
            var studioRoot = ResolveStudioRootFromContext();
            var faceDriver = studioRoot != null ? studioRoot.GetComponent<ArkItBlendshapeDriver>() : null;
            if (faceDriver == null)
            {
                return;
            }

            const float deltaTime = 0.016f;
            foreach (var control in LipControls)
            {
                var rawValue = _lipTargetValues.TryGetValue(control.Key, out var foundValue) ? foundValue : 0f;
                var normalized = control.Max <= 0.001f ? 0f : rawValue / control.Max;
                var t = Mathf.InverseLerp(_lipResponseStart, Mathf.Max(_lipResponseStart + 0.001f, _lipResponseEnd), normalized);
                var shaped = Mathf.Pow(Mathf.Clamp01(t), Mathf.Max(0.01f, _lipResponseFalloff));
                var targetWeight = shaped * control.Max;

                foreach (var blendshape in control.Blendshapes)
                {
                    var currentWeight = _lipAppliedValues.TryGetValue(blendshape, out var current) ? current : faceDriver.GetBlendshapeWeight(blendshape);
                    var nextWeight = Mathf.Lerp(currentWeight, targetWeight, 1f - Mathf.Exp(-_lipResponseSmoothing * deltaTime));
                    faceDriver.TrySetBlendshapeWeight(blendshape, nextWeight);
                    _lipAppliedValues[blendshape] = nextWeight;
                }
            }

            UpdateAutoBlinkPreview(faceDriver);
        }

        private void UpdateAutoBlinkPreview(ArkItBlendshapeDriver faceDriver)
        {
            var currentTime = EditorApplication.timeSinceStartup;
            if (!_autoBlinkEnabled)
            {
                ResetAutoBlinkPreview(faceDriver);
                return;
            }

            if (faceDriver == null)
            {
                return;
            }

            var deltaTime = _autoBlinkLastUpdateTime <= 0d ? 0f : (float)Math.Max(0d, currentTime - _autoBlinkLastUpdateTime);
            _autoBlinkLastUpdateTime = currentTime;

            if (_autoBlinkRemainingClosures <= 0)
            {
                if (_autoBlinkNextTriggerTime <= 0f)
                {
                    ScheduleNextBlink(true);
                }

                if ((float)currentTime >= _autoBlinkNextTriggerTime)
                {
                    StartBlinkSequence();
                }
            }

            var targetWeight = 0f;
            if (_autoBlinkRemainingClosures > 0)
            {
                var phaseDuration = _autoBlinkClosingPhase ? Mathf.Max(0.01f, _autoBlinkCloseDuration) : Mathf.Max(0.01f, _autoBlinkOpenDuration);
                var elapsed = (float)(currentTime - _autoBlinkPhaseStartedAt);
                var phaseT = Mathf.Clamp01(elapsed / phaseDuration);
                targetWeight = _autoBlinkClosingPhase
                    ? Mathf.Lerp(0f, _autoBlinkCloseAmount, phaseT)
                    : Mathf.Lerp(_autoBlinkCloseAmount, 0f, phaseT);

                if (elapsed >= phaseDuration)
                {
                    if (_autoBlinkClosingPhase)
                    {
                        _autoBlinkClosingPhase = false;
                        _autoBlinkPhaseStartedAt = currentTime;
                    }
                    else
                    {
                        _autoBlinkRemainingClosures--;
                        if (_autoBlinkRemainingClosures > 0)
                        {
                            _autoBlinkClosingPhase = true;
                            _autoBlinkPhaseStartedAt = currentTime + _autoBlinkNextGap;
                        }
                        else
                        {
                            _autoBlinkPhaseStartedAt = 0d;
                            ScheduleNextBlink();
                        }
                    }
                }
            }

            if (_autoBlinkPhaseStartedAt > currentTime && _autoBlinkRemainingClosures > 0)
            {
                targetWeight = 0f;
            }

            var smoothing = deltaTime <= 0f ? 1f : 1f - Mathf.Exp(-20f * deltaTime);
            _autoBlinkCurrentWeight = Mathf.Lerp(_autoBlinkCurrentWeight, targetWeight, smoothing);
            ApplyBlinkWeight(faceDriver, _autoBlinkCurrentWeight);
        }

        private void StartBlinkSequence()
        {
            _autoBlinkLastRandomRoll = UnityEngine.Random.value;
            _autoBlinkRemainingClosures = _autoBlinkLastRandomRoll <= Mathf.Clamp01(_autoBlinkDoubleChance) ? 2 : 1;
            _autoBlinkNextGap = UnityEngine.Random.Range(_autoBlinkDoubleGapMin, Mathf.Max(_autoBlinkDoubleGapMin + 0.01f, _autoBlinkDoubleGapMax));
            _autoBlinkClosingPhase = true;
            _autoBlinkPhaseStartedAt = EditorApplication.timeSinceStartup;
        }

        private void ScheduleNextBlink(bool immediate = false)
        {
            _autoBlinkIntervalMax = Mathf.Max(_autoBlinkIntervalMin + 0.1f, _autoBlinkIntervalMax);
            var delay = immediate ? UnityEngine.Random.Range(_autoBlinkIntervalMin, _autoBlinkIntervalMax) : UnityEngine.Random.Range(_autoBlinkIntervalMin, _autoBlinkIntervalMax);
            _autoBlinkNextTriggerTime = (float)EditorApplication.timeSinceStartup + delay;
        }

        private void ResetAutoBlinkPreview(ArkItBlendshapeDriver faceDriver)
        {
            _autoBlinkRemainingClosures = 0;
            _autoBlinkClosingPhase = false;
            _autoBlinkPhaseStartedAt = 0d;
            _autoBlinkLastUpdateTime = 0d;
            _autoBlinkCurrentWeight = 0f;
            _autoBlinkNextTriggerTime = 0f;
            if (faceDriver != null)
            {
                ApplyBlinkWeight(faceDriver, 0f);
            }
        }

        private static void ApplyBlinkWeight(ArkItBlendshapeDriver faceDriver, float weight)
        {
            if (faceDriver == null)
            {
                return;
            }

            faceDriver.TrySetBlendshapeWeight("eyeBlinkLeft", weight);
            faceDriver.TrySetBlendshapeWeight("eyeBlinkRight", weight);
        }

        private void DrawArkitExpressionBuilder(ExpressionLibraryManager expressionLibrary, ArkItBlendshapeDriver faceDriver)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("ARKit Expression Builder", EditorStyles.boldLabel);

            if (_expressionRenderer == null)
            {
                EditorGUILayout.HelpBox("Select or auto-detect a face renderer first so the builder can find ARKit blendshapes.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_builderBlendshapeOptions.Count == 0)
            {
                RefreshBuilderBlendshapeOptions();
            }

            EditorGUILayout.HelpBox("Use ARKit-style sliders to pose the face, name the expression, then click Build Expression to save it directly into the expression library for runtime and AI use.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-Detect Face Mapping"))
            {
                AutoDetectBuilderBlendshapes();
                PullWeightsFromFaceDriver(faceDriver);
                Repaint();
            }

            if (GUILayout.Button("Pull Current Face"))
            {
                PullWeightsFromFaceDriver(faceDriver);
            }

            if (GUILayout.Button("Reset Builder"))
            {
                ResetBuilderWeights();
                expressionLibrary?.ResetToNeutral();
            }

            if (GUILayout.Button("Generate Lip Sync Mapping"))
            {
                GenerateDetectedProfileLipSyncMapping();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            _builderPresetName = EditorGUILayout.TextField("Expression Name", _builderPresetName);
            _builderCategory = (ExpressionCategory)EditorGUILayout.EnumPopup("Category", _builderCategory);
            _builderDescription = EditorGUILayout.TextField("Description", _builderDescription);
            _builderTransitionTime = EditorGUILayout.Slider("Transition Time", _builderTransitionTime, 0.05f, 1f);

            EditorGUILayout.Space(8f);
            var controls = ExpressionBuilderHelper.GetDefaultControls();
            for (var i = 0; i < controls.Count; i++)
            {
                var control = controls[i];
                DrawBuilderControlRow(control, expressionLibrary, faceDriver);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _selectedExpressionPreset != null;
            if (GUILayout.Button("Load Selected Into Builder"))
            {
                LoadPresetIntoBuilder(_selectedExpressionPreset);
            }

            GUI.enabled = expressionLibrary != null && _selectedExpressionPreset != null;
            if (GUILayout.Button("Delete Selected Preset"))
            {
                if (EditorUtility.DisplayDialog("Delete Expression", $"Delete '{_selectedExpressionPreset.displayName}' from the expression library?", "Delete", "Cancel"))
                {
                    if (expressionLibrary.DeletePreset(_selectedExpressionPreset))
                    {
                        _selectedExpressionPreset = expressionLibrary.LoadedPresets.FirstOrDefault();
                    }
                }
            }

            GUI.enabled = expressionLibrary != null;
            if (GUILayout.Button("Build Expression", GUILayout.Height(32f)))
            {
                BuildExpressionPreset(expressionLibrary);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawBuilderControlRow(
            ExpressionBuilderHelper.ControlDefinition control,
            ExpressionLibraryManager expressionLibrary,
            ArkItBlendshapeDriver faceDriver)
        {
            if (!_builderWeights.ContainsKey(control.key))
            {
                _builderWeights[control.key] = 0f;
            }

            if (!_builderBlendshapeMap.ContainsKey(control.key))
            {
                _builderBlendshapeMap[control.key] = string.Empty;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(control.displayName, GUILayout.Width(135f));

            var popupIndex = GetBuilderPopupIndex(_builderBlendshapeMap[control.key]);
            var newIndex = EditorGUILayout.Popup(popupIndex, _builderBlendshapeOptions.ToArray(), GUILayout.Width(220f));
            var selectedBlendshape = newIndex > 0 && newIndex < _builderBlendshapeOptions.Count ? _builderBlendshapeOptions[newIndex] : string.Empty;
            if (!string.Equals(selectedBlendshape, _builderBlendshapeMap[control.key], StringComparison.Ordinal))
            {
                _builderBlendshapeMap[control.key] = selectedBlendshape;
                _builderWeights[control.key] = !string.IsNullOrWhiteSpace(selectedBlendshape) && faceDriver != null
                    ? faceDriver.GetBlendshapeWeight(selectedBlendshape)
                    : 0f;
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_builderBlendshapeMap[control.key])))
            {
                var currentValue = _builderWeights[control.key];
                var newValue = EditorGUILayout.Slider(currentValue, 0f, 100f);
                if (Math.Abs(newValue - currentValue) > 0.01f)
                {
                    _builderWeights[control.key] = newValue;
                    ApplyBuilderPreview(expressionLibrary, faceDriver);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawDiagnosticsTab()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("System Scan", EditorStyles.miniButtonLeft)) _diagnosticsTab = DiagnosticsTab.SystemScan;
            if (GUILayout.Button("Runtime", EditorStyles.miniButtonMid)) _diagnosticsTab = DiagnosticsTab.Runtime;
            if (GUILayout.Button("Logs", EditorStyles.miniButtonRight)) _diagnosticsTab = DiagnosticsTab.Logs;
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);

            switch (_diagnosticsTab)
            {
                case DiagnosticsTab.SystemScan:
                    DrawSystemScanTab();
                    break;
                case DiagnosticsTab.Runtime:
                    DrawRuntimeTab();
                    break;
                case DiagnosticsTab.Logs:
                    DrawLogsTab();
                    break;
            }
        }

        private void DrawSystemScanTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("System Scan", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Checks scene components, source asset setup, prefab output, model paths, and missing links before you hit play.", MessageType.Info);
            if (GUILayout.Button("Run Full System Scan", GUILayout.Height(34f)))
            {
                PerformSystemScan();
            }

            if (_lastScanReport != null)
            {
                DrawScanResults(_lastScanReport);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Runtime Diagnostics", EditorStyles.boldLabel);
            var runtimeOverlay = FindFirstObjectByType<RuntimeConversationOverlay>();
            var runtimeBrain = FindFirstObjectByType<NyxaraCompanionBrain>();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Runtime Prompt Check", EditorStyles.miniBoldLabel);
            if (runtimeOverlay == null)
            {
                EditorGUILayout.HelpBox(
                    EditorApplication.isPlaying
                        ? "No RuntimeConversationOverlay found in the active scene, so the prompt-check bar cannot be toggled from here."
                        : "Enter Play Mode to attach to the live RuntimeConversationOverlay and toggle the prompt-check bar from here.",
                    MessageType.Info);
            }
            else
            {
                var showPromptCheckBar = EditorGUILayout.Toggle("Enable Prompt Check Bar", runtimeOverlay.ShowDiagnosticPromptCheckBar);
                if (showPromptCheckBar != runtimeOverlay.ShowDiagnosticPromptCheckBar)
                {
                    runtimeOverlay.ShowDiagnosticPromptCheckBar = showPromptCheckBar;
                    EditorUtility.SetDirty(runtimeOverlay);
                }

                var smartVoiceCapture = EditorGUILayout.Toggle("Enable Smart Voice Capture", runtimeOverlay.EnableSmartVoiceCapture);
                if (smartVoiceCapture != runtimeOverlay.EnableSmartVoiceCapture)
                {
                    runtimeOverlay.EnableSmartVoiceCapture = smartVoiceCapture;
                    EditorUtility.SetDirty(runtimeOverlay);
                }

                EditorGUILayout.HelpBox(
                    showPromptCheckBar
                        ? "The runtime overlay will show the prompt-check sections and mirror them to the Unity console."
                        : "The runtime overlay will stay player-facing only, without the prompt-check sections or console payloads.",
                    MessageType.None);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Reply Mode", EditorStyles.miniBoldLabel);
            if (runtimeBrain == null)
            {
                EditorGUILayout.HelpBox(
                    EditorApplication.isPlaying
                        ? "No live NyxaraCompanionBrain was found, so reply mode cannot be changed from here."
                        : "Enter Play Mode to switch between final character mode and diagnostic inspector mode.",
                    MessageType.Info);
            }
            else
            {
                var selectedMode = (NyxaraReplyMode)EditorGUILayout.EnumPopup("Runtime Reply Mode", runtimeBrain.ReplyMode);
                if (selectedMode != runtimeBrain.ReplyMode)
                {
                    runtimeBrain.ReplyMode = selectedMode;
                    EditorUtility.SetDirty(runtimeBrain);
                }

                EditorGUILayout.HelpBox(
                    selectedMode == NyxaraReplyMode.DiagnosticInspector
                        ? "Diagnostic Inspector mode is active. Nyxara will answer more like a robotic inspection assistant and report memory/state directly."
                        : "Character mode is active. Nyxara will stay immersive, humanized, and more evasive when appropriate.",
                    MessageType.None);
            }
            EditorGUILayout.EndVertical();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox("Runtime diagnostics only update during Play Mode.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_runtimeMonitor == null)
            {
                EditorGUILayout.HelpBox("No RuntimeDiagnosticsMonitor found in the active scene.", MessageType.Warning);
                if (GUILayout.Button("Add Runtime Diagnostics Monitor"))
                {
                    var monitorObject = new GameObject("RuntimeDiagnosticsMonitor");
                    _runtimeMonitor = monitorObject.AddComponent<RuntimeDiagnosticsMonitor>();
                    Selection.activeGameObject = monitorObject;
                }

                EditorGUILayout.EndVertical();
                return;
            }

            var studioRoot = ResolveStudioRootFromContext();
            var brain = studioRoot != null ? studioRoot.GetComponent<NyxaraCompanionBrain>() : null;
            var ttsService = FindFirstObjectByType<PiperTtsService>();
            SyncRuntimeReplyText(brain);

            EditorGUILayout.Space(6f);
            DrawRuntimeReplyOutputSection(brain, ttsService);
            EditorGUILayout.Space(6f);
            DrawRuntimeMemorySection(brain);
            EditorGUILayout.Space(6f);

            var report = _runtimeMonitor.CurrentReport;
            if (report != null)
            {
                DrawRuntimeMetrics(report);
                DrawLiveState(report.runtimeSnapshot);
            }
            else
            {
                EditorGUILayout.LabelField("Waiting for runtime data...");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLogsTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Unity Logs", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _logFilter = EditorGUILayout.TextField("Filter", _logFilter);
            _showErrors = EditorGUILayout.ToggleLeft("Errors", _showErrors, GUILayout.Width(70f));
            _showWarnings = EditorGUILayout.ToggleLeft("Warnings", _showWarnings, GUILayout.Width(85f));
            _showInfo = EditorGUILayout.ToggleLeft("Info", _showInfo, GUILayout.Width(60f));
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                _logEntries.Clear();
            }

            EditorGUILayout.EndHorizontal();
            foreach (var entry in FilterLogs())
            {
                var originalColor = GUI.color;
                GUI.color = GetLogColor(entry.Type);
                EditorGUILayout.LabelField($"[{entry.Timestamp}] {entry.Type}: {entry.Message}", EditorStyles.wordWrappedLabel);
                GUI.color = originalColor;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawScanResults(SystemDiagnosticsReport report)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"Scan completed at {report.timestamp} in {report.durationMs:F0} ms", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(report.IsHealthy ? "Overall Status: Healthy" : "Overall Status: Issues Found", EditorStyles.boldLabel);
            EditorGUILayout.Space(6f);

            if (report.findings != null && report.findings.Count > 0)
            {
                foreach (var finding in report.findings)
                {
                    EditorGUILayout.HelpBox($"{finding.title}\n{finding.message}", ToEditorMessageType(finding.severity));
                }

                EditorGUILayout.Space(6f);
            }

            DrawComponentStatusCard(report.llmStatus);
            DrawComponentStatusCard(report.sttStatus);
            DrawComponentStatusCard(report.ttsStatus);
            DrawComponentStatusCard(report.faceStatus);
            DrawComponentStatusCard(report.lipSyncStatus);
            DrawComponentStatusCard(report.expressionStatus);

            foreach (var issue in report.configIssues)
            {
                var type = issue.severity switch
                {
                    IssueSeverity.Critical => MessageType.Error,
                    IssueSeverity.Warning => MessageType.Warning,
                    _ => MessageType.Info
                };
                EditorGUILayout.HelpBox($"{issue.component}: {issue.issue}\nSuggestion: {issue.suggestion}", type);
            }

            foreach (var path in report.pathValidations)
            {
                EditorGUILayout.LabelField($"{path.name}: {(path.exists ? "Found" : "Missing")}  {path.path}", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawRuntimeMetrics(SystemDiagnosticsReport report)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical("box", GUILayout.Width(200f));
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Memory: {report.performance.memoryUsageMB} MB");
            EditorGUILayout.LabelField($"CPU: {report.performance.cpuUsagePercent:F1}%");
            EditorGUILayout.LabelField($"Threads: {report.performance.activeThreads}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box", GUILayout.Width(200f));
            EditorGUILayout.LabelField("Latency", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"LLM: {report.performance.averageLLMLatencyMs:F0} ms");
            EditorGUILayout.LabelField($"STT: {report.performance.averageSTTLatencyMs:F0} ms");
            EditorGUILayout.LabelField($"TTS: {report.performance.averageTTSLatencyMs:F0} ms");
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLiveState(RuntimeSnapshot snapshot)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Mood: {snapshot.currentMood}");
            EditorGUILayout.LabelField($"Task: {snapshot.currentTask}");
            EditorGUILayout.LabelField($"Intent: {snapshot.lastIntent}");
            EditorGUILayout.LabelField($"Action: {snapshot.lastAction}");
            EditorGUILayout.LabelField($"Signal: {snapshot.lastSignal}");
            EditorGUILayout.LabelField($"Dialogue: {snapshot.lastDialogue}", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField($"Speaking: {snapshot.isSpeaking}  Thinking: {snapshot.isThinking}");
            EditorGUILayout.LabelField($"Working Memory: {snapshot.memoryCount}");
            EditorGUILayout.LabelField($"Saved Event Memory: {snapshot.savedEventMemoryCount}");
            EditorGUILayout.LabelField($"Saved Relationship Memory: {snapshot.savedRelationshipMemoryCount}");
            if (!string.IsNullOrWhiteSpace(snapshot.memoryStorageSummary))
            {
                EditorGUILayout.LabelField($"Storage: {snapshot.memoryStorageSummary}", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeMemorySection(NyxaraCompanionBrain brain)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Memory Status", EditorStyles.boldLabel);

            var memoryController = brain != null ? brain.MemoryController : null;
            if (memoryController == null)
            {
                EditorGUILayout.HelpBox("No RecentMemoryController is attached to the active Nyxara brain.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField($"Working Memory Entries: {memoryController.GetMemoryCount()}");
            EditorGUILayout.LabelField($"Saved Event Memory Entries: {memoryController.GetSavedEventCount()}");
            EditorGUILayout.LabelField($"Saved Relationship Memory Entries: {memoryController.GetSavedRelationshipEventCount()}");
            EditorGUILayout.LabelField("Storage Summary");
            EditorGUILayout.LabelField(memoryController.GetMemoryStorageSummary(), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox(
                "Working memory is short-term scene context. Event memory stores structured JSON conversation events. Relationship memory stores meaningful long-term shifts like trust or affection changes.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawComponentStatusCard(ComponentStatus status)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(status.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(status.statusMessage ?? string.Empty, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(status.stateLabel) ? (status.isOperational ? "Operational" : "Offline") : status.stateLabel, EditorStyles.miniLabel);
            if (!string.IsNullOrWhiteSpace(status.recommendedAction))
            {
                EditorGUILayout.HelpBox(status.recommendedAction, status.isOperational ? MessageType.Info : MessageType.Warning);
            }

            if (status.details != null)
            {
                foreach (var detail in status.details)
                {
                    if (!string.IsNullOrWhiteSpace(detail))
                    {
                        EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private List<LogEntry> FilterLogs()
        {
            IEnumerable<LogEntry> filtered = _logEntries;
            if (!string.IsNullOrWhiteSpace(_logFilter))
            {
                filtered = filtered.Where(entry => entry.Message.IndexOf(_logFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return filtered.Where(entry =>
            {
                var isError = entry.Type == LogType.Error || entry.Type == LogType.Exception || entry.Type == LogType.Assert;
                var isWarning = entry.Type == LogType.Warning;
                var isInfo = !isError && !isWarning;
                return (_showErrors || !isError) && (_showWarnings || !isWarning) && (_showInfo || !isInfo);
            }).ToList();
        }

        private void FindRuntimeMonitor()
        {
            _runtimeMonitor = FindFirstObjectByType<RuntimeDiagnosticsMonitor>();
        }

        private void PerformSystemScan()
        {
            var scanner = new StudioSystemScanner(_config);
            _lastScanReport = scanner.Scan();
            LogSystemScanToConsole(_lastScanReport);
            Repaint();
        }

        private static void LogSystemScanToConsole(SystemDiagnosticsReport report)
        {
            if (report == null)
            {
                return;
            }

            Debug.Log($"[Nyxara Scan] Completed at {report.timestamp} in {report.durationMs:F0} ms. Healthy={report.IsHealthy}");
            LogComponentStatus(report.llmStatus);
            LogComponentStatus(report.sttStatus);
            LogComponentStatus(report.ttsStatus);
            LogComponentStatus(report.faceStatus);
            LogComponentStatus(report.lipSyncStatus);
            LogComponentStatus(report.expressionStatus);

            foreach (var issue in report.configIssues)
            {
                var message = $"[Nyxara Scan][{issue.severity}] {issue.component}: {issue.issue} | Suggestion: {issue.suggestion}";
                if (issue.severity == IssueSeverity.Critical)
                {
                    Debug.LogError(message);
                }
                else if (issue.severity == IssueSeverity.Warning)
                {
                    Debug.LogWarning(message);
                }
                else
                {
                    Debug.Log(message);
                }
            }

            if (report.findings != null)
            {
                foreach (var finding in report.findings)
                {
                    var findingMessage = $"[Nyxara Scan][Finding] {finding.title}: {finding.message}";
                    switch (finding.severity)
                    {
                        case DiagnosticSeverity.Error:
                            Debug.LogError(findingMessage);
                            break;
                        case DiagnosticSeverity.Warning:
                            Debug.LogWarning(findingMessage);
                            break;
                        default:
                            Debug.Log(findingMessage);
                            break;
                    }
                }
            }

            foreach (var path in report.pathValidations)
            {
                Debug.Log($"[Nyxara Scan][Path] {path.name}: {(path.exists ? "Found" : "Missing")} | {path.path}");
            }
        }

        private static void LogComponentStatus(ComponentStatus status)
        {
            if (status == null)
            {
                return;
            }

            var message = $"[Nyxara Scan][Component] {status.name}: {(status.isOperational ? "Operational" : "Offline")} | {status.statusMessage}";
            if (!status.isPresent)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }

            if (!string.IsNullOrWhiteSpace(status.recommendedAction))
            {
                Debug.Log($"[Nyxara Scan][Advice] {status.name}: {status.recommendedAction}");
            }

            if (status.details != null)
            {
                foreach (var detail in status.details)
                {
                    if (!string.IsNullOrWhiteSpace(detail))
                    {
                        Debug.Log($"[Nyxara Scan][Detail] {status.name}: {detail}");
                    }
                }
            }
        }

        private static MessageType ToEditorMessageType(DiagnosticSeverity severity)
        {
            return severity switch
            {
                DiagnosticSeverity.Error => MessageType.Error,
                DiagnosticSeverity.Warning => MessageType.Warning,
                _ => MessageType.Info
            };
        }

        private static Color GetLogColor(LogType type)
        {
            return type switch
            {
                LogType.Error => new Color(1f, 0.55f, 0.55f),
                LogType.Exception => new Color(1f, 0.55f, 0.55f),
                LogType.Assert => new Color(1f, 0.55f, 0.55f),
                LogType.Warning => new Color(1f, 0.9f, 0.55f),
                _ => Color.white
            };
        }

        private static void DrawPathStatus(string label, string path, bool allowRelative)
        {
            var exists = false;
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (allowRelative && !Path.IsPathRooted(path))
                {
                    var absolute = ResolveAbsoluteOrProjectPath(path);
                    exists = File.Exists(absolute);
                }
                else
                {
                    exists = File.Exists(path);
                }
            }

            EditorGUILayout.LabelField($"{label} Status", exists ? "Found" : "Missing");
        }

        private static string ResolveModelStatusPath(string configuredPath)
        {
            var resolvedConfiguredPath = ResolveAbsoluteOrProjectPath(configuredPath);
            if (!string.IsNullOrWhiteSpace(resolvedConfiguredPath) && File.Exists(resolvedConfiguredPath))
            {
                return resolvedConfiguredPath;
            }

            var configuredFileName = GetSafeFileName(configuredPath);
            if (!string.IsNullOrWhiteSpace(configuredFileName))
            {
                var configuredStreamingAssetsModel = Path.Combine(Application.streamingAssetsPath, "Models", configuredFileName);
                if (File.Exists(configuredStreamingAssetsModel))
                {
                    return configuredStreamingAssetsModel;
                }
            }

            var localModel = Path.Combine(Application.streamingAssetsPath, "Models", CompanionStackDefaults.QwenModelFileName);
            if (File.Exists(localModel))
            {
                return localModel;
            }

            return resolvedConfiguredPath;
        }

        private static string ResolveAbsoluteOrProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var sanitizedPath = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(sanitizedPath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(sanitizedPath))
            {
                return sanitizedPath;
            }

            if (sanitizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(Application.dataPath, sanitizedPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
            }

            try
            {
                var streamingAssetsPath = Path.Combine(
                    Application.streamingAssetsPath,
                    sanitizedPath.Replace("Assets/StreamingAssets/", string.Empty)
                        .Replace("StreamingAssets/", string.Empty)
                        .Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(streamingAssetsPath) || Directory.Exists(streamingAssetsPath))
                {
                    return streamingAssetsPath;
                }

                return Path.Combine(Directory.GetCurrentDirectory(), sanitizedPath.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (ArgumentException)
            {
                return sanitizedPath.Replace('\\', '/');
            }
        }

        private void ResetStudioWorkspace()
        {
            if (_config == null)
            {
                return;
            }

            NyxaraCompanionStudioBuilder.ResetStudio(_config);
            ResetStudioRigDefaults(_config);
            ResetWindowState();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            Repaint();
        }

        private void ResetWindowState(bool resetSelectedTab = true)
        {
            if (resetSelectedTab)
            {
                _currentTab = StudioTab.Studio;
                _diagnosticsTab = DiagnosticsTab.SystemScan;
            }
            else
            {
                _currentTab = LoadPersistedStudioTab();
                _diagnosticsTab = LoadPersistedDiagnosticsTab();
            }

            _scrollPosition = Vector2.zero;
            _lastScanReport = null;
            _logFilter = string.Empty;
            _showErrors = true;
            _showWarnings = true;
            _showInfo = true;
            _lastRuntimeRepaint = 0d;
            _expressionRenderer = null;
            _lipSyncData = null;
            _selectedExpressionPreset = null;
            _expressionModeEnabled = false;
            _llmTestReply = string.Empty;
            _runtimeReplyText = string.Empty;
            _fullSystemTestStatus = string.Empty;
            _microphoneTranscript = string.Empty;
            _testingVoiceClip = LoadDefaultTestingVoiceClip();
            _profileJson = string.Empty;
            _runtimeJson = string.Empty;
            _lipResponseStart = 0f;
            _lipResponseEnd = 1f;
            _lipResponseFalloff = 1.35f;
            _lipResponseSmoothing = 12f;
            _autoBlinkNextTriggerTime = 0f;
            _autoBlinkNextGap = 0.12f;
            _autoBlinkLastRandomRoll = 0f;
            _autoBlinkRemainingClosures = 0;
            _autoBlinkPhaseStartedAt = 0d;
            _autoBlinkLastUpdateTime = 0d;
            _autoBlinkClosingPhase = false;
            _autoBlinkCurrentWeight = 0f;
            _builderPresetName = "New ARKit Expression";
            _builderDescription = string.Empty;
            _builderCategory = ExpressionCategory.Emotion;
            _builderTransitionTime = 0.15f;
            _builderBlendshapeMap.Clear();
            _builderWeights.Clear();
            _builderBlendshapeOptions.Clear();
            _lipTargetValues.Clear();
            _lipAppliedValues.Clear();
            _logEntries.Clear();
        }

        private void PersistWindowSelection()
        {
            EditorPrefs.SetInt(StudioTabPrefsKey, (int)_currentTab);
            EditorPrefs.SetInt(DiagnosticsTabPrefsKey, (int)_diagnosticsTab);
        }

        private void PersistLipMixerSettings()
        {
            EditorPrefs.SetFloat(LipResponseStartPrefsKey, _lipResponseStart);
            EditorPrefs.SetFloat(LipResponseEndPrefsKey, _lipResponseEnd);
            EditorPrefs.SetFloat(LipResponseFalloffPrefsKey, _lipResponseFalloff);
            EditorPrefs.SetFloat(LipResponseSmoothingPrefsKey, _lipResponseSmoothing);
            EditorPrefs.SetBool(AutoBlinkEnabledPrefsKey, _autoBlinkEnabled);
            EditorPrefs.SetFloat(AutoBlinkIntervalMinPrefsKey, _autoBlinkIntervalMin);
            EditorPrefs.SetFloat(AutoBlinkIntervalMaxPrefsKey, _autoBlinkIntervalMax);
            EditorPrefs.SetFloat(AutoBlinkCloseAmountPrefsKey, _autoBlinkCloseAmount);
            EditorPrefs.SetFloat(AutoBlinkCloseDurationPrefsKey, _autoBlinkCloseDuration);
            EditorPrefs.SetFloat(AutoBlinkOpenDurationPrefsKey, _autoBlinkOpenDuration);
            EditorPrefs.SetFloat(AutoBlinkDoubleChancePrefsKey, _autoBlinkDoubleChance);
            EditorPrefs.SetFloat(AutoBlinkDoubleGapMinPrefsKey, _autoBlinkDoubleGapMin);
            EditorPrefs.SetFloat(AutoBlinkDoubleGapMaxPrefsKey, _autoBlinkDoubleGapMax);

            foreach (var control in LipControls)
            {
                var startValue = _lipSliderStartValues.TryGetValue(control.Key, out var storedStart) ? storedStart : 0f;
                var endValue = _lipSliderEndValues.TryGetValue(control.Key, out var storedEnd) ? storedEnd : control.Max;
                EditorPrefs.SetFloat($"{LipSliderStartPrefsPrefix}{control.Key}", startValue);
                EditorPrefs.SetFloat($"{LipSliderEndPrefsPrefix}{control.Key}", endValue);
            }
        }

        private void LoadPersistedLipMixerSettings()
        {
            _lipResponseStart = EditorPrefs.GetFloat(LipResponseStartPrefsKey, _lipResponseStart);
            _lipResponseEnd = EditorPrefs.GetFloat(LipResponseEndPrefsKey, _lipResponseEnd);
            _lipResponseFalloff = EditorPrefs.GetFloat(LipResponseFalloffPrefsKey, _lipResponseFalloff);
            _lipResponseSmoothing = EditorPrefs.GetFloat(LipResponseSmoothingPrefsKey, _lipResponseSmoothing);
            _autoBlinkEnabled = EditorPrefs.GetBool(AutoBlinkEnabledPrefsKey, _autoBlinkEnabled);
            _autoBlinkIntervalMin = EditorPrefs.GetFloat(AutoBlinkIntervalMinPrefsKey, _autoBlinkIntervalMin);
            _autoBlinkIntervalMax = EditorPrefs.GetFloat(AutoBlinkIntervalMaxPrefsKey, _autoBlinkIntervalMax);
            _autoBlinkCloseAmount = EditorPrefs.GetFloat(AutoBlinkCloseAmountPrefsKey, _autoBlinkCloseAmount);
            _autoBlinkCloseDuration = EditorPrefs.GetFloat(AutoBlinkCloseDurationPrefsKey, _autoBlinkCloseDuration);
            _autoBlinkOpenDuration = EditorPrefs.GetFloat(AutoBlinkOpenDurationPrefsKey, _autoBlinkOpenDuration);
            _autoBlinkDoubleChance = EditorPrefs.GetFloat(AutoBlinkDoubleChancePrefsKey, _autoBlinkDoubleChance);
            _autoBlinkDoubleGapMin = EditorPrefs.GetFloat(AutoBlinkDoubleGapMinPrefsKey, _autoBlinkDoubleGapMin);
            _autoBlinkDoubleGapMax = EditorPrefs.GetFloat(AutoBlinkDoubleGapMaxPrefsKey, _autoBlinkDoubleGapMax);

            foreach (var control in LipControls)
            {
                _lipSliderStartValues[control.Key] = EditorPrefs.GetFloat($"{LipSliderStartPrefsPrefix}{control.Key}", 0f);
                _lipSliderEndValues[control.Key] = EditorPrefs.GetFloat($"{LipSliderEndPrefsPrefix}{control.Key}", control.Max);
            }
        }

        private void SyncRuntimeReplyText(NyxaraCompanionBrain brain)
        {
            if (brain == null)
            {
                _runtimeReplyText = string.Empty;
                return;
            }

            _runtimeReplyText = brain.LastReply ?? string.Empty;
        }

        private void DrawRuntimeReplyOutputSection(NyxaraCompanionBrain brain, PiperTtsService ttsService)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Runtime Reply Output", EditorStyles.miniBoldLabel);

            if (brain == null)
            {
                EditorGUILayout.HelpBox("No NyxaraCompanionBrain found in the active studio root, so live reply text cannot be shown here yet.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var voiceState = ttsService != null && ttsService.IsConfigured
                ? "Voice output available"
                : "Voice output unavailable or disabled";
            EditorGUILayout.HelpBox($"Reply text is always the primary output. {voiceState}; TTS is optional and uses the same reply text only when voice is enabled.", MessageType.None);

            if (string.IsNullOrWhiteSpace(_runtimeReplyText))
            {
                EditorGUILayout.LabelField("Last Reply Text", "<no reply yet>");
            }
            else
            {
                EditorGUILayout.LabelField("Last Reply Text", EditorStyles.miniBoldLabel);
                EditorGUILayout.TextArea(_runtimeReplyText, GUILayout.MinHeight(60f));
            }

            EditorGUILayout.EndVertical();
        }

        private bool ApplyLipSliderRange(string controlKey, float startValue, float endValue)
        {
            var changed = false;
            if (!_lipSliderStartValues.TryGetValue(controlKey, out var currentStart) || Math.Abs(currentStart - startValue) > 0.001f)
            {
                _lipSliderStartValues[controlKey] = startValue;
                changed = true;
            }

            if (!_lipSliderEndValues.TryGetValue(controlKey, out var currentEnd) || Math.Abs(currentEnd - endValue) > 0.001f)
            {
                _lipSliderEndValues[controlKey] = endValue;
                changed = true;
            }

            return changed;
        }

        private static bool ApplyIfChanged(ref float field, float nextValue)
        {
            if (Math.Abs(field - nextValue) <= 0.001f)
            {
                return false;
            }

            field = nextValue;
            return true;
        }

        private static bool ApplyIfChanged(ref bool field, bool nextValue)
        {
            if (field == nextValue)
            {
                return false;
            }

            field = nextValue;
            return true;
        }

        private static StudioTab LoadPersistedStudioTab()
        {
            var value = EditorPrefs.GetInt(StudioTabPrefsKey, (int)StudioTab.Studio);
            return Enum.IsDefined(typeof(StudioTab), value) ? (StudioTab)value : StudioTab.Studio;
        }

        private static DiagnosticsTab LoadPersistedDiagnosticsTab()
        {
            var value = EditorPrefs.GetInt(DiagnosticsTabPrefsKey, (int)DiagnosticsTab.SystemScan);
            return Enum.IsDefined(typeof(DiagnosticsTab), value) ? (DiagnosticsTab)value : DiagnosticsTab.SystemScan;
        }

        private static void ResetStudioRigDefaults(AICompanionStudioConfig config)
        {
            config.createStudioEnvironment = true;
            config.createStudioCamera = true;
            config.createStudioLights = true;
            config.createSceneInstance = true;
            config.saveRootPrefab = true;
            config.createProfileIfMissing = true;
            config.autoAttachBootstrap = true;
            config.enableRuntimeConversationOverlay = true;
            config.showRuntimeConversationOverlay = true;
            config.enableSmartVoiceCapture = false;
            config.runtimeMicHoldKey = KeyCode.V;
            config.runtimePromptPopupKey = KeyCode.T;

            config.characterLocalPosition = Vector3.zero;
            config.characterLocalEuler = Vector3.zero;
            config.focusHeightOffset = 1.55f;
            config.cameraPivotOffset = Vector3.zero;
            config.cameraDistance = 1.35f;
            config.cameraHeight = 1.6f;
            config.cameraYaw = 0f;
            config.cameraFieldOfView = 26f;

            config.keyLightIntensity = 2.2f;
            config.fillLightIntensity = 0.9f;
            config.rimLightIntensity = 1.35f;
            config.studioBackgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);
        }

        private static AICompanionStudioConfig LoadOrCreateConfig()
        {
            AssetDatabase.Refresh();
            var config = LoadKnownConfigAsset();
            if (config == null)
            {
                config = FindAnyStudioConfigAsset();
            }

            if (config != null)
            {
                ApplyDefaultPathsIfEmpty(config);
                return config;
            }

            EnsureConfigAssetFolder();
            config = ScriptableObject.CreateInstance<AICompanionStudioConfig>();
            ApplyDefaultPathsIfEmpty(config);
            var configParentFolder = GetAssetParentFolder(DefaultConfigPath);
            var configParentDirectory = GetAbsolutePathFromAssetPath(configParentFolder);
            var parentFolderReady = !string.IsNullOrWhiteSpace(configParentFolder) &&
                (AssetDatabase.IsValidFolder(configParentFolder) ||
                 (!string.IsNullOrWhiteSpace(configParentDirectory) && Directory.Exists(configParentDirectory)));
            if (!parentFolderReady)
            {
                throw new InvalidOperationException($"Nyxara Studio could not create the config folder at {configParentFolder}.");
            }

            AssetDatabase.CreateAsset(config, DefaultConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static AICompanionStudioConfig LoadKnownConfigAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<AICompanionStudioConfig>(DefaultConfigPath);
            if (config == null)
            {
                config = AssetDatabase.LoadAssetAtPath<AICompanionStudioConfig>(LegacyRootConfigPath);
            }

            if (config == null)
            {
                config = AssetDatabase.LoadAssetAtPath<AICompanionStudioConfig>(LegacyDefaultConfigPath);
            }

            return config;
        }

        private static AICompanionStudioConfig FindAnyStudioConfigAsset()
        {
            var configGuids = AssetDatabase.FindAssets("t:AICompanionStudioConfig");
            foreach (var guid in configGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                var config = AssetDatabase.LoadAssetAtPath<AICompanionStudioConfig>(assetPath);
                if (config != null)
                {
                    return config;
                }
            }

            return null;
        }

        private static void EnsureConfigAssetFolder()
        {
            EnsureAssetFolderPath(DefaultStudioRootFolder);
            EnsureAssetFolderPath(DefaultGeneratedFolder);
            var absoluteGeneratedFolder = GetAbsolutePathFromAssetPath(DefaultGeneratedFolder);
            if (!string.IsNullOrWhiteSpace(absoluteGeneratedFolder))
            {
                Directory.CreateDirectory(absoluteGeneratedFolder);
            }

            AssetDatabase.Refresh();
        }

        private static AICompanionStudioConfig CreateTemporaryConfig()
        {
            var config = ScriptableObject.CreateInstance<AICompanionStudioConfig>();
            ApplyDefaultPathsIfEmpty(config);
            return config;
        }

        private static void ApplyDefaultPathsIfEmpty(AICompanionStudioConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.llmModelPath))
            {
                config.llmModelPath = CompanionStackDefaults.QwenModelPath;
            }

            if (string.IsNullOrWhiteSpace(config.whisperModelRelativePath))
            {
                config.whisperModelRelativePath = CompanionStackDefaults.WhisperModelRelativePath;
            }

            var normalizedRoot = NormalizeLegacyStudioRoot(config.rootFolder);
            if (!string.Equals(normalizedRoot, config.rootFolder, StringComparison.Ordinal))
            {
                config.rootFolder = normalizedRoot;
            }

            if (string.IsNullOrWhiteSpace(config.rootFolder))
            {
                config.rootFolder = DefaultStudioRootFolder;
            }

            config.generatedFolder = NormalizeLegacyStudioSubfolder(config.generatedFolder, "Generated");
            if (string.IsNullOrWhiteSpace(config.generatedFolder))
            {
                config.generatedFolder = DefaultGeneratedFolder;
            }

            config.prefabFolder = NormalizeLegacyStudioSubfolder(config.prefabFolder, "Prefabs");
            if (string.IsNullOrWhiteSpace(config.prefabFolder))
            {
                config.prefabFolder = $"{config.generatedFolder}/Prefabs";
            }

            config.companionPrefabFolder = NormalizeLegacyStudioSubfolder(config.companionPrefabFolder, "Companions");
            if (string.IsNullOrWhiteSpace(config.companionPrefabFolder))
            {
                config.companionPrefabFolder = $"{config.generatedFolder}/Companions";
            }

            config.profileFolder = NormalizeLegacyStudioSubfolder(config.profileFolder, "Profiles");
            if (string.IsNullOrWhiteSpace(config.profileFolder))
            {
                config.profileFolder = $"{config.generatedFolder}/Profiles";
            }

            config.expressionFolder = NormalizeLegacyStudioSubfolder(config.expressionFolder, "Expressions");
            if (string.IsNullOrWhiteSpace(config.expressionFolder))
            {
                config.expressionFolder = $"{config.generatedFolder}/Expressions";
            }
        }

        private static void NormalizeOptionalDependencyState(AICompanionStudioConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (string.Equals(config.piperVoicePath, CompanionStackDefaults.PiperVoiceRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                var defaultVoicePath = Path.Combine(Application.streamingAssetsPath, CompanionStackDefaults.PiperVoiceRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(defaultVoicePath))
                {
                    config.piperVoicePath = string.Empty;
                }
            }

            if (!NyxaraIntegrationValidator.HasInProcessPiperAssets() &&
                (string.IsNullOrWhiteSpace(config.piperExecutablePath) || string.IsNullOrWhiteSpace(config.piperVoicePath)))
            {
                config.ttsEnabled = false;
            }
        }

        private static string NormalizeLegacyStudioRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                string.Equals(path, DefaultGeneratedFolder, StringComparison.Ordinal) ||
                string.Equals(path, "Assets/Nyxara Ai Studio/AICompanionStudio", StringComparison.Ordinal) ||
                string.Equals(path, "Assets/Nyxara AI Studio/AICompanionStudio", StringComparison.Ordinal) ||
                string.Equals(path, "Assets/NyxaraAIStudio", StringComparison.Ordinal) ||
                string.Equals(path, "Assets/AICompanionStudio", StringComparison.Ordinal) ||
                !IsInsidePackageRoot(path))
            {
                return DefaultStudioRootFolder;
            }

            return path;
        }

        private static string NormalizeLegacyStudioSubfolder(string path, string childFolder)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (string.Equals(path, $"Assets/NyxaraAIStudio/{childFolder}", StringComparison.Ordinal) ||
                string.Equals(path, $"Assets/AICompanionStudio/{childFolder}", StringComparison.Ordinal) ||
                string.Equals(path, $"Assets/Nyxara Ai Studio/AICompanionStudio/{childFolder}", StringComparison.Ordinal) ||
                string.Equals(path, $"Assets/Nyxara AI Studio/AICompanionStudio/{childFolder}", StringComparison.Ordinal) ||
                !IsInsidePackageRoot(path))
            {
                return childFolder == "Generated"
                    ? DefaultGeneratedFolder
                    : $"{DefaultGeneratedFolder}/{childFolder}";
            }

            return path;
        }

        private static bool IsInsidePackageRoot(string path)
        {
            return string.Equals(path, DefaultStudioRootFolder, StringComparison.Ordinal) ||
                   string.Equals(path, DefaultGeneratedFolder, StringComparison.Ordinal) ||
                   path.StartsWith(DefaultStudioRootFolder + "/", StringComparison.Ordinal);
        }

        private static void EnsureAssetFolderPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var normalizedPath = assetPath.Replace('\\', '/').Trim('/');
            var segments = normalizedPath.Split('/');
            if (segments.Length < 2 || !string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var current = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var createdGuid = AssetDatabase.CreateFolder(current, segments[i]);
                    var createdPath = AssetDatabase.GUIDToAssetPath(createdGuid);
                    if (!string.IsNullOrWhiteSpace(createdPath))
                    {
                        current = createdPath;
                        continue;
                    }
                }

                current = next;
            }
        }

        private static string GetAssetParentFolder(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            var normalizedPath = assetPath.Replace('\\', '/');
            var lastSlashIndex = normalizedPath.LastIndexOf('/');
            return lastSlashIndex > 0 ? normalizedPath.Substring(0, lastSlashIndex) : string.Empty;
        }

        private static string GetAbsolutePathFromAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            var normalizedPath = assetPath.Replace('\\', '/');
            if (!normalizedPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var relativePath = normalizedPath.Length == "Assets".Length
                ? string.Empty
                : normalizedPath.Substring("Assets".Length).TrimStart('/');
            return string.IsNullOrWhiteSpace(relativePath)
                ? Application.dataPath
                : Path.Combine(Application.dataPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private void EnsureTestingAssetsLoaded()
        {
            if (_testingVoiceClip == null)
            {
                _testingVoiceClip = LoadDefaultTestingVoiceClip();
            }
        }

        private static AudioClip LoadDefaultTestingVoiceClip()
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AICompanion/Audio/Testing Audio/Nyxara Testing Ai Voice.wav");
        }

        private static List<RendererEntry> GetRendererEntries(GameObject sourcePrefab)
        {
            var entries = new List<RendererEntry>();
            if (sourcePrefab == null)
            {
                return entries;
            }

            foreach (var renderer in sourcePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                entries.Add(new RendererEntry
                {
                    Path = GetRelativePath(renderer.transform, sourcePrefab.transform),
                    Label = $"{renderer.name} ({renderer.sharedMesh?.name ?? "No Mesh"})"
                });
            }

            return entries;
        }

        private static string GetRelativePath(Transform current, Transform root)
        {
            if (current == null || root == null || current == root)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var walker = current;
            while (walker != null && walker != root)
            {
                names.Add(walker.name);
                walker = walker.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private void EnsureBuilderState(ArkItBlendshapeDriver faceDriver, ExpressionLibraryManager expressionLibrary)
        {
            if (_expressionRenderer == null && faceDriver != null && faceDriver.TargetRenderer != null)
            {
                _expressionRenderer = faceDriver.TargetRenderer;
            }

            RefreshBuilderBlendshapeOptions();
            if (_builderBlendshapeMap.Count == 0)
            {
                AutoDetectBuilderBlendshapes();
            }

            if (_builderWeights.Count == 0)
            {
                PullWeightsFromFaceDriver(faceDriver);
            }

            if (_selectedExpressionPreset == null && expressionLibrary != null && expressionLibrary.LoadedPresets.Count > 0)
            {
                _selectedExpressionPreset = expressionLibrary.LoadedPresets[0];
            }
        }

        private void RefreshBuilderBlendshapeOptions()
        {
            _builderBlendshapeOptions.Clear();
            _builderBlendshapeOptions.Add("<None>");
            _builderBlendshapeOptions.AddRange(GetAvailableBuilderBlendshapeNames());
        }

        private void AutoDetectBuilderBlendshapes()
        {
            var detected = ExpressionBuilderHelper.AutoDetectBlendshapes(GetAvailableBuilderRenderers());
            foreach (var control in ExpressionBuilderHelper.GetDefaultControls())
            {
                _builderBlendshapeMap[control.key] = detected.TryGetValue(control.key, out var blendshapeName) ? blendshapeName : string.Empty;
                if (!_builderWeights.ContainsKey(control.key))
                {
                    _builderWeights[control.key] = 0f;
                }
            }
        }

        private void DrawFaceProfilePanel(
            GameObject studioRoot,
            ExpressionLibraryManager expressionLibrary,
            ArkItBlendshapeDriver faceDriver)
        {
            var renderers = GetAvailableBuilderRenderers();
            var blendshapeNames = ExpressionBuilderHelper.GetBlendshapeNames(renderers);
            var profiles = ExpressionBuilderHelper.DetectCompatibilityProfiles(blendshapeNames);
            var profileSummary = profiles.Count > 0 ? string.Join(", ", profiles) : "Custom/Unknown";
            var rendererSummary = renderers.Count > 0
                ? string.Join(", ", renderers.Select(renderer => renderer != null ? renderer.name : "<Missing>"))
                : "No compatible face renderers found";

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Face Profile", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"Detected profile: {profileSummary}", MessageType.Info);
            EditorGUILayout.LabelField("Renderers", rendererSummary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Expression Library", expressionLibrary != null ? expressionLibrary.ExpressionLibraryPath : "Missing in scene");

            if (renderers.Count == 0)
            {
                EditorGUILayout.HelpBox("Assign or auto-detect a face renderer first so the compatibility tools can inspect the model.", MessageType.Warning);
            }
            else
            {
                var recommendedPreset = profiles.Contains("CC/Reallusion", StringComparer.OrdinalIgnoreCase)
                    ? "CC/Reallusion"
                    : profiles.Contains("VRM", StringComparer.OrdinalIgnoreCase)
                        ? "VRM"
                    : profiles.Contains("Viseme/VTuber", StringComparer.OrdinalIgnoreCase)
                        ? "Viseme/VTuber"
                        : profiles.Contains("Unreal/MetaHuman-like", StringComparer.OrdinalIgnoreCase)
                            ? "Unreal/MetaHuman-like"
                            : profiles.Contains("ARKit", StringComparer.OrdinalIgnoreCase)
                                ? "ARKit"
                                : "Custom/Unknown";

                EditorGUILayout.LabelField("Recommended Preset", recommendedPreset);
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = renderers.Count > 0;
            if (GUILayout.Button("Auto-Apply Detected Face Preset"))
            {
                SyncExpressionLibraryToDetectedProfile(expressionLibrary, faceDriver, true);
                AutoDetectBuilderBlendshapes();
                PullWeightsFromFaceDriver(faceDriver);
                expressionLibrary?.ResetToNeutral();
                Debug.Log($"[Nyxara Face Profile] Applied detected face preset: {profileSummary}");
                Repaint();
            }

            if (GUILayout.Button("Generate Profile Lip Sync"))
            {
                GenerateDetectedProfileLipSyncMapping();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = studioRoot != null;
            if (GUILayout.Button("Log Live Face Driver Targets"))
            {
                LogLiveFaceDriverTargets(studioRoot, expressionLibrary, faceDriver);
            }
            GUI.enabled = true;

            if (GUILayout.Button("Log Face Blendshape Report"))
            {
                LogFaceBlendshapeReport(studioRoot, expressionLibrary);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void SyncExpressionLibraryToDetectedProfile(
            ExpressionLibraryManager expressionLibrary,
            ArkItBlendshapeDriver faceDriver,
            bool logChange)
        {
            if (_config == null || expressionLibrary == null || faceDriver == null || faceDriver.TargetRenderer == null)
            {
                return;
            }

            var resolvedPath = NyxaraCompanionStudioBuilder.ResolveExpressionLibraryPath(
                _config,
                faceDriver.TargetRenderer,
                faceDriver.AdditionalRenderers);
            if (string.IsNullOrWhiteSpace(resolvedPath) ||
                string.Equals(expressionLibrary.ExpressionLibraryPath, resolvedPath, StringComparison.Ordinal))
            {
                return;
            }

            expressionLibrary.SetExpressionLibraryPath(resolvedPath);
            EditorUtility.SetDirty(expressionLibrary);
            AssetDatabase.SaveAssets();
            if (expressionLibrary.LoadedPresets.Count > 0)
            {
                if (_selectedExpressionPreset == null || !expressionLibrary.LoadedPresets.Contains(_selectedExpressionPreset))
                {
                    _selectedExpressionPreset = expressionLibrary.LoadedPresets[0];
                }
            }
            else
            {
                _selectedExpressionPreset = null;
            }

            if (logChange)
            {
                var profile = NyxaraCompanionStudioBuilder.DetectPrimaryFaceProfile(
                    faceDriver.TargetRenderer,
                    faceDriver.AdditionalRenderers);
                Debug.Log($"[Nyxara Face Profile] Expression library switched to '{resolvedPath}' for profile '{profile}'.");
            }
        }

        private void GenerateDetectedProfileLipSyncMapping()
        {
            var renderers = GetAvailableBuilderRenderers();
            if (renderers.Count == 0)
            {
                Debug.LogWarning("[Nyxara LipSync] No renderers available for compatibility mapping.");
                return;
            }

            if (_lipSyncData == null && _config != null)
            {
                _lipSyncData = NyxaraCompanionStudioBuilder.EnsureLipSyncDataForEditor(_config);
            }

            if (_lipSyncData == null)
            {
                Debug.LogWarning("[Nyxara LipSync] No LipSyncData asset available.");
                return;
            }

            var detected = ExpressionBuilderHelper.AutoDetectBlendshapes(renderers);
            var profiles = ExpressionBuilderHelper.DetectCompatibilityProfiles(ExpressionBuilderHelper.GetBlendshapeNames(renderers));
            _lipSyncData.visemeMappings = BuildCompatibilityVisemeMappings(detected, profiles);
            EditorUtility.SetDirty(_lipSyncData);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Nyxara LipSync] Generated compatibility lip sync mapping for profile(s): {string.Join(", ", profiles)}");
        }

        private static List<VisemeMapping> BuildCompatibilityVisemeMappings(IReadOnlyDictionary<string, string> detected, IReadOnlyCollection<string> profiles)
        {
            string Join(params string[] keys)
            {
                var names = keys
                    .Where(key => detected.TryGetValue(key, out _))
                    .Select(key => detected[key])
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return string.Join(", ", names);
            }

            string Pick(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (detected.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return string.Empty;
            }

            var useVrmProfile = profiles != null && profiles.Contains("VRM", StringComparer.OrdinalIgnoreCase);
            if (useVrmProfile)
            {
                return new List<VisemeMapping>
                {
                    new() { viseme = Viseme.AA, blendshapeName = Pick("jawOpen"), intensity = 78f, jawOpenContribution = 0.95f },
                    new() { viseme = Viseme.AH, blendshapeName = Pick("jawOpen"), intensity = 72f, jawOpenContribution = 0.9f },
                    new() { viseme = Viseme.IY, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight", "mouthSmileLeft", "mouthSmileRight"), intensity = 70f, jawOpenContribution = 0.08f },
                    new() { viseme = Viseme.IH, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight"), intensity = 54f, jawOpenContribution = 0.06f },
                    new() { viseme = Viseme.UH, blendshapeName = Pick("mouthPucker"), intensity = 68f, jawOpenContribution = 0.08f },
                    new() { viseme = Viseme.OW, blendshapeName = Join("mouthFunnel", "mouthPucker"), intensity = 72f, jawOpenContribution = 0.14f },
                    new() { viseme = Viseme.AO, blendshapeName = Join("mouthFunnel", "mouthPucker"), intensity = 64f, jawOpenContribution = 0.12f },
                    new() { viseme = Viseme.AW, blendshapeName = Join("jawOpen", "mouthFunnel"), intensity = 62f, jawOpenContribution = 0.32f },
                    new() { viseme = Viseme.OY, blendshapeName = Join("mouthFunnel", "mouthStretchLeft", "mouthStretchRight"), intensity = 54f, jawOpenContribution = 0.1f },
                    new() { viseme = Viseme.W, blendshapeName = Pick("mouthPucker"), intensity = 60f, jawOpenContribution = 0.05f },
                    new() { viseme = Viseme.EH, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight"), intensity = 56f, jawOpenContribution = 0.08f },
                    new() { viseme = Viseme.ER, blendshapeName = Join("mouthFunnel", "mouthStretchLeft", "mouthStretchRight"), intensity = 42f, jawOpenContribution = 0.08f },
                    new() { viseme = Viseme.FV, blendshapeName = Pick("mouthClose"), intensity = 40f, jawOpenContribution = 0.02f },
                    new() { viseme = Viseme.TH, blendshapeName = Pick("jawOpen"), intensity = 28f, jawOpenContribution = 0.25f },
                    new() { viseme = Viseme.DH, blendshapeName = Pick("jawOpen"), intensity = 24f, jawOpenContribution = 0.2f },
                    new() { viseme = Viseme.SZ, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight"), intensity = 34f, jawOpenContribution = 0.02f },
                    new() { viseme = Viseme.SH, blendshapeName = Join("mouthPucker", "mouthFunnel"), intensity = 40f, jawOpenContribution = 0.04f },
                    new() { viseme = Viseme.HH, blendshapeName = Pick("jawOpen"), intensity = 20f, jawOpenContribution = 0.2f },
                    new() { viseme = Viseme.M, blendshapeName = Pick("mouthClose"), intensity = 68f, jawOpenContribution = 0f },
                    new() { viseme = Viseme.BPM, blendshapeName = Pick("mouthClose"), intensity = 72f, jawOpenContribution = 0f },
                    new() { viseme = Viseme.N, blendshapeName = Pick("mouthClose"), intensity = 36f, jawOpenContribution = 0.04f },
                    new() { viseme = Viseme.NG, blendshapeName = Pick("mouthClose"), intensity = 30f, jawOpenContribution = 0.06f },
                    new() { viseme = Viseme.L, blendshapeName = Pick("jawOpen"), intensity = 24f, jawOpenContribution = 0.16f },
                    new() { viseme = Viseme.R, blendshapeName = Join("mouthPucker", "jawOpen"), intensity = 34f, jawOpenContribution = 0.12f },
                    new() { viseme = Viseme.Y, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight", "mouthSmileLeft", "mouthSmileRight"), intensity = 40f, jawOpenContribution = 0.05f },
                    new() { viseme = Viseme.DT, blendshapeName = Pick("mouthClose"), intensity = 22f, jawOpenContribution = 0.04f },
                    new() { viseme = Viseme.GK, blendshapeName = Pick("jawOpen"), intensity = 24f, jawOpenContribution = 0.15f },
                    new() { viseme = Viseme.sil, blendshapeName = Pick("mouthClose"), intensity = 0f, jawOpenContribution = 0f }
                };
            }

            return new List<VisemeMapping>
            {
                new() { viseme = Viseme.AA, blendshapeName = Pick("jawOpen"), intensity = 27.9f, jawOpenContribution = 1f },
                new() { viseme = Viseme.AH, blendshapeName = Join("jawOpen", "mouthLowerDownLeft", "mouthLowerDownRight"), intensity = 24f, jawOpenContribution = 0.85f },
                new() { viseme = Viseme.IY, blendshapeName = Join("mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight"), intensity = 40f, jawOpenContribution = 0.15f },
                new() { viseme = Viseme.IH, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight", "mouthSmileLeft", "mouthSmileRight"), intensity = 28f, jawOpenContribution = 0.12f },
                new() { viseme = Viseme.UH, blendshapeName = Pick("mouthPucker"), intensity = 80f, jawOpenContribution = 0.2f },
                new() { viseme = Viseme.OW, blendshapeName = Join("mouthFunnel", "mouthPucker"), intensity = 59.4f, jawOpenContribution = 0.3f },
                new() { viseme = Viseme.AO, blendshapeName = Join("mouthFunnel", "mouthPucker"), intensity = 52f, jawOpenContribution = 0.25f },
                new() { viseme = Viseme.AW, blendshapeName = Join("mouthFunnel", "mouthPucker", "jawOpen"), intensity = 50f, jawOpenContribution = 0.45f },
                new() { viseme = Viseme.OY, blendshapeName = Join("mouthFunnel", "mouthSmileLeft", "mouthSmileRight"), intensity = 42f, jawOpenContribution = 0.2f },
                new() { viseme = Viseme.W, blendshapeName = Pick("mouthPucker"), intensity = 62f, jawOpenContribution = 0.08f },
                new() { viseme = Viseme.EH, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight", "mouthDimpleLeft", "mouthDimpleRight"), intensity = 35f, jawOpenContribution = 0.15f },
                new() { viseme = Viseme.ER, blendshapeName = Join("mouthFunnel", "mouthStretchLeft", "mouthStretchRight"), intensity = 30f, jawOpenContribution = 0.12f },
                new() { viseme = Viseme.FV, blendshapeName = Join("mouthPressLeft", "mouthPressRight"), intensity = 30f, jawOpenContribution = 0.05f },
                new() { viseme = Viseme.TH, blendshapeName = Join("tongueOut", "jawOpen"), intensity = 24f, jawOpenContribution = 0.35f },
                new() { viseme = Viseme.DH, blendshapeName = Join("tongueOut", "jawOpen"), intensity = 18f, jawOpenContribution = 0.25f },
                new() { viseme = Viseme.SZ, blendshapeName = Join("mouthStretchLeft", "mouthStretchRight"), intensity = 20f, jawOpenContribution = 0.06f },
                new() { viseme = Viseme.SH, blendshapeName = Join("mouthPucker", "mouthFunnel"), intensity = 26f, jawOpenContribution = 0.08f },
                new() { viseme = Viseme.HH, blendshapeName = Pick("jawOpen"), intensity = 12f, jawOpenContribution = 0.3f },
                new() { viseme = Viseme.M, blendshapeName = Pick("mouthClose"), intensity = 36.7f, jawOpenContribution = 0f },
                new() { viseme = Viseme.BPM, blendshapeName = Pick("mouthClose"), intensity = 42f, jawOpenContribution = 0f },
                new() { viseme = Viseme.N, blendshapeName = Join("mouthClose", "jawOpen"), intensity = 18f, jawOpenContribution = 0.1f },
                new() { viseme = Viseme.NG, blendshapeName = Join("mouthClose", "jawOpen"), intensity = 16f, jawOpenContribution = 0.14f },
                new() { viseme = Viseme.L, blendshapeName = Join("tongueOut", "jawOpen"), intensity = 16f, jawOpenContribution = 0.18f },
                new() { viseme = Viseme.R, blendshapeName = Join("mouthPucker", "jawOpen"), intensity = 20f, jawOpenContribution = 0.14f },
                new() { viseme = Viseme.Y, blendshapeName = Join("mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight"), intensity = 24f, jawOpenContribution = 0.08f },
                new() { viseme = Viseme.DT, blendshapeName = Join("jawOpen", "mouthClose"), intensity = 12f, jawOpenContribution = 0.08f },
                new() { viseme = Viseme.GK, blendshapeName = Pick("jawOpen"), intensity = 16f, jawOpenContribution = 0.18f },
                new() { viseme = Viseme.sil, blendshapeName = Pick("mouthClose"), intensity = 0f, jawOpenContribution = 0f }
            };
        }

        private void PullWeightsFromFaceDriver(ArkItBlendshapeDriver faceDriver)
        {
            foreach (var control in ExpressionBuilderHelper.GetDefaultControls())
            {
                if (!_builderBlendshapeMap.TryGetValue(control.key, out var blendshapeName) || string.IsNullOrWhiteSpace(blendshapeName))
                {
                    _builderWeights[control.key] = 0f;
                    continue;
                }

                _builderWeights[control.key] = faceDriver != null ? faceDriver.GetBlendshapeWeight(blendshapeName) : GetRendererBlendshapeWeight(blendshapeName);
            }
        }

        private void ResetBuilderWeights()
        {
            foreach (var control in ExpressionBuilderHelper.GetDefaultControls())
            {
                _builderWeights[control.key] = 0f;
            }
        }

        private void LoadPresetIntoBuilder(ExpressionPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            _builderPresetName = preset.displayName;
            _builderDescription = preset.description;
            _builderCategory = preset.category;
            _builderTransitionTime = preset.transitionTimeInSeconds;

            if (_builderBlendshapeMap.Count == 0)
            {
                AutoDetectBuilderBlendshapes();
            }

            var loadedWeights = ExpressionBuilderHelper.LoadControlWeightsFromPreset(preset, _builderBlendshapeMap);
            foreach (var pair in loadedWeights)
            {
                _builderWeights[pair.Key] = pair.Value;
            }
        }

        private void BuildExpressionPreset(ExpressionLibraryManager expressionLibrary)
        {
            if (expressionLibrary == null)
            {
                EditorUtility.DisplayDialog("Expression Library Missing", "Add or build an ExpressionLibraryManager on the studio root first.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(_builderPresetName))
            {
                EditorUtility.DisplayDialog("Name Required", "Enter a name for the new expression before building it.", "OK");
                return;
            }

            var blendshapeWeights = ExpressionBuilderHelper.BuildBlendshapeWeights(
                _builderBlendshapeMap,
                _builderWeights,
                GetAvailableBuilderBlendshapeNames());
            if (blendshapeWeights.Count == 0)
            {
                EditorUtility.DisplayDialog("No ARKit Weights", "Move at least one ARKit slider before building the expression.", "OK");
                return;
            }

            var preset = expressionLibrary.SavePreset(
                _builderPresetName,
                _builderCategory,
                _builderDescription,
                _builderTransitionTime,
                blendshapeWeights);

            if (preset != null)
            {
                _selectedExpressionPreset = preset;
                EnsureExpressionModeForEditing(ResolveStudioRootFromContext());
                expressionLibrary.ApplyPreset(preset);
                EditorGUIUtility.PingObject(preset);
            }
        }

        private void ApplyBuilderPreview(ExpressionLibraryManager expressionLibrary, ArkItBlendshapeDriver faceDriver)
        {
            EnsureExpressionModeForEditing(ResolveStudioRootFromContext());
            var blendshapeWeights = ExpressionBuilderHelper.BuildBlendshapeWeights(
                _builderBlendshapeMap,
                _builderWeights,
                GetAvailableBuilderBlendshapeNames());

            if (expressionLibrary != null)
            {
                expressionLibrary.ApplyExpressionWeights(blendshapeWeights);
                return;
            }

            if (faceDriver == null)
            {
                return;
            }

            foreach (var pair in blendshapeWeights)
            {
                faceDriver.TrySetBlendshapeWeight(pair.Key, pair.Value);
            }
        }

        private int GetBuilderPopupIndex(string selectedBlendshape)
        {
            if (string.IsNullOrWhiteSpace(selectedBlendshape))
            {
                return 0;
            }

            var index = _builderBlendshapeOptions.FindIndex(option => string.Equals(option, selectedBlendshape, StringComparison.Ordinal));
            return index >= 0 ? index : 0;
        }

        private float GetRendererBlendshapeWeight(string blendshapeName)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return 0f;
            }

            var maxWeight = 0f;
            foreach (var renderer in GetAvailableBuilderRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var index = renderer.sharedMesh.GetBlendShapeIndex(blendshapeName);
                if (index >= 0)
                {
                    maxWeight = Mathf.Max(maxWeight, renderer.GetBlendShapeWeight(index));
                }
            }

            return maxWeight;
        }

        private List<SkinnedMeshRenderer> GetAvailableBuilderRenderers()
        {
            var studioRoot = ResolveStudioRootFromContext();
            var renderers = new List<SkinnedMeshRenderer>();
            var expressionLibrary = studioRoot != null ? studioRoot.GetComponent<ExpressionLibraryManager>() : null;
            if (expressionLibrary?.TargetFaceRenderers != null)
            {
                foreach (var renderer in expressionLibrary.TargetFaceRenderers)
                {
                    if (renderer != null && !renderers.Contains(renderer))
                    {
                        renderers.Add(renderer);
                    }
                }
            }

            if (renderers.Count == 0 && _expressionRenderer != null)
            {
                renderers.Add(_expressionRenderer);
            }

            return renderers;
        }

        private List<string> GetAvailableBuilderBlendshapeNames()
        {
            return ExpressionBuilderHelper.GetBlendshapeNames(GetAvailableBuilderRenderers());
        }

        private void LogFaceBlendshapeReport(GameObject studioRoot, ExpressionLibraryManager expressionLibrary)
        {
            var renderers = new List<SkinnedMeshRenderer>();
            if (expressionLibrary?.TargetFaceRenderers != null)
            {
                renderers.AddRange(expressionLibrary.TargetFaceRenderers.Where(renderer => renderer != null));
            }

            if (renderers.Count == 0 && studioRoot != null)
            {
                renderers.AddRange(studioRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            }

            if (renderers.Count == 0)
            {
                Debug.LogWarning("[Nyxara Face Report] No SkinnedMeshRenderer found for expression diagnostics.");
                return;
            }

            Debug.Log($"[Nyxara Face Report] Renderers={renderers.Count}");
            foreach (var renderer in renderers.Distinct())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var blendshapeNames = new List<string>();
                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    var name = renderer.sharedMesh.GetBlendShapeName(i);
                    if (NyxaraCompanionStudioWindow.ContainsFaceDebugKeyword(name))
                    {
                        blendshapeNames.Add(name);
                    }
                }

                var summary = blendshapeNames.Count > 0 ? string.Join(", ", blendshapeNames) : "No mouth/jaw-related blendshapes detected";
                Debug.Log($"[Nyxara Face Report] Renderer='{renderer.name}' Mesh='{renderer.sharedMesh.name}' Blendshapes: {summary}", renderer);
            }
        }

        private void LogLiveFaceDriverTargets(GameObject studioRoot, ExpressionLibraryManager expressionLibrary, ArkItBlendshapeDriver faceDriver)
        {
            if (studioRoot == null)
            {
                Debug.LogWarning("[Nyxara Face Debug] No studio root found.");
                return;
            }

            var lipSyncController = studioRoot.GetComponent<VisemeLipSyncController>();
            var signalRouter = studioRoot.GetComponent<ExpressionSignalRouter>();

            var driverTarget = GetObjectReference<SkinnedMeshRenderer>(faceDriver, "targetRenderer");
            var driverAdditional = GetObjectReferenceList<SkinnedMeshRenderer>(faceDriver, "additionalRenderers");
            var expressionTarget = GetObjectReference<SkinnedMeshRenderer>(expressionLibrary, "targetFaceRenderer");
            var expressionAdditional = GetObjectReferenceList<SkinnedMeshRenderer>(expressionLibrary, "additionalFaceRenderers");
            var lipTarget = GetObjectReference<SkinnedMeshRenderer>(lipSyncController, "faceRenderer");
            var lipAdditional = GetObjectReferenceList<SkinnedMeshRenderer>(lipSyncController, "additionalFaceRenderers");
            var signalTarget = GetObjectReference<SkinnedMeshRenderer>(signalRouter, "targetRenderer");

            Debug.Log($"[Nyxara Face Debug] FaceDriver target={FormatRenderer(driverTarget)} | additional={FormatRenderers(driverAdditional)}");
            Debug.Log($"[Nyxara Face Debug] ExpressionLibrary target={FormatRenderer(expressionTarget)} | additional={FormatRenderers(expressionAdditional)}");
            Debug.Log($"[Nyxara Face Debug] LipSync target={FormatRenderer(lipTarget)} | additional={FormatRenderers(lipAdditional)}");
            Debug.Log($"[Nyxara Face Debug] SignalRouter target={FormatRenderer(signalTarget)}");

            if (faceDriver == null)
            {
                Debug.LogWarning("[Nyxara Face Debug] ArkItBlendshapeDriver missing.");
                return;
            }

            var jawApplied = faceDriver.TrySetBlendshapeWeight("jawOpen", 25f);
            var smileApplied = faceDriver.TrySetBlendshapeWeight("mouthSmileLeft", 20f);
            var jawReadback = faceDriver.GetBlendshapeWeight("jawOpen");
            var smileReadback = faceDriver.GetBlendshapeWeight("mouthSmileLeft");
            Debug.Log($"[Nyxara Face Debug] Test write jawOpen=25 applied={jawApplied} readback={jawReadback:0.0}");
            Debug.Log($"[Nyxara Face Debug] Test write mouthSmileLeft=20 applied={smileApplied} readback={smileReadback:0.0}");

            LogBlendshapeDeltaProbe(driverTarget, "jawOpen");
            LogBlendshapeDeltaProbe(driverTarget, "mouthSmileLeft");
        }

        private static bool ContainsFaceDebugKeyword(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.ToLowerInvariant();
            return normalized.Contains("mouth") ||
                   normalized.Contains("jaw") ||
                   normalized.Contains("lip") ||
                   normalized.Contains("smile") ||
                   normalized.Contains("frown") ||
                   normalized.Contains("open") ||
                   normalized.Contains("close") ||
                   normalized.Contains("funnel") ||
                   normalized.Contains("pucker") ||
                   normalized.Contains("viseme") ||
                   normalized.Contains("aa") ||
                   normalized.Contains("oh") ||
                   normalized.Contains("ou") ||
                   normalized.Contains("ee") ||
                   normalized.Contains("ih");
        }

        private GameObject ResolveStudioRootFromContext()
        {
            var selected = Selection.activeGameObject;
            while (selected != null)
            {
                if (selected.GetComponent<NyxaraCompanionBrain>() != null)
                {
                    return selected;
                }

                selected = selected.transform.parent != null ? selected.transform.parent.gameObject : null;
            }

            return GameObject.Find(_config != null ? _config.studioRootName : "NyxaraStudioRoot");
        }

        private void SyncTabContextFromStudioRoot(GameObject studioRoot)
        {
            if (studioRoot == null)
            {
                return;
            }

            var expressionLibrary = studioRoot.GetComponent<ExpressionLibraryManager>();
            if (expressionLibrary != null)
            {
                var targetRenderer = GetObjectReference<SkinnedMeshRenderer>(expressionLibrary, "targetFaceRenderer");
                if (targetRenderer != null)
                {
                    _expressionRenderer = targetRenderer;
                }

                if (expressionLibrary.LoadedPresets.Count == 0)
                {
                    expressionLibrary.LoadAllPresets();
                }

                if (_selectedExpressionPreset == null && expressionLibrary.LoadedPresets.Count > 0)
                {
                    _selectedExpressionPreset = expressionLibrary.LoadedPresets[0];
                }

                _expressionModeEnabled = expressionLibrary.ExpressionModeActive;
            }

            var lipSyncController = studioRoot.GetComponent<VisemeLipSyncController>();
            if (lipSyncController != null)
            {
                var lipRenderer = GetObjectReference<SkinnedMeshRenderer>(lipSyncController, "faceRenderer");
                if (lipRenderer != null)
                {
                    _expressionRenderer = lipRenderer;
                }

                var lipSyncData = GetObjectReference<LipSyncData>(lipSyncController, "lipSyncData");
                if (lipSyncData != null)
                {
                    _lipSyncData = lipSyncData;
                }
            }
        }

        private void ApplyExpressionModeToScene(GameObject studioRoot)
        {
            if (studioRoot == null)
            {
                return;
            }

            var faceDriver = studioRoot.GetComponent<ArkItBlendshapeDriver>();
            faceDriver?.SetExpressionMode(_expressionModeEnabled);

            var expressionLibrary = studioRoot.GetComponent<ExpressionLibraryManager>();
            expressionLibrary?.SetExpressionMode(_expressionModeEnabled);

            var signalRouter = studioRoot.GetComponent<ExpressionSignalRouter>();
            signalRouter?.SetExpressionMode(_expressionModeEnabled);

            var lipSyncController = studioRoot.GetComponent<VisemeLipSyncController>();
            lipSyncController?.SetExpressionMode(_expressionModeEnabled);
        }

        private void EnsureExpressionModeForEditing(GameObject studioRoot)
        {
            if (_expressionModeEnabled)
            {
                return;
            }

            _expressionModeEnabled = true;
            ApplyExpressionModeToScene(studioRoot);
            Repaint();
        }

        private void EnsureRuntimeMouthControl(GameObject studioRoot)
        {
            if (!_expressionModeEnabled)
            {
                return;
            }

            _expressionModeEnabled = false;
            ApplyExpressionModeToScene(studioRoot);
            Debug.Log("[Nyxara Test] Switched Expression Mode OFF so lip sync and runtime mouth control can drive the face.");
            Repaint();
        }

        private void EnsureRuntimeLipSyncProfile(GameObject studioRoot, bool logChange)
        {
            if (studioRoot == null)
            {
                return;
            }

            var faceDriver = studioRoot.GetComponent<ArkItBlendshapeDriver>();
            var lipSyncController = studioRoot.GetComponent<VisemeLipSyncController>();
            if (faceDriver == null || lipSyncController == null)
            {
                return;
            }

            var renderers = new List<SkinnedMeshRenderer>();
            if (faceDriver.TargetRenderer != null)
            {
                renderers.Add(faceDriver.TargetRenderer);
            }

            if (faceDriver.AdditionalRenderers != null)
            {
                renderers.AddRange(faceDriver.AdditionalRenderers.Where(renderer => renderer != null && !renderers.Contains(renderer)));
            }

            if (renderers.Count == 0)
            {
                return;
            }

            if (_lipSyncData == null && _config != null)
            {
                _lipSyncData = NyxaraCompanionStudioBuilder.EnsureLipSyncDataForEditor(_config);
            }

            if (_lipSyncData == null)
            {
                return;
            }

            var detected = ExpressionBuilderHelper.AutoDetectBlendshapes(renderers);
            var profiles = ExpressionBuilderHelper.DetectCompatibilityProfiles(ExpressionBuilderHelper.GetBlendshapeNames(renderers));
            var newMappings = BuildCompatibilityVisemeMappings(detected, profiles);
            if (newMappings.Count == 0)
            {
                return;
            }

            _lipSyncData.visemeMappings = newMappings;
            if (_lipSyncData.responseEnd <= _lipSyncData.responseStart)
            {
                _lipSyncData.responseStart = 0f;
                _lipSyncData.responseEnd = 1f;
            }

            if (_lipSyncData.responseFalloff <= 0f)
            {
                _lipSyncData.responseFalloff = 1.35f;
            }

            if (_lipSyncData.responseSmoothing <= 0f)
            {
                _lipSyncData.responseSmoothing = 12f;
            }

            EditorUtility.SetDirty(_lipSyncData);
            AssetDatabase.SaveAssets();

            if (logChange)
            {
                Debug.Log($"[Nyxara LipSync] Refreshed runtime lip sync mapping for profile(s): {string.Join(", ", profiles)}");
            }
        }

        private static string MakeStreamingAssetsRelative(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            try
            {
                var normalizedAbsolute = Path.GetFullPath(absolutePath.Trim().Trim('"'));
                var normalizedStreamingAssets = Path.GetFullPath(Application.streamingAssetsPath);
                if (normalizedAbsolute.StartsWith(normalizedStreamingAssets, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = normalizedAbsolute.Substring(normalizedStreamingAssets.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return relative.Replace('\\', '/');
                }

                return normalizedAbsolute.Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return absolutePath.Trim().Trim('"').Replace('\\', '/');
            }
        }

        private static string GetSafeBrowseDirectory(string configuredPath, string streamingAssetsSubfolder = null)
        {
            try
            {
                var resolvedPath = ResolveAbsoluteOrProjectPath(configuredPath);
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                {
                    if (File.Exists(resolvedPath))
                    {
                        var directory = Path.GetDirectoryName(resolvedPath);
                        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                        {
                            return directory;
                        }
                    }
                    else if (Directory.Exists(resolvedPath))
                    {
                        return resolvedPath;
                    }
                }
            }
            catch (ArgumentException)
            {
            }

            return string.IsNullOrWhiteSpace(streamingAssetsSubfolder)
                ? Application.streamingAssetsPath
                : Path.Combine(Application.streamingAssetsPath, streamingAssetsSubfolder);
        }

        private static bool TryMakeStreamingAssetsRelative(string absolutePath, out string relativePath)
        {
            relativePath = string.Empty;
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            if (!Path.IsPathRooted(absolutePath))
            {
                relativePath = absolutePath.Replace('\\', '/');
                return true;
            }

            try
            {
                var normalizedAbsolute = Path.GetFullPath(absolutePath.Trim().Trim('"'));
                var normalizedStreamingAssets = Path.GetFullPath(Application.streamingAssetsPath);
                if (!normalizedAbsolute.StartsWith(normalizedStreamingAssets, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                relativePath = normalizedAbsolute.Substring(normalizedStreamingAssets.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string GetSafeFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileName(path.Trim().Trim('"'));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static T GetObjectReference<T>(UnityEngine.Object source, string propertyName) where T : UnityEngine.Object
        {
            if (source == null)
            {
                return null;
            }

            var serializedObject = new SerializedObject(source);
            var property = serializedObject.FindProperty(propertyName);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static List<T> GetObjectReferenceList<T>(UnityEngine.Object source, string propertyName) where T : UnityEngine.Object
        {
            var results = new List<T>();
            if (source == null)
            {
                return results;
            }

            var serializedObject = new SerializedObject(source);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                return results;
            }

            for (var i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                if (element?.objectReferenceValue is T typed)
                {
                    results.Add(typed);
                }
            }

            return results;
        }

        private static string FormatRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
            {
                return "<none>";
            }

            var meshName = renderer.sharedMesh?.name ?? "No Mesh";
            var hierarchyPath = GetHierarchyPath(renderer.transform);
            return $"{renderer.name} ({meshName}) path={hierarchyPath} active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} visible={renderer.isVisible} id={renderer.GetInstanceID()}";
        }

        private static string FormatRenderers(IEnumerable<SkinnedMeshRenderer> renderers)
        {
            if (renderers == null)
            {
                return "<none>";
            }

            var names = renderers.Where(renderer => renderer != null).Select(FormatRenderer).ToList();
            return names.Count > 0 ? string.Join(", ", names) : "<none>";
        }

        private static void LogBlendshapeDeltaProbe(SkinnedMeshRenderer renderer, string blendshapeName)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogWarning($"[Nyxara Face Debug] Delta probe skipped for '{blendshapeName}': renderer missing.");
                return;
            }

            var resolvedName = ArkItBlendshapeDriver.ResolveBlendshapeCandidates(blendshapeName)
                .FirstOrDefault(candidate => renderer.sharedMesh.GetBlendShapeIndex(candidate) >= 0);
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                Debug.LogWarning($"[Nyxara Face Debug] Delta probe skipped for '{blendshapeName}': blendshape not found on {renderer.name}.");
                return;
            }

            var index = renderer.sharedMesh.GetBlendShapeIndex(resolvedName);

            var originalWeight = renderer.GetBlendShapeWeight(index);
            var baseMesh = new Mesh();
            var testMesh = new Mesh();

            try
            {
                renderer.SetBlendShapeWeight(index, 0f);
                renderer.BakeMesh(baseMesh);
                renderer.SetBlendShapeWeight(index, 100f);
                renderer.BakeMesh(testMesh);

                var baseVertices = baseMesh.vertices;
                var testVertices = testMesh.vertices;
                var sampleCount = Mathf.Min(baseVertices.Length, testVertices.Length);
                var movedVertices = 0;
                var maxDistance = 0f;

                for (var i = 0; i < sampleCount; i++)
                {
                    var distance = Vector3.Distance(baseVertices[i], testVertices[i]);
                    if (distance > 0.00001f)
                    {
                        movedVertices++;
                        if (distance > maxDistance)
                        {
                            maxDistance = distance;
                        }
                    }
                }

                Debug.Log($"[Nyxara Face Debug] Delta probe '{blendshapeName}' resolved='{resolvedName}' on {renderer.name}: vertices={sampleCount} moved={movedVertices} maxDelta={maxDistance:0.000000}");
            }
            finally
            {
                renderer.SetBlendShapeWeight(index, originalWeight);
                DestroyImmediate(baseMesh);
                DestroyImmediate(testMesh);
            }
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return "<none>";
            }

            var segments = new List<string>();
            var current = target;
            while (current != null)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private sealed class StudioSystemScanner
        {
            private readonly AICompanionStudioConfig _config;

            public StudioSystemScanner(AICompanionStudioConfig config)
            {
                _config = config;
            }

            public SystemDiagnosticsReport Scan()
            {
                var report = new SystemDiagnosticsReport();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var brain = FindFirstObjectByType<NyxaraCompanionBrain>();
                var llmAgent = FindOptionalComponent(null, "LLMAgent");
                var whisperInput = FindFirstObjectByType<WhisperMicrophoneInput>();
                var ttsService = FindFirstObjectByType<PiperTtsService>();
                var faceDriver = FindFirstObjectByType<ArkItBlendshapeDriver>();
                var lipSyncController = FindFirstObjectByType<VisemeLipSyncController>();
                var expressionLibrary = FindFirstObjectByType<ExpressionLibraryManager>();
                var ttsAvailabilityStatus = ttsService != null
                    ? ttsService.AvailabilityStatus
                    : PiperTtsService.EvaluateAvailabilityStatus(_config.ttsEnabled, _config.piperExecutablePath, _config.piperVoicePath);

                report.timestamp = DateTime.Now.ToString("HH:mm:ss");
                var llmOperational = brain != null && brain.IsLlmAvailable;
                var llmMessage = TryGetLlmModelPath(llmAgent, out var liveLlmPath)
                    ? liveLlmPath
                    : (llmOperational ? "Runtime is active" : "Missing LLM or model");
                report.llmStatus = CheckStatus("LLM (Qwen)", brain != null, llmOperational, llmMessage);
                report.sttStatus = CheckStatus("Player Voice Input (Whisper)", whisperInput != null, whisperInput != null && whisperInput.IsWhisperAvailable, whisperInput != null && whisperInput.IsWhisperAvailable ? "Configured" : "Missing WhisperManager");
                report.ttsStatus = CheckStatus("TTS (Piper)", ttsService != null || _config.ttsEnabled, ttsAvailabilityStatus != PiperTtsAvailabilityStatus.InvalidPath, PiperTtsService.GetStatusGuidance(ttsAvailabilityStatus));
                report.ttsStatus.stateLabel = ttsService != null && ttsService.HasInProcessVoice
                    ? "Ready (In-Process)"
                    : PiperTtsService.GetStatusLabel(ttsAvailabilityStatus);
                report.ttsStatus.affectsOverallHealth = false;
                report.faceStatus = CheckStatus("Face System", faceDriver != null, HasObjectReference(faceDriver, "targetRenderer"), faceDriver != null ? "Face driver present" : "Missing face driver");
                report.lipSyncStatus = CheckStatus("Lip Sync", lipSyncController != null, lipSyncController != null, lipSyncController != null ? "Installed" : "Optional component missing");
                report.expressionStatus = CheckStatus("Expression Library", expressionLibrary != null, expressionLibrary != null, expressionLibrary != null ? "Installed" : "Optional component missing");

                report.configIssues = new List<ConfigIssue>();
                report.findings = new List<DiagnosticFinding>();
                if (_config.sourceCharacterPrefab == null)
                {
                    report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Critical, component = "Studio", issue = "No source character assigned", suggestion = "Assign the model or prefab in the Studio tab" });
                }

                AppendLiveLlmDiagnostics(report, llmAgent);
                AppendSttDiagnostics(report, whisperInput);

                if (string.IsNullOrWhiteSpace(_config.preferredFaceRendererPath))
                {
                    report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Warning, component = "Studio", issue = "No preferred face renderer selected", suggestion = "Pick the head or face mesh from the renderer dropdown" });
                }

                if (faceDriver != null && !HasObjectReference(faceDriver, "targetRenderer"))
                {
                    report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Warning, component = "Face", issue = "Face driver has no targetRenderer assigned", suggestion = "Use Build Studio or Apply Rig To Selected Studio Root to wire the face renderer" });
                }

                if (FindFirstObjectByType<ExpressionSignalRouter>() == null)
                {
                    report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Warning, component = "Expression", issue = "No ExpressionSignalRouter in scene", suggestion = "Build the studio root again or add the component manually" });
                }
                else if (!HasObjectReference(FindFirstObjectByType<ExpressionSignalRouter>(), "targetRenderer"))
                {
                    report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Warning, component = "Expression", issue = "ExpressionSignalRouter is present but targetRenderer is not assigned", suggestion = "Use Build Studio or Apply Rig To Selected Studio Root to wire the face renderer" });
                }

                var expectsPiperRuntime = ttsAvailabilityStatus == PiperTtsAvailabilityStatus.Ready;
                if (expectsPiperRuntime && FindFirstObjectByType<PiperTTSPhonemeExtractor>() == null)
                {
                    report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Warning, component = "Lip Sync", issue = "No PiperTTSPhonemeExtractor in scene", suggestion = "Build the studio root again or add the component manually" });
                }
                else if (expectsPiperRuntime)
                {
                    var lipSync = FindFirstObjectByType<VisemeLipSyncController>();
                    if (lipSync != null)
                    {
                        if (!HasObjectReference(lipSync, "faceRenderer"))
                        {
                            report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Warning, component = "Lip Sync", issue = "VisemeLipSyncController has no faceRenderer assigned", suggestion = "Use Build Studio or Apply Rig To Selected Studio Root to wire the face renderer" });
                        }

                        if (!HasObjectReference(lipSync, "phonemeExtractor"))
                        {
                            report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Warning, component = "Lip Sync", issue = "VisemeLipSyncController has no phonemeExtractor assigned", suggestion = "Rebuild the studio root so Piper phoneme extraction can drive mouth motion" });
                        }
                    }
                }

                AppendFaceRendererDiagnostics(report, expressionLibrary, faceDriver);

                var prefabPath = $"{_config.prefabFolder}/{_config.characterName}_StudioRoot.prefab";
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath))
                {
                    report.configIssues.Add(new ConfigIssue { severity = IssueSeverity.Info, component = "Studio", issue = "Studio prefab has not been built yet", suggestion = "Run Build Studio to generate the studio root prefab" });
                }

                report.pathValidations = new List<PathValidation>
                {
                    ValidatePath("LLM Model", ResolveModelStatusPath(_config.llmModelPath), false),
                    ValidateStreamingAssetsPath("Whisper Model", _config.whisperModelRelativePath),
                    new PathValidation { name = "Source Character", path = _config.sourceCharacterPrefab != null ? AssetDatabase.GetAssetPath(_config.sourceCharacterPrefab) : "", exists = _config.sourceCharacterPrefab != null },
                    new PathValidation { name = "Studio Prefab", path = prefabPath, exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null }
                };

                if (ttsService != null && ttsService.HasInProcessVoice)
                {
                    var voiceAssetPath = AssetDatabase.GetAssetPath(ttsService.VoiceModel);
                    report.pathValidations.Add(new PathValidation
                    {
                        name = "Piper Voice (in-process)",
                        path = string.IsNullOrWhiteSpace(voiceAssetPath) ? ttsService.VoiceModel.name : voiceAssetPath,
                        exists = !string.IsNullOrWhiteSpace(voiceAssetPath)
                    });
                }
                else if (_config.ttsEnabled || !string.IsNullOrWhiteSpace(_config.piperExecutablePath) || !string.IsNullOrWhiteSpace(_config.piperVoicePath))
                {
                    report.pathValidations.Add(ValidatePath("Piper Executable", _config.piperExecutablePath, true));
                    report.pathValidations.Add(ValidatePath("Piper Voice", _config.piperVoicePath, true));
                }

                stopwatch.Stop();
                report.durationMs = stopwatch.ElapsedMilliseconds;
                return report;
            }

            private static void AppendLiveLlmDiagnostics(SystemDiagnosticsReport report, Component llmAgent)
            {
                if (report == null || !TryGetLlmRuntimeValues(llmAgent, out var contextSize, out var numThreads, out var numPredict, out var cachePrompt))
                {
                    return;
                }

                report.configIssues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Info,
                    component = "LLM",
                    issue = $"Live runtime config: contextSize={contextSize}, numPredict={numPredict}, numThreads={numThreads}, cachePrompt={(cachePrompt ? "on" : "off")}",
                    suggestion = "Use this to verify the active scene root is actually running the intended fast settings"
                });

                if (contextSize > 4096)
                {
                    report.configIssues.Add(new ConfigIssue
                    {
                        severity = IssueSeverity.Warning,
                        component = "LLM",
                        issue = $"Live contextSize is {contextSize}, which is heavier than the intended fast setting",
                        suggestion = "Run Apply Rig To Selected Studio Root or rebuild so the active LLM uses a 4096 context window"
                    });
                }

                if (numPredict > 96)
                {
                    report.configIssues.Add(new ConfigIssue
                    {
                        severity = IssueSeverity.Warning,
                        component = "LLM",
                        issue = $"Live numPredict is {numPredict}, which may slow replies",
                        suggestion = "Lower numPredict on the live root if you want faster spoken response turns"
                    });
                }
            }

            private static void AppendSttDiagnostics(SystemDiagnosticsReport report, WhisperMicrophoneInput whisperInput)
            {
                if (report == null || whisperInput == null)
                {
                    return;
                }

                report.sttStatus.details.Add($"Mic devices: {whisperInput.AvailableMicrophones.Length}");
                report.sttStatus.details.Add($"Resolved mic: {whisperInput.ActiveMicrophoneDevice ?? "<default>"}");
                report.sttStatus.details.Add($"Mic route: {whisperInput.LastResolvedMicrophoneRoute}");
                report.sttStatus.details.Add($"Capture mode: {whisperInput.CaptureModeLabel}");
                report.sttStatus.details.Add($"Capture failure mode: {whisperInput.LastCaptureFailureMode}");
                report.sttStatus.details.Add($"Last raw STT: {(!string.IsNullOrWhiteSpace(whisperInput.LastRawTranscript) ? whisperInput.LastRawTranscript : "<empty>")}");
                report.sttStatus.details.Add($"Last normalized STT: {(!string.IsNullOrWhiteSpace(whisperInput.LastNormalizedTranscript) ? whisperInput.LastNormalizedTranscript : "<empty>")}");
                report.sttStatus.details.Add($"Rejected transcript debug bypass: {(whisperInput.AllowRejectedTranscriptsInDebug ? "enabled" : "disabled")}");
                report.sttStatus.details.Add($"Music-like transcript bypass: {(whisperInput.AllowMusicLikeTranscriptsInDebug ? "enabled" : "disabled")}");
                report.sttStatus.details.Add($"Quiet rejection bypass: {(whisperInput.BypassQuietCaptureRejectionInDebug ? "enabled" : "disabled")}");
                report.sttStatus.details.Add($"Explicit mic selection: {(whisperInput.ForceExplicitMicrophoneSelection ? "enabled" : "disabled")}");
                report.sttStatus.details.Add($"Speech gate: RMS >= {whisperInput.MinRmsForSpeech:0.0000}, Peak >= {whisperInput.MinPeakForSpeech:0.0000}, PreGain {whisperInput.PreTranscriptionGain:0.00}x");
                report.sttStatus.details.Add($"Transcript filters: Music {(whisperInput.RejectMusicLikeTranscripts ? "enabled" : "disabled")} | Bracket tags {(whisperInput.RejectBracketedNonSpeechTags ? "enabled" : "disabled")}");
                report.sttStatus.details.Add($"Last audio stats: {whisperInput.LastRecordingDurationSeconds:0.00}s | RMS {whisperInput.LastRecordingRms:0.0000} | Peak {whisperInput.LastRecordingPeak:0.0000} | Gain {whisperInput.LastAppliedPreampGain:0.00}x");
                report.sttStatus.details.Add($"Last forwarding decision: {whisperInput.LastForwardingDecision}");
                report.sttStatus.recommendedAction = whisperInput.GetLikelySpeechIssue();

                if (whisperInput.AvailableMicrophones.Length == 0)
                {
                    report.findings.Add(new DiagnosticFinding
                    {
                        severity = DiagnosticSeverity.Error,
                        title = "No Microphone Devices",
                        message = "Unity does not currently detect any microphone input devices for Whisper."
                    });
                    return;
                }

                if (whisperInput.AvailableMicrophones.Length == 1)
                {
                    report.findings.Add(new DiagnosticFinding
                    {
                        severity = DiagnosticSeverity.Info,
                        title = "Single Player Mic Visible",
                        message = "Unity currently sees one player microphone device. If speech is still missed, verify the Windows default input route and microphone privacy permissions."
                    });
                }

                if (whisperInput.ForceExplicitMicrophoneSelection)
                {
                    report.findings.Add(new DiagnosticFinding
                    {
                        severity = DiagnosticSeverity.Info,
                        title = "Using Explicit Mic",
                        message = whisperInput.LastResolvedMicrophoneRoute
                    });
                }

                if (!string.IsNullOrWhiteSpace(whisperInput.LastRawTranscript))
                {
                    report.findings.Add(new DiagnosticFinding
                    {
                        severity = DiagnosticSeverity.Warning,
                        title = "Recent STT Diagnosis",
                        message = $"{whisperInput.LastRawTranscript} -> {whisperInput.GetLikelySpeechIssue()}"
                    });
                }

                if (whisperInput.LastTranscriptWasRejected)
                {
                    report.findings.Add(new DiagnosticFinding
                    {
                        severity = (whisperInput.AllowRejectedTranscriptsInDebug || whisperInput.BypassQuietCaptureRejectionInDebug) && whisperInput.LastTranscriptDebugBypassUsed
                            ? DiagnosticSeverity.Info
                            : DiagnosticSeverity.Warning,
                        title = "Transcript Rejected",
                        message = (whisperInput.AllowRejectedTranscriptsInDebug || whisperInput.BypassQuietCaptureRejectionInDebug) && whisperInput.LastTranscriptDebugBypassUsed
                            ? $"Rejected as '{whisperInput.LastRejectedTranscriptReason}', but allowed through debug bypass."
                            : $"Rejected as '{whisperInput.LastRejectedTranscriptReason}'."
                    });
                }
                else if (!string.IsNullOrWhiteSpace(whisperInput.LastNormalizedTranscript))
                {
                    report.findings.Add(new DiagnosticFinding
                    {
                        severity = whisperInput.LastTranscriptForwardedToBrain ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                        title = whisperInput.LastTranscriptForwardedToBrain ? "Transcript Accepted" : "Transcript Accepted But Not Forwarded",
                        message = whisperInput.LastForwardingDecision
                    });
                }
            }

            private static void AppendFaceRendererDiagnostics(SystemDiagnosticsReport report, ExpressionLibraryManager expressionLibrary, ArkItBlendshapeDriver faceDriver)
            {
                var renderers = new List<SkinnedMeshRenderer>();
                if (expressionLibrary?.TargetFaceRenderers != null)
                {
                    renderers.AddRange(expressionLibrary.TargetFaceRenderers.Where(renderer => renderer != null));
                }

                if (renderers.Count == 0 && faceDriver?.TargetRenderers != null)
                {
                    renderers.AddRange(faceDriver.TargetRenderers.Where(renderer => renderer != null));
                }

                if (renderers.Count == 0)
                {
                    return;
                }

                var distinctRenderers = renderers.Distinct().ToList();
                var allBlendshapeNames = distinctRenderers
                    .Where(renderer => renderer != null && renderer.sharedMesh != null)
                    .SelectMany(GetAllBlendshapeNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var compatibilityProfiles = ExpressionBuilderHelper.DetectCompatibilityProfiles(allBlendshapeNames);
                report.configIssues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Info,
                    component = "Face",
                    issue = $"Detected {distinctRenderers.Count} face renderer(s): {string.Join(", ", distinctRenderers.Select(renderer => renderer.name))}",
                    suggestion = "This should include head, lashes, eyes, and mouth meshes when they are separate"
                });
                report.configIssues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Info,
                    component = "Face",
                    issue = $"Detected compatibility profile(s): {string.Join(", ", compatibilityProfiles)}",
                    suggestion = "Use this to confirm whether the model looks ARKit, CC/Reallusion, Viseme/VTuber, or a custom naming set"
                });

                foreach (var renderer in distinctRenderers)
                {
                    if (renderer == null || renderer.sharedMesh == null)
                    {
                        continue;
                    }

                    var mouthShapes = GetMouthRelatedBlendshapeNames(renderer);
                    var eyeShapes = GetEyeRelatedBlendshapeNames(renderer);
                    var jawShapes = GetJawRelatedBlendshapeNames(renderer);
                    var tongueTeethShapes = GetTongueOrTeethBlendshapeNames(renderer);
                    var hasRecognizedShapes = mouthShapes.Any(ExpressionBuilderHelper.LooksLikeRecognizedControlName);
                    var isMouthRenderer = renderer.name.IndexOf("mouth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         jawShapes.Count > 0 ||
                                         mouthShapes.Count > 0 ||
                                         tongueTeethShapes.Count > 0;
                    var isEyeRenderer = renderer.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       eyeShapes.Count > 0;

                    if (isMouthRenderer)
                    {
                        report.configIssues.Add(new ConfigIssue
                        {
                            severity = IssueSeverity.Info,
                            component = "Face",
                            issue = $"Renderer '{renderer.name}' mouth-related blendshapes: {(mouthShapes.Count > 0 ? string.Join(", ", mouthShapes) : "none")}",
                            suggestion = "If these names do not resemble jaw/mouth/viseme shapes, the inner mouth mesh will need custom mapping or re-rigging"
                        });

                        report.configIssues.Add(new ConfigIssue
                        {
                            severity = IssueSeverity.Info,
                            component = "Face",
                            issue = $"Renderer '{renderer.name}' jaw blendshapes: {(jawShapes.Count > 0 ? string.Join(", ", jawShapes) : "none")}",
                            suggestion = "Jaw shapes should exist if speech or open-mouth expressions need to move the inner mouth"
                        });

                        report.configIssues.Add(new ConfigIssue
                        {
                            severity = IssueSeverity.Info,
                            component = "Face",
                            issue = $"Renderer '{renderer.name}' tongue/teeth blendshapes: {(tongueTeethShapes.Count > 0 ? string.Join(", ", tongueTeethShapes) : "none")}",
                            suggestion = "Tongue or teeth shapes are optional, but useful to diagnose inner-mouth motion"
                        });

                        var hasTeethShape = tongueTeethShapes.Any(name =>
                            name.IndexOf("teeth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("tooth", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!hasTeethShape)
                        {
                            report.configIssues.Add(new ConfigIssue
                            {
                                severity = IssueSeverity.Warning,
                                component = "Face",
                                issue = $"Renderer '{renderer.name}' has no teeth-specific blendshapes",
                                suggestion = "Upper/lower teeth opening usually needs teeth blendshapes or proper jaw bone rigging; jawOpen alone will not create that separation"
                            });
                        }
                    }

                    if (isEyeRenderer)
                    {
                        report.configIssues.Add(new ConfigIssue
                        {
                            severity = IssueSeverity.Info,
                            component = "Face",
                            issue = $"Renderer '{renderer.name}' eye-related blendshapes: {(eyeShapes.Count > 0 ? string.Join(", ", eyeShapes) : "none")}",
                            suggestion = "Eye renderers should expose blink, look, squint, or brow-linked shapes when separated"
                        });
                    }

                    if (isMouthRenderer && mouthShapes.Count == 0)
                    {
                        report.configIssues.Add(new ConfigIssue
                        {
                            severity = IssueSeverity.Warning,
                            component = "Face",
                            issue = $"Renderer '{renderer.name}' exists but has no mouth/jaw-related blendshapes",
                            suggestion = "This usually means the inner mouth mesh cannot respond to the current facial expression system"
                        });
                        continue;
                    }

                    if (isMouthRenderer && !hasRecognizedShapes)
                    {
                        report.configIssues.Add(new ConfigIssue
                        {
                            severity = IssueSeverity.Warning,
                            component = "Face",
                            issue = $"Renderer '{renderer.name}' has mouth blendshapes, but their names do not match the current ARKit/lip-sync expectations",
                            suggestion = $"Detected names: {string.Join(", ", mouthShapes)}"
                        });
                    }
                }
            }

            private static List<string> GetMouthRelatedBlendshapeNames(SkinnedMeshRenderer renderer)
            {
                var names = new List<string>();
                if (renderer == null || renderer.sharedMesh == null)
                {
                    return names;
                }

                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    var name = renderer.sharedMesh.GetBlendShapeName(i);
                    if (ContainsFaceDebugKeyword(name))
                    {
                        names.Add(name);
                    }
                }

                return names;
            }

            private static IEnumerable<string> GetAllBlendshapeNames(SkinnedMeshRenderer renderer)
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    yield break;
                }

                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    yield return renderer.sharedMesh.GetBlendShapeName(i);
                }
            }

            private static List<string> GetEyeRelatedBlendshapeNames(SkinnedMeshRenderer renderer)
            {
                var names = new List<string>();
                if (renderer == null || renderer.sharedMesh == null)
                {
                    return names;
                }

                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    var name = renderer.sharedMesh.GetBlendShapeName(i);
                    if (ExpressionBuilderHelper.IsEyeRelatedBlendshape(name))
                    {
                        names.Add(name);
                    }
                }

                return names;
            }

            private static List<string> GetJawRelatedBlendshapeNames(SkinnedMeshRenderer renderer)
            {
                var names = new List<string>();
                if (renderer == null || renderer.sharedMesh == null)
                {
                    return names;
                }

                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    var name = renderer.sharedMesh.GetBlendShapeName(i);
                    if (ExpressionBuilderHelper.IsJawRelatedBlendshape(name))
                    {
                        names.Add(name);
                    }
                }

                return names;
            }

            private static List<string> GetTongueOrTeethBlendshapeNames(SkinnedMeshRenderer renderer)
            {
                var names = new List<string>();
                if (renderer == null || renderer.sharedMesh == null)
                {
                    return names;
                }

                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    var name = renderer.sharedMesh.GetBlendShapeName(i);
                    if (ExpressionBuilderHelper.IsTongueOrTeethRelatedBlendshape(name))
                    {
                        names.Add(name);
                    }
                }

                return names;
            }

            private static bool LooksLikeRecognizedMouthShape(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                var normalized = value.ToLowerInvariant();
                return normalized.Contains("mouthsmile") ||
                       normalized.Contains("mouthfrown") ||
                       normalized.Contains("mouthclose") ||
                       normalized.Contains("mouthfunnel") ||
                       normalized.Contains("mouthpucker") ||
                       normalized.Contains("jawopen") ||
                       normalized.Contains("mouthopen") ||
                       normalized.Contains("viseme") ||
                       normalized.Contains("aa") ||
                       normalized.Contains("oh") ||
                       normalized.Contains("ou") ||
                       normalized.Contains("ee") ||
                       normalized.Contains("ih");
            }

        private static ComponentStatus CheckStatus(string name, bool present, bool operational, string message)
        {
            return new ComponentStatus
            {
                name = name,
                    isPresent = present,
                    isOperational = operational,
                    statusMessage = message
                };
            }

            private static void AssignStringField(Component component, string fieldName, string value)
            {
                if (component == null)
                {
                    return;
                }

                var serializedObject = new SerializedObject(component);
                var property = serializedObject.FindProperty(fieldName);
                if (property == null || property.propertyType != SerializedPropertyType.String)
                {
                    return;
                }

                property.stringValue = value ?? string.Empty;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            private static PathValidation ValidatePath(string name, string path, bool relativeToAssets)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new PathValidation { name = name, path = path, exists = false };
                }

                var fullPath = path;
                if (!Path.IsPathRooted(path))
                {
                    fullPath = ResolveAbsoluteOrProjectPath(path);
                }

                var exists = File.Exists(fullPath);
                return new PathValidation
                {
                    name = name,
                    path = path,
                    exists = exists,
                    fileSizeMB = exists ? new FileInfo(fullPath).Length / (1024 * 1024) : 0
                };
            }

            private static PathValidation ValidateStreamingAssetsPath(string name, string relativePath)
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    return new PathValidation { name = name, path = relativePath, exists = false };
                }

                var fullPath = Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var exists = File.Exists(fullPath);
                return new PathValidation
                {
                    name = name,
                    path = relativePath,
                    exists = exists,
                    fileSizeMB = exists ? new FileInfo(fullPath).Length / (1024 * 1024) : 0
                };
            }

            private static bool HasObjectReference(UnityEngine.Object target, string fieldName)
            {
                var serializedObject = new SerializedObject(target);
                var property = serializedObject.FindProperty(fieldName);
                return property != null && property.objectReferenceValue != null;
            }
        }
    }
}
#endif
