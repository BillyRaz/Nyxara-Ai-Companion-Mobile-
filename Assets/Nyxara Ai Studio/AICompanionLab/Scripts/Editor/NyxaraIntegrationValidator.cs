// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nyxara.AICompanion.Configuration;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.LipSync;
using Nyxara.AICompanion.Speech;
using Nyxara.AICompanion.Studio;
using Nyxara.AICompanion.UI;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    internal static class NyxaraIntegrationValidator
    {
        private const string LlmUnityPackageName = "ai.undream.llm";
        private const string WhisperPackageName = "com.whisper.unity";
        private const string LlmUnityDefine = "NYXARA_LLMUNITY";
        private const string WhisperDefine = "NYXARA_WHISPER";
        private const string PendingLlmMessage = "LLMUnity package detected, but Unity is still compiling/loading it. Nyxara queued an automatic repair for the AI bindings.";
        private const string PendingWhisperMessage = "whisper.unity package detected, but Unity is still compiling/loading it. Nyxara queued an automatic repair for the speech bindings.";

        internal const string DefaultInProcessPiperModelPath = "Assets/Speech/Piper/clean100.onnx";
        internal const string DefaultInProcessPiperConfigPath = DefaultInProcessPiperModelPath + ".json";
        internal const string DefaultPiperEspeakDataDirectory = "Speech/PiperRuntime/piper/espeak-ng-data";

        internal sealed class ValidationReport
        {
            public readonly List<string> Lines = new();
            public MessageType MessageType = MessageType.Info;
            public bool HasChanges;
            public bool RequestedScriptReload;

            public string Summary => string.Join(Environment.NewLine, Lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        internal sealed class IntegrationSnapshot
        {
            public bool LlmPackageDetected;
            public bool LlmTypeAvailable;
            public bool LlmDefineEnabled;
            public bool LlmBindingPresent;
            public bool LlmModelPathValid;
            public bool WhisperPackageDetected;
            public bool WhisperTypeAvailable;
            public bool WhisperDefineEnabled;
            public bool WhisperBindingPresent;
            public bool WhisperModelPathValid;
            public bool PiperRuntimeValid;
            public bool PiperVoiceValid;
            public bool PiperReady;
        }

        internal static IntegrationSnapshot CaptureSnapshot(AICompanionStudioConfig config)
        {
            return new IntegrationSnapshot
            {
                LlmPackageDetected = IsPackageInstalled(LlmUnityPackageName) || IsTypeAvailable("LLM") || IsTypeAvailable("LLMAgent"),
                LlmTypeAvailable = IsTypeAvailable("LLM") && IsTypeAvailable("LLMAgent"),
                LlmDefineEnabled = HasScriptingDefine(LlmUnityDefine),
                LlmBindingPresent = FindComponentByTypeName("LLMAgent") != null && FindComponentByTypeName("LLM") != null,
                LlmModelPathValid = IsValidFilePath(config != null ? config.llmModelPath : string.Empty, false),
                WhisperPackageDetected = IsPackageInstalled(WhisperPackageName) || IsTypeAvailable("WhisperManager"),
                WhisperTypeAvailable = IsTypeAvailable("WhisperManager"),
                WhisperDefineEnabled = HasScriptingDefine(WhisperDefine),
                WhisperBindingPresent = FindComponentByTypeName("WhisperManager") != null && FindFirstSceneObjectByType<WhisperMicrophoneInput>() != null,
                WhisperModelPathValid = IsValidFilePath(config != null ? config.whisperModelRelativePath : string.Empty, true),
                PiperRuntimeValid = IsValidFilePath(config != null ? config.piperExecutablePath : string.Empty, true),
                PiperVoiceValid = IsValidFilePath(config != null ? config.piperVoicePath : string.Empty, true),
                PiperReady = config != null && (HasInProcessPiperVoice() || HasInProcessPiperAssets() ||
                    PiperTtsService.EvaluateAvailabilityStatus(config.ttsEnabled, config.piperExecutablePath, config.piperVoicePath) == PiperTtsAvailabilityStatus.Ready)
            };
        }

        internal static ValidationReport ValidateAndBind(AICompanionStudioConfig config)
        {
            var report = new ValidationReport();
            if (config == null)
            {
                report.MessageType = MessageType.Error;
                report.Lines.Add("Nyxara AI Studio: No Studio Config is assigned.");
                return report;
            }

            ApplyDefaultPathsIfEmpty(config);
            NormalizeOptionalDependencyState(config);

            var llmPackageDetected = IsPackageInstalled(LlmUnityPackageName) || IsTypeAvailable("LLM") || IsTypeAvailable("LLMAgent");
            var whisperPackageDetected = IsPackageInstalled(WhisperPackageName) || IsTypeAvailable("WhisperManager");

            if (llmPackageDetected && EnsureScriptingDefine(LlmUnityDefine))
            {
                report.HasChanges = true;
                report.RequestedScriptReload = true;
            }

            if (whisperPackageDetected && EnsureScriptingDefine(WhisperDefine))
            {
                report.HasChanges = true;
                report.RequestedScriptReload = true;
            }

            ValidateLlmUnity(config, report, llmPackageDetected);
            ValidateWhisper(config, report, whisperPackageDetected);
            ValidatePiper(config, report);

            if (report.RequestedScriptReload)
            {
                report.Lines.Add("Updated Nyxara scripting defines for detected integrations. Unity may recompile scripts once before the new bindings are fully active.");
            }

            if (report.HasChanges)
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                MarkCurrentSceneDirty();
            }

            if (report.Lines.Count == 0)
            {
                report.Lines.Add("Nyxara AI Studio: No integration changes were needed.");
            }

            if (report.Lines.Any(line => line.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                report.MessageType = report.Lines.Any(line => line.IndexOf("detected", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("found", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? MessageType.Warning
                    : MessageType.Info;
            }

            return report;
        }

        internal static bool RestoreSummaryFromSessionState(string keyPrefix, out string summary, out MessageType messageType)
        {
            var summaryKey = keyPrefix + "ValidationSummary";
            var typeKey = keyPrefix + "ValidationSummaryType";
            summary = SessionState.GetString(summaryKey, string.Empty);
            messageType = (MessageType)SessionState.GetInt(typeKey, (int)MessageType.Info);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return false;
            }

            SessionState.EraseString(summaryKey);
            SessionState.EraseInt(typeKey);
            return true;
        }

        internal static void PersistSummaryToSessionState(string keyPrefix, ValidationReport report)
        {
            SessionState.SetString(keyPrefix + "ValidationSummary", report?.Summary ?? string.Empty);
            SessionState.SetInt(keyPrefix + "ValidationSummaryType", (int)(report?.MessageType ?? MessageType.Info));
        }

        private static void ValidateLlmUnity(AICompanionStudioConfig config, ValidationReport report, bool packageDetected)
        {
            var llmType = ResolveTypeByName("LLM");
            var agentType = ResolveTypeByName("LLMAgent");
            if (!packageDetected || llmType == null || agentType == null)
            {
                if (packageDetected || HasScriptingDefine(LlmUnityDefine))
                {
                    NyxaraCompanionStudioBuilder.QueuePendingIntegrationRepair(config, needsLlmRepair: true, needsWhisperRepair: false);
                    report.Lines.Add(PendingLlmMessage);
                    return;
                }

                var modelFound = TryResolveLlmModelPath(config, out _, out _);
                report.Lines.Add(modelFound
                    ? "LLM model found but LLMUnity package missing."
                    : "LLMUnity package missing.");
                return;
            }

            var modelPathFound = TryResolveLlmModelPath(config, out var resolvedModelPath, out var llmModelSource);
            var roots = FindNyxaraRoots();
            var boundRoots = 0;

            foreach (var root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                var systemsRoot = GetOrCreateChild(root.transform, "AISystems");
                var llmObject = GetOrCreateChild(systemsRoot.transform, "Local LLM");
                var llmComponent = GetOrAddComponentByType(llmObject, llmType, ref report.HasChanges);
                var agentComponent = GetOrAddComponentByType(root, agentType, ref report.HasChanges);
                var brain = root.GetComponent<NyxaraCompanionBrain>();

                if (llmComponent == null || agentComponent == null)
                {
                    continue;
                }

                if (modelPathFound)
                {
                    report.HasChanges |= SetStringPropertyIfMissingOrInvalid(llmComponent, new[] { "_model", "model" }, resolvedModelPath, false);
                }

                report.HasChanges |= AssignObjectReference(agentComponent, "_llm", llmComponent);
                report.HasChanges |= AssignObjectReference(agentComponent, "llm", llmComponent);

                if (brain != null)
                {
                    report.HasChanges |= AssignObjectReference(brain, "agent", agentComponent);
                }

                EditorUtility.SetDirty(root);
                boundRoots++;
            }

            if (boundRoots > 0)
            {
                report.Lines.Add(modelPathFound
                    ? $"LLMUnity detected and bound ({boundRoots} Nyxara root{(boundRoots == 1 ? string.Empty : "s")}, model {llmModelSource})."
                    : $"LLMUnity detected and bound ({boundRoots} Nyxara root{(boundRoots == 1 ? string.Empty : "s")}), but model path missing.");
                return;
            }

            report.Lines.Add(modelPathFound
                ? $"LLMUnity detected. No Nyxara root was available to bind, but model {llmModelSource} is valid."
                : "LLMUnity detected, but no Nyxara root or valid model path was found to bind.");
        }

        private static void ValidateWhisper(AICompanionStudioConfig config, ValidationReport report, bool packageDetected)
        {
            var whisperManagerType = ResolveTypeByName("WhisperManager");
            if (!packageDetected || whisperManagerType == null)
            {
                if (packageDetected || HasScriptingDefine(WhisperDefine))
                {
                    NyxaraCompanionStudioBuilder.QueuePendingIntegrationRepair(config, needsLlmRepair: false, needsWhisperRepair: true);
                    report.Lines.Add(PendingWhisperMessage);
                    return;
                }

                var modelFound = TryResolveWhisperModelPath(config, out _, out _);
                report.Lines.Add(modelFound
                    ? "Whisper model found but whisper.unity package missing."
                    : "whisper.unity package missing.");
                return;
            }

            var modelPathFound = TryResolveWhisperModelPath(config, out var resolvedModelPath, out var whisperModelSource);
            var roots = FindNyxaraRoots();
            var boundRoots = 0;

            foreach (var root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                var systemsRoot = GetOrCreateChild(root.transform, "AISystems");
                var sttObject = GetOrCreateChild(systemsRoot.transform, "Speech To Text");
                var whisperManager = GetOrAddComponentByType(sttObject, whisperManagerType, ref report.HasChanges);
                var whisperInput = sttObject.GetComponent<WhisperMicrophoneInput>();
                if (whisperInput == null)
                {
                    whisperInput = Undo.AddComponent<WhisperMicrophoneInput>(sttObject);
                    report.HasChanges = true;
                }

                var brain = root.GetComponent<NyxaraCompanionBrain>();

                if (whisperManager == null || whisperInput == null)
                {
                    continue;
                }

                if (modelPathFound)
                {
                    report.HasChanges |= SetStringPropertyIfMissingOrInvalid(whisperManager, new[] { "modelPath", "ModelPath" }, resolvedModelPath, true);
                    report.HasChanges |= SetBooleanProperty(whisperManager, "isModelPathInStreamingAssets", true);
                    report.HasChanges |= SetBooleanProperty(whisperManager, "IsModelPathInStreamingAssets", true);
                }

                report.HasChanges |= AssignObjectReference(whisperInput, "whisperManager", whisperManager);
                if (brain != null)
                {
                    report.HasChanges |= AssignObjectReference(whisperInput, "companionBrain", brain);
                }

                EditorUtility.SetDirty(root);
                boundRoots++;
            }

            if (boundRoots > 0)
            {
                report.Lines.Add(modelPathFound
                    ? $"Whisper detected and bound ({boundRoots} Nyxara root{(boundRoots == 1 ? string.Empty : "s")}, model {whisperModelSource})."
                    : $"Whisper detected and bound ({boundRoots} Nyxara root{(boundRoots == 1 ? string.Empty : "s")}), but model path missing.");
                return;
            }

            report.Lines.Add(modelPathFound
                ? $"Whisper detected. No Nyxara root was available to bind, but model {whisperModelSource} is valid."
                : "Whisper detected, but no Nyxara root or valid model path was found to bind.");
        }

        private static void ValidatePiper(AICompanionStudioConfig config, ValidationReport report)
        {
            var runtimeFound = TryResolvePiperRuntimePath(config, out var resolvedRuntimePath, out var runtimeSource);
            var voiceFound = TryResolvePiperVoicePath(config, out var resolvedVoicePath, out var voiceSource);
            var inProcessAssetsFound = HasInProcessPiperAssets();

            if (runtimeFound && !string.Equals(config.piperExecutablePath, resolvedRuntimePath, StringComparison.Ordinal))
            {
                config.piperExecutablePath = resolvedRuntimePath;
                report.HasChanges = true;
            }

            if (voiceFound && !string.Equals(config.piperVoicePath, resolvedVoicePath, StringComparison.Ordinal))
            {
                config.piperVoicePath = resolvedVoicePath;
                report.HasChanges = true;
            }

            var ttsServices = FindAllSceneObjectsOfType<PiperTtsService>();
            var inProcessBound = false;
            foreach (var ttsService in ttsServices)
            {
                if (ttsService == null)
                {
                    continue;
                }

                if (runtimeFound)
                {
                    ttsService.PiperExecutablePath = resolvedRuntimePath;
                }

                if (voiceFound)
                {
                    ttsService.VoiceModelPath = resolvedVoicePath;
                }

                inProcessBound |= BindInProcessPiperAssets(ttsService);
                ttsService.TtsEnabled = config.ttsEnabled;
                EditorUtility.SetDirty(ttsService);
            }

            var inProcessFound = inProcessAssetsFound || inProcessBound || HasInProcessPiperVoice();
            config.ttsEnabled = inProcessFound || (runtimeFound && voiceFound);

            foreach (var ttsService in ttsServices)
            {
                if (ttsService != null)
                {
                    ttsService.TtsEnabled = config.ttsEnabled;
                    EditorUtility.SetDirty(ttsService);
                }
            }

            var phonemeExtractors = FindAllSceneObjectsOfType<PiperTTSPhonemeExtractor>();
            foreach (var phonemeExtractor in phonemeExtractors)
            {
                if (phonemeExtractor == null)
                {
                    continue;
                }

                if (runtimeFound)
                {
                    report.HasChanges |= SetStringProperty(phonemeExtractor, "piperExecutablePath", resolvedRuntimePath);
                }

                if (voiceFound)
                {
                    report.HasChanges |= SetStringProperty(phonemeExtractor, "voiceModelPath", resolvedVoicePath);
                }
            }

            if (inProcessFound)
            {
                report.Lines.Add("In-process Piper voice is configured. Voice output does not require piper.exe.");
                return;
            }

            if (runtimeFound && voiceFound)
            {
                report.Lines.Add($"Piper runtime found ({runtimeSource}) and voice found ({voiceSource}).");
                return;
            }

            if (runtimeFound)
            {
                report.Lines.Add($"Piper runtime found ({runtimeSource}), but voice missing.");
                return;
            }

            if (voiceFound)
            {
                report.Lines.Add($"Piper voice found ({voiceSource}), but runtime missing.");
                return;
            }

            report.Lines.Add("Piper runtime missing and voice missing.");
        }

        private static bool HasInProcessPiperVoice()
        {
            foreach (var ttsService in FindAllSceneObjectsOfType<PiperTtsService>())
            {
                if (ttsService != null && ttsService.HasInProcessVoice)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasInProcessPiperAssets()
        {
            return AssetDatabase.LoadAssetAtPath<ModelAsset>(DefaultInProcessPiperModelPath) != null &&
                   AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultInProcessPiperConfigPath) != null;
        }

        /// <summary>
        /// Assigns the importable in-process voice assets to <paramref name="ttsService"/> when they are missing.
        /// Returns true when the service now has both a ModelAsset and its TextAsset config.
        /// </summary>
        internal static bool BindInProcessPiperAssets(PiperTtsService ttsService)
        {
            if (ttsService == null)
            {
                return false;
            }

            if (ttsService.HasInProcessVoice)
            {
                return true;
            }

            var modelPath = ResolveInProcessModelAssetPath(ttsService.VoiceModelPath);
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
            var config = AssetDatabase.LoadAssetAtPath<TextAsset>(modelPath + ".json");
            if (model == null || config == null)
            {
                return false;
            }

            ttsService.VoiceModel = model;
            ttsService.VoiceConfig = config;

            if (string.IsNullOrWhiteSpace(ttsService.VoiceModelPath))
            {
                ttsService.VoiceModelPath = modelPath;
            }

            var serializedObject = new SerializedObject(ttsService);
            var espeakDataProperty = serializedObject.FindProperty("espeakDataDirectory");
            if (espeakDataProperty != null && string.IsNullOrWhiteSpace(espeakDataProperty.stringValue))
            {
                espeakDataProperty.stringValue = DefaultPiperEspeakDataDirectory;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(ttsService);
            return true;
        }

        private static string ResolveInProcessModelAssetPath(string configuredPath)
        {
            var normalized = (configuredPath ?? string.Empty).Replace('\\', '/').Trim();
            if (normalized.Length > 0 && !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Assets/" + normalized.TrimStart('/');
            }

            if (normalized.Length > 0 &&
                AssetDatabase.LoadAssetAtPath<ModelAsset>(normalized) != null &&
                AssetDatabase.LoadAssetAtPath<TextAsset>(normalized + ".json") != null)
            {
                return normalized;
            }

            return DefaultInProcessPiperModelPath;
        }

        private static bool TryResolveLlmModelPath(AICompanionStudioConfig config, out string resolvedPath, out string source)
        {
            resolvedPath = string.Empty;
            source = "missing";

            if (TryUseExistingValidPath(config != null ? config.llmModelPath : string.Empty, false, out resolvedPath))
            {
                var configuredFileName = Path.GetFileName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(configuredFileName) &&
                    TryFindFirstFile(Path.Combine(Application.streamingAssetsPath, "Models"), configuredFileName, out var localProjectPath))
                {
                    resolvedPath = MakeStreamingAssetsRelative(localProjectPath);
                    if (config != null)
                    {
                        config.llmModelPath = resolvedPath;
                    }

                    source = "StreamingAssets/Models";
                    return true;
                }

                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                if (config != null)
                {
                    config.llmModelPath = resolvedPath;
                }

                source = "config";
                return true;
            }

            var llmComponent = FindComponentByTypeName("LLM");
            if (TryGetAnyStringProperty(llmComponent, new[] { "_model", "model" }, out var liveModelPath) && TryUseExistingValidPath(liveModelPath, false, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.llmModelPath = resolvedPath;
                source = "existing LLM component";
                return true;
            }

            if (TryFindFirstFile(Path.Combine(Application.streamingAssetsPath, "Models"), "*.gguf", out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.llmModelPath = resolvedPath;
                source = "StreamingAssets/Models";
                return true;
            }

            return false;
        }

        private static bool TryResolveWhisperModelPath(AICompanionStudioConfig config, out string resolvedPath, out string source)
        {
            resolvedPath = string.Empty;
            source = "missing";

            if (TryUseExistingValidPath(config != null ? config.whisperModelRelativePath : string.Empty, true, out resolvedPath))
            {
                var configuredFileName = Path.GetFileName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(configuredFileName) &&
                    TryFindFirstFile(Path.Combine(Application.streamingAssetsPath, "Speech"), configuredFileName, out var localProjectPath))
                {
                    resolvedPath = MakeStreamingAssetsRelative(localProjectPath);
                    if (config != null)
                    {
                        config.whisperModelRelativePath = resolvedPath;
                    }

                    source = "StreamingAssets/Speech";
                    return true;
                }

                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                if (config != null)
                {
                    config.whisperModelRelativePath = resolvedPath;
                }

                source = "config";
                return true;
            }

            var whisperManager = FindComponentByTypeName("WhisperManager");
            if (TryGetAnyStringProperty(whisperManager, new[] { "modelPath", "ModelPath" }, out var liveModelPath) && TryUseExistingValidPath(liveModelPath, true, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.whisperModelRelativePath = resolvedPath;
                source = "existing WhisperManager";
                return true;
            }

            if (TryFindFirstFile(Path.Combine(Application.streamingAssetsPath, "Speech"), "*.bin", out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.whisperModelRelativePath = resolvedPath;
                source = "StreamingAssets/Speech";
                return true;
            }

            return false;
        }

        private static bool TryResolvePiperRuntimePath(AICompanionStudioConfig config, out string resolvedPath, out string source)
        {
            resolvedPath = string.Empty;
            source = "missing";

            if (TryUseExistingValidPath(config != null ? config.piperExecutablePath : string.Empty, true, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                source = "config";
                return true;
            }

            var ttsService = FindFirstSceneObjectByType<PiperTtsService>();
            if (ttsService != null && TryUseExistingValidPath(ttsService.PiperExecutablePath, true, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.piperExecutablePath = resolvedPath;
                source = "existing PiperTtsService";
                return true;
            }

            var phonemeExtractor = FindFirstSceneObjectByType<PiperTTSPhonemeExtractor>();
            if (TryGetStringProperty(phonemeExtractor, "piperExecutablePath", out var extractorRuntimePath) && TryUseExistingValidPath(extractorRuntimePath, true, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.piperExecutablePath = resolvedPath;
                source = "existing phoneme extractor";
                return true;
            }

            foreach (var executableName in new[] { "piper.exe", "piper" })
            {
                if (TryFindFirstFile(Path.Combine(Application.streamingAssetsPath, "Speech"), executableName, out resolvedPath))
                {
                    resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                    config.piperExecutablePath = resolvedPath;
                    source = "StreamingAssets/Speech";
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolvePiperVoicePath(AICompanionStudioConfig config, out string resolvedPath, out string source)
        {
            resolvedPath = string.Empty;
            source = "missing";

            if (TryUseExistingValidPath(config != null ? config.piperVoicePath : string.Empty, true, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                source = "config";
                return true;
            }

            var ttsService = FindFirstSceneObjectByType<PiperTtsService>();
            if (ttsService != null && TryUseExistingValidPath(ttsService.VoiceModelPath, true, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.piperVoicePath = resolvedPath;
                source = "existing PiperTtsService";
                return true;
            }

            var phonemeExtractor = FindFirstSceneObjectByType<PiperTTSPhonemeExtractor>();
            if (TryGetStringProperty(phonemeExtractor, "voiceModelPath", out var extractorVoicePath) && TryUseExistingValidPath(extractorVoicePath, true, out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.piperVoicePath = resolvedPath;
                source = "existing phoneme extractor";
                return true;
            }

            if (TryFindFirstFile(Path.Combine(Application.streamingAssetsPath, "Speech", "PiperVoices"), "*.onnx", out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.piperVoicePath = resolvedPath;
                source = "StreamingAssets/Speech/PiperVoices";
                return true;
            }

            if (TryFindFirstFile(Path.Combine(Application.dataPath, "Speech", "Piper"), "*.onnx", out resolvedPath))
            {
                resolvedPath = MakeStreamingAssetsRelative(resolvedPath);
                config.piperVoicePath = resolvedPath;
                source = "Assets/Speech/Piper";
                return true;
            }

            return false;
        }

        private static bool EnsureScriptingDefine(string define)
        {
            var defines = GetScriptingDefines() ?? string.Empty;
            var entries = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Trim())
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (entries.Contains(define, StringComparer.Ordinal))
            {
                return false;
            }

            entries.Add(define);
            SetScriptingDefines(string.Join(";", entries));
            return true;
        }

        private static bool HasScriptingDefine(string define)
        {
            var defines = GetScriptingDefines() ?? string.Empty;
            return defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(entry => string.Equals(entry.Trim(), define, StringComparison.Ordinal));
        }

        private static string GetScriptingDefines()
        {
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var playerSettingsType = typeof(PlayerSettings);
            var namedBuildTargetType = playerSettingsType.Assembly.GetType("UnityEditor.Build.NamedBuildTarget");
            var getMethod = playerSettingsType.GetMethod("GetScriptingDefineSymbols", BindingFlags.Public | BindingFlags.Static, null, namedBuildTargetType != null ? new[] { namedBuildTargetType } : Type.EmptyTypes, null);
            var fromGroupMethod = namedBuildTargetType?.GetMethod("FromBuildTargetGroup", BindingFlags.Public | BindingFlags.Static);

            if (getMethod != null && fromGroupMethod != null)
            {
                var namedBuildTarget = fromGroupMethod.Invoke(null, new object[] { buildTargetGroup });
                var result = getMethod.Invoke(null, new[] { namedBuildTarget });
                return result as string ?? string.Empty;
            }

#pragma warning disable CS0618
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
#pragma warning restore CS0618
        }

        private static void SetScriptingDefines(string defines)
        {
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var playerSettingsType = typeof(PlayerSettings);
            var namedBuildTargetType = playerSettingsType.Assembly.GetType("UnityEditor.Build.NamedBuildTarget");
            var setMethod = playerSettingsType.GetMethod("SetScriptingDefineSymbols", BindingFlags.Public | BindingFlags.Static, null, namedBuildTargetType != null ? new[] { namedBuildTargetType, typeof(string) } : Type.EmptyTypes, null);
            var fromGroupMethod = namedBuildTargetType?.GetMethod("FromBuildTargetGroup", BindingFlags.Public | BindingFlags.Static);

            if (setMethod != null && fromGroupMethod != null)
            {
                var namedBuildTarget = fromGroupMethod.Invoke(null, new object[] { buildTargetGroup });
                setMethod.Invoke(null, new object[] { namedBuildTarget, defines });
                return;
            }

#pragma warning disable CS0618
            PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, defines);
#pragma warning restore CS0618
        }

        private static bool IsPackageInstalled(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return false;
            }

            var packageJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", packageName, "package.json");
            if (File.Exists(packageJsonPath))
            {
                return true;
            }

            var manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            try
            {
                return File.ReadAllText(manifestPath).IndexOf($"\"{packageName}\"", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsTypeAvailable(string typeName)
        {
            return ResolveTypeByName(typeName) != null;
        }

        private static Type ResolveTypeByName(string typeName)
        {
            foreach (var candidateName in EnumerateCandidateTypeNames(typeName))
            {
                var directType = Type.GetType(candidateName, false);
                if (directType != null)
                {
                    return directType;
                }
            }

            var cachedType = TypeCache.GetTypesDerivedFrom<Component>()
                .FirstOrDefault(type => MatchesTypeName(type, typeName));
            if (cachedType != null)
            {
                return cachedType;
            }

            return AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        return ex.Types.Where(type => type != null);
                    }
                })
                .FirstOrDefault(type => type != null && MatchesTypeName(type, typeName));
        }

        private static IEnumerable<string> EnumerateCandidateTypeNames(string typeName)
        {
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                yield return typeName;
            }

            switch (typeName)
            {
                case "LLM":
                    yield return "LLMUnity.LLM";
                    break;
                case "LLMAgent":
                    yield return "LLMUnity.LLMAgent";
                    break;
                case "WhisperManager":
                    yield return "Whisper.WhisperManager";
                    break;
            }
        }

        private static bool MatchesTypeName(Type type, string typeName)
        {
            if (type == null || string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            if (string.Equals(type.Name, typeName, StringComparison.Ordinal) ||
                string.Equals(type.FullName, typeName, StringComparison.Ordinal))
            {
                return true;
            }

            return EnumerateCandidateTypeNames(typeName)
                .Any(candidateName => string.Equals(type.FullName, candidateName, StringComparison.Ordinal));
        }

        private static List<GameObject> FindNyxaraRoots()
        {
            var roots = new List<GameObject>();
            AddRoots(roots, FindAllSceneObjectsOfType<NyxaraCompanionBrain>().Select(component => component.gameObject));
            AddRoots(roots, FindAllSceneObjectsOfType<RuntimeConversationOverlay>().Select(component => component.gameObject));
            AddRoots(roots, FindAllSceneObjectsOfType<ArkItBlendshapeDriver>().Select(component => component.gameObject));

            foreach (var candidate in FindAllSceneObjectsOfType<Transform>()
                .Select(transform => transform != null ? transform.gameObject : null)
                .Where(gameObject => gameObject != null && (gameObject.name.EndsWith("_StudioRoot", StringComparison.OrdinalIgnoreCase) || gameObject.name.EndsWith("_CompanionRoot", StringComparison.OrdinalIgnoreCase))))
            {
                AddUniqueRoot(roots, candidate);
            }

            return roots;
        }

        private static void AddRoots(List<GameObject> roots, IEnumerable<GameObject> candidates)
        {
            foreach (var candidate in candidates)
            {
                AddUniqueRoot(roots, candidate);
            }
        }

        private static void AddUniqueRoot(List<GameObject> roots, GameObject candidate)
        {
            if (candidate == null || roots.Contains(candidate))
            {
                return;
            }

            roots.Add(candidate);
        }

        private static GameObject GetOrCreateChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
            {
                return child.gameObject;
            }

            var created = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(created, $"Create {childName}");
            created.transform.SetParent(parent, false);
            return created;
        }

        private static Component GetOrAddComponentByType(GameObject target, Type type, ref bool hasChanges)
        {
            if (target == null || type == null || !typeof(Component).IsAssignableFrom(type))
            {
                return null;
            }

            var existing = target.GetComponents<Component>().FirstOrDefault(component => component != null && type.IsInstanceOfType(component));
            if (existing != null)
            {
                return existing;
            }

            hasChanges = true;
            return Undo.AddComponent(target, type);
        }

        private static Component FindComponentByTypeName(string typeName)
        {
            return FindAllSceneObjectsOfType<Component>()
                .FirstOrDefault(component => component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal));
        }

        private static T FindFirstSceneObjectByType<T>() where T : UnityEngine.Object
        {
            return FindAllSceneObjectsOfType<T>().FirstOrDefault();
        }

        private static T[] FindAllSceneObjectsOfType<T>() where T : UnityEngine.Object
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .Where(IsSceneObject)
                .ToArray();
        }

        private static bool IsSceneObject(UnityEngine.Object obj)
        {
            if (obj == null || EditorUtility.IsPersistent(obj))
            {
                return false;
            }

            if (obj.hideFlags == HideFlags.NotEditable || obj.hideFlags == HideFlags.HideAndDontSave)
            {
                return false;
            }

            switch (obj)
            {
                case GameObject gameObject:
                    return gameObject.scene.IsValid();
                case Component component:
                    return component.gameObject.scene.IsValid();
                default:
                    return true;
            }
        }

        private static bool AssignObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == value)
            {
                return false;
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            return true;
        }

        private static bool SetStringProperty(Component component, string propertyName, string value)
        {
            if (component == null)
            {
                return false;
            }

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.String || string.Equals(property.stringValue, value ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            property.stringValue = value ?? string.Empty;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
            return true;
        }

        private static bool SetStringPropertyIfMissingOrInvalid(Component component, IEnumerable<string> propertyNames, string value, bool treatAsStreamingAssetPath)
        {
            if (component == null || propertyNames == null)
            {
                return false;
            }

            var propertyNameList = propertyNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            if (propertyNameList.Count == 0)
            {
                return false;
            }

            var changed = false;
            foreach (var propertyName in propertyNameList)
            {
                if (!TryGetStringProperty(component, propertyName, out var existingValue) || !IsValidFilePath(existingValue, treatAsStreamingAssetPath))
                {
                    changed |= SetStringProperty(component, propertyName, value);
                }
            }

            return changed;
        }

        private static bool SetBooleanProperty(Component component, string propertyName, bool value)
        {
            if (component == null)
            {
                return false;
            }

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean || property.boolValue == value)
            {
                return false;
            }

            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
            return true;
        }

        private static bool TryGetStringProperty(Component component, string propertyName, out string value)
        {
            value = string.Empty;
            if (component == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.String)
            {
                return false;
            }

            value = property.stringValue;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetAnyStringProperty(Component component, IEnumerable<string> propertyNames, out string value)
        {
            value = string.Empty;
            if (propertyNames == null)
            {
                return false;
            }

            foreach (var propertyName in propertyNames)
            {
                if (TryGetStringProperty(component, propertyName, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryUseExistingValidPath(string candidatePath, bool treatAsStreamingAssetPath, out string resolvedPath)
        {
            resolvedPath = ResolveAbsoluteOrProjectPath(candidatePath, treatAsStreamingAssetPath);
            return !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath);
        }

        private static bool IsValidFilePath(string candidatePath, bool treatAsStreamingAssetPath)
        {
            return TryUseExistingValidPath(candidatePath, treatAsStreamingAssetPath, out _);
        }

        private static bool TryFindFirstFile(string folderPath, string searchPattern, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return false;
            }

            resolvedPath = Directory.GetFiles(folderPath, searchPattern, SearchOption.AllDirectories).FirstOrDefault();
            return !string.IsNullOrWhiteSpace(resolvedPath);
        }

        private static string ResolveAbsoluteOrProjectPath(string path, bool treatAsStreamingAssetPath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
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

            var streamingRelativePath = sanitizedPath
                .Replace("Assets/StreamingAssets/", string.Empty)
                .Replace("StreamingAssets/", string.Empty)
                .Replace('/', Path.DirectorySeparatorChar);

            if (treatAsStreamingAssetPath)
            {
                var streamingCandidate = Path.Combine(Application.streamingAssetsPath, streamingRelativePath);
                if (File.Exists(streamingCandidate))
                {
                    return streamingCandidate;
                }

                // In-process Piper assets live under Assets/ and are not duplicated into StreamingAssets.
                var assetsCandidate = Path.Combine(Application.dataPath, streamingRelativePath);
                if (File.Exists(assetsCandidate))
                {
                    return assetsCandidate;
                }

                return streamingCandidate;
            }

            var candidate = Path.Combine(Application.streamingAssetsPath, streamingRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var assetsRelativeCandidate = Path.Combine(Application.dataPath, sanitizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(assetsRelativeCandidate))
            {
                return assetsRelativeCandidate;
            }

            return Path.Combine(Directory.GetCurrentDirectory(), sanitizedPath.Replace('/', Path.DirectorySeparatorChar));
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

                var normalizedAssets = Path.GetFullPath(Application.dataPath);
                if (normalizedAbsolute.StartsWith(normalizedAssets, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = normalizedAbsolute.Substring(normalizedAssets.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return "Assets/" + relative.Replace('\\', '/');
                }

                return normalizedAbsolute.Replace('\\', '/');
            }
            catch (Exception)
            {
                return absolutePath.Replace('\\', '/');
            }
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

            if (!HasInProcessPiperAssets() &&
                (string.IsNullOrWhiteSpace(config.piperExecutablePath) || string.IsNullOrWhiteSpace(config.piperVoicePath)))
            {
                config.ttsEnabled = false;
            }
        }

        private static void MarkCurrentSceneDirty()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
            }
        }
    }
}
#endif
