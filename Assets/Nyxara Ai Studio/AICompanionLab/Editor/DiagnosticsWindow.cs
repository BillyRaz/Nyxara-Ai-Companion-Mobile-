// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Diagnostics;
using Nyxara.AICompanion.Expressions;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.LipSync;
using Nyxara.AICompanion.Speech;
using UnityEditor;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    public class DiagnosticsWindow : EditorWindow
    {
        private enum DiagnosticsTab
        {
            SystemScan,
            RuntimeMonitor,
            LogViewer,
            Performance
        }

        private struct LogEntry
        {
            public string Timestamp;
            public string Message;
            public LogType Type;
        }

        private DiagnosticsTab _currentTab = DiagnosticsTab.SystemScan;
        private Vector2 _scrollPosition;
        private SystemDiagnosticsReport _lastScanReport;
        private RuntimeDiagnosticsMonitor _runtimeMonitor;
        private readonly List<LogEntry> _logEntries = new();
        private string _logFilter = string.Empty;
        private bool _showErrors = true;
        private bool _showWarnings = true;
        private bool _showInfo = true;
        private bool _isPlaying;
        private double _lastUpdateTime;

        [MenuItem("Nyxara AI/Diagnostics", false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<DiagnosticsWindow>("Nyxara AI Diagnostics");
            window.minSize = new Vector2(720f, 540f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            Application.logMessageReceived += OnLogMessageReceived;
            FindRuntimeMonitor();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        private void OnEditorUpdate()
        {
            if (_currentTab != DiagnosticsTab.RuntimeMonitor && _currentTab != DiagnosticsTab.Performance)
            {
                return;
            }

            if (EditorApplication.isPlaying != _isPlaying)
            {
                _isPlaying = EditorApplication.isPlaying;
                if (_isPlaying)
                {
                    FindRuntimeMonitor();
                }
            }

            if (_isPlaying && EditorApplication.timeSinceStartup - _lastUpdateTime > 0.5d)
            {
                _lastUpdateTime = EditorApplication.timeSinceStartup;
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
            DrawHeader();
            DrawTabs();

            EditorGUILayout.Space(8f);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_currentTab)
            {
                case DiagnosticsTab.SystemScan:
                    DrawSystemScanTab();
                    break;
                case DiagnosticsTab.RuntimeMonitor:
                    DrawRuntimeMonitorTab();
                    break;
                case DiagnosticsTab.LogViewer:
                    DrawLogViewerTab();
                    break;
                case DiagnosticsTab.Performance:
                    DrawPerformanceTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var healthy = _lastScanReport?.IsHealthy ?? true;
            var originalColor = GUI.color;
            GUI.color = healthy ? new Color(0.35f, 0.85f, 0.35f) : new Color(0.9f, 0.4f, 0.4f);
            GUILayout.Label(healthy ? "System Healthy" : "Issues Detected", EditorStyles.boldLabel);
            GUI.color = originalColor;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Refresh All", EditorStyles.toolbarButton))
            {
                PerformSystemScan();
                FindRuntimeMonitor();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("System Scan", EditorStyles.miniButtonLeft))
            {
                _currentTab = DiagnosticsTab.SystemScan;
            }

            if (GUILayout.Button("Runtime Monitor", EditorStyles.miniButtonMid))
            {
                _currentTab = DiagnosticsTab.RuntimeMonitor;
            }

            if (GUILayout.Button("Log Viewer", EditorStyles.miniButtonMid))
            {
                _currentTab = DiagnosticsTab.LogViewer;
            }

            if (GUILayout.Button("Performance", EditorStyles.miniButtonRight))
            {
                _currentTab = DiagnosticsTab.Performance;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSystemScanTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("System Configuration Scan", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Scans your scene for companion components, path problems, and missing setup.", MessageType.Info);

            if (GUILayout.Button("Perform Full System Scan", GUILayout.Height(34f)))
            {
                PerformSystemScan();
            }

            if (_lastScanReport != null)
            {
                EditorGUILayout.Space(10f);
                DrawScanResults(_lastScanReport);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeMonitorTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Runtime Diagnostics Monitor", EditorStyles.boldLabel);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view live diagnostics.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_runtimeMonitor == null)
            {
                EditorGUILayout.HelpBox("No RuntimeDiagnosticsMonitor found in the scene.", MessageType.Warning);
                if (GUILayout.Button("Add Runtime Diagnostics Monitor"))
                {
                    AddRuntimeMonitor();
                }

                EditorGUILayout.EndVertical();
                return;
            }

            var report = _runtimeMonitor.CurrentReport;
            if (report == null)
            {
                EditorGUILayout.LabelField("Waiting for diagnostics data...");
                EditorGUILayout.EndVertical();
                return;
            }

            DrawRuntimeMetrics(report);
            EditorGUILayout.Space(8f);
            DrawLiveState(report.runtimeSnapshot);
            EditorGUILayout.EndVertical();
        }

        private void DrawLogViewerTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Unity Log Feed", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This mirrors Unity console logs here so you can inspect them beside the diagnostics panels.", MessageType.Info);

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
            EditorGUILayout.Space(8f);

            var filteredLogs = FilterLogs();
            if (filteredLogs.Count == 0)
            {
                EditorGUILayout.HelpBox("No log entries match the current filters.", MessageType.Info);
            }

            foreach (var entry in filteredLogs)
            {
                var originalColor = GUI.color;
                GUI.color = GetLogColor(entry.Type);
                EditorGUILayout.LabelField($"[{entry.Timestamp}] {entry.Type}: {entry.Message}", EditorStyles.wordWrappedLabel);
                GUI.color = originalColor;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPerformanceTab()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Performance Analytics", EditorStyles.boldLabel);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view live memory and latency data.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_runtimeMonitor == null)
            {
                EditorGUILayout.HelpBox("No RuntimeDiagnosticsMonitor found in the scene.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            var report = _runtimeMonitor.CurrentReport;
            if (report == null)
            {
                EditorGUILayout.LabelField("Waiting for diagnostics data...");
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField("Memory Usage", EditorStyles.boldLabel);
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(320f, 20f), Mathf.Clamp01(report.performance.memoryUsageMB / 4096f), $"{report.performance.memoryUsageMB} MB / 4096 MB");

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Latency Targets", EditorStyles.boldLabel);
            DrawLatencyBar("LLM", report.performance.averageLLMLatencyMs, 2000f);
            DrawLatencyBar("STT", report.performance.averageSTTLatencyMs, 1000f);
            DrawLatencyBar("TTS", report.performance.averageTTSLatencyMs, 500f);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Recommendations", EditorStyles.boldLabel);
            DrawRecommendations(report);
            EditorGUILayout.EndVertical();
        }

        private void DrawScanResults(SystemDiagnosticsReport report)
        {
            EditorGUILayout.LabelField($"Scan completed at {report.timestamp} in {report.durationMs:F0} ms", EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            var originalColor = GUI.color;
            GUI.color = report.IsHealthy ? new Color(0.35f, 0.85f, 0.35f) : new Color(0.9f, 0.4f, 0.4f);
            EditorGUILayout.LabelField(report.IsHealthy ? "Overall Status: Healthy" : "Overall Status: Issues Found", EditorStyles.boldLabel);
            GUI.color = originalColor;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Component Status", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawComponentStatusCard(report.llmStatus);
            DrawComponentStatusCard(report.sttStatus);
            DrawComponentStatusCard(report.ttsStatus);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawComponentStatusCard(report.faceStatus);
            DrawComponentStatusCard(report.lipSyncStatus);
            DrawComponentStatusCard(report.expressionStatus);
            EditorGUILayout.EndHorizontal();

            if (report.configIssues.Count > 0)
            {
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField("Configuration Issues", EditorStyles.boldLabel);
                foreach (var issue in report.configIssues)
                {
                    var issueType = issue.severity == IssueSeverity.Critical ? MessageType.Error :
                        issue.severity == IssueSeverity.Warning ? MessageType.Warning : MessageType.Info;
                    EditorGUILayout.HelpBox($"{issue.component}: {issue.issue}\nSuggestion: {issue.suggestion}", issueType);
                }
            }

            if (report.pathValidations.Count > 0)
            {
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField("Path Validation", EditorStyles.boldLabel);
                foreach (var path in report.pathValidations)
                {
                    EditorGUILayout.BeginHorizontal("box");
                    GUILayout.Label(path.exists ? "OK" : "Missing", GUILayout.Width(55f));
                    EditorGUILayout.LabelField(path.name, GUILayout.Width(120f));
                    EditorGUILayout.LabelField(path.path, EditorStyles.wordWrappedMiniLabel);
                    if (path.exists)
                    {
                        EditorGUILayout.LabelField($"{path.fileSizeMB} MB", GUILayout.Width(70f));
                        if (!string.IsNullOrEmpty(path.lastModified))
                        {
                            EditorGUILayout.LabelField(path.lastModified, GUILayout.Width(120f));
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawRuntimeMetrics(SystemDiagnosticsReport report)
        {
            EditorGUILayout.LabelField("Live Metrics", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical("box", GUILayout.Width(220f));
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Memory: {report.performance.memoryUsageMB} MB");
            EditorGUILayout.LabelField($"CPU: {report.performance.cpuUsagePercent:F1}%");
            EditorGUILayout.LabelField($"Threads: {report.performance.activeThreads}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box", GUILayout.Width(220f));
            EditorGUILayout.LabelField("Latency", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"LLM: {report.performance.averageLLMLatencyMs:F0} ms");
            EditorGUILayout.LabelField($"STT: {report.performance.averageSTTLatencyMs:F0} ms");
            EditorGUILayout.LabelField($"TTS: {report.performance.averageTTSLatencyMs:F0} ms");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box", GUILayout.Width(220f));
            EditorGUILayout.LabelField("Component Health", EditorStyles.boldLabel);
            DrawHealthIndicator(report.llmStatus.isOperational, "LLM");
            DrawHealthIndicator(report.sttStatus.isOperational, "STT");
            DrawHealthIndicator(report.ttsStatus.isOperational || !report.ttsStatus.affectsOverallHealth, "TTS");
            DrawHealthIndicator(report.faceStatus.isOperational, "Face");
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLiveState(RuntimeSnapshot snapshot)
        {
            EditorGUILayout.LabelField("Current NPC State", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical("box", GUILayout.Width(300f));
            EditorGUILayout.LabelField($"Mood: {snapshot.currentMood}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Task: {snapshot.currentTask}");
            EditorGUILayout.LabelField($"Trust: {snapshot.trust:P0}");
            EditorGUILayout.LabelField($"Affection: {snapshot.affection:P0}");
            EditorGUILayout.LabelField($"Suspicion: {snapshot.suspicion:P0}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Last Response", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Intent: {snapshot.lastIntent}");
            EditorGUILayout.LabelField($"Action: {snapshot.lastAction}");
            EditorGUILayout.LabelField($"Signal: {snapshot.lastSignal}");
            EditorGUILayout.LabelField($"Dialogue: \"{snapshot.lastDialogue}\"", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            DrawHealthIndicator(snapshot.isSpeaking, "Speaking");
            DrawHealthIndicator(snapshot.isThinking, "Thinking");
            EditorGUILayout.LabelField($"Memory Entries: {snapshot.memoryCount}", GUILayout.Width(140f));
            EditorGUILayout.LabelField($"Last Response: {snapshot.timeSinceLastResponse:F1}s ago", GUILayout.Width(170f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawComponentStatusCard(ComponentStatus status)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(210f));
            var originalColor = GUI.color;
            var cardHealthy = status.isOperational || !status.affectsOverallHealth;
            GUI.color = cardHealthy ? new Color(0.35f, 0.85f, 0.35f) : (status.isPresent ? new Color(1f, 0.8f, 0.35f) : Color.gray);
            EditorGUILayout.LabelField(status.name, EditorStyles.boldLabel);
            GUI.color = originalColor;

            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(status.stateLabel) ? (status.isOperational ? "Operational" : "Offline") : status.stateLabel, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(status.statusMessage ?? string.Empty, EditorStyles.wordWrappedMiniLabel);
            if (status.lastResponseTimeMs > 0f)
            {
                EditorGUILayout.LabelField($"Last Response: {status.lastResponseTimeMs:F0} ms", EditorStyles.miniLabel);
            }

            if (!string.IsNullOrEmpty(status.lastError))
            {
                EditorGUILayout.HelpBox(status.lastError, MessageType.Error);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawHealthIndicator(bool isHealthy, string label)
        {
            var originalColor = GUI.color;
            GUI.color = isHealthy ? new Color(0.35f, 0.85f, 0.35f) : new Color(0.85f, 0.45f, 0.45f);
            GUILayout.Label(isHealthy ? "O" : "-", GUILayout.Width(20f));
            GUI.color = originalColor;
            GUILayout.Label(label, GUILayout.Width(100f));
        }

        private void DrawLatencyBar(string label, float latencyMs, float targetMs)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(40f));
            var normalized = Mathf.Clamp01(targetMs <= 0f ? 0f : latencyMs / targetMs);
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(220f, 18f), normalized, $"{latencyMs:F0} ms");
            EditorGUILayout.LabelField($"/ {targetMs:F0} ms", GUILayout.Width(70f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRecommendations(SystemDiagnosticsReport report)
        {
            if (report.performance.averageLLMLatencyMs > 3000f)
            {
                EditorGUILayout.HelpBox("LLM latency is high. Consider reducing prompt size or using a smaller GGUF.", MessageType.Warning);
            }

            if (report.performance.memoryUsageMB > 2048)
            {
                EditorGUILayout.HelpBox("Memory use is above 2 GB. Consider unloading unused assets and reducing model size.", MessageType.Warning);
            }

            if (!report.llmStatus.isOperational)
            {
                EditorGUILayout.HelpBox("LLM is not operational. Verify the model path and let LLMUnity finish loading.", MessageType.Error);
            }

            if (report.configIssues.Count == 0 &&
                report.performance.averageLLMLatencyMs <= 3000f &&
                report.performance.memoryUsageMB <= 2048)
            {
                EditorGUILayout.HelpBox("No immediate optimization issues detected.", MessageType.Info);
            }
        }

        private List<LogEntry> FilterLogs()
        {
            IEnumerable<LogEntry> filtered = _logEntries;
            if (!string.IsNullOrWhiteSpace(_logFilter))
            {
                filtered = filtered.Where(entry => entry.Message.IndexOf(_logFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            filtered = filtered.Where(entry =>
            {
                var isError = entry.Type == LogType.Error || entry.Type == LogType.Exception || entry.Type == LogType.Assert;
                var isWarning = entry.Type == LogType.Warning;
                var isInfo = !isError && !isWarning;
                return (_showErrors || !isError) &&
                       (_showWarnings || !isWarning) &&
                       (_showInfo || !isInfo);
            });

            return filtered.ToList();
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

        private void PerformSystemScan()
        {
            var scanner = new SystemDiagnosticsScanner();
            _lastScanReport = scanner.Scan();
            Repaint();
        }

        private void FindRuntimeMonitor()
        {
            _runtimeMonitor = FindFirstObjectByType<RuntimeDiagnosticsMonitor>();
        }

        private void AddRuntimeMonitor()
        {
            var monitorObject = new GameObject("RuntimeDiagnosticsMonitor");
            _runtimeMonitor = monitorObject.AddComponent<RuntimeDiagnosticsMonitor>();
            Selection.activeGameObject = monitorObject;
        }

        private sealed class SystemDiagnosticsScanner
        {
            public SystemDiagnosticsReport Scan()
            {
                var report = new SystemDiagnosticsReport();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var brain = FindFirstObjectByType<NyxaraCompanionBrain>();
                var whisperInput = FindFirstObjectByType<WhisperMicrophoneInput>();
                var ttsService = FindFirstObjectByType<PiperTtsService>();
                var faceDriver = FindFirstObjectByType<ArkItBlendshapeDriver>();
                var lipSyncController = FindFirstObjectByType<VisemeLipSyncController>();
                var expressionLibrary = FindFirstObjectByType<ExpressionLibraryManager>();

                report.timestamp = DateTime.Now.ToString("HH:mm:ss");
                report.llmStatus = CheckLLMComponent(brain);
                report.sttStatus = CheckSTTComponent(whisperInput);
                report.ttsStatus = CheckTTSComponent(ttsService);
                report.faceStatus = CheckFaceComponent(faceDriver);
                report.lipSyncStatus = CheckLipSyncComponent(lipSyncController);
                report.expressionStatus = CheckExpressionComponent(expressionLibrary);
                report.configIssues = FindIssues(brain, ttsService, faceDriver);
                report.pathValidations = ValidatePaths(brain, whisperInput, ttsService);

                stopwatch.Stop();
                report.durationMs = stopwatch.ElapsedMilliseconds;
                return report;
            }

            private static ComponentStatus CheckLLMComponent(NyxaraCompanionBrain brain)
            {
                var status = new ComponentStatus { name = "LLM (Qwen)" };
                if (brain == null)
                {
                    status.isPresent = false;
                    status.isOperational = false;
                    status.statusMessage = "No NyxaraCompanionBrain found in scene";
                    return status;
                }

                status.isPresent = true;
                status.isOperational = brain.IsLlmAvailable;
                status.statusMessage = status.isOperational
                    ? "Configured"
                    : "Nyxara AI Studio: LLMUnity not installed or no LLMAgent assigned";
                return status;
            }

            private static ComponentStatus CheckSTTComponent(WhisperMicrophoneInput input)
            {
                var status = new ComponentStatus { name = "STT (Whisper)" };
                if (input == null)
                {
                    status.isPresent = false;
                    status.isOperational = false;
                    status.statusMessage = "No WhisperMicrophoneInput found in scene";
                    return status;
                }

                status.isPresent = true;
                status.affectsOverallHealth = false;
                status.stateLabel = input.IsWhisperAvailable ? string.Empty : "Optional";
                status.isOperational = input.IsWhisperAvailable;
                status.statusMessage = status.isOperational
                    ? "Configured"
                    : "Nyxara AI Studio: Whisper not installed or no WhisperManager assigned";
                return status;
            }

            private static ComponentStatus CheckTTSComponent(PiperTtsService tts)
            {
                var status = new ComponentStatus { name = "TTS (Piper)" };
                if (tts == null)
                {
                    status.isPresent = false;
                    status.isOperational = true;
                    status.affectsOverallHealth = false;
                    status.stateLabel = "Optional";
                    status.statusMessage = "No PiperTtsService found in scene";
                    return status;
                }

                var availabilityStatus = tts.AvailabilityStatus;
                status.isPresent = true;
                status.isOperational = availabilityStatus != PiperTtsAvailabilityStatus.InvalidPath;
                status.affectsOverallHealth = false;
                status.stateLabel = tts.HasInProcessVoice
                    ? "Ready (In-Process)"
                    : PiperTtsService.GetStatusLabel(availabilityStatus);
                status.statusMessage = PiperTtsService.GetStatusGuidance(availabilityStatus);
                return status;
            }

            private static ComponentStatus CheckFaceComponent(ArkItBlendshapeDriver driver)
            {
                var status = new ComponentStatus { name = "Face System" };
                if (driver == null)
                {
                    status.isPresent = false;
                    status.isOperational = false;
                    status.statusMessage = "No ArkItBlendshapeDriver found in scene";
                    return status;
                }

                status.isPresent = true;
                var renderer = driver.GetComponent<SkinnedMeshRenderer>();
                status.isOperational = renderer != null && renderer.sharedMesh != null;
                status.statusMessage = status.isOperational
                    ? $"Active with {renderer.sharedMesh.blendShapeCount} blendshapes"
                    : "Missing SkinnedMeshRenderer";
                return status;
            }

            private static ComponentStatus CheckLipSyncComponent(VisemeLipSyncController controller)
            {
                var status = new ComponentStatus { name = "Lip Sync" };
                if (controller == null)
                {
                    status.isPresent = false;
                    status.isOperational = true;
                    status.statusMessage = "Optional component not present";
                    return status;
                }

                status.isPresent = true;
                status.isOperational = true;
                status.statusMessage = "Installed and ready";
                return status;
            }

            private static ComponentStatus CheckExpressionComponent(ExpressionLibraryManager library)
            {
                var status = new ComponentStatus { name = "Expression Library" };
                if (library == null)
                {
                    status.isPresent = false;
                    status.isOperational = true;
                    status.statusMessage = "Optional component not present";
                    return status;
                }

                status.isPresent = true;
                var count = library.LoadedPresets?.Count ?? 0;
                status.isOperational = count > 0;
                status.statusMessage = $"{count} expressions loaded";
                return status;
            }

            private static List<ConfigIssue> FindIssues(NyxaraCompanionBrain brain, PiperTtsService tts, ArkItBlendshapeDriver face)
            {
                var issues = new List<ConfigIssue>();

                if (brain != null && brain.CharacterProfile == null)
                {
                    issues.Add(new ConfigIssue
                    {
                        severity = IssueSeverity.Warning,
                        component = "Character",
                        issue = "No CharacterProfile assigned",
                        suggestion = "Assign a CharacterProfile to NyxaraCompanionBrain"
                    });
                }

                if (brain != null && !brain.IsLlmAvailable)
                {
                    issues.Add(new ConfigIssue
                    {
                        severity = IssueSeverity.Warning,
                        component = "LLM",
                        issue = "LLMUnity is unavailable",
                        suggestion = "Install LLMUnity and add the NYXARA_LLMUNITY define to enable AI generation"
                    });
                }

                if (tts != null && tts.AvailabilityStatus == PiperTtsAvailabilityStatus.InvalidPath)
                {
                    issues.Add(new ConfigIssue
                    {
                        severity = IssueSeverity.Warning,
                        component = "TTS",
                        issue = "Piper paths are invalid",
                        suggestion = "Review the Piper executable and voice model paths in Voice Output (Optional)"
                    });
                }

                if (face != null && face.GetComponent<SkinnedMeshRenderer>() == null)
                {
                    issues.Add(new ConfigIssue
                    {
                        severity = IssueSeverity.Warning,
                        component = "Face",
                        issue = "No SkinnedMeshRenderer on the face driver object",
                        suggestion = "Assign your avatar face renderer through the studio setup"
                    });
                }

                return issues;
            }

            private static List<PathValidation> ValidatePaths(NyxaraCompanionBrain brain, WhisperMicrophoneInput whisper, PiperTtsService tts)
            {
                var validations = new List<PathValidation>();

                if (brain != null && brain.IsLlmAvailable)
                {
                    validations.Add(new PathValidation
                    {
                        name = "LLM Model",
                        path = "Configured through LLMUnity",
                        exists = true
                    });
                }

            if (whisper != null && whisper.IsWhisperAvailable && whisper.HasAssignedWhisperManager && !string.IsNullOrWhiteSpace(whisper.ConfiguredModelPath))
            {
                validations.Add(ValidateSinglePath("Whisper Model", whisper.ConfiguredModelPath, true));
            }

                if (tts != null)
                {
                    if (tts.HasInProcessVoice)
                    {
                        var voiceAssetPath = AssetDatabase.GetAssetPath(tts.VoiceModel);
                        validations.Add(new PathValidation
                        {
                            name = "Piper Voice (in-process)",
                            path = string.IsNullOrWhiteSpace(voiceAssetPath) ? tts.VoiceModel.name : voiceAssetPath,
                            exists = !string.IsNullOrWhiteSpace(voiceAssetPath)
                        });
                    }
                    else if (tts.TtsEnabled ||
                             !string.IsNullOrWhiteSpace(tts.PiperExecutablePath) ||
                             !string.IsNullOrWhiteSpace(tts.VoiceModelPath))
                    {
                        validations.Add(ValidateSinglePath("Piper Executable", tts.PiperExecutablePath, true));
                        validations.Add(ValidateSinglePath("Piper Voice", tts.VoiceModelPath, true));
                    }
                }

                return validations;
            }

            private static PathValidation ValidateSinglePath(string name, string path, bool isRelativeToProject = false)
            {
                var validation = new PathValidation { name = name, path = path };
                if (string.IsNullOrWhiteSpace(path))
                {
                    validation.exists = false;
                    return validation;
                }

                var fullPath = path;
                if (isRelativeToProject && !Path.IsPathRooted(path))
                {
                    fullPath = ResolveProjectPath(path);
                }

                validation.exists = File.Exists(fullPath);
                if (validation.exists)
                {
                    var info = new FileInfo(fullPath);
                    validation.fileSizeMB = info.Length / (1024 * 1024);
                    validation.lastModified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                }

                return validation;
            }

            private static string ResolveProjectPath(string path)
            {
                var normalized = path.Replace('\\', '/');
                var streamingAssetsPath = Path.Combine(
                    Application.streamingAssetsPath,
                    normalized.Replace("Assets/StreamingAssets/", string.Empty)
                        .Replace("StreamingAssets/", string.Empty)
                        .Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(streamingAssetsPath))
                {
                    return streamingAssetsPath;
                }

                return Path.Combine(Application.dataPath, normalized.Replace("Assets/", string.Empty).Replace('/', Path.DirectorySeparatorChar));
            }
        }
    }
}
#endif
