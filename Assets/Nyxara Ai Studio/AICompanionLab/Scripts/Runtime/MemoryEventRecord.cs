// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using Nyxara.AICompanion.Data;
using UnityEngine;

namespace Nyxara.AICompanion.Runtime
{
    [Serializable]
    public class MemoryEventRecord
    {
        public string id = Guid.NewGuid().ToString("N");
        public string eventType = "conversation";
        public string playerInput = string.Empty;
        public string npcReply = string.Empty;
        public string rawNpcReply = string.Empty;
        public string parsedVisibleReply = string.Empty;
        public string topic = string.Empty;
        public string mood = "calm";
        public string intent = "neutral";
        public float importance = 0.5f;
        public bool isDuplicateCandidate = true;
        public bool consolidated;
        public bool isRelationshipMemory;
        public string sourceSessionId = string.Empty;
        public string sourceCharacterId = "Nyxara";
        public string timestampUtc = string.Empty;
        public string normalizedKey = string.Empty;
        public string filterDecisionReason = string.Empty;
        public int duplicateCount = 1;
        public List<string> detectedExpressionTags = new();
        public MemoryRelationshipDelta relationshipEffect = new();
        public MemoryStateSnapshot beforeState = new();
        public MemoryStateSnapshot afterState = new();
    }

    [Serializable]
    public class MemoryRelationshipDelta
    {
        public float trustDelta;
        public float affectionDelta;
        public float respectDelta;
        public float suspicionDelta;
        public float familiarityDelta;

        public bool HasMeaningfulChange(float threshold = 0.05f)
        {
            return Mathf.Abs(trustDelta) >= threshold ||
                   Mathf.Abs(affectionDelta) >= threshold ||
                   Mathf.Abs(respectDelta) >= threshold ||
                   Mathf.Abs(suspicionDelta) >= threshold ||
                   Mathf.Abs(familiarityDelta) >= threshold;
        }

        public static MemoryRelationshipDelta FromSnapshots(MemoryStateSnapshot beforeState, MemoryStateSnapshot afterState)
        {
            if (beforeState == null || afterState == null)
            {
                return new MemoryRelationshipDelta();
            }

            return new MemoryRelationshipDelta
            {
                trustDelta = afterState.trust - beforeState.trust,
                affectionDelta = afterState.affection - beforeState.affection,
                respectDelta = afterState.respect - beforeState.respect,
                suspicionDelta = afterState.suspicion - beforeState.suspicion,
                familiarityDelta = afterState.familiarity - beforeState.familiarity
            };
        }
    }

    [Serializable]
    public class MemoryStateSnapshot
    {
        public string mood = "calm";
        public string relationship = "neutral";
        public string currentTask = string.Empty;
        public string currentGoal = string.Empty;
        public string lastPlayerTopic = string.Empty;
        public float trust;
        public float affection;
        public float respect;
        public float suspicion;
        public float familiarity;

        public static MemoryStateSnapshot FromRuntimeState(NPCRuntimeState state)
        {
            if (state == null)
            {
                return new MemoryStateSnapshot();
            }

            return new MemoryStateSnapshot
            {
                mood = state.mood,
                relationship = state.relationship,
                currentTask = state.currentTask,
                currentGoal = state.currentGoal,
                lastPlayerTopic = state.lastPlayerTopic,
                trust = state.trust,
                affection = state.affection,
                respect = state.respect,
                suspicion = state.suspicion,
                familiarity = state.familiarity
            };
        }
    }
}
