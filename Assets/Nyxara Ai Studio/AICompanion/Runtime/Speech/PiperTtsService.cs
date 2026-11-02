// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nyxara.AICompanion.Configuration;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.LipSync;
using Unity.InferenceEngine;
using UnityEngine;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using System.Diagnostics;
using UnityEngine.Networking;
#endif

namespace Nyxara.AICompanion.Speech
{
    public enum PiperTtsAvailabilityStatus
    {
        Ready,
        NotConfigured,
        InvalidPath,
        Disabled
    }

    /// <summary>Runtime state of the in-process Piper inference engine.</summary>
    public enum PiperInitializationState
    {
        NotStarted,
        Initializing,
        Ready,
        Failed
    }

    /// <summary>
    /// Companion voice output. Synthesizes speech in-process with Unity Inference Engine and a Piper voice model,
    /// so no external <c>piper.exe</c> process is required on any platform.
    /// </summary>
    public class PiperTtsService : MonoBehaviour
    {
        private const float InitializationTimeoutSeconds = 180f;

        [Header("Voice Output (Optional)")]
        [SerializeField] private bool ttsEnabled;
        [SerializeField, HideInInspector] private bool hasMigratedLegacyConfiguration;

        [Header("In-Process Piper (required for runtime speech)")]
        [Tooltip("Piper voice model imported by Unity's Inference Engine (Assets/Speech/Piper/clean100.onnx).")]
        [SerializeField] private ModelAsset voiceModel;
        [Tooltip("Matching voice config TextAsset (Assets/Speech/Piper/clean100.onnx.json).")]
        [SerializeField] private TextAsset voiceConfig;
        [Tooltip("Folder with espeak-ng-data, relative to StreamingAssets.")]
        [SerializeField] private string espeakDataDirectory = "Speech/PiperRuntime/piper/espeak-ng-data";
        [Tooltip("Speaker index for multi-speaker voices. clean100 maps \"6209\" to 0.")]
        [SerializeField] private int speakerId = 0;

        [Header("Legacy CLI Paths (editor-only fallback)")]
        [SerializeField] private string piperExecutablePath = string.Empty;
        [SerializeField] private string voiceModelPath = string.Empty;
        [SerializeField] private string outputFileName = CompanionStackDefaults.PiperOutputFileName;
#if UNITY_EDITOR
        [Tooltip("Development only: use the legacy piper.exe when in-process initialization fails. Never included in player builds.")]
        [SerializeField] private bool enableDevelopmentCliFallback;
#endif

        [Header("Playback")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private ArkItBlendshapeDriver faceDriver;
        [SerializeField] private VisemeLipSyncController lipSyncController;
        [SerializeField] private bool useLipSync = true;

        private bool _hasLoggedUnavailable;
        private bool _hasLoggedFailure;
        private string _lastSynthesisError;

        private PiperManager _piperManager;
        private GameObject _piperRuntimeObject;
        private AudioClip _currentClip;

        private PiperInitializationState _initializationState = PiperInitializationState.NotStarted;
        private string _initializationError;
        private TaskCompletionSource<bool> _initializationTcs;
        private Task<bool> _initializationTask;

        private readonly SemaphoreSlim _speakGate = new SemaphoreSlim(1, 1);

        public bool TtsEnabled
        {
            get => ttsEnabled;
            set
            {
                ttsEnabled = value;
                ResetAvailabilityLogging();

                if (Application.isPlaying && value && _initializationTask == null)
                {
                    _ = InitializeAsync();
                }
            }
        }

        public bool IsConfigured => GetAvailabilityStatus() == PiperTtsAvailabilityStatus.Ready;
        public PiperTtsAvailabilityStatus AvailabilityStatus => GetAvailabilityStatus();
        public AudioSource AudioSource => audioSource;
        public ArkItBlendshapeDriver FaceDriver => faceDriver;
        public VisemeLipSyncController LipSyncController => lipSyncController;

        /// <summary>True when the in-process voice model and config are both assigned.</summary>
        public bool HasInProcessVoice => voiceModel != null && voiceConfig != null;

        /// <summary>
        /// True when <paramref name="configuredVoiceModelPath"/> resolves to an importable .onnx file
        /// under Assets/ with a matching .onnx.json next to it.
        /// </summary>
        public static bool HasInProcessVoiceAssets(string configuredVoiceModelPath)
        {
            var resolvedVoiceModelPath = ResolveConfiguredPath(configuredVoiceModelPath);
            if (!IsUnderAssetsFolder(resolvedVoiceModelPath))
            {
                return false;
            }

            return IsInProcessVoiceFileSet(resolvedVoiceModelPath);
        }

        private static bool IsUnderAssetsFolder(string resolvedPath)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return false;
            }

            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(resolvedPath);
            return fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInProcessVoiceFileSet(string resolvedVoiceModelPath)
        {
            if (string.IsNullOrWhiteSpace(resolvedVoiceModelPath) ||
                !File.Exists(resolvedVoiceModelPath) ||
                !string.Equals(Path.GetExtension(resolvedVoiceModelPath), ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return File.Exists(resolvedVoiceModelPath + ".json");
        }

        public ModelAsset VoiceModel
        {
            get => voiceModel;
            set
            {
                voiceModel = value;
                ResetInitialization();
            }
        }

        public TextAsset VoiceConfig
        {
            get => voiceConfig;
            set
            {
                voiceConfig = value;
                ResetInitialization();
            }
        }

        public int SpeakerId
        {
            get => speakerId;
            set => speakerId = Mathf.Max(0, value);
        }

        public string EspeakDataDirectory
        {
            get => espeakDataDirectory;
            set
            {
                espeakDataDirectory = value;
                ResetInitialization();
            }
        }

        /// <summary>Current initialization state of the in-process engine.</summary>
        public PiperInitializationState InitializationState => _initializationState;

        /// <summary>Reason for the last initialization failure, or null.</summary>
        public string InitializationError => _initializationError;

        /// <summary>True only when the engine finished loading and is ready to synthesize.</summary>
        public bool IsInitialized =>
            _initializationState == PiperInitializationState.Ready &&
            _piperManager != null &&
            _piperManager.IsInitialized;

        /// <summary>The runtime manager. Null until initialization starts.</summary>
        public PiperManager RuntimeManager => _piperManager;

        public string PiperExecutablePath
        {
            get => piperExecutablePath;
            set
            {
                piperExecutablePath = value;
                ResetAvailabilityLogging();
            }
        }

        public string VoiceModelPath
        {
            get => voiceModelPath;
            set
            {
                voiceModelPath = value;
                ResetAvailabilityLogging();
            }
        }

        public void SetLipSyncController(VisemeLipSyncController controller)
        {
            lipSyncController = controller;
        }

        public PiperTtsAvailabilityStatus GetAvailabilityStatus()
        {
            if (!ttsEnabled)
            {
                return PiperTtsAvailabilityStatus.Disabled;
            }

            // In-process speech is the primary path and needs no executable.
            if (voiceModel != null && voiceConfig != null)
            {
                return PiperTtsAvailabilityStatus.Ready;
            }

#if UNITY_EDITOR
            if (enableDevelopmentCliFallback &&
                EvaluateAvailabilityStatus(true, piperExecutablePath, voiceModelPath) == PiperTtsAvailabilityStatus.Ready)
            {
                return PiperTtsAvailabilityStatus.Ready;
            }
#endif

            if (EvaluateAvailabilityStatus(true, piperExecutablePath, voiceModelPath) == PiperTtsAvailabilityStatus.Ready)
            {
                return PiperTtsAvailabilityStatus.Ready;
            }

            if (string.IsNullOrWhiteSpace(piperExecutablePath) &&
                string.IsNullOrWhiteSpace(voiceModelPath) &&
                voiceModel == null &&
                voiceConfig == null)
            {
                return PiperTtsAvailabilityStatus.NotConfigured;
            }

            return PiperTtsAvailabilityStatus.InvalidPath;
        }

        /// <summary>
        /// Starts the in-process engine asynchronously. Safe to call repeatedly; concurrent callers share one task.
        /// </summary>
        public Task<bool> InitializeAsync()
        {
            if (IsInitialized)
            {
                return Task.FromResult(true);
            }

            if (_initializationTask != null)
            {
                return _initializationTask;
            }

            _initializationTcs = new TaskCompletionSource<bool>();
            _initializationTask = _initializationTcs.Task;

            if (!isActiveAndEnabled || !Application.isPlaying)
            {
                CompleteInitialization(false, "Piper can only initialize while the component is active in play mode.");
                return _initializationTask;
            }

            StartCoroutine(InitializeRoutine());
            return _initializationTask;
        }

        /// <summary>Allows a new initialization attempt after a failure or asset change.</summary>
        public void ResetInitialization()
        {
            if (_initializationState == PiperInitializationState.Initializing)
            {
                return;
            }

            DestroyPiperManager();
            _initializationTask = null;
            _initializationTcs = null;
            _initializationError = null;
            _initializationState = PiperInitializationState.NotStarted;
            _hasLoggedFailure = false;
        }

        private IEnumerator InitializeRoutine()
        {
            _initializationState = PiperInitializationState.Initializing;
            _initializationError = null;

            if (!ttsEnabled)
            {
                CompleteInitialization(false, "Voice output is disabled.");
                yield break;
            }

            if (voiceModel == null || voiceConfig == null)
            {
                CompleteInitialization(
                    false,
                    "In-process Piper needs both a voice model ModelAsset and its voice config TextAsset. " +
                    "Assign Assets/Speech/Piper/clean100.onnx and clean100.onnx.json.");
                yield break;
            }

            CreatePiperManager();
            if (_piperManager == null)
            {
                CompleteInitialization(false, "The runtime PiperManager could not be created.");
                yield break;
            }

            _piperManager.Initialize();

            float startedAt = Time.realtimeSinceStartup;
            while (_piperManager.IsInitializing &&
                   Time.realtimeSinceStartup - startedAt < InitializationTimeoutSeconds)
            {
                yield return null;
            }

            if (!_piperManager.IsInitialized)
            {
                string error = !string.IsNullOrWhiteSpace(_piperManager.InitializationError)
                    ? _piperManager.InitializationError
                    : (_piperManager.IsInitializing
                        ? $"Piper initialization timed out after {InitializationTimeoutSeconds:F0}s while loading {voiceModel.name}."
                        : "Piper initialization did not complete.");

                DestroyPiperManager();
                CompleteInitialization(false, error);
                yield break;
            }

            CompleteInitialization(true, null);
        }

        private void CompleteInitialization(bool success, string error)
        {
            _initializationState = success
                ? PiperInitializationState.Ready
                : PiperInitializationState.Failed;
            _initializationError = error;
            _initializationTcs?.TrySetResult(success);

            if (success)
            {
                Debug.Log($"[Nyxara TTS] In-process Piper ready (speaker {speakerId}).");
                return;
            }

            Debug.LogWarning($"[Nyxara TTS] In-process Piper is unavailable: {error}");
        }

        private void CreatePiperManager()
        {
            if (_piperManager != null)
            {
                return;
            }

            _piperRuntimeObject = new GameObject("Piper TTS Runtime");
            _piperRuntimeObject.SetActive(false);
            _piperRuntimeObject.transform.SetParent(transform, false);

            var tokenizer = _piperRuntimeObject.AddComponent<ESpeakTokenizer>();
            tokenizer.jsonFile = voiceConfig;

            // RequireComponent adds the AudioSource. It is unused for companion playback but keeps PiperManager self-contained.
            _piperManager = _piperRuntimeObject.AddComponent<PiperManager>();
            _piperManager.modelAsset = voiceModel;
            _piperManager.tokenizer = tokenizer;
            _piperManager.espeakDataDirectory = espeakDataDirectory;
            _piperManager.speakerId = Mathf.Max(0, speakerId);
            _piperManager.initializeOnStart = false;

            // Activate after every field is assigned so Awake/Start see the final configuration.
            _piperRuntimeObject.SetActive(true);
        }

        private void DestroyPiperManager()
        {
            if (_piperRuntimeObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_piperRuntimeObject);
                }

                _piperRuntimeObject = null;
            }

            _piperManager = null;
        }

        public async Task<AudioClip> SpeakAsync(string text)
        {
            return await SpeakAsync(text, CancellationToken.None);
        }

        /// <summary>
        /// Synthesizes and plays <paramref name="text"/> in-process. Requests are serialized so only one
        /// inference call touches the worker at a time. Returns the generated clip, or null on failure/cancellation.
        /// </summary>
        public async Task<AudioClip> SpeakAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var availabilityStatus = GetAvailabilityStatus();
            if (availabilityStatus != PiperTtsAvailabilityStatus.Ready)
            {
                LogUnavailableOnce(availabilityStatus);
                return null;
            }

            bool gateEntered = false;
            try
            {
                try
                {
                    await _speakGate.WaitAsync(cancellationToken);
                    gateEntered = true;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                bool initialized = IsInitialized || await InitializeAsync();
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (!initialized)
                {
#if UNITY_EDITOR
                    if (enableDevelopmentCliFallback)
                    {
                        return await SpeakWithCliFallbackAsync(text);
                    }
#endif
                    LogFailureOnce(_initializationError ?? "Piper could not be initialized.");
                    return null;
                }

                AudioClip clip = await SynthesizeClipAsync(text, cancellationToken);
                if (clip == null)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        LogFailureOnce(_lastSynthesisError ?? "Speech synthesis failed.");
                    }

                    return null;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Destroy(clip);
                    return null;
                }

                PlayClip(clip, text);
                return clip;
            }
            catch (Exception ex)
            {
                LogFailureOnce($"Piper voice output is unavailable: {ex.Message}");
                return null;
            }
            finally
            {
                if (gateEntered)
                {
                    _speakGate.Release();
                }
            }
        }

        private async Task<AudioClip> SynthesizeClipAsync(string text, CancellationToken cancellationToken)
        {
            if (_piperManager == null)
            {
                _lastSynthesisError = "Piper is not initialized.";
                return null;
            }

            var completion = new TaskCompletionSource<AudioClip>();
            _lastSynthesisError = null;

            bool accepted = _piperManager.Synthesize(
                text,
                clip => completion.TrySetResult(clip),
                error =>
                {
                    _lastSynthesisError = error;
                    completion.TrySetResult(null);
                });

            if (!accepted)
            {
                return null;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return await completion.Task;
            }

            var cancellationSignal = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancellationSignal.TrySetResult(true)))
            {
                var finished = await Task.WhenAny(completion.Task, cancellationSignal.Task);
                if (finished != completion.Task)
                {
                    _piperManager?.CancelSynthesis();
                    return null;
                }
            }

            return await completion.Task;
        }

        /// <summary>Stops playback and cancels any active synthesis.</summary>
        public void StopSpeaking()
        {
            _piperManager?.CancelSynthesis();

            if (audioSource != null)
            {
                audioSource.Stop();
                if (audioSource.clip == _currentClip)
                {
                    audioSource.clip = null;
                }
            }

            lipSyncController?.StopLipSync();
            faceDriver?.SetSpeaking(false);

            if (_currentClip != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_currentClip);
                }

                _currentClip = null;
            }
        }

        private void PlayClip(AudioClip clip, string text)
        {
            if (_currentClip != null && _currentClip != clip)
            {
                Destroy(_currentClip);
            }

            _currentClip = clip;

            if (audioSource == null)
            {
                Debug.LogWarning("[Nyxara TTS] No AudioSource assigned. The clip was generated but cannot be played through this service.");
                return;
            }

            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.loop = false;
            audioSource.Play();

            if (useLipSync && lipSyncController != null)
            {
                _ = lipSyncController.SpeakWithLipSync(text, clip.length);
                StartCoroutine(StopLipSyncAfterAudio());
            }

            if (faceDriver != null)
            {
                faceDriver.SetSpeaking(true);
                StartCoroutine(faceDriver.ReleaseSpeakingWhenSourceStops(audioSource));
            }
        }

        private IEnumerator StopLipSyncAfterAudio()
        {
            if (audioSource == null)
            {
                yield break;
            }

            while (audioSource.isPlaying)
            {
                yield return null;
            }

            lipSyncController?.StopLipSync();
        }

#if UNITY_EDITOR
        // -----------------------------------------------------------------
        // Editor-only development fallback. Never compiled into player builds.
        // -----------------------------------------------------------------

        private async Task<AudioClip> SpeakWithCliFallbackAsync(string text)
        {
            var outputPath = Path.Combine(Application.persistentDataPath, outputFileName);
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                await RunPiperAsync(text, outputPath);
                if (!File.Exists(outputPath))
                {
                    LogFailureOnce("Legacy Piper finished without producing an audio file.");
                    return null;
                }

                var clip = await LoadClipAsync(outputPath);
                if (clip != null)
                {
                    PlayClip(clip, text);
                }

                return clip;
            }
            catch (Exception ex)
            {
                LogFailureOnce($"Legacy Piper fallback failed: {ex.Message}");
                return null;
            }
        }

        private async Task RunPiperAsync(string text, string outputPath)
        {
            var resolvedExecutablePath = ResolveConfiguredPath(piperExecutablePath);
            var resolvedVoiceModelPath = ResolveConfiguredPath(voiceModelPath);
            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedExecutablePath,
                Arguments = $"--model \"{resolvedVoiceModelPath}\" --output_file \"{outputPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            await process.StandardInput.WriteAsync(text);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            await WaitForExitAsync(process);

            var stdErr = await stdErrTask;
            if (process.ExitCode != 0)
            {
                var stdOut = await stdOutTask;
                throw new InvalidOperationException(
                    $"Piper exited with code {process.ExitCode}.{Environment.NewLine}{stdOut}{Environment.NewLine}{stdErr}");
            }
        }

        private static Task WaitForExitAsync(Process process)
        {
            if (process.HasExited)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();

            void ProcessExited(object sender, EventArgs args)
            {
                process.Exited -= ProcessExited;
                tcs.TrySetResult(true);
            }

            process.EnableRaisingEvents = true;
            process.Exited += ProcessExited;

            if (process.HasExited)
            {
                process.Exited -= ProcessExited;
                return Task.CompletedTask;
            }

            return tcs.Task;
        }

        private static async Task<AudioClip> LoadClipAsync(string outputPath)
        {
            var uri = new Uri(outputPath).AbsoluteUri;
            using var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV);
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Nyxara TTS] Failed to load generated audio clip: {request.error}");
                return null;
            }

            return DownloadHandlerAudioClip.GetContent(request);
        }
#endif

        private void Awake()
        {
            UpgradeLegacyConfigurationIfNeeded();

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }
        }

        private void Start()
        {
            if (!ttsEnabled)
            {
                return;
            }

            if (voiceModel != null || voiceConfig != null)
            {
                _ = InitializeAsync();
            }
        }

        private void OnDisable()
        {
            StopSpeaking();
        }

        private void OnDestroy()
        {
            StopSpeaking();
            DestroyPiperManager();
            _initializationTcs?.TrySetResult(false);
        }

        private void Reset()
        {
            audioSource = GetComponent<AudioSource>();
            faceDriver = FindFirstObjectByType<ArkItBlendshapeDriver>();
            lipSyncController = FindFirstObjectByType<VisemeLipSyncController>();
        }

        private void OnValidate()
        {
            UpgradeLegacyConfigurationIfNeeded();
            speakerId = Mathf.Max(0, speakerId);
        }

        private void UpgradeLegacyConfigurationIfNeeded()
        {
            if (hasMigratedLegacyConfiguration)
            {
                return;
            }

            if (!ttsEnabled &&
                !string.IsNullOrWhiteSpace(piperExecutablePath) &&
                !string.IsNullOrWhiteSpace(voiceModelPath))
            {
                ttsEnabled = true;
            }

            hasMigratedLegacyConfiguration = true;
        }

        private void ResetAvailabilityLogging()
        {
            _hasLoggedUnavailable = false;
            _hasLoggedFailure = false;
        }

        private void LogUnavailableOnce(PiperTtsAvailabilityStatus status)
        {
            if (_hasLoggedUnavailable || status == PiperTtsAvailabilityStatus.Disabled)
            {
                return;
            }

            _hasLoggedUnavailable = true;
            var message = status switch
            {
                PiperTtsAvailabilityStatus.NotConfigured => "[Nyxara TTS] Voice output is optional and not configured. Text replies will continue without audio.",
                PiperTtsAvailabilityStatus.InvalidPath => "[Nyxara TTS] Voice output is optional but the Piper voice assets are missing. Text replies will continue without audio.",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (status == PiperTtsAvailabilityStatus.InvalidPath)
            {
                Debug.LogWarning(message);
                return;
            }

            Debug.Log(message);
        }

        private void LogFailureOnce(string message)
        {
            if (_hasLoggedFailure)
            {
                return;
            }

            _hasLoggedFailure = true;
            Debug.LogWarning($"[Nyxara TTS] {message}");
        }

        public static PiperTtsAvailabilityStatus EvaluateAvailabilityStatus(bool isEnabled, string configuredExecutablePath, string configuredVoiceModelPath)
        {
            if (!isEnabled)
            {
                return PiperTtsAvailabilityStatus.Disabled;
            }

            var hasExecutable = !string.IsNullOrWhiteSpace(configuredExecutablePath);
            var hasVoice = !string.IsNullOrWhiteSpace(configuredVoiceModelPath);

            if (hasExecutable && hasVoice)
            {
                var resolvedExecutablePath = ResolveConfiguredPath(configuredExecutablePath);
                var resolvedVoiceModelPath = ResolveConfiguredPath(configuredVoiceModelPath);
                if (!string.IsNullOrWhiteSpace(resolvedExecutablePath) &&
                    !string.IsNullOrWhiteSpace(resolvedVoiceModelPath) &&
                    File.Exists(resolvedExecutablePath) &&
                    File.Exists(resolvedVoiceModelPath) &&
                    string.Equals(Path.GetExtension(resolvedVoiceModelPath), ".onnx", StringComparison.OrdinalIgnoreCase))
                {
                    return PiperTtsAvailabilityStatus.Ready;
                }
            }

            // In-process voice assets live under Assets/ and need no piper executable.
            if (hasVoice && HasInProcessVoiceAssets(configuredVoiceModelPath))
            {
                return PiperTtsAvailabilityStatus.Ready;
            }

            if (!hasExecutable && !hasVoice)
            {
                return PiperTtsAvailabilityStatus.NotConfigured;
            }

            return PiperTtsAvailabilityStatus.InvalidPath;
        }

        public static string GetStatusLabel(PiperTtsAvailabilityStatus status)
        {
            return status switch
            {
                PiperTtsAvailabilityStatus.Ready => "Ready",
                PiperTtsAvailabilityStatus.NotConfigured => "Not Configured",
                PiperTtsAvailabilityStatus.InvalidPath => "Invalid Path",
                PiperTtsAvailabilityStatus.Disabled => "Disabled",
                _ => "Unknown"
            };
        }

        public static string GetStatusGuidance(PiperTtsAvailabilityStatus status)
        {
            return status switch
            {
                PiperTtsAvailabilityStatus.Ready => "Voice output is ready (in-process Piper).",
                PiperTtsAvailabilityStatus.NotConfigured => "Voice output is optional. Assign a Piper voice model plus its .onnx.json config to speak replies in-process.",
                PiperTtsAvailabilityStatus.InvalidPath => "Voice output is optional. Assign Assets/Speech/Piper/clean100.onnx and clean100.onnx.json to the voice service.",
                PiperTtsAvailabilityStatus.Disabled => "Voice output is currently disabled. Enable it if you want spoken replies.",
                _ => string.Empty
            };
        }

        public static string ResolveConfiguredPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            var normalized = configuredPath.Replace('\\', '/').TrimStart('/');
            const string assetsStreamingAssetsPrefix = "Assets/StreamingAssets/";
            if (normalized.StartsWith(assetsStreamingAssetsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(assetsStreamingAssetsPrefix.Length);
            }
            else if (normalized.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("StreamingAssets/".Length);
            }

            var streamingAssetsCandidate = Path.Combine(Application.streamingAssetsPath, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(streamingAssetsCandidate))
            {
                return streamingAssetsCandidate;
            }

            // Assets-relative paths (for example "Speech/Piper/clean100.onnx") are used by the editor-only
            // CLI fallback so the model does not have to be duplicated inside StreamingAssets.
            var assetsRelative = normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring("Assets/".Length)
                : normalized;
            var assetsCandidate = Path.Combine(Application.dataPath, assetsRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(assetsCandidate))
            {
                return assetsCandidate;
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), normalized.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
