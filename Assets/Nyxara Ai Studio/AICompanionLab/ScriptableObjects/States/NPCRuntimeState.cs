// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using UnityEngine;

namespace Nyxara.AICompanion.Data
{
    [Serializable]
    public class NPCRuntimeState
    {
        [Header("Dynamic State")]
        public string mood = "calm";
        [Range(0f, 1f)] public float trust = 0.5f;
        [Range(0f, 1f)] public float respect = 0.65f;
        public string relationship = "neutral";
        public string currentTask = "talking with player";
        public string currentGoal = "learn_player_intent";
        [Range(0f, 1f)] public float dangerLevel = 0f;
        [Range(0f, 1f)] public float affection = 0.3f;
        [Range(0f, 1f)] public float suspicion = 0.1f;
        [Range(0f, 1f)] public float familiarity = 0.35f;

        [Header("Behavior Flags")]
        public bool followState;
        public string conversationEnergy = "medium";

        [Header("Recent Context")]
        public string lastPlayerTopic = string.Empty;
        public float timeSinceLastResponse;
        public string currentLocation = string.Empty;
        public string currentFocus = string.Empty;

        public void ApplyMoodShift(string newMood, float intensity = 0.5f)
        {
            mood = newMood;
        }

        public void ModifyTrust(float delta)
        {
            trust = Mathf.Clamp01(trust + delta);
        }

        public void ModifyAffection(float delta)
        {
            affection = Mathf.Clamp01(affection + delta);
        }

        public void ModifyRespect(float delta)
        {
            respect = Mathf.Clamp01(respect + delta);
        }

        public void ModifyFamiliarity(float delta)
        {
            familiarity = Mathf.Clamp01(familiarity + delta);
        }

        public NPCRuntimeState Clone()
        {
            return (NPCRuntimeState)MemberwiseClone();
        }
    }
}
