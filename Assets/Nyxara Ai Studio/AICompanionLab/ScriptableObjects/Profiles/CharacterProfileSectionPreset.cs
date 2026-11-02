// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using UnityEngine;

namespace Nyxara.AICompanion.Data
{
    [CreateAssetMenu(fileName = "NewProfileSectionPreset", menuName = "AI Companion/Profile Section Preset")]
    public class CharacterProfileSectionPreset : ScriptableObject
    {
        public string presetName = "New Preset";
        public CharacterProfilePresetCategory category = CharacterProfilePresetCategory.Personality;
        public CharacterBehaviorData behavior = new();
        public CharacterRelationshipDefaults relationshipDefaults = new();
        public CharacterResponseRules responseRules = new();
    }

    public enum CharacterProfilePresetCategory
    {
        Personality,
        Relationship,
        ResponseStyle
    }
}
