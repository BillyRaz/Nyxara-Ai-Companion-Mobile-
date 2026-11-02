// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using UnityEditor;
#endif
#if NYXARA_LLMUNITY
using LLMUnity;
#endif
using Nyxara.AICompanion.Data;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.Runtime;
using Nyxara.AICompanion.Speech;
using UnityEngine;

namespace Nyxara.AICompanion.Core
{
    public class CompanionBootstrap : MonoBehaviour
    {
        private const string MissingLlmMessage = "Nyxara AI Studio: LLMUnity not installed. AI features disabled.";

        [ContextMenu("Setup Complete Companion")]
        public void SetupCompleteCompanion()
        {
            var brain = GetComponent<NyxaraCompanionBrain>() ?? gameObject.AddComponent<NyxaraCompanionBrain>();
            var tts = GetComponent<PiperTtsService>() ?? GetComponentInChildren<PiperTtsService>(true) ?? gameObject.AddComponent<PiperTtsService>();
            var audioSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>(true) ?? gameObject.AddComponent<AudioSource>();
            var faceDriver = GetComponent<ArkItBlendshapeDriver>() ?? GetComponentInChildren<ArkItBlendshapeDriver>(true) ?? gameObject.AddComponent<ArkItBlendshapeDriver>();
            var signalRouter = GetComponent<ExpressionSignalRouter>() ?? GetComponentInChildren<ExpressionSignalRouter>(true) ?? gameObject.AddComponent<ExpressionSignalRouter>();
            var memory = GetComponent<RecentMemoryController>() ?? GetComponentInChildren<RecentMemoryController>(true) ?? gameObject.AddComponent<RecentMemoryController>();
            var gatekeeper = GetComponent<ActionGatekeeper>() ?? GetComponentInChildren<ActionGatekeeper>(true) ?? gameObject.AddComponent<ActionGatekeeper>();
            var executor = GetComponent<CompanionActionExecutor>() ?? GetComponentInChildren<CompanionActionExecutor>(true) ?? gameObject.AddComponent<CompanionActionExecutor>();

#if NYXARA_LLMUNITY
            var agent = GetComponent<LLMAgent>() ?? GetComponentInChildren<LLMAgent>(true) ?? gameObject.AddComponent<LLMAgent>();
            AssignObjectReference(brain, "agent", agent);
#else
            Debug.LogWarning(MissingLlmMessage, this);
#endif

            AssignObjectReference(brain, "ttsService", tts);
            AssignObjectReference(brain, "faceDriver", faceDriver);
            AssignObjectReference(brain, "signalRouter", signalRouter);
            AssignObjectReference(brain, "memoryController", memory);
            AssignObjectReference(brain, "actionGatekeeper", gatekeeper);

            AssignObjectReference(tts, "audioSource", audioSource);
            AssignObjectReference(tts, "faceDriver", faceDriver);
            AssignObjectReference(gatekeeper, "actionExecutor", executor);

            var profiles = Resources.LoadAll<CharacterProfileData>("");
            if (profiles.Length > 0)
            {
                AssignObjectReference(brain, "characterProfile", profiles[0]);
            }

            Debug.Log("Companion setup complete!");
        }

        private static void AssignObjectReference(Object target, string fieldName, Object value)
        {
#if UNITY_EDITOR
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                return;
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

#if UNITY_EDITOR
        [MenuItem("Nyxara AI/Legacy/Setup Full Companion", false, 200)]
        public static void CreateFullCompanion()
        {
            var go = new GameObject("NyxaraCompanion");
            var bootstrap = go.AddComponent<CompanionBootstrap>();
            bootstrap.SetupCompleteCompanion();
            Selection.activeGameObject = go;
            Debug.Log("Created full companion setup");
        }
#endif
    }
}
