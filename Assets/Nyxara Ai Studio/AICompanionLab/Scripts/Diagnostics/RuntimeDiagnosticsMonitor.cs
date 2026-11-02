// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
#if NYXARA_LLMUNITY
using LLMUnity;
#endif
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Data;
using Nyxara.AICompanion.Expressions;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.LipSync;
using Nyxara.AICompanion.Runtime;
using Nyxara.AICompanion.Speech;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace Nyxara.AICompanion.Diagnostics
{
    public class RuntimeDiagnosticsMonitor : MonoBehaviour
    {
        [Header("Monitoring Settings")]
        [SerializeField] private float updateIntervalSeconds = 1f;
        [SerializeField] private bool enableDetailedLogging = true;
        [SerializeField] private int maxErrorHistory = 20;

        [Header("Component References (Auto-detected)")]
        [SerializeField] private NyxaraCompanionBrain brain;
        [SerializeField] private MonoBehaviour llmAgent;
        [SerializeField] private WhisperMicrophoneInput whisperInput;
        [SerializeField] private PiperTtsService ttsService;
        [SerializeField] private ArkItBlendshapeDriver faceDriver;
        [SerializeField] private VisemeLipSyncController lipSyncController;
        [SerializeField] private ExpressionLibraryManager expressionLibrary;
        [SerializeField] private ActionGatekeeper actionGatekeeper;
        [SerializeField] private RecentMemoryController memoryController;

        private SystemDiagnosticsReport _currentReport = new();
        private readonly Queue<string> _errorHistory = new();
        private readonly Queue<float> _llmLatencies = new();
        private readonly Queue<float> _sttLatencies = new();
        private readonly Queue<float> _ttsLatencies = new();

        private float _lastResponseRealtime;
        private TimeSpan _lastCpuTime = TimeSpan.Zero;
        private float _lastCpuRealtime;

        public event Action<SystemDiagnosticsReport> OnDiagnosticsUpdated;
        public event Action<string, string> OnErrorLogged;

        public SystemDiagnosticsReport CurrentReport => _currentReport;
        public bool IsMonitoring { get; private set; }

        private void Awake()
        {
            AutoDetectComponents();
            _lastCpuRealtime = Time.realtimeSinceStartup;
            _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        }

        private void Start()
        {
            HookComponentEvents();
            StartCoroutine(MonitoringCoroutine());
        }

        private void AutoDetectComponents()
        {
            if (brain == null) brain = FindFirstObjectByType<NyxaraCompanionBrain>();
#if NYXARA_LLMUNITY
            if (llmAgent == null) llmAgent = FindFirstObjectByType<LLMAgent>();
#else
            llmAgent = null;
#endif
            if (whisperInput == null) whisperInput = FindFirstObjectByType<WhisperMicrophoneInput>();
            if (ttsService == null) ttsService = FindFirstObjectByType<PiperTtsService>();
            if (faceDriver == null) faceDriver = FindFirstObjectByType<ArkItBlendshapeDriver>();
            if (lipSyncController == null) lipSyncController = FindFirstObjectByType<VisemeLipSyncController>();
            if (expressionLibrary == null) expressionLibrary = FindFirstObjectByType<ExpressionLibraryManager>();
            if (actionGatekeeper == null) actionGatekeeper = FindFirstObjectByType<ActionGatekeeper>();
            if (memoryController == null) memoryController = FindFirstObjectByType<RecentMemoryController>();
        }

        private void HookComponentEvents()
        {
            if (brain != null)
            {
                brain.ReplyReady += OnBrainReplyReady;
                brain.ResponseParsed += OnResponseParsed;
            }

            if (whisperInput != null)
            {
                whisperInput.TranscriptReady += OnTranscriptReady;
            }
        }

        private IEnumerator MonitoringCoroutine()
        {
            IsMonitoring = true;
            while (IsMonitoring)
            {
                var sw = Stopwatch.StartNew();
                CollectDiagnostics();
                sw.Stop();
                _currentReport.durationMs = sw.ElapsedMilliseconds;
                _currentReport.timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                OnDiagnosticsUpdated?.Invoke(_currentReport);
                yield return new WaitForSeconds(updateIntervalSeconds);
            }
        }

        private void CollectDiagnostics()
        {
            AutoDetectComponents();
            _currentReport.llmStatus = CheckLLMStatus();
            _currentReport.sttStatus = CheckSTTStatus();
            _currentReport.ttsStatus = CheckTTSStatus();
            _currentReport.faceStatus = CheckFaceStatus();
            _currentReport.lipSyncStatus = CheckLipSyncStatus();
            _currentReport.expressionStatus = CheckExpressionStatus();
            _currentReport.performance = CollectPerformanceMetrics();
            _currentReport.pathValidations = ValidatePaths();
            _currentReport.configIssues = FindConfigIssues();
            _currentReport.runtimeSnapshot = CaptureRuntimeSnapshot();
        }

        private ComponentStatus CheckLLMStatus()
        {
            var status = new ComponentStatus { name = "LLM (Qwen)" };

#if NYXARA_LLMUNITY
            var agent = llmAgent as LLMAgent;
            if (agent == null)
            {
                status.isPresent = false;
                status.isOperational = false;
                status.statusMessage = "LLMAgent not found in scene";
                return status;
            }

            status.isPresent = true;
            try
            {
                var llm = agent.llm;
                if (llm == null)
                {
                    status.isOperational = false;
                    status.statusMessage = "LLM reference missing on agent";
                    return status;
                }

                status.isOperational = llm.started && !llm.failed;
                status.statusMessage = status.isOperational ? "Model loaded and ready" : "Model not started yet";
                if (_llmLatencies.Count > 0)
                {
                    status.lastResponseTimeMs = _llmLatencies.Average();
                }
            }
            catch (Exception ex)
            {
                status.isOperational = false;
                status.statusMessage = $"Error: {ex.Message}";
                status.lastError = ex.Message;
            }
#else
            _ = llmAgent;
            status.isPresent = false;
            status.isOperational = false;
            status.affectsOverallHealth = false;
            status.stateLabel = "Optional";
            status.statusMessage = "Nyxara AI Studio: LLMUnity not installed. AI features disabled.";
#endif

            return status;
        }

        private ComponentStatus CheckSTTStatus()
        {
            var status = new ComponentStatus { name = "Player Voice Input (Whisper)" };
            if (whisperInput == null)
            {
                status.isPresent = false;
                status.isOperational = false;
                status.statusMessage = "WhisperMicrophoneInput not found";
                return status;
            }

            status.isPresent = true;
            status.isOperational = whisperInput.enabled && whisperInput.IsWhisperAvailable;
            status.statusMessage = status.isOperational
                ? "Ready for voice input"
                : "Nyxara AI Studio: Whisper not installed or no WhisperManager assigned. Speech-to-text disabled.";
            status.affectsOverallHealth = false;
            status.stateLabel = status.isOperational ? string.Empty : "Optional";
            status.recommendedAction = whisperInput.GetLikelySpeechIssue();
            status.details.Add($"Mic devices: {whisperInput.AvailableMicrophones.Length}");
            status.details.Add($"Resolved mic: {whisperInput.ActiveMicrophoneDevice ?? "<default>"}");
            status.details.Add($"Mic route: {whisperInput.LastResolvedMicrophoneRoute}");
            status.details.Add($"Capture mode: {whisperInput.CaptureModeLabel}");
            status.details.Add($"Capture failure mode: {whisperInput.LastCaptureFailureMode}");
            status.details.Add($"Last raw STT: {(!string.IsNullOrWhiteSpace(whisperInput.LastRawTranscript) ? whisperInput.LastRawTranscript : "<empty>")}");
            status.details.Add($"Last normalized STT: {(!string.IsNullOrWhiteSpace(whisperInput.LastNormalizedTranscript) ? whisperInput.LastNormalizedTranscript : "<empty>")}");
            status.details.Add($"Rejected transcript debug bypass: {(whisperInput.AllowRejectedTranscriptsInDebug ? "enabled" : "disabled")}");
            status.details.Add($"Music-like transcript bypass: {(whisperInput.AllowMusicLikeTranscriptsInDebug ? "enabled" : "disabled")}");
            status.details.Add($"Quiet rejection bypass: {(whisperInput.BypassQuietCaptureRejectionInDebug ? "enabled" : "disabled")}");
            status.details.Add($"Explicit mic selection: {(whisperInput.ForceExplicitMicrophoneSelection ? "enabled" : "disabled")}");
            status.details.Add($"Speech gate: RMS >= {whisperInput.MinRmsForSpeech:0.0000}, Peak >= {whisperInput.MinPeakForSpeech:0.0000}, PreGain {whisperInput.PreTranscriptionGain:0.00}x");
            status.details.Add($"Transcript filters: Music {(whisperInput.RejectMusicLikeTranscripts ? "enabled" : "disabled")} | Bracket tags {(whisperInput.RejectBracketedNonSpeechTags ? "enabled" : "disabled")}");
            status.details.Add($"Last audio stats: {whisperInput.LastRecordingDurationSeconds:0.00}s | RMS {whisperInput.LastRecordingRms:0.0000} | Peak {whisperInput.LastRecordingPeak:0.0000} | Gain {whisperInput.LastAppliedPreampGain:0.00}x");
            status.details.Add($"Last forwarding decision: {whisperInput.LastForwardingDecision}");
            if (_sttLatencies.Count > 0)
            {
                status.lastResponseTimeMs = _sttLatencies.Average();
            }

            return status;
        }

        private ComponentStatus CheckTTSStatus()
        {
            var status = new ComponentStatus { name = "TTS (Piper)" };
            if (ttsService == null)
            {
                status.isPresent = false;
                status.isOperational = true;
                status.affectsOverallHealth = false;
                status.stateLabel = "Optional";
                status.statusMessage = "PiperTtsService not found";
                return status;
            }

            var availabilityStatus = ttsService.AvailabilityStatus;
            status.isPresent = true;
            status.isOperational = availabilityStatus != PiperTtsAvailabilityStatus.InvalidPath;
            status.affectsOverallHealth = false;
            status.stateLabel = ttsService.HasInProcessVoice
                ? "Ready (In-Process)"
                : PiperTtsService.GetStatusLabel(availabilityStatus);
            status.statusMessage = PiperTtsService.GetStatusGuidance(availabilityStatus);
            if (_ttsLatencies.Count > 0)
            {
                status.lastResponseTimeMs = _ttsLatencies.Average();
            }

            return status;
        }

        private ComponentStatus CheckFaceStatus()
        {
            var status = new ComponentStatus { name = "Face System" };
            if (faceDriver == null)
            {
                status.isPresent = false;
                status.isOperational = false;
                status.statusMessage = "ArkItBlendshapeDriver not found";
                return status;
            }

            status.isPresent = true;
            var renderer = faceDriver.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null)
            {
                status.isOperational = false;
                status.statusMessage = "No SkinnedMeshRenderer or mesh assigned on same object";
            }
            else
            {
                status.isOperational = true;
                status.statusMessage = $"Active, {renderer.sharedMesh.blendShapeCount} blendshapes available";
            }

            return status;
        }

        private ComponentStatus CheckLipSyncStatus()
        {
            var status = new ComponentStatus { name = "Lip Sync" };
            if (lipSyncController == null)
            {
                status.isPresent = false;
                status.isOperational = false;
                status.statusMessage = "VisemeLipSyncController not found";
                return status;
            }

            status.isPresent = true;
            status.isOperational = lipSyncController.enabled;
            status.statusMessage = status.isOperational
                ? (lipSyncController.IsSpeaking ? "Currently speaking" : "Ready")
                : "Disabled";
            return status;
        }

        private ComponentStatus CheckExpressionStatus()
        {
            var status = new ComponentStatus { name = "Expression Library" };
            if (expressionLibrary == null)
            {
                status.isPresent = false;
                status.isOperational = false;
                status.statusMessage = "ExpressionLibraryManager not found";
                return status;
            }

            status.isPresent = true;
            var count = expressionLibrary.LoadedPresets?.Count ?? 0;
            status.isOperational = count > 0;
            status.statusMessage = $"{count} expressions loaded";
            return status;
        }

        private PerformanceMetrics CollectPerformanceMetrics()
        {
            return new PerformanceMetrics
            {
                averageLLMLatencyMs = _llmLatencies.Count > 0 ? _llmLatencies.Average() : 0f,
                averageSTTLatencyMs = _sttLatencies.Count > 0 ? _sttLatencies.Average() : 0f,
                averageTTSLatencyMs = _ttsLatencies.Count > 0 ? _ttsLatencies.Average() : 0f,
                memoryUsageMB = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024),
                cpuUsagePercent = GetCurrentCpuUsage(),
                activeThreads = Process.GetCurrentProcess().Threads.Count,
                queueLength = 0
            };
        }

        private float GetCurrentCpuUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var currentRealtime = Time.realtimeSinceStartup;
                var currentCpu = process.TotalProcessorTime;
                var realtimeDelta = currentRealtime - _lastCpuRealtime;
                if (realtimeDelta <= 0.0001f)
                {
                    return 0f;
                }

                var cpuDelta = (float)(currentCpu - _lastCpuTime).TotalMilliseconds;
                _lastCpuRealtime = currentRealtime;
                _lastCpuTime = currentCpu;
                return Mathf.Clamp01(cpuDelta / (Environment.ProcessorCount * realtimeDelta * 1000f)) * 100f;
            }
            catch
            {
                return 0f;
            }
        }

        private List<PathValidation> ValidatePaths()
        {
            var validations = new List<PathValidation>();

#if NYXARA_LLMUNITY
            var agent = llmAgent as LLMAgent;
            if (agent?.llm != null)
            {
                validations.Add(ValidatePath("LLM Model", agent.llm.model));
            }
#endif

#if NYXARA_WHISPER
            if (whisperInput != null && whisperInput.HasAssignedWhisperManager && !string.IsNullOrWhiteSpace(whisperInput.ConfiguredModelPath))
            {
                validations.Add(ValidatePath("Whisper Model", whisperInput.ConfiguredModelPath, true));
            }
#endif

            if (ttsService != null && !ttsService.HasInProcessVoice &&
                (ttsService.TtsEnabled ||
                 !string.IsNullOrWhiteSpace(ttsService.PiperExecutablePath) ||
                 !string.IsNullOrWhiteSpace(ttsService.VoiceModelPath)))
            {
                validations.Add(ValidatePath("Piper Executable", ttsService.PiperExecutablePath, true));
                validations.Add(ValidatePath("Piper Voice", ttsService.VoiceModelPath, true));
            }

            return validations;
        }

        private PathValidation ValidatePath(string name, string path, bool isRelativeToProject = false)
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
                var fileInfo = new FileInfo(fullPath);
                validation.fileSizeMB = fileInfo.Length / (1024 * 1024);
                validation.lastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
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

        private List<ConfigIssue> FindConfigIssues()
        {
            var issues = new List<ConfigIssue>();

#if NYXARA_LLMUNITY
            var agent = llmAgent as LLMAgent;
            if (agent?.llm != null && string.IsNullOrEmpty(agent.llm.model))
            {
                issues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Critical,
                    component = "LLM",
                    issue = "Model path not configured",
                    suggestion = "Set the model path in the LLM component"
                });
            }
            else if (agent == null)
            {
                issues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Warning,
                    component = "LLM",
                    issue = "LLMAgent not assigned",
                    suggestion = "Assign an LLMAgent or install LLMUnity support"
                });
            }
#else
            issues.Add(new ConfigIssue
            {
                severity = IssueSeverity.Warning,
                component = "LLM",
                issue = "LLMUnity not installed",
                suggestion = "Add the NYXARA_LLMUNITY define after installing LLMUnity to enable AI generation"
            });
#endif

            if (brain != null && brain.CharacterProfile == null)
            {
                issues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Warning,
                    component = "Character",
                    issue = "No CharacterProfile assigned",
                    suggestion = "Assign a CharacterProfile ScriptableObject to the brain"
                });
            }

            if (ttsService != null &&
                ttsService.AvailabilityStatus == PiperTtsAvailabilityStatus.Ready &&
                ttsService.AudioSource == null)
            {
                issues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Warning,
                    component = "TTS",
                    issue = "No AudioSource assigned",
                    suggestion = "Add and assign an AudioSource to PiperTtsService"
                });
            }

            if (faceDriver != null)
            {
                var renderer = faceDriver.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null)
                {
                    issues.Add(new ConfigIssue
                    {
                        severity = IssueSeverity.Warning,
                        component = "Face",
                        issue = "No SkinnedMeshRenderer on same GameObject",
                        suggestion = "Add SkinnedMeshRenderer or wire the target renderer through the studio builder"
                    });
                }
            }

            if (whisperInput != null && whisperInput.AvailableMicrophones.Length == 1)
            {
                issues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Info,
                    component = "STT",
                    issue = "Unity currently sees only one microphone device",
                    suggestion = "If voice capture still hears ambience, verify the Windows default input device and microphone privacy settings."
                });
            }

            if (whisperInput != null && !string.IsNullOrWhiteSpace(whisperInput.LastRawTranscript))
            {
                issues.Add(new ConfigIssue
                {
                    severity = IssueSeverity.Info,
                    component = "STT",
                    issue = $"Last raw Whisper transcript: {whisperInput.LastRawTranscript}",
                    suggestion = whisperInput.GetLikelySpeechIssue()
                });
            }

            return issues;
        }

        private RuntimeSnapshot CaptureRuntimeSnapshot()
        {
            var snapshot = new RuntimeSnapshot();
            if (brain != null)
            {
                snapshot.isThinking = brain.IsBusy;
                snapshot.lastDialogue = brain.LastReply ?? string.Empty;
                if (brain.LastParsedResponse != null)
                {
                    snapshot.lastIntent = brain.LastParsedResponse.intent;
                    snapshot.lastAction = brain.LastParsedResponse.action;
                    snapshot.lastSignal = brain.LastParsedResponse.signal;
                }

                if (brain.RuntimeState != null)
                {
                    snapshot.currentMood = brain.RuntimeState.mood;
                    snapshot.trust = brain.RuntimeState.trust;
                    snapshot.affection = brain.RuntimeState.affection;
                    snapshot.suspicion = brain.RuntimeState.suspicion;
                    snapshot.currentTask = brain.RuntimeState.currentTask;
                }
            }

            if (ttsService?.AudioSource != null)
            {
                snapshot.isSpeaking = ttsService.AudioSource.isPlaying;
            }

            if (memoryController != null)
            {
                snapshot.memoryCount = memoryController.GetMemoryCount();
                snapshot.savedEventMemoryCount = memoryController.GetSavedEventCount();
                snapshot.savedRelationshipMemoryCount = memoryController.GetSavedRelationshipEventCount();
                snapshot.memoryStorageSummary = memoryController.GetMemoryStorageSummary();
            }

            snapshot.timeSinceLastResponse = _lastResponseRealtime <= 0f ? 0f : Time.realtimeSinceStartup - _lastResponseRealtime;
            return snapshot;
        }

        private void OnBrainReplyReady(string reply)
        {
            _lastResponseRealtime = Time.realtimeSinceStartup;
        }

        private void OnResponseParsed(NPCResponseData response)
        {
            if (enableDetailedLogging)
            {
                Debug.Log($"[DIAG] Response: Intent={response.intent}, Mood={response.mood}, Signal={response.signal}, Action={response.action}");
            }
        }

        private void OnTranscriptReady(string transcript)
        {
            if (enableDetailedLogging && !string.IsNullOrWhiteSpace(transcript))
            {
                Debug.Log($"[DIAG] Transcript: \"{transcript}\"");
            }
        }

        public void LogError(string component, string error)
        {
            _errorHistory.Enqueue($"[{DateTime.Now:HH:mm:ss}] {component}: {error}");
            while (_errorHistory.Count > maxErrorHistory)
            {
                _errorHistory.Dequeue();
            }

            OnErrorLogged?.Invoke(component, error);
            if (enableDetailedLogging)
            {
                Debug.LogError($"[DIAG ERROR] {component}: {error}");
            }
        }

        public void RecordLLMLatency(float latencyMs)
        {
            EnqueueLatency(_llmLatencies, latencyMs);
        }

        public void RecordSTTLatency(float latencyMs)
        {
            EnqueueLatency(_sttLatencies, latencyMs);
        }

        public void RecordTTSLatency(float latencyMs)
        {
            EnqueueLatency(_ttsLatencies, latencyMs);
        }

        private static void EnqueueLatency(Queue<float> queue, float latencyMs)
        {
            queue.Enqueue(latencyMs);
            while (queue.Count > 10)
            {
                queue.Dequeue();
            }
        }

        public void StopMonitoring()
        {
            IsMonitoring = false;
        }

        private void OnDestroy()
        {
            StopMonitoring();
            if (brain != null)
            {
                brain.ReplyReady -= OnBrainReplyReady;
                brain.ResponseParsed -= OnResponseParsed;
            }

            if (whisperInput != null)
            {
                whisperInput.TranscriptReady -= OnTranscriptReady;
            }
        }
    }
}
