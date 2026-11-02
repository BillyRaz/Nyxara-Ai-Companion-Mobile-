// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;
#if NYXARA_LLMUNITY
using LLMUnity;
#endif
using Nyxara.AICompanion.Data;
using Nyxara.AICompanion.Expressions;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.Parsing;
using Nyxara.AICompanion.Prompting;
using Nyxara.AICompanion.Runtime;
using Nyxara.AICompanion.Speech;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Nyxara.AICompanion.Core
{
    public class NyxaraCompanionBrain : MonoBehaviour
    {
        private const string MissingLlmMessage = "Nyxara AI Studio: LLMUnity not installed. AI features disabled.";
        private const string MissingLlmReferenceMessage = "Nyxara AI Studio: LLMUnity is enabled but no LLMAgent is assigned. AI features disabled.";
        private static readonly Regex LowQualityReplyRegex = new(@"^(intent|mood|action|signal|dialogue|[\[\]\(\)\{\}:_\-]|[A-Za-z]{1,3})\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [Header("Core Components")]
        [SerializeField] private MonoBehaviour agent;
        [SerializeField] private PiperTtsService ttsService;
        [SerializeField] private ArkItBlendshapeDriver faceDriver;
        [SerializeField] private ExpressionSignalRouter signalRouter;
        [SerializeField] private ExpressionTriggerPlayer expressionTriggerPlayer;

        [Header("Data")]
        [SerializeField] private CharacterProfileData characterProfile;
        [SerializeField] private NPCRuntimeState runtimeState;

        [Header("Controllers")]
        [SerializeField] private RecentMemoryController memoryController;
        [SerializeField] private ActionGatekeeper actionGatekeeper;

        [Header("Settings")]
        [SerializeField] private bool speakReplies = true;
        [SerializeField] private bool useStructuredParsing = true;
        [SerializeField] private bool warmupOnStart = true;
        [SerializeField] private string fallbackReply = "I heard you, but I need a moment.";
        [SerializeField] private NyxaraReplyMode replyMode = NyxaraReplyMode.Character;

        private PromptBuilder _promptBuilder;
        private Stopwatch _generationTimer;
        private bool _hasLoggedMissingDependency;
        private bool _hasLoggedMissingReference;
        private Task _warmupTask;
        private bool _warmupCompleted;
        private bool _hasCompletedSuccessfulReply;

#pragma warning disable CS0067
        public event Action<NPCResponseData> ResponseParsed;
#pragma warning restore CS0067
        public event Action<string> ReplyReady;

        public bool IsBusy { get; private set; }
        public NyxaraReplyMode ReplyMode
        {
            get => replyMode;
            set => replyMode = value;
        }
        public string LastReply { get; private set; }
        public NPCResponseData LastParsedResponse { get; private set; }
        public string LastPromptSent { get; private set; }
        public string LastRawLlmResponse { get; private set; }
        public string LastReplyTransformationSummary { get; private set; }
        public string LastRuntimeStateSummary => BuildRuntimeStateSummary();
        public CharacterProfileData CharacterProfile => characterProfile;
        public NPCRuntimeState RuntimeState => runtimeState;
        public dynamic Agent => agent;
        public PiperTtsService TtsService => ttsService;
        public ArkItBlendshapeDriver FaceDriver => faceDriver;
        public ExpressionSignalRouter SignalRouter => signalRouter;
        public ExpressionTriggerPlayer ExpressionTriggerPlayer => expressionTriggerPlayer;
        public RecentMemoryController MemoryController => memoryController;
        public ActionGatekeeper Gatekeeper => actionGatekeeper;
        public bool IsLlmAvailable => TryGetAgent(out _);

        private void Awake()
        {
            _promptBuilder = new PromptBuilder();
            _generationTimer = new Stopwatch();

            EnsureRuntimeStateInitialized();

            if (characterProfile == null)
            {
                Debug.LogWarning("No CharacterProfile assigned to NyxaraCompanionBrain");
            }

            ValidateSetup();
        }

#if NYXARA_LLMUNITY
        private async void Start()
#else
        private void Start()
#endif
        {
            if (!Application.isPlaying || !warmupOnStart)
            {
                return;
            }

            ValidateSetup();

#if NYXARA_LLMUNITY
            if (!TryGetAgent(out var llmAgent))
            {
                return;
            }

            _warmupTask = WarmupAsync(llmAgent);
            await _warmupTask;
#else
            _ = speakReplies;
            _ = useStructuredParsing;
#endif
        }

        private void Update()
        {
            if (!Application.isPlaying || runtimeState == null)
            {
                return;
            }

            runtimeState.timeSinceLastResponse += Time.deltaTime;
        }

        public async Task<string> ReplyToAsync(string userText)
        {
            return await ReplyInternalAsync(userText, true);
        }

        public async Task<string> ReplyToSystemAsync(string systemText)
        {
            return await ReplyInternalAsync(systemText, false);
        }

        private async Task<string> ReplyInternalAsync(string inputText, bool treatAsPlayerInput)
        {
            if (IsBusy)
            {
                Debug.LogWarning("Already generating a reply.");
                return LastReply;
            }

            if (string.IsNullOrWhiteSpace(inputText))
            {
                return string.Empty;
            }

            ValidateSetup();
            EnsureRuntimeStateInitialized();

#if NYXARA_LLMUNITY
            if (!TryGetAgent(out var llmAgent))
            {
                LastPromptSent = string.Empty;
                LastRawLlmResponse = fallbackReply;
                LastReplyTransformationSummary = "LLM was unavailable, so Nyxara used the fallback reply without parsing.";
                LastReply = fallbackReply;
                ReplyReady?.Invoke(fallbackReply);
                return fallbackReply;
            }

            IsBusy = true;
            faceDriver?.SetThinking(true);

            try
            {
                await EnsureWarmupReadyBeforeReplyAsync(llmAgent, treatAsPlayerInput);
                _generationTimer.Restart();

                var memoryString = treatAsPlayerInput
                    ? memoryController?.BuildPromptMemoryBlock(characterProfile?.identity?.characterName, includeSavedMemories: true, diagnosticMode: replyMode == NyxaraReplyMode.DiagnosticInspector) ?? string.Empty
                    : string.Empty;
                var memoryStatusReport = treatAsPlayerInput && replyMode == NyxaraReplyMode.DiagnosticInspector
                    ? memoryController?.BuildMemoryStatusReport(characterProfile?.identity?.characterName) ?? string.Empty
                    : string.Empty;
                var prompt = BuildNpcReplyPrompt(inputText, memoryString, treatAsPlayerInput, memoryStatusReport);
                LastPromptSent = prompt;
                var rawResponse = await GenerateStableReplyAsync(llmAgent, prompt, treatAsPlayerInput);
                LastRawLlmResponse = rawResponse;
                _generationTimer.Stop();

                Debug.Log($"Generation took {_generationTimer.ElapsedMilliseconds}ms | Prompt chars: {prompt.Length}");

                if (string.IsNullOrWhiteSpace(rawResponse))
                {
                    rawResponse = fallbackReply;
                    LastRawLlmResponse = rawResponse;
                }

                var preReplyState = MemoryStateSnapshot.FromRuntimeState(runtimeState);
                NPCResponseData parsedResponse = null;
                var finalDialogue = rawResponse;

                if (useStructuredParsing)
                {
                    parsedResponse = StructuredResponseParser.Parse(rawResponse, runtimeState, characterProfile);
                    LastParsedResponse = parsedResponse;
                    finalDialogue = parsedResponse.dialogue;

                    if (treatAsPlayerInput && runtimeState != null && parsedResponse.mood != runtimeState.mood)
                    {
                        runtimeState.ApplyMoodShift(parsedResponse.mood);
                    }

                    if (signalRouter != null && parsedResponse.signal != "none")
                    {
                        signalRouter.ApplySignal(parsedResponse.signal, parsedResponse.mood);
                    }

                    if (expressionTriggerPlayer != null && parsedResponse.expressionTriggers != null)
                    {
                        foreach (var trigger in parsedResponse.expressionTriggers)
                        {
                            expressionTriggerPlayer.TryPlayTrigger(trigger.key, trigger.intensity);
                        }
                    }

                    if (treatAsPlayerInput && actionGatekeeper != null && parsedResponse.action != "none")
                    {
                        actionGatekeeper.TryExecuteAction(parsedResponse.action, runtimeState);
                    }

                    ResponseParsed?.Invoke(parsedResponse);
                }
                else
                {
                    LastParsedResponse = null;
                }

                LastReplyTransformationSummary = BuildReplyTransformationSummary(rawResponse, finalDialogue, parsedResponse, treatAsPlayerInput);

                LastReply = finalDialogue;
                ReplyReady?.Invoke(finalDialogue);
                runtimeState.timeSinceLastResponse = 0f;
                if (treatAsPlayerInput)
                {
                    runtimeState.lastPlayerTopic = inputText.Trim();
                    memoryController?.AddPlayerMessage(inputText);
                    memoryController?.AddNPCResponse(finalDialogue, parsedResponse?.intent ?? "unknown");
                    memoryController?.RecordConversationEvent(
                        characterProfile?.identity?.characterName,
                        inputText,
                        rawResponse,
                        finalDialogue,
                        runtimeState?.lastPlayerTopic ?? inputText.Trim(),
                        parsedResponse?.mood ?? runtimeState?.mood ?? "calm",
                        parsedResponse?.intent ?? "neutral",
                        parsedResponse?.expressionTriggers,
                        preReplyState,
                        MemoryStateSnapshot.FromRuntimeState(runtimeState));
                }

                if (treatAsPlayerInput && speakReplies && ttsService != null)
                {
                    _ = TrySpeakReplyAsync(finalDialogue);
                }

                if (!string.IsNullOrWhiteSpace(finalDialogue))
                {
                    _hasCompletedSuccessfulReply = true;
                }

                return finalDialogue;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                LastRawLlmResponse = fallbackReply;
                LastReplyTransformationSummary = $"Reply generation failed and Nyxara used the fallback reply. Error: {ex.Message}";
                LastReply = fallbackReply;
                ReplyReady?.Invoke(fallbackReply);
                return fallbackReply;
            }
            finally
            {
                faceDriver?.SetThinking(false);
                IsBusy = false;
            }
#else
            LastPromptSent = string.Empty;
            LastRawLlmResponse = fallbackReply;
            LastReplyTransformationSummary = "LLM integration is disabled, so Nyxara used the fallback reply.";
            LastReply = fallbackReply;
            ReplyReady?.Invoke(fallbackReply);
            return await Task.FromResult(fallbackReply);
#endif
        }

        private async Task TrySpeakReplyAsync(string dialogue)
        {
            try
            {
                await ttsService.SpeakAsync(dialogue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Nyxara Runtime] Voice playback was skipped: {ex.Message}");
            }
        }

        public void SetMood(string newMood)
        {
            if (runtimeState != null)
            {
                runtimeState.mood = newMood;
            }
        }

        public void ModifyTrust(float delta)
        {
            runtimeState?.ModifyTrust(delta);
        }

        public void ResetConversationState(bool keepCurrentRelationshipState)
        {
            var previousTrust = runtimeState?.trust ?? 0.5f;
            var previousAffection = runtimeState?.affection ?? 0.3f;
            var previousRespect = runtimeState?.respect ?? 0.65f;
            var previousSuspicion = runtimeState?.suspicion ?? 0.1f;
            var previousFamiliarity = runtimeState?.familiarity ?? 0.35f;

            runtimeState = characterProfile?.runtimeDefaults != null
                ? characterProfile.runtimeDefaults.Clone()
                : new NPCRuntimeState();

            if (keepCurrentRelationshipState)
            {
                runtimeState.trust = previousTrust;
                runtimeState.affection = previousAffection;
                runtimeState.respect = previousRespect;
                runtimeState.suspicion = previousSuspicion;
                runtimeState.familiarity = previousFamiliarity;
            }
            else if (characterProfile?.relationshipDefaults != null)
            {
                runtimeState.trust = characterProfile.relationshipDefaults.trust;
                runtimeState.affection = characterProfile.relationshipDefaults.affection;
                runtimeState.respect = characterProfile.relationshipDefaults.respect;
                runtimeState.suspicion = characterProfile.relationshipDefaults.suspicion;
                runtimeState.familiarity = characterProfile.relationshipDefaults.familiarity;
            }

            runtimeState.lastPlayerTopic = string.Empty;
            runtimeState.timeSinceLastResponse = 0f;
            LastReply = string.Empty;
            LastParsedResponse = null;
            LastPromptSent = string.Empty;
            LastRawLlmResponse = string.Empty;
            LastReplyTransformationSummary = "Conversation state was reset.";
        }

        public void ResetRelationshipStateToDefaults()
        {
            EnsureRuntimeStateInitialized();
            if (runtimeState == null || characterProfile?.relationshipDefaults == null)
            {
                return;
            }

            runtimeState.trust = characterProfile.relationshipDefaults.trust;
            runtimeState.affection = characterProfile.relationshipDefaults.affection;
            runtimeState.respect = characterProfile.relationshipDefaults.respect;
            runtimeState.suspicion = characterProfile.relationshipDefaults.suspicion;
            runtimeState.familiarity = characterProfile.relationshipDefaults.familiarity;
            runtimeState.relationship = "neutral";
        }

        private void Reset()
        {
#if NYXARA_LLMUNITY
            agent = GetComponent<LLMAgent>();
            if (agent == null)
            {
                agent = GetComponentInChildren<LLMAgent>(true);
            }
#else
            agent = null;
#endif
            ttsService = FindFirstObjectByType<PiperTtsService>();
            faceDriver = FindFirstObjectByType<ArkItBlendshapeDriver>();
            signalRouter = FindFirstObjectByType<ExpressionSignalRouter>();
            expressionTriggerPlayer = FindFirstObjectByType<ExpressionTriggerPlayer>();
            memoryController = GetComponent<RecentMemoryController>();
            actionGatekeeper = GetComponent<ActionGatekeeper>();

            var profiles = Resources.LoadAll<CharacterProfileData>("");
            if (profiles.Length > 0)
            {
                characterProfile = profiles[0];
            }
        }

        private void ValidateSetup()
        {
#if NYXARA_LLMUNITY
            AutoResolveDependencies();
            if (agent == null && !_hasLoggedMissingReference)
            {
                _hasLoggedMissingReference = true;
                Debug.LogWarning(MissingLlmReferenceMessage, this);
            }
#else
            if (!_hasLoggedMissingDependency)
            {
                _hasLoggedMissingDependency = true;
                Debug.LogWarning(MissingLlmMessage, this);
            }
#endif

            AutoResolveOptionalDependencies();
        }

        // This boundary keeps profile/prompt logic scoped to Nyxara's response generation only.
        // Player microphone capture, player controls, and player-facing input systems must remain separate.
        private string BuildNpcReplyPrompt(string userText, string memoryString, bool treatAsPlayerInput, string memoryStatusReport = "")
        {
            return treatAsPlayerInput
                ? _promptBuilder.BuildPrompt(characterProfile, runtimeState, userText, memoryString, replyMode, memoryStatusReport)
                : _promptBuilder.BuildSystemPrompt(characterProfile, runtimeState, userText, memoryString, replyMode, memoryStatusReport);
        }

        private async Task WarmupAsync(LLMAgent llmAgent)
        {
            try
            {
                await llmAgent.Warmup(_promptBuilder.BuildMinimalWarmupPrompt(characterProfile));
                _warmupCompleted = true;
                Debug.Log("[Nyxara Runtime] LLM warmup completed.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Nyxara Runtime] LLM warmup failed: {ex.Message}");
            }
        }

        private async Task EnsureWarmupReadyBeforeReplyAsync(LLMAgent llmAgent, bool treatAsPlayerInput)
        {
            if (!treatAsPlayerInput || _hasCompletedSuccessfulReply || _warmupCompleted)
            {
                return;
            }

            _warmupTask ??= WarmupAsync(llmAgent);

            try
            {
                var completed = await Task.WhenAny(_warmupTask, Task.Delay(2500));
                if (completed != _warmupTask)
                {
                    Debug.LogWarning("[Nyxara Runtime] Warmup did not finish before the first player turn. Proceeding with guarded first reply generation.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Nyxara Runtime] Waiting on warmup failed before first reply: {ex.Message}");
            }
        }

        private async Task<string> GenerateStableReplyAsync(LLMAgent llmAgent, string prompt, bool treatAsPlayerInput)
        {
            var rawResponse = await llmAgent.Chat(prompt, null, null, false);
            if (!treatAsPlayerInput || _hasCompletedSuccessfulReply || !IsLikelyMalformedReply(rawResponse))
            {
                return rawResponse;
            }

            Debug.LogWarning("[Nyxara Runtime] First-turn reply looked malformed. Retrying once with a stabilizing instruction.");
            var retryPrompt = $"{prompt}\n\nRecovery instruction:\nReturn one clean natural spoken reply now. You may optionally prepend a brief expression tag. Do not output labels, fragments, or partial schema words.";
            LastPromptSent = retryPrompt;
            var retriedResponse = await llmAgent.Chat(retryPrompt, null, null, false);
            if (!string.IsNullOrWhiteSpace(retriedResponse))
            {
                LastReplyTransformationSummary = "First-turn stability retry was used because the initial raw LLM reply looked malformed.";
            }

            return retriedResponse;
        }

        private static bool IsLikelyMalformedReply(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return true;
            }

            var trimmed = rawResponse.Trim();
            if (trimmed.Length <= 4)
            {
                return true;
            }

            if (LowQualityReplyRegex.IsMatch(trimmed))
            {
                return true;
            }

            var letterCount = 0;
            foreach (var character in trimmed)
            {
                if (char.IsLetter(character))
                {
                    letterCount++;
                }
            }

            if (letterCount < 4)
            {
                return true;
            }

            var sanitized = trimmed.Replace("\r", " ").Replace("\n", " ").Trim();
            return sanitized.Equals("Intent", StringComparison.OrdinalIgnoreCase) ||
                   sanitized.Equals("[", StringComparison.OrdinalIgnoreCase) ||
                   sanitized.Equals("[N", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildReplyTransformationSummary(string rawResponse, string finalDialogue, NPCResponseData parsedResponse, bool treatAsPlayerInput)
        {
            if (!useStructuredParsing || parsedResponse == null)
            {
                return string.Equals(rawResponse?.Trim(), finalDialogue?.Trim(), StringComparison.Ordinal)
                    ? $"Structured parsing is disabled, so the raw LLM reply was shown as-is. Visible reply: {finalDialogue}"
                    : $"Structured parsing is disabled, but the final reply still differed from the raw output. Visible reply: {finalDialogue}";
            }

            var summary = $"Structured parsing {(treatAsPlayerInput ? "handled player input" : "handled system input")}. ";
            summary += string.Equals(rawResponse?.Trim(), finalDialogue?.Trim(), StringComparison.Ordinal)
                ? "The visible reply matched the raw LLM dialogue. "
                : "The parser cleaned or extracted the visible dialogue from the raw LLM output. ";
            summary += $"Intent={parsedResponse.intent}, Mood={parsedResponse.mood}, Action={parsedResponse.action}, Signal={parsedResponse.signal}.";
            summary += $" Visible reply: {parsedResponse.dialogue}.";

            if (parsedResponse.expressionTriggers != null && parsedResponse.expressionTriggers.Count > 0)
            {
                summary += $" Expression triggers: {string.Join(", ", parsedResponse.expressionTriggers)}.";
            }
            else
            {
                summary += " Expression triggers: none.";
            }

            if (parsedResponse.actionTriggers != null && parsedResponse.actionTriggers.Count > 0)
            {
                summary += $" Action triggers: {string.Join(", ", parsedResponse.actionTriggers)}.";
            }

            return summary;
        }

        private string BuildRuntimeStateSummary()
        {
            var identity = characterProfile?.identity;
            var personality = identity?.personalityTags != null && identity.personalityTags.Count > 0
                ? string.Join(", ", identity.personalityTags)
                : "no personality tags";
            var speechStyleSummary = string.IsNullOrWhiteSpace(identity?.speechStyle) ? "default speech style" : identity.speechStyle;

            if (runtimeState == null)
            {
                return $"Personality: {personality} | Speech style: {speechStyleSummary} | Runtime state unavailable.";
            }

            return $"Personality: {personality} | Speech style: {speechStyleSummary} | Mood: {runtimeState.mood} | " +
                   $"Trust {runtimeState.trust:0.00}, Affection {runtimeState.affection:0.00}, Respect {runtimeState.respect:0.00}, " +
                   $"Suspicion {runtimeState.suspicion:0.00}, Familiarity {runtimeState.familiarity:0.00} | " +
                   $"Relationship: {runtimeState.relationship} | Task: {runtimeState.currentTask} | Goal: {runtimeState.currentGoal} | " +
                   $"Topic: {runtimeState.lastPlayerTopic}";
        }

        private void AutoResolveOptionalDependencies()
        {
            if (expressionTriggerPlayer == null)
            {
                expressionTriggerPlayer = GetComponent<ExpressionTriggerPlayer>() ?? FindFirstObjectByType<ExpressionTriggerPlayer>();
            }

            if (faceDriver == null)
            {
                faceDriver = GetComponent<ArkItBlendshapeDriver>() ?? FindFirstObjectByType<ArkItBlendshapeDriver>();
            }

            if (signalRouter == null)
            {
                signalRouter = GetComponent<ExpressionSignalRouter>() ?? FindFirstObjectByType<ExpressionSignalRouter>();
            }
        }

        private void EnsureRuntimeStateInitialized()
        {
            if (runtimeState != null)
            {
                return;
            }

            runtimeState = characterProfile?.runtimeDefaults != null
                ? characterProfile.runtimeDefaults.Clone()
                : new NPCRuntimeState();

            if (characterProfile?.relationshipDefaults != null)
            {
                runtimeState.trust = characterProfile.relationshipDefaults.trust;
                runtimeState.affection = characterProfile.relationshipDefaults.affection;
                runtimeState.respect = characterProfile.relationshipDefaults.respect;
                runtimeState.suspicion = characterProfile.relationshipDefaults.suspicion;
                runtimeState.familiarity = characterProfile.relationshipDefaults.familiarity;
            }
        }

#if NYXARA_LLMUNITY
        private void AutoResolveDependencies()
        {
            if (agent == null)
            {
                agent = GetComponent<LLMAgent>();
                if (agent == null)
                {
                    agent = GetComponentInChildren<LLMAgent>(true);
                }

                if (agent == null)
                {
                    agent = FindFirstObjectByType<LLMAgent>();
                }
            }
        }

        private bool TryGetAgent(out LLMAgent llmAgent)
        {
            AutoResolveDependencies();
            llmAgent = agent as LLMAgent;
            if (llmAgent != null)
            {
                _hasLoggedMissingReference = false;
                return true;
            }

            if (!_hasLoggedMissingReference)
            {
                _hasLoggedMissingReference = true;
                Debug.LogWarning(MissingLlmReferenceMessage, this);
            }

            return false;
        }
#else
        private bool TryGetAgent(out object llmAgent)
        {
            llmAgent = null;
            return false;
        }
#endif
    }
}
