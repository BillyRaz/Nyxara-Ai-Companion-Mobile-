// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Data;
using Nyxara.AICompanion.Expressions;
using Nyxara.AICompanion.Face;
using Nyxara.AICompanion.Parsing;
using UnityEditor;
using UnityEngine;

namespace Nyxara.AICompanion.Editor
{
    public partial class NyxaraCompanionStudioWindow
    {
        private bool _profileIdentityFoldout = true;
        private bool _profileBehaviorFoldout = true;
        private bool _profileRelationshipFoldout = true;
        private bool _profileRulesFoldout = true;
        private bool _profileRuntimeDefaultsFoldout = true;
        private bool _profileExpressionFoldout = true;
        private bool _profileLiveRuntimeFoldout = true;
        private bool _profileDebugFoldout = true;
        private bool _profileAdvancedFoldout;
        private string _profileParseRawResponse = "[happy][blush] You look pleased with yourself today.";
        private string _profileManualTrigger = "happy";
        private float _profileManualTriggerIntensity = 1f;
        private NPCResponseData _profileParsePreview;
        private int _selectedPersonalityPresetIndex;
        private int _selectedRelationshipPresetIndex;
        private int _selectedResponseStylePresetIndex;
        private CharacterProfileSectionPreset _selectedPersonalityPresetAsset;
        private CharacterProfileSectionPreset _selectedRelationshipPresetAsset;
        private CharacterProfileSectionPreset _selectedResponseStylePresetAsset;
        private CharacterBehaviorData _lastAppliedBehaviorPreset;
        private CharacterRelationshipDefaults _lastAppliedRelationshipPreset;
        private CharacterResponseRules _lastAppliedResponseRulesPreset;

        private static readonly string[] PersonalityPresetNames =
        {
            "Soft",
            "Warm",
            "Playful",
            "Flirty",
            "Guarded",
            "Serious",
            "Confident",
            "Shy",
            "Protective",
            "Tsundere",
            "Companion",
            "VTuber Host",
            "Story NPC"
        };

        private static readonly string[] RelationshipPresetNames =
        {
            "Neutral",
            "Trusted",
            "Affectionate",
            "Formal",
            "Guarded",
            "Suspicious",
            "Longtime Companion",
            "New Arrival"
        };

        private static readonly string[] ResponseStylePresetNames =
        {
            "Soft",
            "Warm",
            "Playful",
            "Flirty",
            "Guarded",
            "Serious",
            "Confident",
            "Shy",
            "Companion",
            "VTuber Host",
            "Story NPC"
        };

        private void DrawStructuredProfileStudio(NyxaraCompanionBrain brain, GameObject studioRoot)
        {
            if (_config == null)
            {
                return;
            }

            if (studioRoot == null)
            {
                EditorGUILayout.HelpBox("No Nyxara Studio root is selected. You can still author the profile asset here, but live runtime state, trigger playback, and scene-bound previews will stay disabled until you select or build a Studio root.", MessageType.Info);
            }

            if (_config.characterProfile == null)
            {
                EditorGUILayout.HelpBox("No character profile asset is assigned yet.", MessageType.Warning);
                if (GUILayout.Button("Create Or Assign Profile Asset"))
                {
                    _config.characterProfile = NyxaraCompanionStudioBuilder.EnsureCharacterProfile(_config);
                    EditorUtility.SetDirty(_config);
                    AssetDatabase.SaveAssets();
                    RefreshProfileJson();
                }

                return;
            }

            var expressionPlayer = studioRoot != null ? studioRoot.GetComponent<ExpressionTriggerPlayer>() : null;
            var expressionLibrary = studioRoot != null ? studioRoot.GetComponent<ExpressionLibraryManager>() : null;
            var faceDriver = studioRoot != null ? studioRoot.GetComponent<ArkItBlendshapeDriver>() : null;

            _config.characterProfile = (CharacterProfileData)EditorGUILayout.ObjectField("Character Profile", _config.characterProfile, typeof(CharacterProfileData), false);
            var profile = _config.characterProfile;
            if (profile == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Profile Asset", AssetDatabase.GetAssetPath(profile));

            DrawIdentitySection(profile);
            DrawBehaviorSection(profile);
            DrawRelationshipDefaultsSection(profile);
            DrawResponseRulesSection(profile);
            DrawRuntimeDefaultsSection(profile);
            DrawExpressionRoutingSection(profile);
            DrawLiveRuntimeSection(brain);
            DrawProfileDebugSection(profile, brain, expressionPlayer, expressionLibrary, faceDriver);
            DrawProfileAdvancedSection(brain);

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Profile Asset"))
            {
                SaveProfileAsset(profile);
            }

            if (GUILayout.Button("Reset To Defaults"))
            {
                if (EditorUtility.DisplayDialog(
                        "Reset Profile",
                        "Reset this profile asset back to the Nyxara Studio default structured values? This keeps the asset but clears your custom profile tuning and trigger mappings.",
                        "Reset",
                        "Cancel"))
                {
                    profile.ResetToDefaults();
                    SaveProfileAsset(profile);
                }
            }

            if (GUILayout.Button("Refresh Profile JSON"))
            {
                RefreshProfileJson();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawIdentitySection(CharacterProfileData profile)
        {
            _profileIdentityFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileIdentityFoldout, "Identity");
            if (_profileIdentityFoldout)
            {
                var identity = profile.identity;
                identity.characterName = EditorGUILayout.TextField("Character Name", identity.characterName);
                identity.role = EditorGUILayout.TextField("Role", identity.role);
                EditorGUILayout.LabelField("Background / Bio");
                identity.backgroundSummary = EditorGUILayout.TextArea(identity.backgroundSummary, GUILayout.MinHeight(70f));
                identity.personalityTags = DrawStringList("Personality Tags", identity.personalityTags, "Tag");
                identity.speechStyle = EditorGUILayout.TextField("Speech Style", identity.speechStyle);
                identity.defaultTone = EditorGUILayout.TextField("Default Tone", identity.defaultTone);
                identity.voiceProfileId = EditorGUILayout.TextField("Voice Profile Id", identity.voiceProfileId);
                MarkProfileDirty(profile);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBehaviorSection(CharacterProfileData profile)
        {
            _profileBehaviorFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileBehaviorFoldout, "Behavior");
            if (_profileBehaviorFoldout)
            {
                DrawPresetToolbar(
                    "Personality Preset",
                    PersonalityPresetNames,
                    ref _selectedPersonalityPresetIndex,
                    ref _selectedPersonalityPresetAsset,
                    CharacterProfilePresetCategory.Personality,
                    profile,
                    ApplySelectedBehaviorPreset,
                    () => SaveCurrentSectionAsPreset(profile, CharacterProfilePresetCategory.Personality));

                var behavior = profile.behavior;
                behavior.playfulness = EditorGUILayout.Slider("Playfulness", behavior.playfulness, 0f, 1f);
                behavior.warmth = EditorGUILayout.Slider("Warmth", behavior.warmth, 0f, 1f);
                behavior.boldness = EditorGUILayout.Slider("Boldness", behavior.boldness, 0f, 1f);
                behavior.teasing = EditorGUILayout.Slider("Teasing", behavior.teasing, 0f, 1f);
                behavior.flirtLevel = EditorGUILayout.Slider("Flirt Level", behavior.flirtLevel, 0f, 1f);
                behavior.protectiveness = EditorGUILayout.Slider("Protectiveness", behavior.protectiveness, 0f, 1f);
                behavior.curiosity = EditorGUILayout.Slider("Curiosity", behavior.curiosity, 0f, 1f);
                behavior.refusalTendency = EditorGUILayout.Slider("Refusal Tendency", behavior.refusalTendency, 0f, 1f);
                behavior.cooperationTendency = EditorGUILayout.Slider("Cooperation Tendency", behavior.cooperationTendency, 0f, 1f);
                MarkProfileDirty(profile);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRelationshipDefaultsSection(CharacterProfileData profile)
        {
            _profileRelationshipFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileRelationshipFoldout, "Relationship Defaults");
            if (_profileRelationshipFoldout)
            {
                DrawPresetToolbar(
                    "Relationship Preset",
                    RelationshipPresetNames,
                    ref _selectedRelationshipPresetIndex,
                    ref _selectedRelationshipPresetAsset,
                    CharacterProfilePresetCategory.Relationship,
                    profile,
                    ApplySelectedRelationshipPreset,
                    () => SaveCurrentSectionAsPreset(profile, CharacterProfilePresetCategory.Relationship));

                var defaults = profile.relationshipDefaults;
                defaults.trust = EditorGUILayout.Slider("Trust", defaults.trust, 0f, 1f);
                defaults.affection = EditorGUILayout.Slider("Affection", defaults.affection, 0f, 1f);
                defaults.respect = EditorGUILayout.Slider("Respect", defaults.respect, 0f, 1f);
                defaults.suspicion = EditorGUILayout.Slider("Suspicion", defaults.suspicion, 0f, 1f);
                defaults.familiarity = EditorGUILayout.Slider("Familiarity", defaults.familiarity, 0f, 1f);
                MarkProfileDirty(profile);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawResponseRulesSection(CharacterProfileData profile)
        {
            _profileRulesFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileRulesFoldout, "Response Rules");
            if (_profileRulesFoldout)
            {
                DrawPresetToolbar(
                    "Response Style Preset",
                    ResponseStylePresetNames,
                    ref _selectedResponseStylePresetIndex,
                    ref _selectedResponseStylePresetAsset,
                    CharacterProfilePresetCategory.ResponseStyle,
                    profile,
                    ApplySelectedResponseStylePreset,
                    () => SaveCurrentSectionAsPreset(profile, CharacterProfilePresetCategory.ResponseStyle));

                var rules = profile.responseRules;
                rules.maxSentenceCount = Mathf.Max(1, EditorGUILayout.IntField("Max Sentence Count", rules.maxSentenceCount));
                rules.responseLengthStyle = (ResponseLengthStyle)EditorGUILayout.EnumPopup("Response Style", rules.responseLengthStyle);
                rules.stayInCharacter = EditorGUILayout.Toggle("Stay In Character", rules.stayInCharacter);
                rules.spokenDialogueOnly = EditorGUILayout.Toggle("Spoken Dialogue Only", rules.spokenDialogueOnly);
                rules.stripNarration = EditorGUILayout.Toggle("Strip Narration", rules.stripNarration);
                rules.stripStageDirections = EditorGUILayout.Toggle("Strip Stage Directions", rules.stripStageDirections);
                rules.allowActionTags = EditorGUILayout.Toggle("Allow Action Tags", rules.allowActionTags);
                rules.allowExpressionTags = EditorGUILayout.Toggle("Allow Expression Tags", rules.allowExpressionTags);
                MarkProfileDirty(profile);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRuntimeDefaultsSection(CharacterProfileData profile)
        {
            _profileRuntimeDefaultsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileRuntimeDefaultsFoldout, "Runtime Defaults");
            if (_profileRuntimeDefaultsFoldout)
            {
                DrawRuntimeStateEditor(profile.runtimeDefaults, "Default");
                MarkProfileDirty(profile);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawExpressionRoutingSection(CharacterProfileData profile)
        {
            _profileExpressionFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileExpressionFoldout, "Expression Routing");
            if (_profileExpressionFoldout)
            {
                var routing = profile.expressionRouting;
                routing.expressionTagSupport = EditorGUILayout.Toggle("Expression Tag Support", routing.expressionTagSupport);
                routing.acceptBracketTags = EditorGUILayout.Toggle("Accept [tag]", routing.acceptBracketTags);
                routing.acceptAsteriskTags = EditorGUILayout.Toggle("Accept *tag*", routing.acceptAsteriskTags);
                routing.useGlobalCooldown = EditorGUILayout.Toggle("Use Global Cooldown", routing.useGlobalCooldown);
                routing.globalCooldown = EditorGUILayout.FloatField("Global Cooldown", routing.globalCooldown);
                routing.ignoreDuplicateTriggers = EditorGUILayout.Toggle("Ignore Duplicates", routing.ignoreDuplicateTriggers);
                routing.duplicateMemoryWindow = EditorGUILayout.FloatField("Duplicate Window", routing.duplicateMemoryWindow);
                routing.duplicateIntensityThreshold = EditorGUILayout.Slider("Duplicate Intensity Threshold", routing.duplicateIntensityThreshold, 0f, 1f);
                routing.useLipSafePlayback = EditorGUILayout.Toggle("Lip Safe Playback", routing.useLipSafePlayback);
                routing.mouthHitDuration = EditorGUILayout.FloatField("Mouth Hit Duration", routing.mouthHitDuration);
                routing.expressionProfileId = EditorGUILayout.TextField("Expression Profile Id", routing.expressionProfileId);

                routing.triggerMappings ??= new List<CharacterExpressionTriggerMapping>();
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Trigger Mappings", EditorStyles.miniBoldLabel);
                for (var i = 0; i < routing.triggerMappings.Count; i++)
                {
                    var mapping = routing.triggerMappings[i] ??= new CharacterExpressionTriggerMapping();
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();
                    mapping.triggerKey = EditorGUILayout.TextField("Trigger Key", mapping.triggerKey);
                    if (GUILayout.Button("Remove", GUILayout.Width(80f)))
                    {
                        routing.triggerMappings.RemoveAt(i);
                        MarkProfileDirty(profile);
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    mapping.aliases = DrawStringList("Aliases", mapping.aliases, "Alias");
                    mapping.preset = (ExpressionPreset)EditorGUILayout.ObjectField("Preset", mapping.preset, typeof(ExpressionPreset), false);
                    mapping.blendSpeed = EditorGUILayout.FloatField("Blend Speed", mapping.blendSpeed);
                    mapping.holdDuration = EditorGUILayout.FloatField("Hold Duration", mapping.holdDuration);
                    mapping.returnSpeed = EditorGUILayout.FloatField("Return Speed", mapping.returnSpeed);
                    mapping.cooldown = EditorGUILayout.FloatField("Cooldown", mapping.cooldown);
                    mapping.priority = EditorGUILayout.IntField("Priority", mapping.priority);
                    mapping.targetBlendshapeValues = DrawBlendshapeWeights(mapping.targetBlendshapeValues);
                    EditorGUILayout.EndVertical();
                }

                if (GUILayout.Button("Add Trigger Mapping"))
                {
                    routing.triggerMappings.Add(new CharacterExpressionTriggerMapping());
                }

                MarkProfileDirty(profile);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLiveRuntimeSection(NyxaraCompanionBrain brain)
        {
            _profileLiveRuntimeFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileLiveRuntimeFoldout, "Runtime State");
            if (_profileLiveRuntimeFoldout)
            {
                if (brain?.RuntimeState == null)
                {
                    EditorGUILayout.HelpBox("Select or build a Nyxara Studio root with a NyxaraCompanionBrain to inspect the live runtime state here.", MessageType.Info);
                }
                else
                {
                    DrawRuntimeStateEditor(brain.RuntimeState, "Current");
                    EditorUtility.SetDirty(brain);
                    if (PrefabUtility.IsPartOfPrefabInstance(brain))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(brain);
                    }

                    if (GUILayout.Button("Refresh Runtime JSON"))
                    {
                        RefreshRuntimeJson(brain);
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawProfileDebugSection(
            CharacterProfileData profile,
            NyxaraCompanionBrain brain,
            ExpressionTriggerPlayer expressionPlayer,
            ExpressionLibraryManager expressionLibrary,
            ArkItBlendshapeDriver faceDriver)
        {
            _profileDebugFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileDebugFoldout, "Parser Test / Preview");
            if (_profileDebugFoldout)
            {
                EditorGUILayout.LabelField("Raw AI Response");
                _profileParseRawResponse = EditorGUILayout.TextArea(_profileParseRawResponse, GUILayout.MinHeight(90f));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Parse Response"))
                {
                    _profileParsePreview = StructuredResponseParser.Parse(_profileParseRawResponse, brain?.RuntimeState ?? profile.runtimeDefaults, profile);
                }

                GUI.enabled = expressionPlayer != null || expressionLibrary != null || faceDriver != null;
                if (GUILayout.Button("Apply Trigger Preview"))
                {
                    ApplyExpressionPreview(profile, expressionPlayer, expressionLibrary, faceDriver);
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (_profileParsePreview != null)
                {
                    EditorGUILayout.LabelField("Detected Expressions", FormatTriggerList(_profileParsePreview.expressionTriggers));
                    EditorGUILayout.LabelField("Detected Actions", FormatTriggerList(_profileParsePreview.actionTriggers));
                    EditorGUILayout.LabelField("Clean Spoken Dialogue");
                    EditorGUILayout.TextArea(_profileParsePreview.dialogue, GUILayout.MinHeight(55f));
                }

                EditorGUILayout.Space(6f);
                _profileManualTrigger = EditorGUILayout.TextField("Manual Trigger", _profileManualTrigger);
                _profileManualTriggerIntensity = EditorGUILayout.Slider("Manual Intensity", _profileManualTriggerIntensity, 0f, 1f);
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = Application.isPlaying && expressionPlayer != null;
                if (GUILayout.Button("Play Trigger Runtime"))
                {
                    expressionPlayer.TryPlayTrigger(_profileManualTrigger, _profileManualTriggerIntensity);
                }

                GUI.enabled = expressionLibrary != null || faceDriver != null;
                if (GUILayout.Button("Preview Trigger On Face"))
                {
                    PreviewTriggerOnFace(profile, expressionLibrary, faceDriver, _profileManualTrigger, _profileManualTriggerIntensity);
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("Runtime trigger playback needs Play Mode. Face preview still works in the editor for quick iteration.", MessageType.None);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawProfileAdvancedSection(NyxaraCompanionBrain brain)
        {
            _profileAdvancedFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_profileAdvancedFoldout, "Advanced JSON / Prompt Tools");
            if (_profileAdvancedFoldout)
            {
                DrawCompanionBioSection();
                EditorGUILayout.Space(8f);
                DrawPromptSenderSection(brain);
                EditorGUILayout.Space(8f);
                DrawRuntimeJsonSection(brain);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRuntimeStateEditor(NPCRuntimeState state, string labelPrefix)
        {
            if (state == null)
            {
                return;
            }

            state.mood = EditorGUILayout.TextField($"{labelPrefix} Mood", state.mood);
            state.currentTask = EditorGUILayout.TextField($"{labelPrefix} Task", state.currentTask);
            state.currentGoal = EditorGUILayout.TextField($"{labelPrefix} Goal", state.currentGoal);
            state.conversationEnergy = EditorGUILayout.TextField($"{labelPrefix} Conversation Energy", state.conversationEnergy);
            state.relationship = EditorGUILayout.TextField($"{labelPrefix} Relationship", state.relationship);
            state.currentLocation = EditorGUILayout.TextField($"{labelPrefix} Location", state.currentLocation);
            state.currentFocus = EditorGUILayout.TextField($"{labelPrefix} Focus", state.currentFocus);
            state.dangerLevel = EditorGUILayout.Slider($"{labelPrefix} Danger Level", state.dangerLevel, 0f, 1f);
            state.trust = EditorGUILayout.Slider($"{labelPrefix} Trust", state.trust, 0f, 1f);
            state.affection = EditorGUILayout.Slider($"{labelPrefix} Affection", state.affection, 0f, 1f);
            state.respect = EditorGUILayout.Slider($"{labelPrefix} Respect", state.respect, 0f, 1f);
            state.suspicion = EditorGUILayout.Slider($"{labelPrefix} Suspicion", state.suspicion, 0f, 1f);
            state.familiarity = EditorGUILayout.Slider($"{labelPrefix} Familiarity", state.familiarity, 0f, 1f);
            state.followState = EditorGUILayout.Toggle($"{labelPrefix} Follow State", state.followState);
            state.lastPlayerTopic = EditorGUILayout.TextField($"{labelPrefix} Last Player Topic", state.lastPlayerTopic);
            state.timeSinceLastResponse = EditorGUILayout.FloatField($"{labelPrefix} Time Since Last Response", state.timeSinceLastResponse);
        }

        private List<string> DrawStringList(string label, List<string> values, string itemLabel)
        {
            values ??= new List<string>();
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            for (var i = 0; i < values.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                values[i] = EditorGUILayout.TextField($"{itemLabel} {i + 1}", values[i]);
                if (GUILayout.Button("-", GUILayout.Width(26f)))
                {
                    values.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button($"Add {itemLabel}"))
            {
                values.Add(string.Empty);
            }

            return values;
        }

        private List<BlendshapeWeight> DrawBlendshapeWeights(List<BlendshapeWeight> weights)
        {
            weights ??= new List<BlendshapeWeight>();
            EditorGUILayout.LabelField("Target Slider Values", EditorStyles.miniBoldLabel);
            for (var i = 0; i < weights.Count; i++)
            {
                var weight = weights[i] ??= new BlendshapeWeight();
                EditorGUILayout.BeginHorizontal();
                weight.blendshapeName = EditorGUILayout.TextField("Blendshape", weight.blendshapeName);
                weight.weight = EditorGUILayout.Slider(weight.weight, 0f, 100f);
                if (GUILayout.Button("-", GUILayout.Width(26f)))
                {
                    weights.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Slider Target"))
            {
                weights.Add(new BlendshapeWeight());
            }

            return weights;
        }

        private void SaveProfileAsset(CharacterProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            EditorUtility.SetDirty(profile);
            if (_config != null)
            {
                EditorUtility.SetDirty(_config);
            }

            AssetDatabase.SaveAssets();
            RefreshProfileJson();
        }

        private void DrawPresetToolbar(
            string label,
            string[] builtInPresetNames,
            ref int selectedPresetIndex,
            ref CharacterProfileSectionPreset selectedPresetAsset,
            CharacterProfilePresetCategory category,
            CharacterProfileData profile,
            Action<CharacterProfileData, CharacterProfileSectionPreset> applyAction,
            Action saveAction)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            selectedPresetIndex = EditorGUILayout.Popup("Built-in", Mathf.Clamp(selectedPresetIndex, 0, builtInPresetNames.Length - 1), builtInPresetNames);
            selectedPresetAsset = (CharacterProfileSectionPreset)EditorGUILayout.ObjectField("Custom Preset", selectedPresetAsset, typeof(CharacterProfileSectionPreset), false);
            if (selectedPresetAsset != null && selectedPresetAsset.category != category)
            {
                EditorGUILayout.HelpBox($"The selected preset asset is a {selectedPresetAsset.category} preset, not a {category} preset.", MessageType.Warning);
            }

            EditorGUILayout.LabelField("Status", GetPresetStatusLabel(category, builtInPresetNames[selectedPresetIndex], selectedPresetAsset, profile));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Preset"))
            {
                applyAction(profile, selectedPresetAsset);
                MarkProfileDirty(profile);
            }

            if (GUILayout.Button("Save Current As Preset"))
            {
                saveAction();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void ApplySelectedBehaviorPreset(CharacterProfileData profile, CharacterProfileSectionPreset selectedPresetAsset)
        {
            var preset = selectedPresetAsset != null && selectedPresetAsset.category == CharacterProfilePresetCategory.Personality
                ? selectedPresetAsset.behavior
                : GetBuiltInBehaviorPreset(PersonalityPresetNames[Mathf.Clamp(_selectedPersonalityPresetIndex, 0, PersonalityPresetNames.Length - 1)]);
            if (preset == null)
            {
                return;
            }

            profile.behavior = CloneBehaviorData(preset);
            _lastAppliedBehaviorPreset = CloneBehaviorData(profile.behavior);
        }

        private void ApplySelectedRelationshipPreset(CharacterProfileData profile, CharacterProfileSectionPreset selectedPresetAsset)
        {
            var preset = selectedPresetAsset != null && selectedPresetAsset.category == CharacterProfilePresetCategory.Relationship
                ? selectedPresetAsset.relationshipDefaults
                : GetBuiltInRelationshipPreset(RelationshipPresetNames[Mathf.Clamp(_selectedRelationshipPresetIndex, 0, RelationshipPresetNames.Length - 1)]);
            if (preset == null)
            {
                return;
            }

            profile.relationshipDefaults = CloneRelationshipDefaults(preset);
            _lastAppliedRelationshipPreset = CloneRelationshipDefaults(profile.relationshipDefaults);
        }

        private void ApplySelectedResponseStylePreset(CharacterProfileData profile, CharacterProfileSectionPreset selectedPresetAsset)
        {
            var preset = selectedPresetAsset != null && selectedPresetAsset.category == CharacterProfilePresetCategory.ResponseStyle
                ? selectedPresetAsset.responseRules
                : GetBuiltInResponseRulesPreset(ResponseStylePresetNames[Mathf.Clamp(_selectedResponseStylePresetIndex, 0, ResponseStylePresetNames.Length - 1)]);
            if (preset == null)
            {
                return;
            }

            profile.responseRules = CloneResponseRules(preset);
            _lastAppliedResponseRulesPreset = CloneResponseRules(profile.responseRules);
        }

        private void SaveCurrentSectionAsPreset(CharacterProfileData profile, CharacterProfilePresetCategory category)
        {
            if (profile == null)
            {
                return;
            }

            var defaultFolder = ResolvePresetFolder();
            EnsureAssetFolderPath(defaultFolder);
            var fileName = EditorUtility.SaveFilePanelInProject(
                "Save Profile Preset",
                $"{profile.identity.characterName}_{category}Preset",
                "asset",
                "Choose where to save the preset asset",
                defaultFolder);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            var asset = CreateInstance<CharacterProfileSectionPreset>();
            asset.category = category;
            asset.presetName = Path.GetFileNameWithoutExtension(fileName);
            asset.behavior = CloneBehaviorData(profile.behavior);
            asset.relationshipDefaults = CloneRelationshipDefaults(profile.relationshipDefaults);
            asset.responseRules = CloneResponseRules(profile.responseRules);
            AssetDatabase.CreateAsset(asset, fileName);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            switch (category)
            {
                case CharacterProfilePresetCategory.Personality:
                    _selectedPersonalityPresetAsset = asset;
                    _lastAppliedBehaviorPreset = CloneBehaviorData(profile.behavior);
                    break;
                case CharacterProfilePresetCategory.Relationship:
                    _selectedRelationshipPresetAsset = asset;
                    _lastAppliedRelationshipPreset = CloneRelationshipDefaults(profile.relationshipDefaults);
                    break;
                case CharacterProfilePresetCategory.ResponseStyle:
                    _selectedResponseStylePresetAsset = asset;
                    _lastAppliedResponseRulesPreset = CloneResponseRules(profile.responseRules);
                    break;
            }
        }

        private string GetPresetStatusLabel(
            CharacterProfilePresetCategory category,
            string builtInPresetName,
            CharacterProfileSectionPreset selectedPresetAsset,
            CharacterProfileData profile)
        {
            return category switch
            {
                CharacterProfilePresetCategory.Personality => BuildPresetStatusLabel(
                    selectedPresetAsset,
                    builtInPresetName,
                    AreBehaviorEqual(profile.behavior, _lastAppliedBehaviorPreset)),
                CharacterProfilePresetCategory.Relationship => BuildPresetStatusLabel(
                    selectedPresetAsset,
                    builtInPresetName,
                    AreRelationshipDefaultsEqual(profile.relationshipDefaults, _lastAppliedRelationshipPreset)),
                CharacterProfilePresetCategory.ResponseStyle => BuildPresetStatusLabel(
                    selectedPresetAsset,
                    builtInPresetName,
                    AreResponseRulesEqual(profile.responseRules, _lastAppliedResponseRulesPreset)),
                _ => "Preset: Custom"
            };
        }

        private static string BuildPresetStatusLabel(CharacterProfileSectionPreset selectedPresetAsset, string builtInPresetName, bool matchesLastApplied)
        {
            var baseName = selectedPresetAsset != null ? selectedPresetAsset.presetName : builtInPresetName;
            return matchesLastApplied
                ? $"Preset: {baseName}"
                : $"Preset: {baseName} (Modified)";
        }

        private string ResolvePresetFolder()
        {
            var profileFolder = _config != null && !string.IsNullOrWhiteSpace(_config.profileFolder)
                ? _config.profileFolder.Replace('\\', '/').TrimEnd('/')
                : "Assets/Nyxara Ai Studio/Generated/Profiles";
            return $"{profileFolder}/Presets";
        }

        private static CharacterBehaviorData GetBuiltInBehaviorPreset(string presetName)
        {
            return presetName switch
            {
                "Soft" => new CharacterBehaviorData { playfulness = 0.25f, warmth = 0.85f, boldness = 0.2f, teasing = 0.1f, flirtLevel = 0.15f, protectiveness = 0.55f, curiosity = 0.55f, refusalTendency = 0.2f, cooperationTendency = 0.85f },
                "Warm" => new CharacterBehaviorData { playfulness = 0.45f, warmth = 0.95f, boldness = 0.35f, teasing = 0.2f, flirtLevel = 0.2f, protectiveness = 0.7f, curiosity = 0.65f, refusalTendency = 0.15f, cooperationTendency = 0.9f },
                "Playful" => new CharacterBehaviorData { playfulness = 0.9f, warmth = 0.7f, boldness = 0.65f, teasing = 0.8f, flirtLevel = 0.35f, protectiveness = 0.45f, curiosity = 0.85f, refusalTendency = 0.15f, cooperationTendency = 0.85f },
                "Flirty" => new CharacterBehaviorData { playfulness = 0.75f, warmth = 0.8f, boldness = 0.7f, teasing = 0.7f, flirtLevel = 0.85f, protectiveness = 0.45f, curiosity = 0.7f, refusalTendency = 0.1f, cooperationTendency = 0.88f },
                "Guarded" => new CharacterBehaviorData { playfulness = 0.2f, warmth = 0.35f, boldness = 0.4f, teasing = 0.15f, flirtLevel = 0.05f, protectiveness = 0.65f, curiosity = 0.45f, refusalTendency = 0.65f, cooperationTendency = 0.45f },
                "Serious" => new CharacterBehaviorData { playfulness = 0.1f, warmth = 0.45f, boldness = 0.55f, teasing = 0.05f, flirtLevel = 0.05f, protectiveness = 0.7f, curiosity = 0.5f, refusalTendency = 0.35f, cooperationTendency = 0.7f },
                "Confident" => new CharacterBehaviorData { playfulness = 0.45f, warmth = 0.65f, boldness = 0.9f, teasing = 0.5f, flirtLevel = 0.35f, protectiveness = 0.5f, curiosity = 0.7f, refusalTendency = 0.2f, cooperationTendency = 0.8f },
                "Shy" => new CharacterBehaviorData { playfulness = 0.35f, warmth = 0.8f, boldness = 0.15f, teasing = 0.08f, flirtLevel = 0.2f, protectiveness = 0.4f, curiosity = 0.65f, refusalTendency = 0.3f, cooperationTendency = 0.78f },
                "Protective" => new CharacterBehaviorData { playfulness = 0.3f, warmth = 0.75f, boldness = 0.7f, teasing = 0.2f, flirtLevel = 0.15f, protectiveness = 0.95f, curiosity = 0.55f, refusalTendency = 0.28f, cooperationTendency = 0.82f },
                "Tsundere" => new CharacterBehaviorData { playfulness = 0.35f, warmth = 0.45f, boldness = 0.7f, teasing = 0.55f, flirtLevel = 0.25f, protectiveness = 0.7f, curiosity = 0.6f, refusalTendency = 0.45f, cooperationTendency = 0.62f },
                "Companion" => new CharacterBehaviorData { playfulness = 0.5f, warmth = 0.82f, boldness = 0.45f, teasing = 0.32f, flirtLevel = 0.18f, protectiveness = 0.78f, curiosity = 0.72f, refusalTendency = 0.15f, cooperationTendency = 0.9f },
                "VTuber Host" => new CharacterBehaviorData { playfulness = 0.88f, warmth = 0.85f, boldness = 0.82f, teasing = 0.45f, flirtLevel = 0.2f, protectiveness = 0.35f, curiosity = 0.9f, refusalTendency = 0.08f, cooperationTendency = 0.95f },
                "Story NPC" => new CharacterBehaviorData { playfulness = 0.25f, warmth = 0.58f, boldness = 0.42f, teasing = 0.12f, flirtLevel = 0.1f, protectiveness = 0.55f, curiosity = 0.5f, refusalTendency = 0.22f, cooperationTendency = 0.7f },
                _ => new CharacterBehaviorData()
            };
        }

        private static CharacterRelationshipDefaults GetBuiltInRelationshipPreset(string presetName)
        {
            return presetName switch
            {
                "Trusted" => new CharacterRelationshipDefaults { trust = 0.82f, affection = 0.55f, respect = 0.8f, suspicion = 0.08f, familiarity = 0.72f },
                "Affectionate" => new CharacterRelationshipDefaults { trust = 0.7f, affection = 0.88f, respect = 0.72f, suspicion = 0.06f, familiarity = 0.78f },
                "Formal" => new CharacterRelationshipDefaults { trust = 0.52f, affection = 0.2f, respect = 0.85f, suspicion = 0.12f, familiarity = 0.18f },
                "Guarded" => new CharacterRelationshipDefaults { trust = 0.28f, affection = 0.12f, respect = 0.45f, suspicion = 0.62f, familiarity = 0.15f },
                "Suspicious" => new CharacterRelationshipDefaults { trust = 0.18f, affection = 0.08f, respect = 0.32f, suspicion = 0.82f, familiarity = 0.1f },
                "Longtime Companion" => new CharacterRelationshipDefaults { trust = 0.92f, affection = 0.72f, respect = 0.9f, suspicion = 0.03f, familiarity = 0.9f },
                "New Arrival" => new CharacterRelationshipDefaults { trust = 0.4f, affection = 0.18f, respect = 0.52f, suspicion = 0.24f, familiarity = 0.12f },
                _ => new CharacterRelationshipDefaults()
            };
        }

        private static CharacterResponseRules GetBuiltInResponseRulesPreset(string presetName)
        {
            return presetName switch
            {
                "Soft" => new CharacterResponseRules { maxSentenceCount = 2, responseLengthStyle = ResponseLengthStyle.Short, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "Warm" => new CharacterResponseRules { maxSentenceCount = 3, responseLengthStyle = ResponseLengthStyle.Medium, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "Playful" => new CharacterResponseRules { maxSentenceCount = 3, responseLengthStyle = ResponseLengthStyle.Medium, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "Flirty" => new CharacterResponseRules { maxSentenceCount = 3, responseLengthStyle = ResponseLengthStyle.Medium, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "Guarded" => new CharacterResponseRules { maxSentenceCount = 2, responseLengthStyle = ResponseLengthStyle.Short, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = false },
                "Serious" => new CharacterResponseRules { maxSentenceCount = 3, responseLengthStyle = ResponseLengthStyle.Medium, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = false },
                "Confident" => new CharacterResponseRules { maxSentenceCount = 3, responseLengthStyle = ResponseLengthStyle.Medium, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "Shy" => new CharacterResponseRules { maxSentenceCount = 2, responseLengthStyle = ResponseLengthStyle.Short, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "Companion" => new CharacterResponseRules { maxSentenceCount = 3, responseLengthStyle = ResponseLengthStyle.Medium, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "VTuber Host" => new CharacterResponseRules { maxSentenceCount = 4, responseLengthStyle = ResponseLengthStyle.Long, stayInCharacter = true, spokenDialogueOnly = true, stripNarration = true, stripStageDirections = true, allowActionTags = false, allowExpressionTags = true },
                "Story NPC" => new CharacterResponseRules { maxSentenceCount = 4, responseLengthStyle = ResponseLengthStyle.Long, stayInCharacter = true, spokenDialogueOnly = false, stripNarration = false, stripStageDirections = true, allowActionTags = true, allowExpressionTags = true },
                _ => new CharacterResponseRules()
            };
        }

        private static CharacterBehaviorData CloneBehaviorData(CharacterBehaviorData source)
        {
            if (source == null)
            {
                return new CharacterBehaviorData();
            }

            return new CharacterBehaviorData
            {
                playfulness = source.playfulness,
                warmth = source.warmth,
                boldness = source.boldness,
                teasing = source.teasing,
                flirtLevel = source.flirtLevel,
                protectiveness = source.protectiveness,
                curiosity = source.curiosity,
                refusalTendency = source.refusalTendency,
                cooperationTendency = source.cooperationTendency
            };
        }

        private static CharacterRelationshipDefaults CloneRelationshipDefaults(CharacterRelationshipDefaults source)
        {
            if (source == null)
            {
                return new CharacterRelationshipDefaults();
            }

            return new CharacterRelationshipDefaults
            {
                trust = source.trust,
                affection = source.affection,
                respect = source.respect,
                suspicion = source.suspicion,
                familiarity = source.familiarity
            };
        }

        private static CharacterResponseRules CloneResponseRules(CharacterResponseRules source)
        {
            if (source == null)
            {
                return new CharacterResponseRules();
            }

            return new CharacterResponseRules
            {
                maxSentenceCount = source.maxSentenceCount,
                responseLengthStyle = source.responseLengthStyle,
                stayInCharacter = source.stayInCharacter,
                spokenDialogueOnly = source.spokenDialogueOnly,
                stripNarration = source.stripNarration,
                stripStageDirections = source.stripStageDirections,
                allowActionTags = source.allowActionTags,
                allowExpressionTags = source.allowExpressionTags
            };
        }

        private static bool AreBehaviorEqual(CharacterBehaviorData left, CharacterBehaviorData right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return Approximately(left.playfulness, right.playfulness) &&
                   Approximately(left.warmth, right.warmth) &&
                   Approximately(left.boldness, right.boldness) &&
                   Approximately(left.teasing, right.teasing) &&
                   Approximately(left.flirtLevel, right.flirtLevel) &&
                   Approximately(left.protectiveness, right.protectiveness) &&
                   Approximately(left.curiosity, right.curiosity) &&
                   Approximately(left.refusalTendency, right.refusalTendency) &&
                   Approximately(left.cooperationTendency, right.cooperationTendency);
        }

        private static bool AreRelationshipDefaultsEqual(CharacterRelationshipDefaults left, CharacterRelationshipDefaults right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return Approximately(left.trust, right.trust) &&
                   Approximately(left.affection, right.affection) &&
                   Approximately(left.respect, right.respect) &&
                   Approximately(left.suspicion, right.suspicion) &&
                   Approximately(left.familiarity, right.familiarity);
        }

        private static bool AreResponseRulesEqual(CharacterResponseRules left, CharacterResponseRules right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left.maxSentenceCount == right.maxSentenceCount &&
                   left.responseLengthStyle == right.responseLengthStyle &&
                   left.stayInCharacter == right.stayInCharacter &&
                   left.spokenDialogueOnly == right.spokenDialogueOnly &&
                   left.stripNarration == right.stripNarration &&
                   left.stripStageDirections == right.stripStageDirections &&
                   left.allowActionTags == right.allowActionTags &&
                   left.allowExpressionTags == right.allowExpressionTags;
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.0001f;
        }

        private void MarkProfileDirty(CharacterProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            EditorUtility.SetDirty(profile);
        }

        private static string FormatTriggerList(IEnumerable<ResponseTriggerTag> triggers)
        {
            var items = triggers?.ToList() ?? new List<ResponseTriggerTag>();
            return items.Count == 0 ? "None" : string.Join(", ", items.Select(item => item.ToString()));
        }

        private void ApplyExpressionPreview(
            CharacterProfileData profile,
            ExpressionTriggerPlayer expressionPlayer,
            ExpressionLibraryManager expressionLibrary,
            ArkItBlendshapeDriver faceDriver)
        {
            if (_profileParsePreview?.expressionTriggers == null || _profileParsePreview.expressionTriggers.Count == 0)
            {
                return;
            }

            var first = _profileParsePreview.expressionTriggers[0];
            if (Application.isPlaying && expressionPlayer != null)
            {
                expressionPlayer.TryPlayTrigger(first.key, first.intensity);
                return;
            }

            PreviewTriggerOnFace(profile, expressionLibrary, faceDriver, first.key, first.intensity);
        }

        private void PreviewTriggerOnFace(
            CharacterProfileData profile,
            ExpressionLibraryManager expressionLibrary,
            ArkItBlendshapeDriver faceDriver,
            string triggerKey,
            float intensity)
        {
            var mapping = profile?.ResolveExpressionTrigger(triggerKey);
            if (mapping == null)
            {
                Debug.LogWarning($"[Nyxara Profile] No expression mapping found for trigger '{triggerKey}'.");
                return;
            }

            var weights = mapping.BuildTargetWeights(intensity);
            EnsureExpressionModeForEditing(ResolveStudioRootFromContext());
            if (expressionLibrary != null)
            {
                expressionLibrary.ApplyExpressionWeights(weights);
                return;
            }

            if (faceDriver == null)
            {
                return;
            }

            foreach (var pair in weights)
            {
                faceDriver.TrySetBlendshapeWeight(pair.Key, pair.Value);
            }
        }
    }
}
#endif
