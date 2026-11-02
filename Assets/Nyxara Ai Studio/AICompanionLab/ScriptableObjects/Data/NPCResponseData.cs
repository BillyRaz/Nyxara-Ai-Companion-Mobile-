// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nyxara.AICompanion.Data
{
    [Serializable]
    public class NPCResponseData
    {
        public string intent = "neutral";
        public string mood = "calm";
        public string action = "none";
        public string signal = "none";
        public string dialogue = string.Empty;
        public string rawDialogue = string.Empty;
        public List<ResponseTriggerTag> expressionTriggers = new();
        public List<ResponseTriggerTag> actionTriggers = new();

        public bool IsValid => !string.IsNullOrWhiteSpace(dialogue);

        public override string ToString()
        {
            var expressionSummary = expressionTriggers != null && expressionTriggers.Count > 0
                ? string.Join(", ", expressionTriggers.Select(trigger => trigger.ToString()))
                : "none";
            return $"[Intent:{intent} Mood:{mood} Action:{action} Signal:{signal} Expressions:{expressionSummary}] {dialogue}";
        }
    }

    [Serializable]
    public class ResponseTriggerTag
    {
        public string key = string.Empty;
        public float intensity = 1f;
        public string sourceFormat = "brackets";

        public override string ToString()
        {
            return intensity >= 0.99f ? key : $"{key}:{intensity:0.##}";
        }
    }
}
