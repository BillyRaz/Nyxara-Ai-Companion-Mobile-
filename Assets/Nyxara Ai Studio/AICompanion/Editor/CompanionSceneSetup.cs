// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using Nyxara.AICompanion.Configuration;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.Speech;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    public static class CompanionSceneSetup
    {
        private const string MissingLlmMessage = "Nyxara AI Studio: LLMUnity not installed. AI features disabled.";
        private const string MissingWhisperMessage = "Nyxara AI Studio: Whisper not installed. Speech-to-text features disabled.";

        [MenuItem("Nyxara AI/Legacy/Create Bootstrap Objects", false, 201)]
        public static void CreateBootstrapObjects()
        {
            var root = FindOrCreate("Nyxara Companion");

            var llmObject = FindOrCreate("Local LLM", root.transform);
            var agentObject = FindOrCreate("Companion Agent", root.transform);
            var whisperObject = FindOrCreate("Speech To Text", root.transform);

            var llm = GetOrAddOptionalComponent(llmObject, "LLM", IntegrationType.LlmUnity);
            if (llm != null)
            {
                AssignStringProperty(llm, "model", ResolvePreferredModelPath());
                AssignStringProperty(llm, "_model", ResolvePreferredModelPath());
                AssignIntProperty(llm, "contextSize", 8192);
                AssignIntProperty(llm, "_contextSize", 8192);
                AssignIntProperty(llm, "numThreads", -1);
                AssignIntProperty(llm, "_numThreads", -1);
                AssignIntProperty(llm, "numGPULayers", 0);
                AssignIntProperty(llm, "_numGPULayers", 0);
            }

            var agent = GetOrAddOptionalComponent(agentObject, "LLMAgent", IntegrationType.LlmUnity);
            if (agent != null)
            {
                AssignObjectReference(agent, "llm", llm);
                AssignObjectReference(agent, "_llm", llm);
                AssignStringProperty(agent, "systemPrompt", CompanionStackDefaults.DefaultSystemPrompt);
                AssignStringProperty(agent, "_systemPrompt", CompanionStackDefaults.DefaultSystemPrompt);
                AssignFloatProperty(agent, "temperature", 0.7f);
                AssignFloatProperty(agent, "topP", 0.9f);
            }

            var ttsObject = FindOrCreate("Speech Synthesis", root.transform);
            var audioSource = ttsObject.GetComponent<AudioSource>() ?? ttsObject.AddComponent<AudioSource>();
            var tts = ttsObject.GetComponent<PiperTtsService>() ?? ttsObject.AddComponent<PiperTtsService>();
            tts.PiperExecutablePath = ResolvePreferredPiperExecutablePath();
            tts.VoiceModelPath = ResolvePreferredPiperVoicePath();
            NyxaraIntegrationValidator.BindInProcessPiperAssets(tts);

            var faceObject = FindOrCreate("Face Driver", root.transform);
            var faceDriver = faceObject.GetComponent<ArkItBlendshapeDriver>() ?? faceObject.AddComponent<ArkItBlendshapeDriver>();

            var whisper = GetOrAddOptionalComponent(whisperObject, "WhisperManager", IntegrationType.Whisper);
            if (whisper != null)
            {
                AssignStringProperty(whisper, "ModelPath", CompanionStackDefaults.WhisperModelRelativePath);
                AssignBoolProperty(whisper, "IsModelPathInStreamingAssets", true);
            }

            var micInput = whisperObject.GetComponent<WhisperMicrophoneInput>() ?? whisperObject.AddComponent<WhisperMicrophoneInput>();
            var brain = agentObject.GetComponent<NyxaraCompanionBrain>() ?? agentObject.AddComponent<NyxaraCompanionBrain>();

            AssignObjectReference(tts, "audioSource", audioSource);
            AssignObjectReference(tts, "faceDriver", faceDriver);
            AssignObjectReference(brain, "agent", agent);
            AssignObjectReference(brain, "ttsService", tts);
            AssignObjectReference(brain, "faceDriver", faceDriver);
            AssignObjectReference(micInput, "whisperManager", whisper);
            AssignObjectReference(micInput, "companionBrain", brain);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private static GameObject FindOrCreate(string name, Transform parent = null)
        {
            GameObject existing = null;
            if (parent == null)
            {
                existing = GameObject.Find(name);
            }
            else
            {
                var child = parent.Find(name);
                existing = child != null ? child.gameObject : null;
            }

            if (existing != null)
            {
                return existing;
            }

            var created = new GameObject(name);
            if (parent != null)
            {
                created.transform.SetParent(parent);
            }

            return created;
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

        private static void AssignStringProperty(Component component, string fieldName, string value)
        {
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.stringValue = value ?? string.Empty;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignIntProperty(Component component, string fieldName, int value)
        {
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.intValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignFloatProperty(Component component, string fieldName, float value)
        {
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.floatValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignBoolProperty(Component component, string fieldName, bool value)
        {
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string ResolvePreferredModelPath()
        {
            var localModelPath = Path.Combine(Application.dataPath, "StreamingAssets", "Models", CompanionStackDefaults.QwenModelFileName);
            if (File.Exists(localModelPath))
            {
                return localModelPath;
            }

            return CompanionStackDefaults.QwenModelPath;
        }

        private static string ResolvePreferredPiperExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(CompanionStackDefaults.PiperExecutablePath) &&
                File.Exists(CompanionStackDefaults.PiperExecutablePath))
            {
                return CompanionStackDefaults.PiperExecutablePath;
            }

            return string.Empty;
        }

        private static string ResolvePreferredPiperVoicePath()
        {
            var localVoicePath = Path.Combine(Application.dataPath, "StreamingAssets", "Speech", "PiperVoices", CompanionStackDefaults.PiperVoiceFileName);
            if (File.Exists(localVoicePath))
            {
                return CompanionStackDefaults.PiperVoiceRelativePath;
            }

            return string.Empty;
        }

        private static Component GetOrAddOptionalComponent(GameObject target, string typeName, IntegrationType integrationType)
        {
            if (!IsIntegrationEnabled(integrationType))
            {
                LogMissingIntegration(integrationType, target);
                return null;
            }

            var component = target.GetComponents<Component>().FirstOrDefault(existing => existing != null && existing.GetType().Name == typeName);
            if (component != null)
            {
                return component;
            }

            var resolvedType = ResolveTypeByName(typeName);
            if (resolvedType == null || !typeof(Component).IsAssignableFrom(resolvedType))
            {
                LogMissingIntegration(integrationType, target);
                return null;
            }

            return Undo.AddComponent(target, resolvedType);
        }

        private static Type ResolveTypeByName(string typeName)
        {
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
                .FirstOrDefault(type => type != null && type.Name == typeName);
        }

        private static bool IsIntegrationEnabled(IntegrationType integrationType)
        {
            switch (integrationType)
            {
                case IntegrationType.LlmUnity:
#if NYXARA_LLMUNITY
                    return true;
#else
                    return false;
#endif
                case IntegrationType.Whisper:
#if NYXARA_WHISPER
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        private static void LogMissingIntegration(IntegrationType integrationType, UnityEngine.Object context)
        {
            switch (integrationType)
            {
                case IntegrationType.LlmUnity:
                    Debug.LogWarning(MissingLlmMessage, context);
                    break;
                case IntegrationType.Whisper:
                    Debug.LogWarning(MissingWhisperMessage, context);
                    break;
            }
        }

        private enum IntegrationType
        {
            LlmUnity,
            Whisper
        }
    }
}
#endif
