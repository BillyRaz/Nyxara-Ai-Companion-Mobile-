// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nyxara.AICompanion.Configuration;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Data;
using Nyxara.AICompanion.Expressions;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.LipSync;
using Nyxara.AICompanion.Runtime;
using Nyxara.AICompanion.Speech;
using Nyxara.AICompanion.Studio;
using Nyxara.AICompanion.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    public static class NyxaraCompanionStudioBuilder
    {
        private const string DefaultStudioRootFolder = "Assets/Nyxara AI Studio";
        private const string DefaultGeneratedFolder = DefaultStudioRootFolder + "/Generated";
        private const string LlmUnityPackageName = "ai.undream.llm";
        private const string WhisperPackageName = "com.whisper.unity";
        private const string MissingLlmMessage = "Nyxara AI Studio: LLMUnity not installed. AI features disabled.";
        private const string MissingWhisperMessage = "Nyxara AI Studio: Whisper not installed. Speech-to-text features disabled.";
        private const string MissingUrpMessage = "Nyxara AI Studio: Universal Render Pipeline not installed. Using built-in camera and light setup.";
        private const string PendingLlmMessage = "Nyxara AI Studio: LLMUnity package detected, but Unity is still compiling/loading it. Nyxara will repair AI bindings automatically once scripts finish reloading.";
        private const string PendingWhisperMessage = "Nyxara AI Studio: Whisper package detected, but Unity is still compiling/loading it. Nyxara will repair speech bindings automatically once scripts finish reloading.";
        private const string PendingRepairConfigPathKey = "NyxaraStudio.PendingRepair.ConfigPath";
        private const string PendingRepairLlmKey = "NyxaraStudio.PendingRepair.LLM";
        private const string PendingRepairWhisperKey = "NyxaraStudio.PendingRepair.Whisper";

        private const string InProcessPiperModelAssetPath = "Assets/Speech/Piper/clean100.onnx";
        private const string InProcessPiperConfigAssetPath = "Assets/Speech/Piper/clean100.onnx.json";
        private const string PiperEspeakDataDirectory = "Speech/PiperRuntime/piper/espeak-ng-data";

        private static string _activeBuildConfigPath;
        private static bool _pendingRepairUpdateHookInstalled;

        private enum OptionalIntegration
        {
            LlmUnity,
            Whisper
        }

        public static void EnsureFolderStructure(AICompanionStudioConfig config)
        {
            EnsureAssetFolderPath(string.IsNullOrWhiteSpace(config.rootFolder) ? DefaultStudioRootFolder : config.rootFolder);
            EnsureAssetFolderPath(config.prefabFolder);
            EnsureAssetFolderPath(config.companionPrefabFolder);
            EnsureAssetFolderPath(config.profileFolder);
            EnsureAssetFolderPath(config.generatedFolder);
            EnsureAssetFolderPath(config.expressionFolder);
            AssetDatabase.Refresh();
        }

        public static CharacterProfileData EnsureCharacterProfile(AICompanionStudioConfig config)
        {
            if (config == null)
            {
                return null;
            }

            if (config.characterProfile != null)
            {
                return config.characterProfile;
            }

            if (!config.createProfileIfMissing)
            {
                return null;
            }

            EnsureAssetFolderPath(string.IsNullOrWhiteSpace(config.rootFolder) ? DefaultStudioRootFolder : config.rootFolder);
            EnsureAssetFolderPath(config.generatedFolder);
            EnsureAssetFolderPath(config.profileFolder);

            var assetPath = $"{config.profileFolder}/{config.characterName}Profile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<CharacterProfileData>(assetPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CharacterProfileData>();
                profile.characterName = config.characterName;
                profile.name = $"{config.characterName}Profile";
                AssetDatabase.CreateAsset(profile, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            config.characterProfile = profile;
            EditorUtility.SetDirty(config);
            return profile;
        }

        public static GameObject BuildStudioRoot(AICompanionStudioConfig config)
        {
            _activeBuildConfigPath = AssetDatabase.GetAssetPath(config);
            EnsureFolderStructure(config);
            var profile = EnsureCharacterProfile(config);
            RemoveExistingStudioRoot(config.studioRootName);

            try
            {
                var root = CreateRootFromSource(config, out var characterRoot, out var characterInstance);
                if (root == null)
                {
                    return null;
                }

                var systemsRoot = GetOrCreateChild(root.transform, "AISystems");

                var llmObject = GetOrCreateChild(systemsRoot.transform, "Local LLM");
                var llm = GetOrAddOptionalComponent(llmObject, "LLM", OptionalIntegration.LlmUnity);
                if (llm != null)
                {
                    AssignStringField(llm, "model", ResolveModelPathForLlm(config));
                    AssignStringField(llm, "_model", ResolveModelPathForLlm(config));
                    AssignIntField(llm, "contextSize", config.llmContextSize);
                    AssignIntField(llm, "_contextSize", config.llmContextSize);
                    AssignIntField(llm, "numThreads", config.llmNumThreads);
                    AssignIntField(llm, "_numThreads", config.llmNumThreads);
                    AssignIntField(llm, "numGPULayers", 0);
                    AssignIntField(llm, "_numGPULayers", 0);
                }

                var sttObject = GetOrCreateChild(systemsRoot.transform, "Speech To Text");
                var whisperManager = GetOrAddOptionalComponent(sttObject, "WhisperManager", OptionalIntegration.Whisper);
                if (whisperManager != null)
                {
                    AssignStringField(whisperManager, "ModelPath", config.whisperModelRelativePath);
                    AssignBoolField(whisperManager, "IsModelPathInStreamingAssets", true);
                }

                var speechObject = GetOrCreateChild(systemsRoot.transform, "Speech Synthesis");
                var audioSource = GetOrAddComponent<AudioSource>(speechObject);
                var tts = GetOrAddComponent<PiperTtsService>(speechObject);
                tts.TtsEnabled = config.ttsEnabled;
                tts.PiperExecutablePath = config.piperExecutablePath;
                tts.VoiceModelPath = config.piperVoicePath;
                var resolvedModelPath1 = ResolveVoiceModelAssetPath(config.piperVoicePath);
                AssignAssetReference(tts, "voiceModel", resolvedModelPath1);
                AssignAssetReference(tts, "voiceConfig", resolvedModelPath1 + ".json");
                AssignStringField(tts, "espeakDataDirectory", PiperEspeakDataDirectory);
                AssignIntField(tts, "speakerId", 0);
                AssignBoolField(tts, "enableDevelopmentCliFallback", false);
                AssignObjectReference(tts, "audioSource", audioSource);

                var agent = GetOrAddOptionalComponent(root, "LLMAgent", OptionalIntegration.LlmUnity);
                if (agent != null)
                {
                    AssignObjectReference(agent, "llm", llm);
                    AssignObjectReference(agent, "_llm", llm);
                    AssignStringField(agent, "systemPrompt", "You are Nyxara, a concise, expressive companion. Respond briefly, naturally, and follow the requested output format exactly.");
                    AssignStringField(agent, "_systemPrompt", "You are Nyxara, a concise, expressive companion. Respond briefly, naturally, and follow the requested output format exactly.");
                    AssignFloatField(agent, "temperature", config.llmTemperature);
                    AssignFloatField(agent, "topP", config.llmTopP);
                    AssignIntField(agent, "topK", config.llmTopK);
                    AssignFloatField(agent, "minP", config.llmMinP);
                    AssignFloatField(agent, "repeatPenalty", config.llmRepeatPenalty);
                    AssignIntField(agent, "numPredict", config.llmNumPredict);
                    AssignBoolField(agent, "cachePrompt", config.llmCachePrompt);
                }

                var brain = GetOrAddComponent<NyxaraCompanionBrain>(root);
                var faceDriver = GetOrAddComponent<ArkItBlendshapeDriver>(root);
                var signalRouter = GetOrAddComponent<ExpressionSignalRouter>(root);
                var expressionLibrary = GetOrAddComponent<ExpressionLibraryManager>(root);
                var expressionTriggerPlayer = GetOrAddComponent<ExpressionTriggerPlayer>(root);
                var memoryController = GetOrAddComponent<RecentMemoryController>(root);
                var actionGatekeeper = GetOrAddComponent<ActionGatekeeper>(root);
                var actionExecutor = GetOrAddComponent<CompanionActionExecutor>(root);
                var lipSyncController = GetOrAddComponent<VisemeLipSyncController>(root);
                var phonemeExtractor = GetOrAddComponent<PiperTTSPhonemeExtractor>(root);
                var microphoneInput = GetOrAddComponent<WhisperMicrophoneInput>(sttObject);

                if (config.autoAttachBootstrap)
                {
                    GetOrAddComponent<CompanionBootstrap>(root);
                }

                RefreshPreferredFaceRendererPath(config, characterInstance);
                var faceRenderer = ResolveFaceRenderer(config, characterInstance);
                if (config.createStudioEnvironment)
                {
                    CreateStudioEnvironment(config, root, characterRoot.transform, faceRenderer);
                }

                ConfigureRootSystems(config, root, characterInstance, profile);
                RepairDetectedIntegrations(config, root);

                if (config.saveRootPrefab)
                {
                    var prefabPath = $"{config.prefabFolder}/{config.characterName}_StudioRoot.prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }

                if (config.createSceneInstance)
                {
                    Selection.activeGameObject = root;
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                    return root;
                }

                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.Refresh();
                return null;
            }
            finally
            {
                _activeBuildConfigPath = null;
            }
        }

        private static void RepairDetectedIntegrations(AICompanionStudioConfig config, GameObject root)
        {
            if (config == null || root == null)
            {
                return;
            }

            var report = NyxaraIntegrationValidator.ValidateAndBind(config);
            if (!report.HasChanges)
            {
                return;
            }

            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(root);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static GameObject FinalizeCompanionRoot(AICompanionStudioConfig config, GameObject root)
        {
            if (config == null || root == null)
            {
                return null;
            }

            EnsureFolderStructure(config);
            EnsureCharacterProfile(config);
            var characterRoot = FindCharacterRoot(root.transform);
            var characterInstance = config.sourceIsExistingRootPrefab
                ? characterRoot?.gameObject
                : (characterRoot != null && characterRoot.childCount > 0 ? characterRoot.GetChild(0).gameObject : null);
            ConfigureRootSystems(config, root, characterInstance, config.characterProfile);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var prefabPath = $"{config.companionPrefabFolder}/{config.characterName}_CompanionRoot.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.UserAction);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return prefab;
        }

        public static void ApplyStudioRigToExistingRoot(AICompanionStudioConfig config, GameObject root)
        {
            if (config == null || root == null)
            {
                return;
            }

            var characterRoot = FindCharacterRoot(root.transform);
            if (characterRoot == null)
            {
                return;
            }

            Transform characterInstance = config.sourceIsExistingRootPrefab ? characterRoot : (characterRoot.childCount > 0 ? characterRoot.GetChild(0) : null);
            if (characterInstance != null)
            {
                characterInstance.localPosition = config.characterLocalPosition;
            }

            var studioRig = root.transform.Find("StudioRig");
            if (studioRig != null)
            {
                Undo.DestroyObjectImmediate(studioRig.gameObject);
            }

            RefreshPreferredFaceRendererPath(config, characterInstance != null ? characterInstance.gameObject : null);
            var faceRenderer = ResolveFaceRenderer(config, characterInstance != null ? characterInstance.gameObject : null);
            if (config.createStudioEnvironment)
            {
                CreateStudioEnvironment(config, root, characterRoot, faceRenderer);
            }

            ConfigureRootSystems(config, root, characterInstance != null ? characterInstance.gameObject : null, config.characterProfile);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static void ResetStudio(AICompanionStudioConfig config)
        {
            if (config == null)
            {
                return;
            }

            RemoveExistingStudioRoot(config.studioRootName);

            var prefabPath = $"{config.prefabFolder}/{config.characterName}_StudioRoot.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                AssetDatabase.DeleteAsset(prefabPath);
            }

            var companionPrefabPath = $"{config.companionPrefabFolder}/{config.characterName}_CompanionRoot.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(companionPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(companionPrefabPath);
            }

            AssetDatabase.Refresh();
        }

        public static void FaceCharacterTowardStudioCamera(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var characterRoot = FindCharacterRoot(root.transform);
            if (characterRoot == null)
            {
                return;
            }

            var cameraTransform = root.transform.Find("StudioRig/StudioCamera");
            if (cameraTransform == null)
            {
                return;
            }

            var character = characterRoot.childCount > 0 ? characterRoot.GetChild(0) : characterRoot;
            var toCamera = cameraTransform.position - character.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Undo.RecordObject(character, "Face Character Toward Studio Camera");
            character.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        private static SkinnedMeshRenderer ResolveFaceRenderer(AICompanionStudioConfig config, GameObject characterInstance)
        {
            if (characterInstance != null && !string.IsNullOrWhiteSpace(config.preferredFaceRendererPath))
            {
                var preferred = characterInstance.transform.Find(config.preferredFaceRendererPath);
                if (preferred != null)
                {
                    return preferred.GetComponent<SkinnedMeshRenderer>();
                }
            }

            if (characterInstance == null)
            {
                return null;
            }

            var renderers = characterInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return null;
            }

            return renderers
                .OrderByDescending(ScoreFaceRenderer)
                .ThenBy(renderer => renderer.transform.GetSiblingIndex())
                .FirstOrDefault();
        }

        private static int ScoreFaceRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return int.MinValue;
            }

            var score = 0;
            var rendererName = renderer.name.ToLowerInvariant();
            var meshName = renderer.sharedMesh.name.ToLowerInvariant();

            if (rendererName.Contains("head") || meshName.Contains("head"))
            {
                score += 50;
            }

            if (rendererName.Contains("face") || meshName.Contains("face"))
            {
                score += 30;
            }

            if (rendererName.Contains("lash") || meshName.Contains("lash"))
            {
                score -= 40;
            }

            if (rendererName.Contains("hair") || meshName.Contains("hair"))
            {
                score -= 60;
            }

            if (rendererName.Contains("body") || meshName.Contains("body"))
            {
                score -= 50;
            }

            var mesh = renderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var blendshapeName = mesh.GetBlendShapeName(i).ToLowerInvariant();
                if (blendshapeName.Contains("jaw"))
                {
                    score += 20;
                }

                if (blendshapeName.Contains("mouth") || blendshapeName.Contains("lip"))
                {
                    score += 12;
                }

                if (blendshapeName.Contains("tongue"))
                {
                    score += 8;
                }

                if (blendshapeName.Contains("eye") || blendshapeName.Contains("brow"))
                {
                    score += 4;
                }
            }

            return score;
        }

        private static void RefreshPreferredFaceRendererPath(AICompanionStudioConfig config, GameObject characterInstance)
        {
            if (config == null || characterInstance == null)
            {
                return;
            }

            var resolvedRenderer = ResolveFaceRenderer(config, characterInstance);
            if (resolvedRenderer == null)
            {
                return;
            }

            var relativePath = GetRelativePath(resolvedRenderer.transform, characterInstance.transform);
            if (string.Equals(config.preferredFaceRendererPath, relativePath, StringComparison.Ordinal))
            {
                return;
            }

            config.preferredFaceRendererPath = relativePath;
            EditorUtility.SetDirty(config);
        }

        public static List<SkinnedMeshRenderer> DetectAdditionalFaceRenderers(AICompanionStudioConfig config, GameObject characterInstance)
        {
            if (characterInstance == null)
            {
                return new List<SkinnedMeshRenderer>();
            }

            var primaryRenderer = config != null ? ResolveFaceRenderer(config, characterInstance) : null;
            return CollectAdditionalFaceRenderers(characterInstance, primaryRenderer);
        }

        private static List<SkinnedMeshRenderer> CollectAdditionalFaceRenderers(
            GameObject characterInstance,
            SkinnedMeshRenderer primaryRenderer,
            IEnumerable<string> manualPaths = null,
            bool includeAutoDetected = true)
        {
            var results = new List<SkinnedMeshRenderer>();
            if (characterInstance == null)
            {
                return results;
            }

            var seen = new HashSet<SkinnedMeshRenderer>();
            if (primaryRenderer != null)
            {
                seen.Add(primaryRenderer);
            }

            if (manualPaths != null)
            {
                foreach (var path in manualPaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var manualRenderer = characterInstance.transform.Find(path)?.GetComponent<SkinnedMeshRenderer>();
                    if (manualRenderer != null && seen.Add(manualRenderer))
                    {
                        results.Add(manualRenderer);
                    }
                }
            }

            if (!includeAutoDetected)
            {
                return results;
            }

            var renderers = characterInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var primaryBlendshapes = new HashSet<string>();
            if (primaryRenderer != null && primaryRenderer.sharedMesh != null)
            {
                for (var i = 0; i < primaryRenderer.sharedMesh.blendShapeCount; i++)
                {
                    primaryBlendshapes.Add(primaryRenderer.sharedMesh.GetBlendShapeName(i));
                }
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMesh == null || !seen.Add(renderer))
                {
                    continue;
                }

                if (LooksLikeRelatedFaceRenderer(renderer, primaryBlendshapes))
                {
                    results.Add(renderer);
                }
            }

            return results;
        }

        private static bool LooksLikeRelatedFaceRenderer(SkinnedMeshRenderer renderer, HashSet<string> primaryBlendshapes)
        {
            var transformName = renderer.transform.name.ToLowerInvariant();
            var meshName = (renderer.sharedMesh != null ? renderer.sharedMesh.name : string.Empty).ToLowerInvariant();
            var likelyFaceName =
                transformName.Contains("lash") ||
                transformName.Contains("eye") ||
                transformName.Contains("brow") ||
                transformName.Contains("mouth") ||
                transformName.Contains("teeth") ||
                transformName.Contains("cornea") ||
                transformName.Contains("caruncle") ||
                meshName.Contains("lash") ||
                meshName.Contains("eye") ||
                meshName.Contains("brow") ||
                meshName.Contains("mouth") ||
                meshName.Contains("teeth") ||
                meshName.Contains("cornea") ||
                meshName.Contains("caruncle");

            var hasBlendshapeOverlap = false;
            if (renderer.sharedMesh != null)
            {
                for (var i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    var blendshapeName = renderer.sharedMesh.GetBlendShapeName(i);
                    if (primaryBlendshapes.Contains(blendshapeName))
                    {
                        hasBlendshapeOverlap = true;
                        break;
                    }
                }
            }

            return likelyFaceName || hasBlendshapeOverlap;
        }

        private static string GetRelativePath(Transform current, Transform root)
        {
            if (current == null || root == null || current == root)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var cursor = current;
            while (cursor != null && cursor != root)
            {
                names.Add(cursor.name);
                cursor = cursor.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static void ConfigureRootSystems(AICompanionStudioConfig config, GameObject root, GameObject characterInstance, CharacterProfileData profile)
        {
            if (config == null || root == null)
            {
                return;
            }

            var faceRenderer = ResolveFaceRenderer(config, characterInstance);
            var additionalFaceRenderers = CollectAdditionalFaceRenderers(
                characterInstance,
                faceRenderer,
                config.additionalFaceRendererPaths,
                !config.additionalFaceRenderersInitialized);

            var tts = root.GetComponentInChildren<PiperTtsService>(true);
            var audioSource = tts != null ? GetObjectReference<AudioSource>(tts, "audioSource") : root.GetComponentInChildren<AudioSource>(true);
            var llm = GetOptionalComponentInChildren(root, "LLM");
            var faceDriver = root.GetComponent<ArkItBlendshapeDriver>();
            var signalRouter = root.GetComponent<ExpressionSignalRouter>();
            var expressionLibrary = root.GetComponent<ExpressionLibraryManager>();
            var expressionTriggerPlayer = root.GetComponent<ExpressionTriggerPlayer>() ?? GetOrAddComponent<ExpressionTriggerPlayer>(root);
            var lipSyncController = root.GetComponent<VisemeLipSyncController>();
            var phonemeExtractor = root.GetComponent<PiperTTSPhonemeExtractor>();
            var runtimeOverlay = root.GetComponent<RuntimeConversationOverlay>() ?? GetOrAddComponent<RuntimeConversationOverlay>(root);
            var brain = root.GetComponent<NyxaraCompanionBrain>();
            var memoryController = root.GetComponent<RecentMemoryController>();
            var actionGatekeeper = root.GetComponent<ActionGatekeeper>();
            var actionExecutor = root.GetComponent<CompanionActionExecutor>();
            var agent = GetOptionalComponent(root, "LLMAgent");
            var whisperInput = root.GetComponentInChildren<WhisperMicrophoneInput>(true);
            var whisperManager = GetOptionalComponentInChildren(root, "WhisperManager");
            var lipSyncData = EnsureLipSyncData(config);

            if (llm != null)
            {
                AssignStringField(llm, "model", ResolveModelPathForLlm(config));
                AssignIntField(llm, "_contextSize", config.llmContextSize);
                AssignIntField(llm, "_numThreads", config.llmNumThreads);
                AssignIntField(llm, "_numGPULayers", 0);
            }

            if (agent != null)
            {
                AssignObjectReference(agent, "_llm", llm);
                AssignFloatField(agent, "temperature", config.llmTemperature);
                AssignFloatField(agent, "topP", config.llmTopP);
                AssignIntField(agent, "topK", config.llmTopK);
                AssignFloatField(agent, "minP", config.llmMinP);
                AssignFloatField(agent, "repeatPenalty", config.llmRepeatPenalty);
                AssignIntField(agent, "numPredict", config.llmNumPredict);
                AssignBoolField(agent, "cachePrompt", config.llmCachePrompt);
                AssignStringField(agent, "_systemPrompt", "You are Nyxara, a concise, expressive companion. Respond briefly, naturally, and follow the requested output format exactly.");
            }

            if (faceDriver != null)
            {
                AssignObjectReference(faceDriver, "targetRenderer", faceRenderer);
                AssignObjectReferenceList(faceDriver, "additionalRenderers", additionalFaceRenderers);
                AssignBoolField(faceDriver, "expressionModeActive", false);
            }

            if (signalRouter != null)
            {
                AssignObjectReference(signalRouter, "targetRenderer", faceRenderer);
                AssignBoolField(signalRouter, "expressionModeActive", false);
            }

            if (expressionLibrary != null)
            {
                AssignObjectReference(expressionLibrary, "targetFaceRenderer", faceRenderer);
                AssignObjectReferenceList(expressionLibrary, "additionalFaceRenderers", additionalFaceRenderers);
                AssignStringField(expressionLibrary, "expressionLibraryPath", ResolveExpressionLibraryPath(config, faceRenderer, additionalFaceRenderers));
                AssignBoolField(expressionLibrary, "expressionModeActive", false);
                NyxaraExpressionLibraryBuilder.SyncPresetLibraryList(expressionLibrary);
            }

            if (expressionTriggerPlayer != null)
            {
                AssignObjectReference(expressionTriggerPlayer, "faceDriver", faceDriver);
                AssignObjectReference(expressionTriggerPlayer, "expressionLibrary", expressionLibrary);
                AssignObjectReference(expressionTriggerPlayer, "characterProfile", profile);
            }

            if (lipSyncController != null)
            {
                AssignObjectReference(lipSyncController, "faceRenderer", faceRenderer);
                AssignObjectReferenceList(lipSyncController, "additionalFaceRenderers", additionalFaceRenderers);
                AssignObjectReference(lipSyncController, "lipSyncData", lipSyncData);
                AssignObjectReference(lipSyncController, "phonemeExtractor", phonemeExtractor);
                AssignObjectReference(lipSyncController, "audioSource", audioSource);
                AssignBoolField(lipSyncController, "expressionModeActive", false);
                AssignFloatField(lipSyncController, "mouthOpenAmount", 0.45f);
                AssignFloatField(lipSyncController, "visemeIntensityScale", 0.6f);
                AssignFloatField(lipSyncController, "lowerLipDropAmount", 0.18f);
                AssignFloatField(lipSyncController, "upperLipRaiseAmount", 0.08f);
                AssignFloatField(lipSyncController, "mouthStretchAmount", 0.06f);
                AssignFloatField(lipSyncController, "releaseDuration", 0.08f);
            }

            if (phonemeExtractor != null)
            {
                AssignStringField(phonemeExtractor, "piperExecutablePath", config.piperExecutablePath);
                AssignStringField(phonemeExtractor, "voiceModelPath", config.piperVoicePath);
            }

            if (whisperManager != null)
            {
                AssignStringField(whisperManager, "ModelPath", config.whisperModelRelativePath);
                AssignBoolField(whisperManager, "IsModelPathInStreamingAssets", true);
                EditorUtility.SetDirty(whisperManager);
            }

            if (tts != null)
            {
                tts.TtsEnabled = config.ttsEnabled;
                tts.PiperExecutablePath = config.piperExecutablePath;
                tts.VoiceModelPath = config.piperVoicePath;
                var resolvedModelPath1 = ResolveVoiceModelAssetPath(config.piperVoicePath);
                AssignAssetReference(tts, "voiceModel", resolvedModelPath1);
                AssignAssetReference(tts, "voiceConfig", resolvedModelPath1 + ".json");
                AssignStringField(tts, "espeakDataDirectory", PiperEspeakDataDirectory);
                AssignIntField(tts, "speakerId", 0);
                AssignBoolField(tts, "enableDevelopmentCliFallback", false);
                AssignObjectReference(tts, "faceDriver", faceDriver);
                AssignObjectReference(tts, "lipSyncController", lipSyncController);
                if (audioSource != null)
                {
                    AssignObjectReference(tts, "audioSource", audioSource);
                }
            }

            if (actionGatekeeper != null)
            {
                AssignObjectReference(actionGatekeeper, "actionExecutor", actionExecutor);
            }

            if (memoryController != null)
            {
                AssignIntField(memoryController, "maxMemoryEntries", 3);
            }

            if (actionExecutor != null)
            {
                AssignObjectReference(actionExecutor, "companionTransform", root.transform);
                AssignObjectReference(actionExecutor, "playerTransform", config.playerTransform);
            }

            if (brain != null)
            {
                AssignObjectReference(brain, "agent", agent);
                AssignObjectReference(brain, "ttsService", tts);
                AssignObjectReference(brain, "faceDriver", faceDriver);
                AssignObjectReference(brain, "signalRouter", signalRouter);
                AssignObjectReference(brain, "expressionTriggerPlayer", expressionTriggerPlayer);
                AssignObjectReference(brain, "memoryController", memoryController);
                AssignObjectReference(brain, "actionGatekeeper", actionGatekeeper);
                AssignObjectReference(brain, "characterProfile", profile);
            }

            if (whisperInput != null)
            {
                AssignObjectReference(whisperInput, "whisperManager", whisperManager);
                AssignObjectReference(whisperInput, "companionBrain", brain);
            }

            if (runtimeOverlay != null)
            {
                AssignObjectReference(runtimeOverlay, "whisperInput", whisperInput);
                AssignObjectReference(runtimeOverlay, "companionBrain", brain);
                AssignBoolField(runtimeOverlay, "showOverlay", config.enableRuntimeConversationOverlay && config.showRuntimeConversationOverlay);
                AssignBoolField(runtimeOverlay, "enabled", config.enableRuntimeConversationOverlay);
                AssignBoolField(runtimeOverlay, "enableSmartVoiceCapture", config.enableSmartVoiceCapture);
                AssignEnumField(runtimeOverlay, "micHoldKey", config.runtimeMicHoldKey);
                AssignEnumField(runtimeOverlay, "promptPopupKey", config.runtimePromptPopupKey);
            }

            if (lipSyncData != null)
            {
                AssignFloatField(lipSyncData, "smoothTime", 0.08f);
                AssignFloatField(lipSyncData, "jawOpenMultiplier", 0.6f);
                AssignFloatField(lipSyncData, "responseStart", 0f);
                AssignFloatField(lipSyncData, "responseEnd", 1f);
                AssignFloatField(lipSyncData, "responseFalloff", 1.35f);
                AssignFloatField(lipSyncData, "responseSmoothing", 12f);
            }

            var studioListener = root.GetComponentInChildren<AudioListener>(true);
            if (studioListener != null)
            {
                EnsureSingleAudioListener(studioListener);
            }

            EditorUtility.SetDirty(root);
            PersistPrefabOverrides(root, llm, agent);
        }

        private static LipSyncData EnsureLipSyncData(AICompanionStudioConfig config)
        {
            if (config == null)
            {
                return null;
            }

            var assetPath = $"{config.generatedFolder}/{config.characterName}_LipSyncData.asset";
            var existing = AssetDatabase.LoadAssetAtPath<LipSyncData>(assetPath);
            if (existing != null)
            {
                NormalizeLipSyncMappings(existing);
                return existing;
            }

            var data = ScriptableObject.CreateInstance<LipSyncData>();
            data.visemeMappings = new List<VisemeMapping>
            {
                new() { viseme = Viseme.AA, blendshapeName = "jawOpen", intensity = 27.9f, jawOpenContribution = 1f },
                new() { viseme = Viseme.IY, blendshapeName = "mouthSmileLeft, mouthSmileRight", intensity = 40f, jawOpenContribution = 0.15f },
                new() { viseme = Viseme.UH, blendshapeName = "mouthPucker", intensity = 80f, jawOpenContribution = 0.2f },
                new() { viseme = Viseme.OW, blendshapeName = "mouthFunnel", intensity = 59.4f, jawOpenContribution = 0.3f },
                new() { viseme = Viseme.EH, blendshapeName = "mouthStretchLeft, mouthStretchRight", intensity = 35f, jawOpenContribution = 0.15f },
                new() { viseme = Viseme.FV, blendshapeName = "mouthPressLeft, mouthPressRight", intensity = 30f, jawOpenContribution = 0.05f },
                new() { viseme = Viseme.M, blendshapeName = "mouthClose", intensity = 36.7f, jawOpenContribution = 0f },
                new() { viseme = Viseme.sil, blendshapeName = "mouthClose", intensity = 0f, jawOpenContribution = 0f }
            };
            NormalizeLipSyncMappings(data);

            AssetDatabase.CreateAsset(data, assetPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        public static LipSyncData EnsureLipSyncDataForEditor(AICompanionStudioConfig config)
        {
            return EnsureLipSyncData(config);
        }

        private static void NormalizeLipSyncMappings(LipSyncData data)
        {
            if (data == null || data.visemeMappings == null)
            {
                return;
            }

            foreach (var mapping in data.visemeMappings)
            {
                if (mapping == null)
                {
                    continue;
                }

                mapping.blendshapeName = mapping.viseme switch
                {
                    Viseme.IY when string.Equals(mapping.blendshapeName, "mouthSmileLeft") => "mouthSmileLeft, mouthSmileRight",
                    Viseme.EH when string.Equals(mapping.blendshapeName, "mouthStretchLeft") => "mouthStretchLeft, mouthStretchRight",
                    Viseme.FV when string.Equals(mapping.blendshapeName, "mouthPressLeft") => "mouthPressLeft, mouthPressRight",
                    _ => mapping.blendshapeName
                };

                switch (mapping.viseme)
                {
                    case Viseme.AA:
                        mapping.intensity = 27.9f;
                        break;
                    case Viseme.IY:
                        mapping.intensity = 40f;
                        break;
                    case Viseme.UH:
                        mapping.intensity = 80f;
                        break;
                    case Viseme.OW:
                        mapping.intensity = 59.4f;
                        break;
                    case Viseme.EH:
                        mapping.intensity = 35f;
                        break;
                    case Viseme.M:
                        mapping.intensity = 36.7f;
                        break;
                }
            }

            EditorUtility.SetDirty(data);
        }

        private static GameObject CreateRootFromSource(AICompanionStudioConfig config, out Transform characterRoot, out GameObject characterInstance)
        {
            characterRoot = null;
            characterInstance = null;

            if (config.sourceIsExistingRootPrefab && config.sourceCharacterPrefab != null)
            {
                var instantiatedRoot = PrefabUtility.InstantiatePrefab(config.sourceCharacterPrefab) as GameObject;
                if (instantiatedRoot == null)
                {
                    instantiatedRoot = UnityEngine.Object.Instantiate(config.sourceCharacterPrefab);
                }

                Undo.RegisterCreatedObjectUndo(instantiatedRoot, "Create AI Companion Studio Root");
                instantiatedRoot.name = config.studioRootName;

                characterRoot = FindCharacterRoot(instantiatedRoot.transform);
                characterInstance = characterRoot.gameObject;
                ApplyCharacterTransform(config, characterRoot);
                return instantiatedRoot;
            }

            var root = new GameObject(config.studioRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create AI Companion Studio Root");

            var characterRootObject = CreateUndoGameObject("CharacterRoot", root.transform);
            characterRoot = characterRootObject.transform;

            if (config.sourceCharacterPrefab != null)
            {
                characterInstance = PrefabUtility.InstantiatePrefab(config.sourceCharacterPrefab) as GameObject;
                if (characterInstance == null)
                {
                    characterInstance = UnityEngine.Object.Instantiate(config.sourceCharacterPrefab);
                }

                Undo.RegisterCreatedObjectUndo(characterInstance, "Instantiate Source Character");
                DetachSourceInstanceFromImportedPrefab(characterInstance);
                characterInstance.name = config.sourceCharacterPrefab.name;
                Undo.SetTransformParent(characterInstance.transform, characterRoot, "Parent Source Character");
                ApplyCharacterTransform(config, characterInstance.transform);
            }

            return root;
        }

        private static void DetachSourceInstanceFromImportedPrefab(GameObject instance)
        {
            if (instance == null || !PrefabUtility.IsPartOfAnyPrefab(instance))
            {
                return;
            }

            var assetType = PrefabUtility.GetPrefabAssetType(instance);
            if (assetType != PrefabAssetType.Model && assetType != PrefabAssetType.Regular && assetType != PrefabAssetType.Variant)
            {
                return;
            }

            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        private static Transform FindCharacterRoot(Transform root)
        {
            var namedRoot = root.Find("CharacterRoot");
            if (namedRoot != null)
            {
                return namedRoot;
            }

            return root;
        }

        private static void ApplyCharacterTransform(AICompanionStudioConfig config, Transform target)
        {
            if (target == null)
            {
                return;
            }

            target.localPosition = config.characterLocalPosition;
            target.localEulerAngles = config.characterLocalEuler;
        }

        private static void CreateStudioEnvironment(AICompanionStudioConfig config, GameObject root, Transform characterRoot, SkinnedMeshRenderer faceRenderer)
        {
            var studioRig = CreateUndoGameObject("StudioRig", root.transform);

            var focusTarget = CreateUndoGameObject("FocusTarget", studioRig.transform);
            focusTarget.transform.position = GetFocusPosition(config, faceRenderer, characterRoot);

            if (config.createStudioCamera)
            {
                CreateStudioCamera(config, studioRig.transform, focusTarget.transform);
            }

            if (config.createStudioLights)
            {
                CreateStudioLights(config, studioRig.transform, focusTarget.transform);
            }
        }

        private static Vector3 GetFocusPosition(AICompanionStudioConfig config, SkinnedMeshRenderer faceRenderer, Transform characterRoot)
        {
            if (faceRenderer != null)
            {
                var bounds = faceRenderer.bounds;
                return bounds.center + Vector3.up * 0.02f + config.cameraPivotOffset;
            }

            return characterRoot.position + Vector3.up * config.focusHeightOffset + config.cameraPivotOffset;
        }

        private static void CreateStudioCamera(AICompanionStudioConfig config, Transform studioRig, Transform focusTarget)
        {
            var cameraObject = CreateUndoGameObject("StudioCamera", studioRig);

            var direction = Quaternion.Euler(0f, config.cameraYaw, 0f) * Vector3.back;
            cameraObject.transform.position = focusTarget.position + (direction * config.cameraDistance) + (Vector3.up * (config.cameraHeight - focusTarget.position.y));
            cameraObject.transform.LookAt(focusTarget.position);

            var camera = cameraObject.GetComponent<Camera>();
            if (camera == null)
            {
                camera = Undo.AddComponent<Camera>(cameraObject);
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = config.studioBackgroundColor;
            camera.fieldOfView = config.cameraFieldOfView;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.depthTextureMode = DepthTextureMode.Depth;

            var listener = cameraObject.GetComponent<AudioListener>();
            if (listener == null)
            {
                listener = Undo.AddComponent<AudioListener>(cameraObject);
            }

            listener.enabled = true;
            EnsureSingleAudioListener(listener);

            ConfigureOptionalUniversalCamera(cameraObject);
        }

        private static void EnsureSingleAudioListener(AudioListener preferredListener)
        {
            if (preferredListener == null)
            {
                return;
            }

            preferredListener.enabled = true;
            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var listener in listeners)
            {
                if (listener == null || listener == preferredListener)
                {
                    continue;
                }

                if (!listener.enabled)
                {
                    continue;
                }

                Undo.RecordObject(listener, "Disable Extra Audio Listener");
                listener.enabled = false;
                EditorUtility.SetDirty(listener);
            }

            EditorUtility.SetDirty(preferredListener);
        }

        private static void CreateStudioLights(AICompanionStudioConfig config, Transform studioRig, Transform focusTarget)
        {
            CreatePortraitLight("KeyLight", studioRig, focusTarget.position, new Vector3(-32f, 28f, 0f), config.keyLightIntensity, new Color(1f, 0.96f, 0.9f));
            CreatePortraitLight("FillLight", studioRig, focusTarget.position, new Vector3(24f, -18f, 0f), config.fillLightIntensity, new Color(0.78f, 0.86f, 1f));
            CreatePortraitLight("RimLight", studioRig, focusTarget.position, new Vector3(145f, 18f, 0f), config.rimLightIntensity, new Color(1f, 0.98f, 0.95f));
        }

        private static void ConfigureOptionalUniversalCamera(GameObject cameraObject)
        {
            var additionalCameraType = ResolveTypeByName("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (additionalCameraType == null)
            {
                Debug.LogWarning(MissingUrpMessage, cameraObject);
                return;
            }

            var additionalCameraData = cameraObject.GetComponent(additionalCameraType) ?? Undo.AddComponent(cameraObject, additionalCameraType);
            if (additionalCameraData == null)
            {
                return;
            }

            AssignBoolField(additionalCameraData, "renderPostProcessing", true);
            AssignEnumByName(additionalCameraData, "antialiasing", "SubpixelMorphologicalAntiAliasing");
            AssignEnumByName(additionalCameraData, "antialiasingQuality", "High");
            AssignBoolField(additionalCameraData, "stopNaN", true);
            AssignBoolField(additionalCameraData, "dithering", true);
        }

        private static void ConfigureOptionalUniversalLight(GameObject lightObject)
        {
            var additionalLightType = ResolveTypeByName("UnityEngine.Rendering.Universal.UniversalAdditionalLightData");
            if (additionalLightType == null)
            {
                return;
            }

            _ = lightObject.GetComponent(additionalLightType) ?? Undo.AddComponent(lightObject, additionalLightType);
        }

        private static void CreatePortraitLight(string name, Transform parent, Vector3 focusPosition, Vector3 eulerAngles, float intensity, Color color)
        {
            var lightObject = CreateUndoGameObject(name, parent);
            lightObject.transform.position = focusPosition + (Quaternion.Euler(eulerAngles) * new Vector3(0f, 0f, -2.5f));
            lightObject.transform.LookAt(focusPosition);

            ConfigureOptionalUniversalLight(lightObject);

            var light = lightObject.GetComponent<Light>();
            if (light == null)
            {
                light = Undo.AddComponent<Light>(lightObject);
            }

            if (light == null)
            {
                return;
            }

            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = 8f;
            light.spotAngle = 52f;
            light.innerSpotAngle = 35f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.92f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.2f;
        }

        private static string ResolveAbsoluteOrProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            var streamingAssetsPath = Path.Combine(
                Application.streamingAssetsPath,
                path.Replace("Assets/StreamingAssets/", string.Empty)
                    .Replace("StreamingAssets/", string.Empty)
                    .Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(streamingAssetsPath))
            {
                return streamingAssetsPath;
            }

            return Path.Combine(Application.dataPath, path.Replace("Assets/", string.Empty).Replace('/', Path.DirectorySeparatorChar));
        }

        private static GameObject CreateUndoGameObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            if (parent != null)
            {
                Undo.SetTransformParent(go.transform, parent, $"Parent {name}");
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            return go;
        }

        private static GameObject GetOrCreateChild(Transform parent, string childName)
        {
            var existing = parent.Find(childName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            return CreateUndoGameObject(childName, parent);
        }

        private static Component GetOptionalComponent(GameObject target, string typeName)
        {
            return target.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().Name == typeName);
        }

        private static Component GetOptionalComponentInChildren(GameObject target, string typeName)
        {
            return target.GetComponentsInChildren<Component>(true).FirstOrDefault(component => component != null && component.GetType().Name == typeName);
        }

        private static Component GetOrAddOptionalComponent(GameObject target, string typeName, OptionalIntegration integration)
        {
            var existing = GetOptionalComponent(target, typeName);
            if (existing != null)
            {
                return existing;
            }

            if (!IsIntegrationEnabled(integration))
            {
                if (ShouldDeferIntegrationBinding(integration))
                {
                    QueuePendingIntegrationRepair(integration);
                    LogPendingIntegration(integration, target);
                    return null;
                }

                LogMissingIntegration(integration, target);
                return null;
            }

            var resolvedType = ResolveTypeByName(typeName);
            if (resolvedType == null || !typeof(Component).IsAssignableFrom(resolvedType))
            {
                if (ShouldDeferIntegrationBinding(integration))
                {
                    QueuePendingIntegrationRepair(integration);
                    LogPendingIntegration(integration, target);
                    return null;
                }

                LogMissingIntegration(integration, target);
                return null;
            }

            return Undo.AddComponent(target, resolvedType);
        }

        internal static void QueuePendingIntegrationRepair(AICompanionStudioConfig config, bool needsLlmRepair, bool needsWhisperRepair)
        {
            if (config == null)
            {
                return;
            }

            var configPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrWhiteSpace(configPath))
            {
                return;
            }

            QueuePendingIntegrationRepair(configPath, needsLlmRepair, needsWhisperRepair);
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            if (existing != null)
            {
                return existing;
            }

            return Undo.AddComponent<T>(target);
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
                .FirstOrDefault(type =>
                    type != null &&
                    MatchesTypeName(type, typeName));
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

        private static bool IsIntegrationEnabled(OptionalIntegration integration)
        {
            switch (integration)
            {
                case OptionalIntegration.LlmUnity:
                    if (ResolveTypeByName("LLM") != null && ResolveTypeByName("LLMAgent") != null)
                    {
                        return true;
                    }
#if NYXARA_LLMUNITY
                    return true;
#else
                    return false;
#endif
                case OptionalIntegration.Whisper:
                    if (ResolveTypeByName("WhisperManager") != null)
                    {
                        return true;
                    }
#if NYXARA_WHISPER
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        private static bool IsIntegrationPackageDetected(OptionalIntegration integration)
        {
            var packageName = integration switch
            {
                OptionalIntegration.LlmUnity => LlmUnityPackageName,
                OptionalIntegration.Whisper => WhisperPackageName,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(packageName))
            {
                return false;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return false;
            }

            var packagesFolder = Path.Combine(projectRoot, "Packages", packageName);
            if (Directory.Exists(packagesFolder))
            {
                return true;
            }

            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifestText = File.ReadAllText(manifestPath);
                    if (manifestText.IndexOf($"\"{packageName}\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                }
            }

            var packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
            if (!Directory.Exists(packageCacheRoot))
            {
                return false;
            }

            var packageCachePrefix = packageName + "@";
            return Directory.GetDirectories(packageCacheRoot, packageCachePrefix + "*", SearchOption.TopDirectoryOnly).Length > 0;
        }

        private static bool ShouldDeferIntegrationBinding(OptionalIntegration integration)
        {
            if (IsIntegrationPackageDetected(integration))
            {
                return true;
            }

            return integration switch
            {
                OptionalIntegration.LlmUnity => HasScriptingDefine("NYXARA_LLMUNITY"),
                OptionalIntegration.Whisper => HasScriptingDefine("NYXARA_WHISPER"),
                _ => false
            };
        }

        private static void QueuePendingIntegrationRepair(OptionalIntegration integration)
        {
            if (string.IsNullOrWhiteSpace(_activeBuildConfigPath))
            {
                return;
            }

            QueuePendingIntegrationRepair(
                _activeBuildConfigPath,
                integration == OptionalIntegration.LlmUnity,
                integration == OptionalIntegration.Whisper);
        }

        private static void QueuePendingIntegrationRepair(string configPath, bool needsLlmRepair, bool needsWhisperRepair)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                return;
            }

            SessionState.SetString(PendingRepairConfigPathKey, configPath);
            if (needsLlmRepair)
            {
                SessionState.SetBool(PendingRepairLlmKey, true);
            }

            if (needsWhisperRepair)
            {
                SessionState.SetBool(PendingRepairWhisperKey, true);
            }

            EnsurePendingRepairUpdateHook();
        }

        [InitializeOnLoadMethod]
        private static void EnsurePendingRepairUpdateHook()
        {
            if (_pendingRepairUpdateHookInstalled)
            {
                return;
            }

            EditorApplication.update += ProcessPendingIntegrationRepair;
            _pendingRepairUpdateHookInstalled = true;
        }

        private static void ProcessPendingIntegrationRepair()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            var configPath = SessionState.GetString(PendingRepairConfigPathKey, string.Empty);
            if (string.IsNullOrWhiteSpace(configPath))
            {
                return;
            }

            var wantsLlmRepair = SessionState.GetBool(PendingRepairLlmKey, false);
            var wantsWhisperRepair = SessionState.GetBool(PendingRepairWhisperKey, false);
            if (!wantsLlmRepair && !wantsWhisperRepair)
            {
                ClearPendingIntegrationRepair();
                return;
            }

            if ((wantsLlmRepair && !ResolveIntegrationReady(OptionalIntegration.LlmUnity)) ||
                (wantsWhisperRepair && !ResolveIntegrationReady(OptionalIntegration.Whisper)))
            {
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<AICompanionStudioConfig>(configPath);
            if (config == null)
            {
                ClearPendingIntegrationRepair();
                return;
            }

            var report = NyxaraIntegrationValidator.ValidateAndBind(config);
            if (report.HasChanges)
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            ClearPendingIntegrationRepair();
        }

        private static bool ResolveIntegrationReady(OptionalIntegration integration)
        {
            return integration switch
            {
                OptionalIntegration.LlmUnity => ResolveTypeByName("LLM") != null && ResolveTypeByName("LLMAgent") != null,
                OptionalIntegration.Whisper => ResolveTypeByName("WhisperManager") != null,
                _ => false
            };
        }

        private static void ClearPendingIntegrationRepair()
        {
            SessionState.EraseString(PendingRepairConfigPathKey);
            SessionState.SetBool(PendingRepairLlmKey, false);
            SessionState.SetBool(PendingRepairWhisperKey, false);
        }

        private static void LogMissingIntegration(OptionalIntegration integration, UnityEngine.Object context)
        {
            switch (integration)
            {
                case OptionalIntegration.LlmUnity:
                    Debug.LogWarning(MissingLlmMessage, context);
                    break;
                case OptionalIntegration.Whisper:
                    Debug.LogWarning(MissingWhisperMessage, context);
                    break;
            }
        }

        private static void LogPendingIntegration(OptionalIntegration integration, UnityEngine.Object context)
        {
            switch (integration)
            {
                case OptionalIntegration.LlmUnity:
                    Debug.LogWarning(PendingLlmMessage, context);
                    break;
                case OptionalIntegration.Whisper:
                    Debug.LogWarning(PendingWhisperMessage, context);
                    break;
            }
        }

        private static bool HasScriptingDefine(string define)
        {
            if (string.IsNullOrWhiteSpace(define))
            {
                return false;
            }

            var defines = GetScriptingDefines();
            if (string.IsNullOrWhiteSpace(defines))
            {
                return false;
            }

            return defines
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
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

        private static string ResolveModelPathForLlm(AICompanionStudioConfig config)
        {
            var configuredPath = ResolveAbsoluteOrProjectPath(config.llmModelPath);
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                var configuredFileName = Path.GetFileName(configuredPath);
                if (TryGetStreamingAssetsModelRelativePath(configuredFileName, out var copiedRelativePath))
                {
                    return copiedRelativePath;
                }

                return configuredPath;
            }

            var configuredNameOnly = Path.GetFileName(config.llmModelPath ?? string.Empty);
            if (TryGetStreamingAssetsModelRelativePath(configuredNameOnly, out var configuredRelativePath))
            {
                return configuredRelativePath;
            }

            var streamingAssetsModel = Path.Combine(Application.streamingAssetsPath, "Models", CompanionStackDefaults.QwenModelFileName);
            if (File.Exists(streamingAssetsModel))
            {
                return Path.Combine("Models", CompanionStackDefaults.QwenModelFileName).Replace('\\', '/');
            }

            if (TryFindFirstStreamingAssetsModelRelativePath(out var discoveredRelativePath))
            {
                if (config != null)
                {
                    config.llmModelPath = discoveredRelativePath;
                }

                return discoveredRelativePath;
            }

            return configuredPath;
        }

        private static bool TryGetStreamingAssetsModelRelativePath(string modelFileName, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrWhiteSpace(modelFileName))
            {
                return false;
            }

            var modelPath = Path.Combine(Application.streamingAssetsPath, "Models", modelFileName);
            if (!File.Exists(modelPath))
            {
                return false;
            }

            relativePath = Path.Combine("Models", modelFileName).Replace('\\', '/');
            return true;
        }

        private static bool TryFindFirstStreamingAssetsModelRelativePath(out string relativePath)
        {
            relativePath = null;

            var modelsRoot = Path.Combine(Application.streamingAssetsPath, "Models");
            if (!Directory.Exists(modelsRoot))
            {
                return false;
            }

            var modelPath = Directory.GetFiles(modelsRoot, "*.gguf", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return false;
            }

            var relativeModelPath = modelPath.Substring(modelsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            relativePath = Path.Combine("Models", relativeModelPath).Replace('\\', '/');
            return true;
        }

        public static string ResolveExpressionLibraryPath(
            AICompanionStudioConfig config,
            SkinnedMeshRenderer faceRenderer,
            IEnumerable<SkinnedMeshRenderer> additionalFaceRenderers)
        {
            var baseFolder = string.IsNullOrWhiteSpace(config?.expressionFolder)
                ? $"{DefaultGeneratedFolder}/Expressions"
                : config.expressionFolder;
            var profile = DetectPrimaryFaceProfile(faceRenderer, additionalFaceRenderers);
            var characterSegment = SanitizePathSegment(config?.characterName, "Character");
            var profileSegment = SanitizePathSegment(profile, "Custom_Unknown");
            return $"{baseFolder}/{characterSegment}/{profileSegment}";
        }

        public static string DetectPrimaryFaceProfile(
            SkinnedMeshRenderer faceRenderer,
            IEnumerable<SkinnedMeshRenderer> additionalFaceRenderers)
        {
            var renderers = new List<SkinnedMeshRenderer>();
            if (faceRenderer != null)
            {
                renderers.Add(faceRenderer);
            }

            if (additionalFaceRenderers != null)
            {
                renderers.AddRange(additionalFaceRenderers.Where(renderer => renderer != null && !renderers.Contains(renderer)));
            }

            var blendshapeNames = ExpressionBuilderHelper.GetBlendshapeNames(renderers);
            var profiles = ExpressionBuilderHelper.DetectCompatibilityProfiles(blendshapeNames);
            return profiles.FirstOrDefault(profile => !string.Equals(profile, "Custom/Unknown", StringComparison.OrdinalIgnoreCase))
                   ?? profiles.FirstOrDefault()
                   ?? "Custom/Unknown";
        }

        private static string SanitizePathSegment(string value, string fallback)
        {
            var chosen = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(chosen.Select(ch => invalidChars.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray());
        }

        private static void RemoveExistingStudioRoot(string rootName)
        {
            if (string.IsNullOrWhiteSpace(rootName))
            {
                return;
            }

            var existing = GameObject.Find(rootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            var combined = $"{parent}/{child}";
            if (AssetDatabase.IsValidFolder(combined))
            {
                return;
            }

            AssetDatabase.CreateFolder(parent, child);
        }

        private static void EnsureAssetFolderPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var normalizedPath = assetPath.Replace('\\', '/').Trim('/');
            var segments = normalizedPath.Split('/');
            if (segments.Length == 0 || !string.Equals(segments[0], "Assets", StringComparison.Ordinal))
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

        private static string ResolveVoiceModelAssetPath(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var normalized = configuredPath.Replace('\\', '/').Trim();
                if (!normalized.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                {
                    normalized = "Assets/" + normalized.TrimStart('/');
                }

                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(normalized) != null)
                {
                    return normalized;
                }
            }

            return InProcessPiperModelAssetPath;
        }

        private static void AssignAssetReference(UnityEngine.Object target, string fieldName, string assetPath)
        {
            if (target == null || string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null)
            {
                AssignObjectReference(target, fieldName, asset);
            }
        }

        private static void AssignObjectReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T GetObjectReference<T>(UnityEngine.Object source, string fieldName) where T : UnityEngine.Object
        {
            if (source == null)
            {
                return null;
            }

            var serializedObject = new SerializedObject(source);
            var property = serializedObject.FindProperty(fieldName);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static void AssignObjectReferenceList(UnityEngine.Object target, string fieldName, IReadOnlyList<SkinnedMeshRenderer> values)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null || !property.isArray)
            {
                return;
            }

            property.arraySize = values?.Count ?? 0;
            if (values != null)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignStringField(UnityEngine.Object target, string fieldName, string value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.stringValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignFloatField(UnityEngine.Object target, string fieldName, float value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.floatValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignIntField(UnityEngine.Object target, string fieldName, int value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.intValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignBoolField(UnityEngine.Object target, string fieldName, bool value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignEnumByName(UnityEngine.Object target, string fieldName, string enumName)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            var names = property.enumNames;
            if (names == null || names.Length == 0)
            {
                return;
            }

            var index = Array.IndexOf(names, enumName);
            if (index < 0)
            {
                index = Array.FindIndex(names, existing => string.Equals(existing, enumName, StringComparison.OrdinalIgnoreCase));
            }

            if (index < 0)
            {
                return;
            }

            property.enumValueIndex = index;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PersistPrefabOverrides(GameObject root, params UnityEngine.Object[] objectsToApply)
        {
            if (root == null)
            {
                return;
            }

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return;
            }

            foreach (var target in objectsToApply)
            {
                if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target))
                {
                    continue;
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                PrefabUtility.ApplyObjectOverride(target, prefabPath, InteractionMode.AutomatedAction);
            }

            AssetDatabase.SaveAssets();
        }

        private static void AssignEnumField(UnityEngine.Object target, string fieldName, Enum value)
        {
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.intValue = Convert.ToInt32(value);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
