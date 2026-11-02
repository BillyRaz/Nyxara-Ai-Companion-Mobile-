// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Linq;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Speech;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nyxara.AICompanion.UI
{
    // Runtime player input UI is intentionally separated from Nyxara's authored character profile.
    // This overlay should only forward player text/voice input and display runtime status.
    public class RuntimeConversationOverlay : MonoBehaviour
    {
        [SerializeField] private WhisperMicrophoneInput whisperInput;
        [SerializeField] private NyxaraCompanionBrain companionBrain;
        [SerializeField] private KeyCode micHoldKey = KeyCode.V;
        [SerializeField] private KeyCode promptPopupKey = KeyCode.T;
        [SerializeField] private bool enableSmartVoiceCapture;
        [SerializeField] private float smartCaptureSpeechStartRms = 0.02f;
        [SerializeField] private float smartCaptureSpeechStartPeak = 0.06f;
        [SerializeField] private float smartCaptureSpeechEndRms = 0.008f;
        [SerializeField] private float smartCaptureSpeechEndPeak = 0.02f;
        [SerializeField] private float smartCaptureSpeechConfirmSeconds = 0.2f;
        [SerializeField] private float smartCaptureMinSpeechSecondsBeforePauseSend = 0.45f;
        [SerializeField] private float smartCaptureSilenceSecondsToSend = 0.85f;
        [SerializeField] private float smartCaptureMaxSpeechSeconds = 8f;
        [SerializeField] private float smartCaptureMaxListenSecondsBeforeSpeechRetry = 3.5f;
        [SerializeField] private float smartCaptureNoiseFloorFollowSpeed = 0.08f;
        [SerializeField] private float smartCaptureRelativeEndRmsFactor = 0.38f;
        [SerializeField] private float smartCaptureRelativeEndPeakFactor = 0.42f;
        [SerializeField] private float smartCaptureNoiseFloorRmsMultiplier = 2.2f;
        [SerializeField] private float smartCaptureNoiseFloorPeakMultiplier = 2.0f;
        [SerializeField] private float smartCapturePostReplyCooldownSeconds = 6.5f;
        [SerializeField] private int smartCaptureRecordingLengthSeconds = 10;
        [SerializeField] private float smartCaptureRetryCooldownSeconds = 0.9f;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showDiagnosticPromptCheckBar;
        [SerializeField] private bool showPromptPopup;
        [SerializeField] private string promptText = "Hello Nyxara.";

        private bool _micHeldLastFrame;
        private bool _isShuttingDown;
        private bool _smartSpeechDetected;
        private bool _voiceSendInFlight;
        private float _smartLastSpeechRealtime;
        private float _smartSpeechDetectedRealtime;
        private float _smartListeningStartedRealtime;
        private float _smartSpeechCandidateSinceRealtime = -1f;
        private float _smartSilenceCandidateSinceRealtime = -1f;
        private float _smartStatsUnavailableSinceRealtime = -1f;
        private float _smartRetryNotBeforeRealtime;
        private float _smartNoiseFloorRms;
        private float _smartNoiseFloorPeak;
        private float _smartDetectedSpeechRms;
        private float _smartDetectedSpeechPeak;
        private int _defaultRecordingLengthSeconds;
        private string _status = "Initializing...";
        private string _lastTranscript = string.Empty;
        private string _lastReply = string.Empty;

        public bool ShowDiagnosticPromptCheckBar
        {
            get => showDiagnosticPromptCheckBar;
            set => showDiagnosticPromptCheckBar = value;
        }

        public bool EnableSmartVoiceCapture
        {
            get => enableSmartVoiceCapture;
            set
            {
                if (enableSmartVoiceCapture == value)
                {
                    return;
                }

                enableSmartVoiceCapture = value;
                ResetSmartCaptureState();
                Debug.Log(enableSmartVoiceCapture
                    ? "[Nyxara Runtime] Smart voice capture enabled."
                    : "[Nyxara Runtime] Smart voice capture disabled.");

                if (!enableSmartVoiceCapture && whisperInput != null && whisperInput.IsRecording)
                {
                    whisperInput.CancelRecording();
                }

                if (!enableSmartVoiceCapture)
                {
                    RestoreDefaultRecordingWindow();
                }
            }
        }

        private void Awake()
        {
            if (whisperInput == null)
            {
                whisperInput = GetComponentInChildren<WhisperMicrophoneInput>(true);
            }

            if (companionBrain == null)
            {
                companionBrain = GetComponent<NyxaraCompanionBrain>();
            }

            _defaultRecordingLengthSeconds = whisperInput != null
                ? whisperInput.RecordingLengthSeconds
                : 5;
        }

        private void OnEnable()
        {
            _isShuttingDown = false;

            if (whisperInput != null)
            {
                whisperInput.TranscriptReady += HandleTranscriptReady;
            }

            if (companionBrain != null)
            {
                companionBrain.ReplyReady += HandleReplyReady;
            }

            _status = GetReadyStatus();
        }

        private void OnDisable()
        {
            BeginSmartCaptureShutdown();

            if (whisperInput != null)
            {
                whisperInput.TranscriptReady -= HandleTranscriptReady;
            }

            if (companionBrain != null)
            {
                companionBrain.ReplyReady -= HandleReplyReady;
            }
        }

        private void OnDestroy()
        {
            BeginSmartCaptureShutdown();
        }

        private void OnApplicationQuit()
        {
            BeginSmartCaptureShutdown();
        }

        private void Update()
        {
            if (_isShuttingDown)
            {
                return;
            }

            if (GetKeyDownCompat(promptPopupKey))
            {
                showPromptPopup = !showPromptPopup;
                if (showPromptPopup && enableSmartVoiceCapture && whisperInput != null && whisperInput.IsRecording)
                {
                    whisperInput.CancelRecording();
                    RestoreDefaultRecordingWindow();
                    ResetSmartCaptureState();
                    _status = "Typed input opened.";
                }
            }

            var sttReady = whisperInput != null && whisperInput.IsWhisperAvailable;
            var brainPresent = companionBrain != null;
            var llmReady = brainPresent && companionBrain.IsLlmAvailable;
            var canContinueActiveVoiceFlow = whisperInput != null &&
                (whisperInput.IsRecording || _voiceSendInFlight || _smartSpeechDetected || enableSmartVoiceCapture);

            if (!sttReady || !brainPresent || (!llmReady && !canContinueActiveVoiceFlow))
            {
                _status = GetReadyStatus();
                return;
            }

            if (_voiceSendInFlight)
            {
                return;
            }

            if (!showPromptPopup)
            {
                if (enableSmartVoiceCapture)
                {
                    UpdateSmartVoiceCapture();
                    return;
                }

                var micHeld = GetKeyCompat(micHoldKey);
                if (micHeld && !_micHeldLastFrame && !whisperInput.IsRecording)
                {
                    if (companionBrain != null && companionBrain.IsBusy)
                    {
                        _status = "Nyxara is still replying. Wait before sending another mic input.";
                    }
                    else
                    {
                        whisperInput.StartRecording();
                        _status = whisperInput.IsRecording
                            ? $"Recording... release {micHoldKey} to send"
                            : "Microphone failed to start recording.";
                        Debug.Log($"[Nyxara Runtime] Mic hold started with key {micHoldKey}.");
                    }
                }

                if (!micHeld && _micHeldLastFrame && whisperInput.IsRecording)
                {
                    _ = StopAndSendAsync("Mic hold released. Transcribing and sending...");
                }

                _micHeldLastFrame = micHeld;
            }
        }

        private void UpdateSmartVoiceCapture()
        {
            if (_isShuttingDown || whisperInput == null || companionBrain == null)
            {
                return;
            }

            var canOnlyWaitForBrain = companionBrain.IsBusy && !whisperInput.IsRecording;
            if (canOnlyWaitForBrain)
            {
                _status = "Nyxara is still replying. Smart voice capture is waiting.";
                return;
            }

            if (!whisperInput.IsRecording)
            {
                if (Time.realtimeSinceStartup < _smartRetryNotBeforeRealtime)
                {
                    _status = "Smart capture is waiting before retrying...";
                    return;
                }

                ApplySmartRecordingWindow();
                whisperInput.StartRecording();
                if (whisperInput.IsRecording)
                {
                    _smartListeningStartedRealtime = Time.realtimeSinceStartup;
                    _smartLastSpeechRealtime = Time.realtimeSinceStartup;
                    _smartSpeechDetectedRealtime = 0f;
                    _smartSpeechCandidateSinceRealtime = -1f;
                    _smartSilenceCandidateSinceRealtime = -1f;
                    _smartNoiseFloorRms = 0f;
                    _smartNoiseFloorPeak = 0f;
                    _smartDetectedSpeechRms = 0f;
                    _smartDetectedSpeechPeak = 0f;
                    _smartSpeechDetected = false;
                    _status = "Smart capture listening...";
                    Debug.Log("[Nyxara Runtime] Smart voice capture started listening.");
                }
                else
                {
                    RestoreDefaultRecordingWindow();
                    _smartRetryNotBeforeRealtime = Time.realtimeSinceStartup + smartCaptureRetryCooldownSeconds;
                    _status = "Smart capture could not start the microphone.";
                }

                return;
            }

            if (!whisperInput.TryGetLiveCaptureStats(out var rms, out var peak, out var capturedSeconds))
            {
                HandleSmartCaptureWithoutLiveStats();
                return;
            }

            _smartStatsUnavailableSinceRealtime = -1f;

            var speechStartRms = Mathf.Max(whisperInput.MinRmsForSpeech, smartCaptureSpeechStartRms);
            var speechStartPeak = Mathf.Max(whisperInput.MinPeakForSpeech, smartCaptureSpeechStartPeak);
            var speechEndRms = Mathf.Max(whisperInput.MinRmsForSpeech * 0.75f, smartCaptureSpeechEndRms);
            var speechEndPeak = Mathf.Max(whisperInput.MinPeakForSpeech * 0.75f, smartCaptureSpeechEndPeak);
            var aboveSpeechStartGate = rms >= speechStartRms || peak >= speechStartPeak;

            if (!_smartSpeechDetected)
            {
                if (!aboveSpeechStartGate)
                {
                    if (_smartNoiseFloorRms <= 0f)
                    {
                        _smartNoiseFloorRms = rms;
                        _smartNoiseFloorPeak = peak;
                    }
                    else
                    {
                        _smartNoiseFloorRms = Mathf.Lerp(_smartNoiseFloorRms, rms, smartCaptureNoiseFloorFollowSpeed);
                        _smartNoiseFloorPeak = Mathf.Lerp(_smartNoiseFloorPeak, peak, smartCaptureNoiseFloorFollowSpeed);
                    }
                }

                if (smartCaptureMaxListenSecondsBeforeSpeechRetry > 0f &&
                    capturedSeconds >= smartCaptureMaxListenSecondsBeforeSpeechRetry)
                {
                    whisperInput.CancelRecording();
                    RestoreDefaultRecordingWindow();
                    ResetSmartCaptureState();
                    _smartRetryNotBeforeRealtime = Time.realtimeSinceStartup + 0.2f;
                    _status = "Smart capture retrying microphone listen...";
                    Debug.Log($"[Nyxara Runtime] Smart voice capture heard no confirmed speech after {capturedSeconds:0.00}s. Restarting the listen window.");
                    return;
                }

                if (aboveSpeechStartGate)
                {
                    if (_smartSpeechCandidateSinceRealtime < 0f)
                    {
                        _smartSpeechCandidateSinceRealtime = Time.realtimeSinceStartup;
                    }

                    var candidateDuration = Time.realtimeSinceStartup - _smartSpeechCandidateSinceRealtime;
                    if (candidateDuration >= smartCaptureSpeechConfirmSeconds)
                    {
                        _smartSpeechDetected = true;
                        _smartLastSpeechRealtime = Time.realtimeSinceStartup;
                        _smartSpeechDetectedRealtime = _smartLastSpeechRealtime;
                        _smartSpeechCandidateSinceRealtime = -1f;
                        _smartSilenceCandidateSinceRealtime = -1f;
                        _smartDetectedSpeechRms = rms;
                        _smartDetectedSpeechPeak = peak;
                        _status = "Smart capture detected speech...";
                        Debug.Log($"[Nyxara Runtime] Smart voice capture detected sustained speech. rms={rms:0.0000}, peak={peak:0.0000}, confirm={candidateDuration:0.00}s");
                        return;
                    }

                    _status = "Smart capture confirming speech...";
                    return;
                }

                _smartSpeechCandidateSinceRealtime = -1f;
            }

            if (_smartSpeechDetected)
            {
                var speechDuration = Time.realtimeSinceStartup - _smartSpeechDetectedRealtime;
                var dynamicEndRms = Mathf.Max(
                    speechEndRms,
                    _smartNoiseFloorRms * smartCaptureNoiseFloorRmsMultiplier,
                    _smartDetectedSpeechRms * smartCaptureRelativeEndRmsFactor);
                var dynamicEndPeak = Mathf.Max(
                    speechEndPeak,
                    _smartNoiseFloorPeak * smartCaptureNoiseFloorPeakMultiplier,
                    _smartDetectedSpeechPeak * smartCaptureRelativeEndPeakFactor);
                var hasSpeechNow = rms >= dynamicEndRms || peak >= dynamicEndPeak;

                if (hasSpeechNow)
                {
                    _smartDetectedSpeechRms = Mathf.Max(_smartDetectedSpeechRms, rms);
                    _smartDetectedSpeechPeak = Mathf.Max(_smartDetectedSpeechPeak, peak);
                    _smartLastSpeechRealtime = Time.realtimeSinceStartup;
                    _smartSilenceCandidateSinceRealtime = -1f;
                    _status = "Smart capture hearing speech...";
                    if (speechDuration >= smartCaptureMaxSpeechSeconds)
                    {
                        Debug.Log($"[Nyxara Runtime] Smart voice capture reached max speech window ({speechDuration:0.00}s). Sending as a failsafe because no natural pause was detected.");
                        ResetSmartCaptureState();
                        _ = StopAndSendAsync("Smart capture max speech window reached. Transcribing and sending...");
                    }
                    return;
                }

                if (_smartSilenceCandidateSinceRealtime < 0f)
                {
                    _smartSilenceCandidateSinceRealtime = Time.realtimeSinceStartup;
                }

                var silenceDuration = Time.realtimeSinceStartup - _smartSilenceCandidateSinceRealtime;
                if (silenceDuration >= smartCaptureSilenceSecondsToSend)
                {
                    if (speechDuration < smartCaptureMinSpeechSecondsBeforePauseSend)
                    {
                        Debug.Log($"[Nyxara Runtime] Smart voice capture ignored a short burst ({speechDuration:0.00}s) followed by silence.");
                        ResetSmartCaptureState();
                        _smartRetryNotBeforeRealtime = Time.realtimeSinceStartup + 0.2f;
                        _status = "Smart capture ignored a short noise burst. Listening again...";
                        return;
                    }

                    Debug.Log($"[Nyxara Runtime] Smart voice capture detected end of speech after {silenceDuration:0.00}s of silence. Sending.");
                    ResetSmartCaptureState();
                    _ = StopAndSendAsync("Smart capture finished. Transcribing and sending...");
                    return;
                }
            }

            if (!_smartSpeechDetected &&
                whisperInput.RecordingLengthSeconds > 0 &&
                capturedSeconds >= whisperInput.RecordingLengthSeconds - 0.15f)
            {
                whisperInput.CancelRecording();
                _smartLastSpeechRealtime = Time.realtimeSinceStartup;
                _smartSpeechCandidateSinceRealtime = -1f;
                _status = "Smart capture is still listening...";
                Debug.Log("[Nyxara Runtime] Smart voice capture restarted idle listening window.");
            }
        }

        private void HandleSmartCaptureWithoutLiveStats()
        {
            if (whisperInput == null || !whisperInput.IsRecording)
            {
                _smartStatsUnavailableSinceRealtime = -1f;
                return;
            }

            if (_smartStatsUnavailableSinceRealtime < 0f)
            {
                _smartStatsUnavailableSinceRealtime = Time.realtimeSinceStartup;
                Debug.Log(_smartSpeechDetected
                    ? "[Nyxara Runtime] Smart voice capture lost live mic levels after speech started. Waiting briefly before sending."
                    : "[Nyxara Runtime] Smart voice capture could not read live mic levels yet. Waiting for fallback window.");
            }

            _status = _smartSpeechDetected ? "Smart capture finishing speech..." : "Smart capture listening...";

            if (_smartSpeechDetected)
            {
                var statsUnavailableDuration = Time.realtimeSinceStartup - _smartStatsUnavailableSinceRealtime;
                var utteranceDuration = _smartSpeechDetectedRealtime > 0f
                    ? Time.realtimeSinceStartup - _smartSpeechDetectedRealtime
                    : 0f;

                if (statsUnavailableDuration >= 0.45f &&
                    utteranceDuration >= smartCaptureMinSpeechSecondsBeforePauseSend)
                {
                    Debug.Log("[Nyxara Runtime] Smart voice capture lost live levels mid-utterance. Sending buffered speech now.");
                    ResetSmartCaptureState();
                    _ = StopAndSendAsync("Smart capture finished. Transcribing and sending...");
                    return;
                }
            }

            var capturedSeconds = whisperInput.GetCurrentCaptureSeconds();
            if (capturedSeconds <= 0f)
            {
                return;
            }

            if (whisperInput.RecordingLengthSeconds > 0 &&
                capturedSeconds >= whisperInput.RecordingLengthSeconds - 0.15f)
            {
                Debug.Log("[Nyxara Runtime] Smart voice capture fallback reached the capture window limit. Sending buffered audio.");
                ResetSmartCaptureState();
                _ = StopAndSendAsync("Smart capture fallback window filled. Transcribing and sending...");
            }
        }

        private async System.Threading.Tasks.Task StopAndSendAsync(string transcribingStatus = "Transcribing and sending...")
        {
            if (_isShuttingDown || _voiceSendInFlight || whisperInput == null)
            {
                return;
            }

            _voiceSendInFlight = true;
            _status = transcribingStatus;
            Debug.Log($"[Nyxara Runtime] {transcribingStatus}");
            try
            {
                _lastTranscript = await whisperInput.StopRecordingAndTranscribeAsync();
                RestoreDefaultRecordingWindow();

                if (_isShuttingDown)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(_lastTranscript))
                {
                    _smartRetryNotBeforeRealtime = Time.realtimeSinceStartup + smartCaptureRetryCooldownSeconds;
                    _status = !string.IsNullOrWhiteSpace(whisperInput.LastRejectedTranscriptReason)
                        ? $"No usable speech detected: {whisperInput.LastRejectedTranscriptReason}"
                        : "No usable speech detected.";
                }
                else
                {
                    _status = whisperInput.LastTranscriptForwardedToBrain
                        ? "Transcript sent to Nyxara."
                        : whisperInput.LastForwardingDecision;
                }
                Debug.Log($"[Nyxara Runtime] Transcript: {_lastTranscript}");

                if (showDiagnosticPromptCheckBar && string.IsNullOrWhiteSpace(_lastTranscript))
                {
                    LogPromptCheckToConsole("voice", false, _status);
                }
            }
            catch (Exception ex)
            {
                _status = $"Mic send failed: {ex.Message}";
                Debug.LogException(ex);
            }
            finally
            {
                _voiceSendInFlight = false;
            }
        }

        private async void SendPrompt()
        {
            if (companionBrain == null || string.IsNullOrWhiteSpace(promptText))
            {
                return;
            }

            if (enableSmartVoiceCapture && whisperInput != null && whisperInput.IsRecording)
            {
                whisperInput.CancelRecording();
                RestoreDefaultRecordingWindow();
                ResetSmartCaptureState();
            }

            _lastTranscript = promptText;
            _status = "Sending typed prompt...";
            Debug.Log($"[Nyxara Runtime] Sending typed prompt: {promptText}");
            try
            {
                _lastReply = await companionBrain.ReplyToAsync(promptText);
                _status = "Typed prompt sent.";
            }
            catch (Exception ex)
            {
                _status = $"Prompt send failed: {ex.Message}";
                Debug.LogException(ex);
            }
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            var overlayHeight = showPromptPopup ? 420f : 300f;
            if (showDiagnosticPromptCheckBar)
            {
                overlayHeight += 300f;
            }

            GUILayout.BeginArea(new Rect(16f, 16f, 420f, overlayHeight), GUI.skin.window);
            GUILayout.Label("Nyxara Runtime");
            GUILayout.Space(4f);
            GUILayout.Label($"Status: {GetPlayerFacingStatus()}");
            GUILayout.Space(8f);
            GUILayout.Label("Player Input");
            GUILayout.TextArea(string.IsNullOrWhiteSpace(_lastTranscript) ? "<waiting for player input>" : _lastTranscript, GUILayout.MinHeight(54f));
            GUILayout.Space(8f);
            GUILayout.Label("Nyxara Reply");
            GUILayout.TextArea(string.IsNullOrWhiteSpace(_lastReply) ? "<waiting for Nyxara reply>" : _lastReply, GUILayout.MinHeight(64f));
            GUILayout.Space(8f);
            EnableSmartVoiceCapture = GUILayout.Toggle(EnableSmartVoiceCapture, "Enable Smart Voice Capture");
            GUILayout.Label(enableSmartVoiceCapture ? "Auto detect speech is ON" : $"Hold {micHoldKey} to talk");
            GUILayout.Label($"Press {promptPopupKey} for typed input");

            if (showDiagnosticPromptCheckBar)
            {
                GUILayout.Space(10f);
                GUILayout.Label("Prompt Check");
                GUILayout.Label("What Was Sent To The LLM");
                GUILayout.TextArea(GetLastPromptSentForDisplay(), GUILayout.MinHeight(56f));
                GUILayout.Label("What Came Back");
                GUILayout.TextArea(GetLastRawReplyForDisplay(), GUILayout.MinHeight(56f));
                GUILayout.Label("Parsed Visible Reply");
                GUILayout.TextArea(GetParsedVisibleReplyForDisplay(), GUILayout.MinHeight(48f));
                GUILayout.Label("Detected Expression Tags");
                GUILayout.TextArea(GetDetectedExpressionTagsForDisplay(), GUILayout.MinHeight(40f));
                GUILayout.Label("Filtering / Parsing");
                GUILayout.TextArea(GetFilterAndParseSummaryForDisplay(), GUILayout.MinHeight(72f));
                GUILayout.Label("Nyxara Runtime State");
                GUILayout.TextArea(GetRuntimeStateSummaryForDisplay(), GUILayout.MinHeight(72f));
            }

            if (showPromptPopup)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Typed Prompt");
                GUI.SetNextControlName("NyxaraRuntimePromptField");
                promptText = GUILayout.TextArea(promptText, GUILayout.MinHeight(64f));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Send Prompt", GUILayout.Height(28f)))
                {
                    SendPrompt();
                }

                if (GUILayout.Button("Close", GUILayout.Height(28f)))
                {
                    showPromptPopup = false;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private void HandleTranscriptReady(string transcript)
        {
            _lastTranscript = transcript ?? string.Empty;
        }

        private void HandleReplyReady(string reply)
        {
            _lastReply = reply ?? string.Empty;
            _status = "Reply ready.";
            _smartRetryNotBeforeRealtime = Time.realtimeSinceStartup + smartCapturePostReplyCooldownSeconds;

            if (whisperInput != null && whisperInput.IsRecording)
            {
                whisperInput.CancelRecording();
            }

            RestoreDefaultRecordingWindow();
            ResetSmartCaptureState();

            if (showDiagnosticPromptCheckBar)
            {
                LogPromptCheckToConsole(showPromptPopup ? "typed" : "voice", true, _status);
            }
        }

        private string GetPlayerFacingStatus()
        {
            if (whisperInput == null || companionBrain == null)
            {
                return GetReadyStatus();
            }

            if (!whisperInput.IsWhisperAvailable || !companionBrain.IsLlmAvailable)
            {
                return GetReadyStatus();
            }

            if (whisperInput.IsRecording)
            {
                return enableSmartVoiceCapture && !_smartSpeechDetected ? "Listening" : "Listening";
            }

            if (_status.StartsWith("Transcribing", StringComparison.OrdinalIgnoreCase))
            {
                return "Transcribing";
            }

            if (companionBrain.IsBusy)
            {
                return IsVoiceAvailable() ? "Thinking" : "Thinking - Voice Disabled / Text Only";
            }

            if (_status.StartsWith("Reply ready", StringComparison.OrdinalIgnoreCase) ||
                _status.StartsWith("Typed prompt sent", StringComparison.OrdinalIgnoreCase) ||
                _status.StartsWith("Transcript sent", StringComparison.OrdinalIgnoreCase))
            {
                return IsVoiceAvailable() ? "Replying" : "Voice Disabled / Text Only";
            }

            return IsVoiceAvailable() ? "Ready" : "Ready - Voice Disabled / Text Only";
        }

        private void ResetSmartCaptureState()
        {
            _smartSpeechDetected = false;
            _smartLastSpeechRealtime = 0f;
            _smartSpeechDetectedRealtime = 0f;
            _smartListeningStartedRealtime = 0f;
            _smartSpeechCandidateSinceRealtime = -1f;
            _smartSilenceCandidateSinceRealtime = -1f;
            _smartStatsUnavailableSinceRealtime = -1f;
            _smartNoiseFloorRms = 0f;
            _smartNoiseFloorPeak = 0f;
            _smartDetectedSpeechRms = 0f;
            _smartDetectedSpeechPeak = 0f;
        }

        private void BeginSmartCaptureShutdown()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            ResetSmartCaptureState();
            RestoreDefaultRecordingWindow();

            if (whisperInput != null && whisperInput.IsRecording)
            {
                whisperInput.CancelRecording();
            }
        }

        private void ApplySmartRecordingWindow()
        {
            if (whisperInput == null)
            {
                return;
            }

            var desiredWindow = Mathf.Max(whisperInput.RecordingLengthSeconds, smartCaptureRecordingLengthSeconds);
            whisperInput.SetRecordingLengthSeconds(desiredWindow);
        }

        private void RestoreDefaultRecordingWindow()
        {
            if (whisperInput == null)
            {
                return;
            }

            whisperInput.SetRecordingLengthSeconds(_defaultRecordingLengthSeconds);
        }

        private bool IsVoiceAvailable()
        {
            return companionBrain != null &&
                   companionBrain.TtsService != null &&
                   companionBrain.TtsService.IsConfigured;
        }

        private string GetLastPromptSentForDisplay()
        {
            if (companionBrain == null || string.IsNullOrWhiteSpace(companionBrain.LastPromptSent))
            {
                return "<no prompt sent yet>";
            }

            return companionBrain.LastPromptSent;
        }

        private string GetLastRawReplyForDisplay()
        {
            if (companionBrain == null || string.IsNullOrWhiteSpace(companionBrain.LastRawLlmResponse))
            {
                return "<no LLM reply yet>";
            }

            return companionBrain.LastRawLlmResponse;
        }

        private string GetFilterAndParseSummaryForDisplay()
        {
            var inputSummary = whisperInput == null
                ? "Input filter: whisper input unavailable."
                : string.IsNullOrWhiteSpace(whisperInput.LastForwardingDecision)
                    ? "Input filter: no capture decision yet."
                    : $"Input filter: {whisperInput.LastForwardingDecision}";

            if (whisperInput != null && !string.IsNullOrWhiteSpace(whisperInput.LastRejectedTranscriptReason))
            {
                inputSummary += $" Rejection reason: {whisperInput.LastRejectedTranscriptReason}.";
            }

            var replySummary = companionBrain == null || string.IsNullOrWhiteSpace(companionBrain.LastReplyTransformationSummary)
                ? "Reply parse: no parsing summary yet."
                : $"Reply parse: {companionBrain.LastReplyTransformationSummary}";

            return $"{inputSummary}\n{replySummary}";
        }

        private string GetParsedVisibleReplyForDisplay()
        {
            if (companionBrain == null || string.IsNullOrWhiteSpace(companionBrain.LastReply))
            {
                return "<no parsed visible reply yet>";
            }

            return companionBrain.LastReply;
        }

        private string GetDetectedExpressionTagsForDisplay()
        {
            var parsed = companionBrain?.LastParsedResponse;
            if (parsed?.expressionTriggers == null || parsed.expressionTriggers.Count == 0)
            {
                return "<none>";
            }

            return string.Join(", ", parsed.expressionTriggers.Select(trigger => trigger.ToString()));
        }

        private string GetRuntimeStateSummaryForDisplay()
        {
            if (companionBrain == null || string.IsNullOrWhiteSpace(companionBrain.LastRuntimeStateSummary))
            {
                return "<runtime state unavailable>";
            }

            return companionBrain.LastRuntimeStateSummary;
        }

        private void LogPromptCheckToConsole(string source, bool newLlmRequestWasSent, string currentStatus)
        {
            var promptSent = GetLastPromptSentForDisplay();
            var rawReply = GetLastRawReplyForDisplay();
            var filterSummary = GetFilterAndParseSummaryForDisplay();
            var runtimeSummary = GetRuntimeStateSummaryForDisplay();
            var currentInputSummary = string.IsNullOrWhiteSpace(currentStatus) ? "<no current input status>" : currentStatus;
            var currentInputText = string.IsNullOrWhiteSpace(_lastTranscript) ? "<empty>" : _lastTranscript;

            if (!newLlmRequestWasSent)
            {
                Debug.Log(
                    $"[Nyxara Prompt Check][{source}] Current input status:{Environment.NewLine}{currentInputSummary}{Environment.NewLine}" +
                    $"Current input text:{Environment.NewLine}{currentInputText}{Environment.NewLine}{Environment.NewLine}" +
                    $"No new LLM request was sent because the latest input was rejected or unusable.{Environment.NewLine}{Environment.NewLine}" +
                    $"Last successful prompt sent to the LLM:{Environment.NewLine}{promptSent}{Environment.NewLine}{Environment.NewLine}" +
                    $"Last successful LLM reply:{Environment.NewLine}{rawReply}{Environment.NewLine}{Environment.NewLine}" +
                    $"How filtering/parsing changed it:{Environment.NewLine}{filterSummary}{Environment.NewLine}{Environment.NewLine}" +
                    $"Nyxara current personality/runtime state:{Environment.NewLine}{runtimeSummary}");
                return;
            }

            Debug.Log(
                $"[Nyxara Prompt Check][{source}] What was sent to the LLM:{Environment.NewLine}{promptSent}{Environment.NewLine}{Environment.NewLine}" +
                $"What came back:{Environment.NewLine}{rawReply}{Environment.NewLine}{Environment.NewLine}" +
                $"How filtering/parsing changed it:{Environment.NewLine}{filterSummary}{Environment.NewLine}{Environment.NewLine}" +
                $"Nyxara current personality/runtime state:{Environment.NewLine}{runtimeSummary}");
        }

        private string GetReadyStatus()
        {
            if (whisperInput == null)
            {
                return "STT: missing microphone input";
            }

            if (!whisperInput.IsWhisperAvailable)
            {
                return "STT: Whisper not installed or WhisperManager missing";
            }

            if (companionBrain == null)
            {
                return "Brain: missing";
            }

            if (!companionBrain.IsLlmAvailable)
            {
                return "LLM: LLMUnity not installed or agent missing";
            }

            return "Systems ready";
        }

        private static bool GetKeyCompat(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            if (TryGetKeyboardKeyState(key, out var isPressed, out _))
            {
                return isPressed;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#else
            return false;
#endif
        }

        private static bool GetKeyDownCompat(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            if (TryGetKeyboardKeyState(key, out _, out var wasPressedThisFrame))
            {
                return wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static bool TryGetKeyboardKeyState(KeyCode key, out bool isPressed, out bool wasPressedThisFrame)
        {
            isPressed = false;
            wasPressedThisFrame = false;
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            var button = key switch
            {
                KeyCode.A => keyboard.aKey,
                KeyCode.B => keyboard.bKey,
                KeyCode.C => keyboard.cKey,
                KeyCode.D => keyboard.dKey,
                KeyCode.E => keyboard.eKey,
                KeyCode.F => keyboard.fKey,
                KeyCode.G => keyboard.gKey,
                KeyCode.H => keyboard.hKey,
                KeyCode.I => keyboard.iKey,
                KeyCode.J => keyboard.jKey,
                KeyCode.K => keyboard.kKey,
                KeyCode.L => keyboard.lKey,
                KeyCode.M => keyboard.mKey,
                KeyCode.N => keyboard.nKey,
                KeyCode.O => keyboard.oKey,
                KeyCode.P => keyboard.pKey,
                KeyCode.Q => keyboard.qKey,
                KeyCode.R => keyboard.rKey,
                KeyCode.S => keyboard.sKey,
                KeyCode.T => keyboard.tKey,
                KeyCode.U => keyboard.uKey,
                KeyCode.V => keyboard.vKey,
                KeyCode.W => keyboard.wKey,
                KeyCode.X => keyboard.xKey,
                KeyCode.Y => keyboard.yKey,
                KeyCode.Z => keyboard.zKey,
                KeyCode.Alpha0 => keyboard.digit0Key,
                KeyCode.Alpha1 => keyboard.digit1Key,
                KeyCode.Alpha2 => keyboard.digit2Key,
                KeyCode.Alpha3 => keyboard.digit3Key,
                KeyCode.Alpha4 => keyboard.digit4Key,
                KeyCode.Alpha5 => keyboard.digit5Key,
                KeyCode.Alpha6 => keyboard.digit6Key,
                KeyCode.Alpha7 => keyboard.digit7Key,
                KeyCode.Alpha8 => keyboard.digit8Key,
                KeyCode.Alpha9 => keyboard.digit9Key,
                KeyCode.Space => keyboard.spaceKey,
                KeyCode.Return => keyboard.enterKey,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey,
                KeyCode.LeftShift => keyboard.leftShiftKey,
                KeyCode.RightShift => keyboard.rightShiftKey,
                KeyCode.LeftControl => keyboard.leftCtrlKey,
                KeyCode.RightControl => keyboard.rightCtrlKey,
                KeyCode.LeftAlt => keyboard.leftAltKey,
                KeyCode.RightAlt => keyboard.rightAltKey,
                KeyCode.Tab => keyboard.tabKey,
                KeyCode.BackQuote => keyboard.backquoteKey,
                KeyCode.Escape => keyboard.escapeKey,
                _ => null
            };

            if (button == null)
            {
                return false;
            }

            isPressed = button.isPressed;
            wasPressedThisFrame = button.wasPressedThisFrame;
            return true;
        }
#endif
    }
}
