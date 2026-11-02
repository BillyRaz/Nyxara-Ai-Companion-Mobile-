// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using Nyxara.AICompanion.Expressions;
using UnityEngine;

namespace Nyxara.AICompanion.Data
{
    [CreateAssetMenu(fileName = "NewCharacterProfile", menuName = "AI Companion/Character Profile")]
    public class CharacterProfileData : ScriptableObject
    {
        [Header("Structured Profile")]
        public CharacterIdentityData identity = new();
        public CharacterBehaviorData behavior = new();
        public CharacterRelationshipDefaults relationshipDefaults = new();
        public CharacterResponseRules responseRules = new();
        public NPCRuntimeState runtimeDefaults = new();
        public ExpressionRoutingSettings expressionRouting = new();

        [Header("Legacy Mirrors")]
        [HideInInspector] public string characterName = "Nyxara";
        [HideInInspector] public string role = "companion";
        [HideInInspector] public List<string> corePersonality = new() { "witty", "protective", "curious" };
        [HideInInspector] public string speechStyle = "short, natural, expressive";
        [HideInInspector] public string backgroundSummary = "A sharp, emotionally aware companion who reads the player well.";
        [HideInInspector] public List<string> likes = new() { "bravery", "honesty", "playfulness" };
        [HideInInspector] public List<string> dislikes = new() { "cowardice", "arrogance" };
        [HideInInspector] public string defaultMood = "calm";
        [HideInInspector] [Range(0f, 1f)] public float flirtLevel = 0.3f;
        [HideInInspector] [Range(0f, 1f)] public float teaseLevel = 0.5f;
        [HideInInspector] public string voiceProfileId = "young_warm_female";
        [HideInInspector] public string expressionProfileId = "social_readable";

        private void OnEnable()
        {
            MigrateLegacyDataIfNeeded();
            SyncLegacyMirrors();
        }

        private void OnValidate()
        {
            MigrateLegacyDataIfNeeded();
            SyncLegacyMirrors();
        }

        public CharacterExpressionTriggerMapping ResolveExpressionTrigger(string triggerKey)
        {
            if (expressionRouting == null || expressionRouting.triggerMappings == null || string.IsNullOrWhiteSpace(triggerKey))
            {
                return null;
            }

            return expressionRouting.triggerMappings.FirstOrDefault(mapping => mapping != null && mapping.Matches(triggerKey));
        }

        public void ResetToDefaults()
        {
            identity = new CharacterIdentityData();
            behavior = new CharacterBehaviorData();
            relationshipDefaults = new CharacterRelationshipDefaults();
            responseRules = new CharacterResponseRules();
            runtimeDefaults = new NPCRuntimeState();
            expressionRouting = new ExpressionRoutingSettings();
            SyncLegacyMirrors();
        }

        private void MigrateLegacyDataIfNeeded()
        {
            identity ??= new CharacterIdentityData();
            behavior ??= new CharacterBehaviorData();
            relationshipDefaults ??= new CharacterRelationshipDefaults();
            responseRules ??= new CharacterResponseRules();
            runtimeDefaults ??= new NPCRuntimeState();
            expressionRouting ??= new ExpressionRoutingSettings();

            if (string.IsNullOrWhiteSpace(identity.characterName))
            {
                identity.characterName = string.IsNullOrWhiteSpace(characterName) ? "Nyxara" : characterName;
            }

            if (string.IsNullOrWhiteSpace(identity.role))
            {
                identity.role = string.IsNullOrWhiteSpace(role) ? "companion" : role;
            }

            if (identity.personalityTags == null || identity.personalityTags.Count == 0)
            {
                identity.personalityTags = corePersonality != null && corePersonality.Count > 0
                    ? new List<string>(corePersonality)
                    : new List<string> { "witty", "protective", "curious" };
            }

            if (string.IsNullOrWhiteSpace(identity.speechStyle))
            {
                identity.speechStyle = string.IsNullOrWhiteSpace(speechStyle) ? "short, natural, expressive" : speechStyle;
            }

            if (string.IsNullOrWhiteSpace(identity.defaultTone))
            {
                identity.defaultTone = "warm";
            }

            if (string.IsNullOrWhiteSpace(identity.backgroundSummary))
            {
                identity.backgroundSummary = string.IsNullOrWhiteSpace(backgroundSummary)
                    ? "A sharp, emotionally aware companion who reads the player well."
                    : backgroundSummary;
            }

            if (identity.likes == null || identity.likes.Count == 0)
            {
                identity.likes = likes != null && likes.Count > 0
                    ? new List<string>(likes)
                    : new List<string> { "bravery", "honesty", "playfulness" };
            }

            if (identity.dislikes == null || identity.dislikes.Count == 0)
            {
                identity.dislikes = dislikes != null && dislikes.Count > 0
                    ? new List<string>(dislikes)
                    : new List<string> { "cowardice", "arrogance" };
            }

            if (string.IsNullOrWhiteSpace(identity.voiceProfileId))
            {
                identity.voiceProfileId = string.IsNullOrWhiteSpace(voiceProfileId) ? "young_warm_female" : voiceProfileId;
            }

            if (string.IsNullOrWhiteSpace(expressionRouting.expressionProfileId))
            {
                expressionRouting.expressionProfileId = string.IsNullOrWhiteSpace(expressionProfileId) ? "social_readable" : expressionProfileId;
            }

            behavior.flirtLevel = Mathf.Max(behavior.flirtLevel, flirtLevel);
            behavior.teasing = Mathf.Max(behavior.teasing, teaseLevel);
            runtimeDefaults.mood = string.IsNullOrWhiteSpace(runtimeDefaults.mood)
                ? (string.IsNullOrWhiteSpace(defaultMood) ? "calm" : defaultMood)
                : runtimeDefaults.mood;

            relationshipDefaults.trust = Mathf.Max(relationshipDefaults.trust, runtimeDefaults.trust);
            relationshipDefaults.affection = Mathf.Max(relationshipDefaults.affection, runtimeDefaults.affection);
            relationshipDefaults.suspicion = Mathf.Max(relationshipDefaults.suspicion, runtimeDefaults.suspicion);
        }

        private void SyncLegacyMirrors()
        {
            characterName = identity.characterName;
            role = identity.role;
            corePersonality = identity.personalityTags ?? new List<string>();
            speechStyle = identity.speechStyle;
            backgroundSummary = identity.backgroundSummary;
            likes = identity.likes ?? new List<string>();
            dislikes = identity.dislikes ?? new List<string>();
            defaultMood = runtimeDefaults?.mood ?? "calm";
            flirtLevel = behavior.flirtLevel;
            teaseLevel = behavior.teasing;
            voiceProfileId = identity.voiceProfileId;
            expressionProfileId = expressionRouting.expressionProfileId;
        }
    }

    [Serializable]
    public class CharacterIdentityData
    {
        public string characterName = "Nyxara";
        public string role = "companion";
        [TextArea(3, 6)] public string backgroundSummary = "A sharp, emotionally aware companion who reads the player well.";
        public List<string> personalityTags = new() { "witty", "protective", "curious" };
        public string speechStyle = "short, natural, expressive";
        public string defaultTone = "warm";
        public List<string> likes = new() { "bravery", "honesty", "playfulness" };
        public List<string> dislikes = new() { "cowardice", "arrogance" };
        public string voiceProfileId = "young_warm_female";
    }

    [Serializable]
    public class CharacterBehaviorData
    {
        [Range(0f, 1f)] public float playfulness = 0.5f;
        [Range(0f, 1f)] public float warmth = 0.7f;
        [Range(0f, 1f)] public float boldness = 0.45f;
        [Range(0f, 1f)] public float teasing = 0.5f;
        [Range(0f, 1f)] public float flirtLevel = 0.3f;
        [Range(0f, 1f)] public float protectiveness = 0.65f;
        [Range(0f, 1f)] public float curiosity = 0.8f;
        [Range(0f, 1f)] public float refusalTendency = 0.2f;
        [Range(0f, 1f)] public float cooperationTendency = 0.8f;
    }

    [Serializable]
    public class CharacterRelationshipDefaults
    {
        [Range(0f, 1f)] public float trust = 0.5f;
        [Range(0f, 1f)] public float affection = 0.3f;
        [Range(0f, 1f)] public float respect = 0.65f;
        [Range(0f, 1f)] public float suspicion = 0.1f;
        [Range(0f, 1f)] public float familiarity = 0.35f;
    }

    [Serializable]
    public class CharacterResponseRules
    {
        [Min(1)] public int maxSentenceCount = 2;
        public ResponseLengthStyle responseLengthStyle = ResponseLengthStyle.Medium;
        public bool stayInCharacter = true;
        public bool spokenDialogueOnly = true;
        public bool stripNarration = true;
        public bool stripStageDirections = true;
        public bool allowActionTags = false;
        public bool allowExpressionTags = true;
    }

    [Serializable]
    public class ExpressionRoutingSettings
    {
        public bool expressionTagSupport = true;
        public bool acceptBracketTags = true;
        public bool acceptAsteriskTags = true;
        public bool useGlobalCooldown = true;
        public bool ignoreDuplicateTriggers = true;
        public bool useLipSafePlayback = true;
        public string expressionProfileId = "social_readable";
        [Min(0f)] public float globalCooldown = 0.35f;
        [Min(0f)] public float duplicateMemoryWindow = 1.25f;
        [Range(0f, 1f)] public float duplicateIntensityThreshold = 0.12f;
        [Min(0.01f)] public float mouthHitDuration = 0.08f;
        public List<CharacterExpressionTriggerMapping> triggerMappings = new();
    }

    [Serializable]
    public class CharacterExpressionTriggerMapping
    {
        public string triggerKey = "happy";
        public List<string> aliases = new();
        public ExpressionPreset preset;
        public List<BlendshapeWeight> targetBlendshapeValues = new();
        [Min(0.01f)] public float blendSpeed = 8f;
        [Min(0f)] public float holdDuration = 0.8f;
        [Min(0.01f)] public float returnSpeed = 6f;
        [Min(0f)] public float cooldown = 0.3f;
        public int priority;

        public bool Matches(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                return false;
            }

            if (string.Equals(triggerKey, trigger, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (aliases == null)
            {
                return false;
            }

            return aliases.Any(alias => !string.IsNullOrWhiteSpace(alias) && string.Equals(alias, trigger, StringComparison.OrdinalIgnoreCase));
        }

        public Dictionary<string, float> BuildTargetWeights(float intensity = 1f)
        {
            var resolved = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            if (preset != null)
            {
                foreach (var pair in preset.ToDictionary())
                {
                    resolved[pair.Key] = Mathf.Clamp(pair.Value * Mathf.Clamp01(intensity), 0f, 100f);
                }
            }

            if (targetBlendshapeValues != null)
            {
                foreach (var target in targetBlendshapeValues)
                {
                    if (target == null || string.IsNullOrWhiteSpace(target.blendshapeName))
                    {
                        continue;
                    }

                    resolved[target.blendshapeName] = Mathf.Clamp(target.weight * Mathf.Clamp01(intensity), 0f, 100f);
                }
            }

            return resolved;
        }
    }

    public enum ResponseLengthStyle
    {
        Short,
        Medium,
        Long
    }
}
