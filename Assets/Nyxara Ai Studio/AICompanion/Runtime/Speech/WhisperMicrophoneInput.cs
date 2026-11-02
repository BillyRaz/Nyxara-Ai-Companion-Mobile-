// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nyxara.AICompanion.Core;
using UnityEngine;
#if NYXARA_WHISPER
using Whisper;
#endif

namespace Nyxara.AICompanion.Speech
{
    // Player voice capture is intentionally independent from character profiles and prompt authoring.
    // This component only records/transcribes player speech and must not inherit behavioral rules from Nyxara's profile.
    public class WhisperMicrophoneInput : MonoBehaviour
    {
        private const string MissingWhisperMessage = "Nyxara AI Studio: Whisper not installed. Speech-to-text features disabled.";
        private const string MissingWhisperReferenceMessage = "Nyxara AI Studio: Whisper is enabled but no WhisperManager is assigned. Speech-to-text features disabled.";
        private static readonly Regex NonSpeechTranscriptRegex = new(@"^\s*(\[[^\]]+\]|\([^()]+\))\s*$", RegexOptions.Compiled);
        private static readonly Regex ContainsWordRegex = new(@"[A-Za-z]{2,}", RegexOptions.Compiled);
        private static readonly Regex AmbientDescriptorRegex = new(@"\b(engine|revving|water|running|bell|birds?|chirping|wind|traffic|noise|static|hum|buzz|music|clapping|footsteps?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonWordNoiseRegex = new(@"^[^A-Za-z0-9]+$", RegexOptions.Compiled);
        private static readonly Regex BracketedTagRegex = new(@"^\s*[\[(].*[\])]\s*$", RegexOptions.Compiled);
        private static readonly Regex MusicWrappedTranscriptRegex = new(@"^\s*[♪♫♬♩].*[♪♫♬♩]\s*$", RegexOptions.Compiled);
        private static readonly Regex MusicMarkerRegex = new(@"[♪♫♬♩]", RegexOptions.Compiled);
        private static readonly Regex RepeatedLyricPhraseRegex = new(@"\b(.{8,}?)\b\s*,?\s+\b\1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ConversationalCueRegex = new(@"\b(hi|hello|hey|what|why|how|who|where|when|can|could|would|will|name|help)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonAlphanumericForEchoRegex = new(@"[^a-z0-9\s]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [SerializeField] private MonoBehaviour whisperManager;
        [SerializeField] private NyxaraCompanionBrain companionBrain;
        [SerializeField] private string microphoneDevice = string.Empty;
        [SerializeField] private string preferredMicrophoneName = string.Empty;
        [SerializeField] private bool preferDefaultMicrophone = true;
        [SerializeField] private bool forceExplicitMicrophoneSelection = false;
        [SerializeField] private int recordingLengthSeconds = 5;
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private bool autoReplyAfterTranscription = true;
        [SerializeField] private float minimumRecordingSeconds = 0.35f;
        [SerializeField] private float silenceRmsThreshold = 0.008f;
        [SerializeField] private float minPeakForSpeech = 0.025f;
        [SerializeField] private float preTranscriptionGain = 1.5f;
        [SerializeField] private bool ignoreLikelyNonSpeechTranscripts = true;
        [SerializeField] private bool blockWhileBrainBusy = true;
        [SerializeField] private bool autoFallbackToFirstAvailableMic = true;
        [SerializeField] private bool logAvailableMicrophonesOnEnable = true;
        [SerializeField] private bool logDetailedAudioDiagnostics = true;
        [SerializeField] private bool defaultToNaturalCapture = true;
        [SerializeField] private bool boostQuietSpeechBeforeTranscription = false;
        [SerializeField] private float targetPeakAmplitude = 0.9f;
        [SerializeField] private float quietSpeechPeakThreshold = 0.12f;
        [SerializeField] private float maxPreampGain = 8f;
        [SerializeField] private bool trimEdgeSilenceBeforeTranscription = false;
        [SerializeField] private float edgeSilenceThreshold = 0.0035f;
        [SerializeField] private bool allowRejectedTranscriptsInDebug = false;
        [SerializeField] private bool bypassQuietCaptureRejectionInDebug = false;
        [SerializeField] private bool rejectMusicLikeTranscripts = true;
        [SerializeField] private bool rejectBracketedNonSpeechTags = true;
        [SerializeField] private bool allowMusicLikeTranscriptsInDebug = false;
        [SerializeField] private bool logDetailedTranscriptValidation = true;
        [SerializeField] private bool rejectNpcEchoTranscripts = true;
        [SerializeField] private int npcEchoMaxWordsBeyondReply = 4;
        [SerializeField] private int npcEchoMinSharedWords = 5;

        private AudioClip _recordingClip;
        private bool _hasLoggedMissingDependency;
        private bool _hasLoggedMissingReference;
        private string _activeRecordingDevice;
        private int _lastKnownSamplePosition;
        private string _lastMicrophoneDiagnostic = "Microphone not initialized.";
        private string _lastRawTranscript = string.Empty;
        private float _lastRecordingDurationSeconds;
        private float _lastRecordingRms;
        private float _lastRecordingPeak;
        private float _lastAppliedPreampGain = 1f;
        private bool _loggedDevicesThisSession;
        private bool _captureModeChosenAtRuntime;
        private string _lastNormalizedTranscript = string.Empty;
        private string _lastRejectedTranscriptReason = string.Empty;
        private bool _lastTranscriptWasRejected;
        private bool _lastTranscriptDebugBypassUsed;
        private bool _lastTranscriptForwardedToBrain;
        private string _lastForwardingDecision = "No transcript processed yet.";
        private string _lastCaptureFailureMode = "No capture yet";
        private string _lastResolvedMicrophoneRoute = "Using default mic fallback";

#pragma warning disable CS0067
        public event Action<string> TranscriptReady;
#pragma warning restore CS0067

        public bool IsRecording { get; private set; }
        public string LastTranscript { get; private set; }
        public NyxaraCompanionBrain CompanionBrain => companionBrain;
        public bool IsWhisperAvailable => TryGetWhisperManager(out _);
        public bool HasAssignedWhisperManager => whisperManager != null;
        public string ActiveMicrophoneDevice => ResolveMicrophoneDevice();
        public string[] AvailableMicrophones => Microphone.devices ?? Array.Empty<string>();
        public string ActiveRecordingDevice => string.IsNullOrWhiteSpace(_activeRecordingDevice) ? "<default>" : _activeRecordingDevice;
        public string LastMicrophoneDiagnostic => _lastMicrophoneDiagnostic;
        public string LastRawTranscript => _lastRawTranscript;
        public float LastRecordingDurationSeconds => _lastRecordingDurationSeconds;
        public float LastRecordingRms => _lastRecordingRms;
        public float LastRecordingPeak => _lastRecordingPeak;
        public float LastAppliedPreampGain => _lastAppliedPreampGain;
        public bool IsNaturalMicCapture => !boostQuietSpeechBeforeTranscription && !trimEdgeSilenceBeforeTranscription;
        public string CaptureModeLabel => IsNaturalMicCapture ? "Natural" : "Assisted";
        public string LastNormalizedTranscript => _lastNormalizedTranscript;
        public string LastRejectedTranscriptReason => _lastRejectedTranscriptReason;
        public bool LastTranscriptWasRejected => _lastTranscriptWasRejected;
        public bool LastTranscriptDebugBypassUsed => _lastTranscriptDebugBypassUsed;
        public bool LastTranscriptForwardedToBrain => _lastTranscriptForwardedToBrain;
        public string LastForwardingDecision => _lastForwardingDecision;
        public bool AllowRejectedTranscriptsInDebug => allowRejectedTranscriptsInDebug;
        public bool BypassQuietCaptureRejectionInDebug => bypassQuietCaptureRejectionInDebug;
        public bool ForceExplicitMicrophoneSelection => forceExplicitMicrophoneSelection;
        public string PreferredMicrophoneName => preferredMicrophoneName;
        public float MinRmsForSpeech => silenceRmsThreshold;
        public float MinPeakForSpeech => minPeakForSpeech;
        public float PreTranscriptionGain => preTranscriptionGain;
        public bool LogDetailedAudioDiagnostics => logDetailedAudioDiagnostics;
        public bool RejectMusicLikeTranscripts => rejectMusicLikeTranscripts;
        public bool RejectBracketedNonSpeechTags => rejectBracketedNonSpeechTags;
        public bool AllowMusicLikeTranscriptsInDebug => allowMusicLikeTranscriptsInDebug;
        public bool LogDetailedTranscriptValidation => logDetailedTranscriptValidation;
        public string LastCaptureFailureMode => _lastCaptureFailureMode;
        public string LastResolvedMicrophoneRoute => _lastResolvedMicrophoneRoute;
        public int RecordingLengthSeconds => recordingLengthSeconds;

        public void SetRecordingLengthSeconds(int seconds)
        {
            recordingLengthSeconds = Mathf.Clamp(seconds, 1, 60);
        }

        public string ConfiguredModelPath
        {
            get
            {
#if NYXARA_WHISPER
                return TryGetWhisperManager(out var manager) ? manager.ModelPath : string.Empty;
#else
                return string.Empty;
#endif
            }
        }

        public void StartRecording()
        {
            if (IsRecording)
            {
                return;
            }

            if (!CanUseWhisper())
            {
                return;
            }

            if (blockWhileBrainBusy && companionBrain != null && companionBrain.IsBusy)
            {
                Debug.Log("[Nyxara STT] Ignoring mic start because Nyxara is still generating a reply.");
                return;
            }

            LogAvailableMicrophonesIfNeeded();

            var deviceToUse = ResolveMicrophoneDevice();
            if (deviceToUse == null && (Microphone.devices == null || Microphone.devices.Length == 0))
            {
                _lastMicrophoneDiagnostic = "Unity did not report any microphone devices.";
                _lastCaptureFailureMode = "No microphone devices available";
                Debug.LogWarning("[Nyxara STT] No microphone devices were found by Unity.");
                return;
            }

            _recordingClip = Microphone.Start(deviceToUse, false, recordingLengthSeconds, sampleRate);
            _activeRecordingDevice = deviceToUse;
            _lastKnownSamplePosition = 0;
            IsRecording = _recordingClip != null;

            if (!IsRecording && autoFallbackToFirstAvailableMic && deviceToUse == null)
            {
                var fallbackDevice = GetFirstAvailableMicrophone();
                if (!string.IsNullOrWhiteSpace(fallbackDevice))
                {
                    Debug.LogWarning($"[Nyxara STT] Default microphone route failed to start. Retrying with explicit device '{fallbackDevice}'.");
                    _recordingClip = Microphone.Start(fallbackDevice, false, recordingLengthSeconds, sampleRate);
                    _activeRecordingDevice = fallbackDevice;
                    _lastKnownSamplePosition = 0;
                    _lastResolvedMicrophoneRoute = $"Using explicit mic '{fallbackDevice}' after default fallback failed";
                    IsRecording = _recordingClip != null;
                }
            }

            if (!IsRecording)
            {
                _lastCaptureFailureMode = "Failed to start microphone capture";
                _lastMicrophoneDiagnostic = $"Failed to start microphone recording. Requested device: '{deviceToUse ?? "<default>"}'.";
                Debug.LogWarning($"Nyxara AI Studio: {_lastMicrophoneDiagnostic}");
            }
            else
            {
                _lastCaptureFailureMode = "Capture in progress";
                _lastMicrophoneDiagnostic = $"Recording from '{ActiveRecordingDevice}' at {sampleRate} Hz.";
                Debug.Log($"[Nyxara STT] {_lastMicrophoneDiagnostic}");
            }
        }

        public void CancelRecording()
        {
            if (!IsRecording && _recordingClip == null)
            {
                return;
            }

            StopMicrophoneRecording();
            ResetTranscriptDecisionState();
            LastTranscript = string.Empty;
            _lastRawTranscript = string.Empty;
            _lastNormalizedTranscript = string.Empty;
            _lastRejectedTranscriptReason = "recording canceled";
            _lastCaptureFailureMode = "Recording canceled";
            _lastForwardingDecision = "Recording canceled before transcription.";
            _lastMicrophoneDiagnostic = "Recording canceled before transcription.";
            _lastKnownSamplePosition = 0;
        }

        public bool TryGetLiveCaptureStats(out float rms, out float peak, out float capturedSeconds)
        {
            rms = 0f;
            peak = 0f;
            capturedSeconds = 0f;

            if (!IsRecording || _recordingClip == null)
            {
                return false;
            }

            var deviceToUse = _activeRecordingDevice;
            var samplePosition = GetCurrentSamplePosition(deviceToUse);
            if (samplePosition <= 0 || sampleRate <= 0)
            {
                return false;
            }

            capturedSeconds = samplePosition / (float)sampleRate;

            var channels = Mathf.Max(1, _recordingClip.channels);
            var windowSampleFrames = Mathf.Clamp(Mathf.RoundToInt(sampleRate * 0.2f), 256, samplePosition);
            var sampleBuffer = new float[windowSampleFrames * channels];
            var offsetSamples = Mathf.Max(0, samplePosition - windowSampleFrames);
            if (!_recordingClip.GetData(sampleBuffer, offsetSamples))
            {
                return false;
            }

            peak = ComputePeak(sampleBuffer);
            double sumSquares = 0d;
            for (var i = 0; i < sampleBuffer.Length; i++)
            {
                sumSquares += sampleBuffer[i] * sampleBuffer[i];
            }

            rms = sampleBuffer.Length > 0
                ? Mathf.Sqrt((float)(sumSquares / sampleBuffer.Length))
                : 0f;

            return true;
        }

        public float GetCurrentCaptureSeconds()
        {
            if (!IsRecording || _recordingClip == null || sampleRate <= 0)
            {
                return 0f;
            }

            var samplePosition = GetCurrentSamplePosition(_activeRecordingDevice);
            if (samplePosition <= 0)
            {
                return 0f;
            }

            return samplePosition / (float)sampleRate;
        }

#if NYXARA_WHISPER
        public async Task<string> StopRecordingAndTranscribeAsync()
#else
        public Task<string> StopRecordingAndTranscribeAsync()
#endif
        {
#if NYXARA_WHISPER
            if (!IsRecording || _recordingClip == null)
            {
                return string.Empty;
            }

            ResetTranscriptDecisionState();

            if (!CanUseWhisper())
            {
                StopMicrophoneRecording();
                return string.Empty;
            }

            var deviceToUse = _activeRecordingDevice;
            var samplePosition = GetCurrentSamplePosition(deviceToUse);
            Microphone.End(deviceToUse);
            IsRecording = false;
            if (samplePosition <= 0)
            {
                samplePosition = Mathf.Clamp(_lastKnownSamplePosition, 0, _recordingClip.samples);
            }

            if (samplePosition <= 0)
            {
                _lastMicrophoneDiagnostic = $"Microphone '{deviceToUse ?? "<default>"}' returned no captured samples.";
                RejectCaptureBeforeTranscription("no captured samples", "No audio samples were captured.", _lastMicrophoneDiagnostic, true);
                _activeRecordingDevice = null;
                _lastKnownSamplePosition = 0;
                return string.Empty;
            }

            var clip = PrepareClipForTranscription(_recordingClip, samplePosition);
            if (clip == null || clip.length < minimumRecordingSeconds)
            {
                _lastRecordingDurationSeconds = clip != null ? clip.length : 0f;
                _lastMicrophoneDiagnostic = $"Recorded clip was too short ({_lastRecordingDurationSeconds:0.00}s).";
                RejectCaptureBeforeTranscription("captured clip too short", "Rejected before STT because the clip was too short.", "Ignoring transcript because the captured clip was too short.");
                _activeRecordingDevice = null;
                _lastKnownSamplePosition = 0;
                return string.Empty;
            }

            _lastRecordingDurationSeconds = clip.length;
            _lastRecordingRms = ComputeRms(clip);
            _lastRecordingPeak = ComputePeak(clip);
            if (logDetailedAudioDiagnostics)
            {
                Debug.Log($"[Nyxara STT] Capture stats: duration={_lastRecordingDurationSeconds:0.00}s, rms={_lastRecordingRms:0.0000}, peak={_lastRecordingPeak:0.0000}, gain={_lastAppliedPreampGain:0.00}x, route={_lastResolvedMicrophoneRoute}, activeMic='{ActiveRecordingDevice}'.");
            }
            var quietCaptureReason = EvaluateQuietCaptureReason(_lastRecordingRms, _lastRecordingPeak);
            if (!string.IsNullOrWhiteSpace(quietCaptureReason))
            {
                _lastMicrophoneDiagnostic = $"Captured audio RMS {_lastRecordingRms:0.0000} and peak {_lastRecordingPeak:0.0000} did not pass the speech gate (min RMS {silenceRmsThreshold:0.0000}, min peak {minPeakForSpeech:0.0000}).";
                if (bypassQuietCaptureRejectionInDebug)
                {
                    _lastTranscriptDebugBypassUsed = true;
                    _lastCaptureFailureMode = "Capture too quiet before transcription (bypass active)";
                    _lastForwardingDecision = $"Quiet capture flagged before STT, but debug bypass allowed Whisper transcription: {quietCaptureReason}.";
                    Debug.LogWarning($"[Nyxara STT] Quiet capture bypass active. Continuing to Whisper despite gate: {quietCaptureReason}");
                }
                else
                {
                    RejectCaptureBeforeTranscription("captured audio too quiet", "Rejected before STT because the captured audio was too quiet.", $"Ignoring transcript because the captured clip was too quiet. {quietCaptureReason}", true);
                    _activeRecordingDevice = null;
                    _lastKnownSamplePosition = 0;
                    return string.Empty;
                }
            }

            var manager = GetWhisperManagerOrNull();
            if (manager == null)
            {
                _activeRecordingDevice = null;
                _lastKnownSamplePosition = 0;
                return string.Empty;
            }

            var result = await manager.GetTextAsync(clip);
            _lastRawTranscript = result?.Result?.Trim() ?? string.Empty;
            var transcript = NormalizeTranscript(result?.Result);

            LastTranscript = transcript;
            TranscriptReady?.Invoke(transcript);
            _lastMicrophoneDiagnostic = string.IsNullOrWhiteSpace(transcript)
                ? $"Audio captured from '{ActiveRecordingDevice}', but the transcript was filtered or empty. RMS={_lastRecordingRms:0.0000}, Peak={_lastRecordingPeak:0.0000}, Gain={_lastAppliedPreampGain:0.00}x."
                : $"Audio captured from '{ActiveRecordingDevice}' and transcribed successfully. RMS={_lastRecordingRms:0.0000}, Peak={_lastRecordingPeak:0.0000}, Gain={_lastAppliedPreampGain:0.00}x.";

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _lastCaptureFailureMode = GetCaptureFailureModeForTranscriptRejection(_lastRejectedTranscriptReason);
                _lastForwardingDecision = string.IsNullOrWhiteSpace(_lastRejectedTranscriptReason)
                    ? "No usable speech detected."
                    : $"No usable speech detected: {_lastRejectedTranscriptReason}.";
            }
            else
            {
                _lastCaptureFailureMode = "Forwarded successfully";
            }

            if (autoReplyAfterTranscription &&
                companionBrain != null &&
                !string.IsNullOrWhiteSpace(transcript) &&
                (!blockWhileBrainBusy || !companionBrain.IsBusy))
            {
                _lastTranscriptForwardedToBrain = true;
                _lastCaptureFailureMode = "Forwarded successfully";
                _lastForwardingDecision = "Transcript accepted and forwarded to Nyxara.";
                await companionBrain.ReplyToAsync(transcript);
            }
            else if (companionBrain != null && companionBrain.IsBusy && !string.IsNullOrWhiteSpace(transcript))
            {
                _lastTranscriptForwardedToBrain = false;
                _lastCaptureFailureMode = "Transcript accepted but not forwarded";
                _lastForwardingDecision = "Transcript accepted, but not forwarded because Nyxara is already generating a reply.";
                Debug.Log("[Nyxara STT] Transcript captured, but auto-send was skipped because Nyxara is still replying.");
            }
            else if (!string.IsNullOrWhiteSpace(transcript))
            {
                _lastTranscriptForwardedToBrain = false;
                _lastCaptureFailureMode = "Transcript accepted but not forwarded";
                _lastForwardingDecision = autoReplyAfterTranscription
                    ? "Transcript accepted, but no companion brain was available for forwarding."
                    : "Transcript accepted, but auto-reply after transcription is disabled.";
            }

            _activeRecordingDevice = null;
            _lastKnownSamplePosition = 0;

            return transcript;
#else
            if (!IsRecording || _recordingClip == null)
            {
                return Task.FromResult(string.Empty);
            }

            if (!CanUseWhisper())
            {
                StopMicrophoneRecording();
                return Task.FromResult(string.Empty);
            }

            var samplePosition = Microphone.GetPosition(microphoneDevice);
            Microphone.End(microphoneDevice);
            IsRecording = false;

            if (samplePosition <= 0)
            {
                return Task.FromResult(string.Empty);
            }

            _ = autoReplyAfterTranscription;
            return Task.FromResult(string.Empty);
#endif
        }

        private void Awake()
        {
            ValidateSetup();
        }

        private void OnEnable()
        {
            ValidateSetup();
            if (defaultToNaturalCapture && !_captureModeChosenAtRuntime)
            {
                SetNaturalMicCapture(true, false);
            }
            LogAvailableMicrophonesIfNeeded();
        }

        private bool CanUseWhisper()
        {
            ValidateSetup();
            return TryGetWhisperManager(out _);
        }

        private void ValidateSetup()
        {
#if NYXARA_WHISPER
            if (whisperManager == null && !_hasLoggedMissingReference)
            {
                _hasLoggedMissingReference = true;
                Debug.LogWarning(MissingWhisperReferenceMessage, this);
            }
#else
            if (!_hasLoggedMissingDependency)
            {
                _hasLoggedMissingDependency = true;
                Debug.LogWarning(MissingWhisperMessage, this);
            }
#endif
        }

        private void StopMicrophoneRecording()
        {
            var deviceToUse = _activeRecordingDevice ?? ResolveMicrophoneDevice();
            if (Microphone.IsRecording(deviceToUse))
            {
                Microphone.End(deviceToUse);
            }

            IsRecording = false;
            _activeRecordingDevice = null;
            _lastKnownSamplePosition = 0;
        }

        private int GetCurrentSamplePosition(string deviceToUse)
        {
            if (_recordingClip == null)
            {
                return 0;
            }

            var samplePosition = Microphone.GetPosition(deviceToUse);
            if (samplePosition > 0)
            {
                _lastKnownSamplePosition = Mathf.Clamp(samplePosition, 0, _recordingClip.samples);
                return _lastKnownSamplePosition;
            }

            return Mathf.Clamp(_lastKnownSamplePosition, 0, _recordingClip.samples);
        }

        private string ResolveMicrophoneDevice()
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                _lastResolvedMicrophoneRoute = "No microphone devices available";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredMicrophoneName))
            {
                foreach (var device in devices)
                {
                    if (device.IndexOf(preferredMicrophoneName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _lastResolvedMicrophoneRoute = $"Using explicit preferred mic '{device}'";
                        return device;
                    }
                }

                Debug.LogWarning($"[Nyxara STT] Preferred microphone '{preferredMicrophoneName}' was not found. Falling back to other routing rules.");
            }

            if (!string.IsNullOrWhiteSpace(microphoneDevice))
            {
                foreach (var device in devices)
                {
                    if (string.Equals(device, microphoneDevice, StringComparison.Ordinal))
                    {
                        _lastResolvedMicrophoneRoute = $"Using explicit mic '{device}'";
                        return device;
                    }
                }

                Debug.LogWarning($"[Nyxara STT] Configured microphone '{microphoneDevice}' was not found. Falling back to {(preferDefaultMicrophone ? "default" : "first available")} microphone.");
            }

            if (forceExplicitMicrophoneSelection)
            {
                _lastResolvedMicrophoneRoute = $"Using explicit first detected mic '{devices[0]}'";
                return devices[0];
            }

            if (preferDefaultMicrophone)
            {
                _lastResolvedMicrophoneRoute = "Using default mic fallback";
                return null;
            }

            _lastResolvedMicrophoneRoute = $"Using first available mic '{devices[0]}'";
            return devices[0];
        }

        public void UseDefaultMicrophone()
        {
            microphoneDevice = string.Empty;
            preferredMicrophoneName = string.Empty;
            preferDefaultMicrophone = true;
            forceExplicitMicrophoneSelection = false;
            _lastResolvedMicrophoneRoute = "Using default mic fallback";
            _lastMicrophoneDiagnostic = "Configured to use the system default microphone.";
            Debug.Log("[Nyxara STT] Switched to system default microphone routing.");
        }

        public void CycleToNextMicrophone()
        {
            var devices = AvailableMicrophones;
            if (devices.Length == 0)
            {
                _lastMicrophoneDiagnostic = "No microphone devices are available to cycle.";
                Debug.LogWarning("[Nyxara STT] No microphone devices are available to cycle.");
                return;
            }

            preferDefaultMicrophone = false;
            forceExplicitMicrophoneSelection = true;
            var currentIndex = Array.FindIndex(devices, device => string.Equals(device, microphoneDevice, StringComparison.Ordinal));
            var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % devices.Length;
            microphoneDevice = devices[nextIndex];
            preferredMicrophoneName = microphoneDevice;
            _lastResolvedMicrophoneRoute = $"Using explicit mic '{microphoneDevice}'";
            _lastMicrophoneDiagnostic = $"Selected microphone '{microphoneDevice}'.";
            Debug.Log($"[Nyxara STT] {_lastMicrophoneDiagnostic}");
        }

        public string GetMicrophoneDebugSummary()
        {
            var devices = AvailableMicrophones;
            var resolvedDevice = ResolveMicrophoneDevice();
            var configuredDevice = string.IsNullOrWhiteSpace(microphoneDevice) ? "<none>" : microphoneDevice;
            var preferredDevice = string.IsNullOrWhiteSpace(preferredMicrophoneName) ? "<none>" : preferredMicrophoneName;
            var availableLabel = devices.Length == 0 ? "<none>" : string.Join(", ", devices);
            return $"Preferred: {preferredDevice} | Configured: {configuredDevice} | Resolved: {resolvedDevice ?? "<default>"} | Route: {_lastResolvedMicrophoneRoute} | Active: {ActiveRecordingDevice} | Devices: {availableLabel}";
        }

        public string GetLikelySpeechIssue()
        {
            if (AvailableMicrophones.Length == 0)
            {
                return "Unity does not see any microphone devices.";
            }

            if (_lastTranscriptWasRejected && !string.IsNullOrWhiteSpace(_lastRejectedTranscriptReason))
            {
                return string.Equals(_lastRejectedTranscriptReason, "blank audio", StringComparison.OrdinalIgnoreCase)
                    ? "Whisper received audio but returned [BLANK_AUDIO]. The mic path is active, but spoken voice still is not reaching Whisper strongly enough."
                    : _lastRejectedTranscriptReason.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Whisper produced a music-like or lyric-style transcript, so Nyxara blocked it before dialogue generation."
                    : $"Transcript was rejected: {_lastRejectedTranscriptReason}.";
            }

            if (string.Equals(_lastCaptureFailureMode, "No capture yet", StringComparison.Ordinal))
            {
                return "No recent player voice capture has been processed yet.";
            }

            if (_lastRecordingDurationSeconds > 0f && !string.IsNullOrWhiteSpace(EvaluateQuietCaptureReason(_lastRecordingRms, _lastRecordingPeak)))
            {
                return "Captured audio is too quiet. Check Windows mic gain, input privacy permissions, and physical microphone selection.";
            }

            if (!string.IsNullOrWhiteSpace(_lastRawTranscript) && NonSpeechTranscriptRegex.IsMatch(_lastRawTranscript))
            {
                return "Whisper hears environmental audio instead of speech. Move closer to the mic or fix the Windows/default input source.";
            }

            if (string.Equals(_lastRawTranscript, "[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase))
            {
                return "Whisper received near-empty audio. The mic path is alive, but spoken voice is not reaching it strongly enough.";
            }

            if (!string.IsNullOrWhiteSpace(_lastMicrophoneDiagnostic) && _lastMicrophoneDiagnostic.IndexOf("filtered or empty", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Audio reached Whisper, but no usable spoken transcript was produced.";
            }

            return "No clear voice issue detected from the last capture.";
        }

        private string GetFirstAvailableMicrophone()
        {
            var devices = AvailableMicrophones;
            return devices.Length > 0 ? devices[0] : null;
        }

        private void LogAvailableMicrophonesIfNeeded()
        {
            if (!logAvailableMicrophonesOnEnable || _loggedDevicesThisSession)
            {
                return;
            }

            _loggedDevicesThisSession = true;
            var devices = AvailableMicrophones;
            _lastMicrophoneDiagnostic = devices.Length == 0
                ? "Unity reported no microphone devices."
                : $"Detected {devices.Length} microphone device(s): {string.Join(", ", devices)}";
            Debug.Log($"[Nyxara STT] {_lastMicrophoneDiagnostic}");
            if (devices.Length > 0)
            {
                Debug.Log($"[Nyxara STT] Microphone routing summary: {GetMicrophoneDebugSummary()}");
            }
        }

        private AudioClip PrepareClipForTranscription(AudioClip clip, int sampleLength)
        {
            var channels = clip.channels;
            var samples = new float[sampleLength * channels];
            clip.GetData(samples, 0);

            var preparedSamples = samples;
            if (trimEdgeSilenceBeforeTranscription)
            {
                preparedSamples = TrimEdgeSilence(preparedSamples, channels, edgeSilenceThreshold);
            }

            _lastAppliedPreampGain = 1f;
            if (preTranscriptionGain > 1f)
            {
                var safeGain = Mathf.Clamp(preTranscriptionGain, 1f, maxPreampGain);
                ApplyGain(preparedSamples, safeGain);
                _lastAppliedPreampGain = safeGain;
            }

            if (boostQuietSpeechBeforeTranscription)
            {
                var detectedPeak = ComputePeak(preparedSamples);
                if (detectedPeak > 0f && detectedPeak < quietSpeechPeakThreshold)
                {
                    var gain = Mathf.Clamp(targetPeakAmplitude / detectedPeak, 1f, maxPreampGain);
                    ApplyGain(preparedSamples, gain);
                    _lastAppliedPreampGain *= gain;
                }
            }

            var preparedSampleLength = preparedSamples.Length / channels;
            var preparedClip = AudioClip.Create($"{clip.name}_prepared", preparedSampleLength, channels, clip.frequency, false);
            preparedClip.SetData(preparedSamples, 0);
            return preparedClip;
        }

        public void SetNaturalMicCapture(bool enabled)
        {
            SetNaturalMicCapture(enabled, true);
        }

        public void SetAllowRejectedTranscriptsInDebug(bool enabled)
        {
            allowRejectedTranscriptsInDebug = enabled;
            Debug.Log(enabled
                ? "[Nyxara STT] Debug bypass enabled. Rejected transcripts may still be forwarded for testing."
                : "[Nyxara STT] Debug bypass disabled. Rejected transcripts will not be forwarded.");
        }

        public void SetAllowMusicLikeTranscriptsInDebug(bool enabled)
        {
            allowMusicLikeTranscriptsInDebug = enabled;
            Debug.Log(enabled
                ? "[Nyxara STT] Music-like transcript debug bypass enabled. Lyric/noise-style transcripts may be kept for testing."
                : "[Nyxara STT] Music-like transcript debug bypass disabled. Music-like transcripts will be rejected.");
        }

        public void SetBypassQuietCaptureRejectionInDebug(bool enabled)
        {
            bypassQuietCaptureRejectionInDebug = enabled;
            Debug.Log(enabled
                ? "[Nyxara STT] Quiet capture debug bypass enabled. Quiet audio will still be sent to Whisper for testing."
                : "[Nyxara STT] Quiet capture debug bypass disabled. Quiet audio will be blocked before Whisper.");
        }

        public void SetForceExplicitMicrophoneSelection(bool enabled)
        {
            forceExplicitMicrophoneSelection = enabled;
            preferDefaultMicrophone = !enabled && string.IsNullOrWhiteSpace(microphoneDevice) && string.IsNullOrWhiteSpace(preferredMicrophoneName);
            Debug.Log(enabled
                ? "[Nyxara STT] Explicit microphone selection enabled. Nyxara will avoid '<default>' routing when possible."
                : "[Nyxara STT] Explicit microphone selection disabled. Nyxara may use '<default>' routing again.");
        }

        private void SetNaturalMicCapture(bool enabled, bool markAsChosenAtRuntime)
        {
            if (markAsChosenAtRuntime)
            {
                _captureModeChosenAtRuntime = true;
            }

            if (enabled)
            {
                boostQuietSpeechBeforeTranscription = false;
                trimEdgeSilenceBeforeTranscription = false;
                _lastMicrophoneDiagnostic = "Natural mic capture enabled. Nyxara will use raw microphone audio without speech boosting.";
                Debug.Log("[Nyxara STT] Natural mic capture enabled.");
                return;
            }

            boostQuietSpeechBeforeTranscription = true;
            trimEdgeSilenceBeforeTranscription = true;
            _lastMicrophoneDiagnostic = "Assisted mic capture enabled. Nyxara will lightly prepare quiet audio before transcription.";
            Debug.Log("[Nyxara STT] Assisted mic capture enabled.");
        }

        private void ResetTranscriptDecisionState()
        {
            _lastTranscriptWasRejected = false;
            _lastTranscriptDebugBypassUsed = false;
            _lastTranscriptForwardedToBrain = false;
            _lastRejectedTranscriptReason = string.Empty;
            _lastNormalizedTranscript = string.Empty;
            _lastCaptureFailureMode = "Capture pending";
            _lastForwardingDecision = "Transcript processing started.";
        }

        private void RejectCaptureBeforeTranscription(string rejectionReason, string forwardingDecision, string logMessage, bool warning = false)
        {
            _lastTranscriptWasRejected = true;
            _lastTranscriptDebugBypassUsed = false;
            _lastTranscriptForwardedToBrain = false;
            _lastRejectedTranscriptReason = rejectionReason;
            _lastRawTranscript = string.Empty;
            _lastNormalizedTranscript = string.Empty;
            _lastCaptureFailureMode = rejectionReason switch
            {
                "no captured samples" => "Captured clip was empty",
                "captured clip too short" => "Captured clip was too short",
                "captured audio too quiet" => "Capture too quiet before transcription",
                _ => "Capture rejected before transcription"
            };
            _lastForwardingDecision = forwardingDecision;
            LastTranscript = string.Empty;
            TranscriptReady?.Invoke(string.Empty);

            Debug.Log("[Nyxara STT] Raw transcript: ");
            Debug.Log("[Nyxara STT] Normalized transcript: ");
            Debug.Log($"[Nyxara STT] Rejected before Whisper transcription: {rejectionReason}");

            if (warning)
            {
                Debug.LogWarning($"[Nyxara STT] {logMessage}");
            }
            else
            {
                Debug.Log($"[Nyxara STT] {logMessage}");
            }
        }

        private string NormalizeTranscript(string rawTranscript)
        {
            var transcript = rawTranscript?.Trim() ?? string.Empty;
            Debug.Log($"[Nyxara STT] Raw transcript: {transcript}");
            if (string.IsNullOrWhiteSpace(transcript))
            {
                _lastCaptureFailureMode = "Transcribed but empty";
                _lastForwardingDecision = "Whisper returned an empty transcript.";
                Debug.Log("[Nyxara STT] Normalized transcript: ");
                Debug.Log("[Nyxara STT] Rejected as likely non-speech: empty transcript");
                return string.Empty;
            }

            if (string.Equals(transcript, "[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase))
            {
                _lastTranscriptWasRejected = true;
                _lastRejectedTranscriptReason = "blank audio";
                _lastCaptureFailureMode = "Transcribed but empty";
                _lastForwardingDecision = "Whisper returned [BLANK_AUDIO].";

                if (allowRejectedTranscriptsInDebug)
                {
                    _lastTranscriptDebugBypassUsed = true;
                    _lastNormalizedTranscript = transcript;
                    _lastForwardingDecision = "Whisper returned [BLANK_AUDIO], but debug bypass kept the raw token for testing.";
                    Debug.Log($"[Nyxara STT] Normalized transcript: {transcript}");
                    Debug.LogWarning("[Nyxara STT] Whisper returned [BLANK_AUDIO], but debug bypass kept it for inspection.");
                    return transcript;
                }

                Debug.Log("[Nyxara STT] Normalized transcript: ");
                Debug.Log("[Nyxara STT] Rejected as likely non-speech: blank audio");
                return string.Empty;
            }

            var normalized = transcript;
            if (ignoreLikelyNonSpeechTranscripts && IsLikelyNonPlayerAudioTranscript(transcript, out var rejectionReason))
            {
                _lastTranscriptWasRejected = true;
                _lastRejectedTranscriptReason = rejectionReason;
                var allowForDebug = allowRejectedTranscriptsInDebug ||
                    (allowMusicLikeTranscriptsInDebug && rejectionReason.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0);

                if (allowForDebug)
                {
                    _lastTranscriptDebugBypassUsed = true;
                    _lastNormalizedTranscript = normalized;
                    _lastForwardingDecision = $"Rejected transcript was allowed through debug bypass: {rejectionReason}.";
                    Debug.Log($"[Nyxara STT] Normalized transcript: {normalized}");
                    Debug.LogWarning($"[Nyxara STT] Rejected as likely non-speech, but forwarded in debug bypass mode: {rejectionReason}");
                    return normalized;
                }

                Debug.Log("[Nyxara STT] Normalized transcript: ");
                Debug.Log($"[Nyxara STT] Rejected as likely non-speech: {rejectionReason}");
                _lastForwardingDecision = $"Rejected as likely non-speech: {rejectionReason}.";
                return string.Empty;
            }

            if (rejectNpcEchoTranscripts && IsLikelyNpcEchoTranscript(normalized, out var echoReason))
            {
                _lastTranscriptWasRejected = true;
                _lastRejectedTranscriptReason = echoReason;

                if (allowRejectedTranscriptsInDebug)
                {
                    _lastTranscriptDebugBypassUsed = true;
                    _lastNormalizedTranscript = normalized;
                    _lastForwardingDecision = $"Rejected transcript was allowed through debug bypass: {echoReason}.";
                    Debug.Log($"[Nyxara STT] Normalized transcript: {normalized}");
                    Debug.LogWarning($"[Nyxara STT] Rejected as likely Nyxara echo, but forwarded in debug bypass mode: {echoReason}");
                    return normalized;
                }

                Debug.Log("[Nyxara STT] Normalized transcript: ");
                Debug.Log($"[Nyxara STT] Rejected as likely non-player speech: {echoReason}");
                _lastForwardingDecision = $"Rejected as likely non-player speech: {echoReason}.";
                return string.Empty;
            }

            _lastNormalizedTranscript = normalized;
            _lastForwardingDecision = "Transcript accepted by normalization.";
            if (logDetailedTranscriptValidation)
            {
                Debug.Log($"[Nyxara STT] Transcript validation accepted player speech candidate: {normalized}");
            }
            Debug.Log($"[Nyxara STT] Normalized transcript: {normalized}");
            return normalized;
        }

        private bool IsLikelyNonPlayerAudioTranscript(string transcript, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(transcript))
            {
                reason = "empty transcript";
                return true;
            }

            var normalized = transcript.Trim();
            if (string.Equals(normalized, "[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase))
            {
                reason = "blank audio";
                return true;
            }

            var tokenCount = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

            if (rejectMusicLikeTranscripts && IsLikelyMusicLikeTranscript(normalized, tokenCount, out reason))
            {
                return true;
            }

            if (rejectBracketedNonSpeechTags && NonSpeechTranscriptRegex.IsMatch(normalized))
            {
                reason = AmbientDescriptorRegex.IsMatch(normalized)
                    ? "environmental tag"
                    : "bracketed or parenthesized non-speech tag";
                return true;
            }

            if (rejectBracketedNonSpeechTags && BracketedTagRegex.IsMatch(normalized) && tokenCount <= 4)
            {
                reason = "tag-like ambient description";
                return true;
            }

            if (normalized.Length <= 2 || tokenCount == 1 && normalized.Length <= 3)
            {
                reason = "very short junk";
                return true;
            }

            if (NonWordNoiseRegex.IsMatch(normalized))
            {
                reason = "pure non-word noise";
                return true;
            }

            if (!ContainsWordRegex.IsMatch(normalized))
            {
                reason = "no clear spoken words";
                return true;
            }

            if (AmbientDescriptorRegex.IsMatch(normalized) && tokenCount <= 4)
            {
                reason = "common ambient descriptor";
                return true;
            }

            return false;
        }

        private bool IsLikelyMusicLikeTranscript(string transcript, int tokenCount, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return false;
            }

            if (MusicWrappedTranscriptRegex.IsMatch(transcript) || MusicMarkerRegex.IsMatch(transcript))
            {
                reason = "music-like transcript";
                return true;
            }

            var hasConversationalCue = transcript.IndexOf('?', StringComparison.Ordinal) >= 0 ||
                ConversationalCueRegex.IsMatch(transcript);
            if (!hasConversationalCue && tokenCount >= 8 && RepeatedLyricPhraseRegex.IsMatch(transcript))
            {
                reason = "music-like repeated lyric phrase";
                return true;
            }

            return false;
        }

        private bool IsLikelyNpcEchoTranscript(string transcript, out string reason)
        {
            reason = string.Empty;
            if (companionBrain == null || string.IsNullOrWhiteSpace(transcript))
            {
                return false;
            }

            var lastReply = companionBrain.LastReply;
            if (string.IsNullOrWhiteSpace(lastReply))
            {
                return false;
            }

            var transcriptComparable = NormalizeForEchoComparison(transcript);
            var replyComparable = NormalizeForEchoComparison(lastReply);
            if (string.IsNullOrWhiteSpace(transcriptComparable) || string.IsNullOrWhiteSpace(replyComparable))
            {
                return false;
            }

            if (string.Equals(transcriptComparable, replyComparable, StringComparison.Ordinal))
            {
                reason = "matched Nyxara's last reply almost exactly";
                return true;
            }

            if (transcriptComparable.Contains(replyComparable, StringComparison.Ordinal) ||
                replyComparable.Contains(transcriptComparable, StringComparison.Ordinal))
            {
                var transcriptWords = transcriptComparable.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var replyWords = replyComparable.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var lengthDelta = Mathf.Abs(transcriptWords.Length - replyWords.Length);
                if (lengthDelta <= npcEchoMaxWordsBeyondReply)
                {
                    reason = "closely matched Nyxara's most recent spoken reply";
                    return true;
                }
            }

            var transcriptSet = new HashSet<string>(transcriptComparable.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            var replySet = new HashSet<string>(replyComparable.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            transcriptSet.RemoveWhere(word => word.Length <= 2);
            replySet.RemoveWhere(word => word.Length <= 2);
            if (transcriptSet.Count == 0 || replySet.Count == 0)
            {
                return false;
            }

            transcriptSet.IntersectWith(replySet);
            if (transcriptSet.Count >= npcEchoMinSharedWords)
            {
                reason = $"shared {transcriptSet.Count} significant words with Nyxara's last reply";
                return true;
            }

            return false;
        }

        private static string NormalizeForEchoComparison(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();
            normalized = NonAlphanumericForEchoRegex.Replace(normalized, " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private string EvaluateQuietCaptureReason(float rms, float peak)
        {
            var rmsTooLow = rms < silenceRmsThreshold;
            var peakTooLow = peak < minPeakForSpeech;
            if (!rmsTooLow && !peakTooLow)
            {
                return string.Empty;
            }

            if (rmsTooLow && peakTooLow)
            {
                return $"RMS {rms:0.0000} < {silenceRmsThreshold:0.0000} and peak {peak:0.0000} < {minPeakForSpeech:0.0000}";
            }

            if (rmsTooLow)
            {
                return $"RMS {rms:0.0000} < {silenceRmsThreshold:0.0000}";
            }

            return $"peak {peak:0.0000} < {minPeakForSpeech:0.0000}";
        }

        private static string GetCaptureFailureModeForTranscriptRejection(string rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                return "Transcribed but empty";
            }

            if (string.Equals(rejectionReason, "blank audio", StringComparison.OrdinalIgnoreCase))
            {
                return "Rejected due to blank audio token";
            }

            if (rejectionReason.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Rejected due to music-like transcript";
            }

            if (rejectionReason.IndexOf("bracket", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rejectionReason.IndexOf("tag", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Rejected due to bracketed non-speech tag";
            }

            return "Rejected by normalization";
        }

        private static float ComputeRms(AudioClip clip)
        {
            if (clip == null)
            {
                return 0f;
            }

            var sampleCount = clip.samples * clip.channels;
            if (sampleCount <= 0)
            {
                return 0f;
            }

            var samples = new float[sampleCount];
            clip.GetData(samples, 0);
            double sumSquares = 0d;
            for (var i = 0; i < samples.Length; i++)
            {
                sumSquares += samples[i] * samples[i];
            }

            return Mathf.Sqrt((float)(sumSquares / samples.Length));
        }

        private static float ComputePeak(AudioClip clip)
        {
            if (clip == null)
            {
                return 0f;
            }

            var sampleCount = clip.samples * clip.channels;
            if (sampleCount <= 0)
            {
                return 0f;
            }

            var samples = new float[sampleCount];
            clip.GetData(samples, 0);
            return ComputePeak(samples);
        }

        private static float ComputePeak(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return 0f;
            }

            var peak = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var abs = Mathf.Abs(samples[i]);
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            return peak;
        }

        private static void ApplyGain(float[] samples, float gain)
        {
            if (samples == null || samples.Length == 0 || gain <= 1f)
            {
                return;
            }

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            }
        }

        private static float[] TrimEdgeSilence(float[] samples, int channels, float threshold)
        {
            if (samples == null || samples.Length == 0 || channels <= 0)
            {
                return samples ?? Array.Empty<float>();
            }

            var frameCount = samples.Length / channels;
            var startFrame = 0;
            var endFrame = frameCount - 1;

            while (startFrame < frameCount && FramePeak(samples, startFrame, channels) < threshold)
            {
                startFrame++;
            }

            while (endFrame > startFrame && FramePeak(samples, endFrame, channels) < threshold)
            {
                endFrame--;
            }

            var trimmedFrameCount = Mathf.Max(1, endFrame - startFrame + 1);
            if (trimmedFrameCount == frameCount)
            {
                return samples;
            }

            var trimmed = new float[trimmedFrameCount * channels];
            Array.Copy(samples, startFrame * channels, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        private static float FramePeak(float[] samples, int frameIndex, int channels)
        {
            var peak = 0f;
            var offset = frameIndex * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                var abs = Mathf.Abs(samples[offset + channel]);
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            return peak;
        }

        private void Reset()
        {
#if NYXARA_WHISPER
            whisperManager = FindFirstObjectByType<WhisperManager>();
#else
            whisperManager = null;
#endif
            companionBrain = FindFirstObjectByType<NyxaraCompanionBrain>();
            if (Microphone.devices != null && Microphone.devices.Length > 0)
            {
                microphoneDevice = Microphone.devices[0];
            }
        }

#if NYXARA_WHISPER
        private WhisperManager GetWhisperManagerOrNull()
        {
            if (TryGetWhisperManager(out var manager))
            {
                return manager;
            }

            return null;
        }

        private bool TryGetWhisperManager(out WhisperManager manager)
        {
            manager = whisperManager as WhisperManager;
            if (manager != null)
            {
                return true;
            }

            if (!_hasLoggedMissingReference)
            {
                _hasLoggedMissingReference = true;
                Debug.LogWarning(MissingWhisperReferenceMessage, this);
            }

            return false;
        }
#else
        private bool TryGetWhisperManager(out object manager)
        {
            manager = null;
            return false;
        }
#endif
    }
}
