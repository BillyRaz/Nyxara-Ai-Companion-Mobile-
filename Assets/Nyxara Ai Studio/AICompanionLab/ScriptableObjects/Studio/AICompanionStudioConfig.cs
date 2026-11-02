// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using Nyxara.AICompanion.Configuration;
using Nyxara.AICompanion.Data;
using UnityEngine;

namespace Nyxara.AICompanion.Studio
{
    [CreateAssetMenu(fileName = "NyxaraAIStudioConfig", menuName = "Nyxara AI/Studio Config")]
    public class AICompanionStudioConfig : ScriptableObject
    {
        private const string PackageRootFolder = "Assets/Nyxara AI Studio";
        private const string GeneratedRootFolder = PackageRootFolder + "/Generated";

        [Header("Identity")]
        public string characterName = "Nyxara";
        public string studioRootName = "NyxaraStudioRoot";

        [Header("Source")]
        public GameObject sourceCharacterPrefab;
        public bool sourceIsExistingRootPrefab = false;
        public string preferredFaceRendererPath = string.Empty;
        public Transform playerTransform;

        [Header("Data")]
        public CharacterProfileData characterProfile;

        [Header("AI Paths")]
        public string llmModelPath = "";
        public string whisperModelRelativePath = CompanionStackDefaults.WhisperModelRelativePath;
        public bool ttsEnabled = false;
        [HideInInspector] public bool hasMigratedLegacyTtsConfiguration = false;
        public string piperExecutablePath = CompanionStackDefaults.PiperExecutablePath;
        public string piperVoicePath = string.Empty;

        [Header("Build Options")]
        public bool createSceneInstance = true;
        public bool saveRootPrefab = true;
        public bool createProfileIfMissing = true;
        public bool autoAttachBootstrap = true;
        public bool createStudioEnvironment = true;
        public bool createStudioCamera = true;
        public bool createStudioLights = true;

        [Header("Runtime Input")]
        public bool enableRuntimeConversationOverlay = true;
        public bool showRuntimeConversationOverlay = true;
        public bool enableSmartVoiceCapture = false;
        public KeyCode runtimeMicHoldKey = KeyCode.V;
        public KeyCode runtimePromptPopupKey = KeyCode.T;

        [Header("LLM Runtime")]
        public int llmContextSize = 4096;
        public int llmNumThreads = -1;
        public int llmNumPredict = 96;
        public bool llmCachePrompt = true;
        public float llmTemperature = 0.2f;
        public float llmTopP = 0.85f;
        public int llmTopK = 30;
        public float llmMinP = 0.08f;
        public float llmRepeatPenalty = 1.05f;

        [Header("Studio Framing")]
        public Vector3 characterLocalPosition = Vector3.zero;
        public Vector3 characterLocalEuler = Vector3.zero;
        public float focusHeightOffset = 1.55f;
        public Vector3 cameraPivotOffset = Vector3.zero;
        public float cameraDistance = 1.35f;
        public float cameraHeight = 1.6f;
        public float cameraYaw = 0f;
        public float cameraFieldOfView = 26f;

        [Header("Studio Lighting")]
        public float keyLightIntensity = 2.2f;
        public float fillLightIntensity = 0.9f;
        public float rimLightIntensity = 1.35f;
        public Color studioBackgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);

        [Header("Output Paths")]
        public string rootFolder = PackageRootFolder;
        public string prefabFolder = GeneratedRootFolder + "/Prefabs";
        public string companionPrefabFolder = GeneratedRootFolder + "/Companions";
        public string profileFolder = GeneratedRootFolder + "/Profiles";
        public string generatedFolder = GeneratedRootFolder;
        public string expressionFolder = GeneratedRootFolder + "/Expressions";

        private void OnEnable()
        {
            UpgradeLegacyTtsConfigurationIfNeeded();
            UpgradeLegacyOutputPathsIfNeeded();
        }

        private void OnValidate()
        {
            UpgradeLegacyTtsConfigurationIfNeeded();
            UpgradeLegacyOutputPathsIfNeeded();
        }

        private void UpgradeLegacyTtsConfigurationIfNeeded()
        {
            if (hasMigratedLegacyTtsConfiguration)
            {
                return;
            }

            if (!ttsEnabled &&
                !string.IsNullOrWhiteSpace(piperExecutablePath) &&
                !string.IsNullOrWhiteSpace(piperVoicePath))
            {
                ttsEnabled = true;
            }

            hasMigratedLegacyTtsConfiguration = true;
        }

        private void UpgradeLegacyOutputPathsIfNeeded()
        {
            rootFolder = NormalizeRootFolder(rootFolder);
            prefabFolder = NormalizeOutputFolder(prefabFolder, "Prefabs");
            companionPrefabFolder = NormalizeOutputFolder(companionPrefabFolder, "Companions");
            profileFolder = NormalizeOutputFolder(profileFolder, "Profiles");
            generatedFolder = NormalizeGeneratedFolder(generatedFolder);
            expressionFolder = NormalizeOutputFolder(expressionFolder, "Expressions");
        }

        private static string NormalizeRootFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                IsLegacyRootPath(path) ||
                string.Equals(path, GeneratedRootFolder, System.StringComparison.Ordinal) ||
                !IsInsidePackageRoot(path))
            {
                return PackageRootFolder;
            }

            return path;
        }

        private static string NormalizeGeneratedFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || IsLegacySubfolderPath(path, "Generated") || !IsInsidePackageRoot(path))
            {
                return GeneratedRootFolder;
            }

            return path;
        }

        private static string NormalizeOutputFolder(string path, string childFolder)
        {
            if (string.IsNullOrWhiteSpace(path) || IsLegacySubfolderPath(path, childFolder) || !IsInsidePackageRoot(path))
            {
                return GeneratedRootFolder + "/" + childFolder;
            }

            return path;
        }

        private static bool IsLegacyRootPath(string path)
        {
            return string.Equals(path, "Assets/Nyxara Ai Studio/AICompanionStudio", System.StringComparison.Ordinal) ||
                   string.Equals(path, "Assets/Nyxara AI Studio/AICompanionStudio", System.StringComparison.Ordinal) ||
                   string.Equals(path, "Assets/NyxaraAIStudio", System.StringComparison.Ordinal) ||
                   string.Equals(path, "Assets/AICompanionStudio", System.StringComparison.Ordinal);
        }

        private static bool IsLegacySubfolderPath(string path, string childFolder)
        {
            return string.Equals(path, $"Assets/Nyxara Ai Studio/AICompanionStudio/{childFolder}", System.StringComparison.Ordinal) ||
                   string.Equals(path, $"Assets/Nyxara AI Studio/AICompanionStudio/{childFolder}", System.StringComparison.Ordinal) ||
                   string.Equals(path, $"Assets/NyxaraAIStudio/{childFolder}", System.StringComparison.Ordinal) ||
                   string.Equals(path, $"Assets/AICompanionStudio/{childFolder}", System.StringComparison.Ordinal);
        }

        private static bool IsInsidePackageRoot(string path)
        {
            return string.Equals(path, PackageRootFolder, System.StringComparison.Ordinal) ||
                   string.Equals(path, GeneratedRootFolder, System.StringComparison.Ordinal) ||
                   path.StartsWith(PackageRootFolder + "/", System.StringComparison.Ordinal);
        }
    }
}
