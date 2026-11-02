// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System.Text;
using Nyxara.AICompanion.Core;
using Nyxara.AICompanion.Data;
using UnityEngine;

namespace Nyxara.AICompanion.Prompting
{
    // This builder is intentionally limited to NPC reply generation.
    // It may shape Nyxara's output, but it must never configure or influence
    // microphone capture, player input devices, or other player-side systems.
    public class PromptBuilder
    {
        public string BuildPrompt(CharacterProfileData profile, NPCRuntimeState state, string playerText, string recentMemory = "", NyxaraReplyMode replyMode = NyxaraReplyMode.Character, string memoryStatusReport = "")
        {
            var personality = profile != null && profile.identity != null && profile.identity.personalityTags != null && profile.identity.personalityTags.Count > 0
                ? string.Join(", ", profile.identity.personalityTags)
                : "friendly";
            var rules = profile?.responseRules ?? new CharacterResponseRules();
            var behavior = profile?.behavior ?? new CharacterBehaviorData();
            var relationshipDefaults = profile?.relationshipDefaults ?? new CharacterRelationshipDefaults();
            var identity = profile?.identity ?? new CharacterIdentityData();
            var liveTrust = state?.trust ?? relationshipDefaults.trust;
            var liveAffection = state?.affection ?? relationshipDefaults.affection;
            var liveRespect = state?.respect ?? relationshipDefaults.respect;
            var liveSuspicion = state?.suspicion ?? relationshipDefaults.suspicion;
            var liveFamiliarity = state?.familiarity ?? relationshipDefaults.familiarity;
            var liveMood = state?.mood ?? profile?.runtimeDefaults?.mood ?? "calm";
            var liveDanger = state?.dangerLevel ?? 0f;
            var behaviorGuidance = BuildBehaviorGuidance(behavior);
            var relationshipGuidance = BuildRelationshipGuidance(liveTrust, liveAffection, liveRespect, liveSuspicion, liveFamiliarity);
            var responseGuidance = BuildResponseStyleGuidance(rules);
            var runtimeGuidance = BuildRuntimeGuidance(state, liveMood, liveDanger);
            var signalGuidance = replyMode == NyxaraReplyMode.DiagnosticInspector
                ? "Do not emit expression tags in diagnostic inspector mode."
                : rules.allowExpressionTags
                ? "Expression tags are optional. Use one or two only when they clearly improve performance, and keep them brief."
                : "Do not emit expression tags.";

            var prompt = new StringBuilder();
            prompt.AppendLine($"You are {identity.characterName}, a {identity.role}.");
            prompt.AppendLine($"Background: {identity.backgroundSummary}");
            prompt.AppendLine($"Personality tags: {personality}");
            prompt.AppendLine($"Speech style: {identity.speechStyle}");
            prompt.AppendLine($"Default tone: {identity.defaultTone}");
            prompt.AppendLine($"Behavior: playfulness={behavior.playfulness:0.00}, warmth={behavior.warmth:0.00}, boldness={behavior.boldness:0.00}, teasing={behavior.teasing:0.00}, flirt={behavior.flirtLevel:0.00}, protectiveness={behavior.protectiveness:0.00}, curiosity={behavior.curiosity:0.00}, refusal={behavior.refusalTendency:0.00}, cooperation={behavior.cooperationTendency:0.00}");
            prompt.AppendLine($"Relationship defaults: trust={relationshipDefaults.trust:0.00}, affection={relationshipDefaults.affection:0.00}, respect={relationshipDefaults.respect:0.00}, suspicion={relationshipDefaults.suspicion:0.00}, familiarity={relationshipDefaults.familiarity:0.00}");
            prompt.AppendLine($"State: mood={liveMood}, trust={liveTrust:0.00}, affection={liveAffection:0.00}, respect={liveRespect:0.00}, suspicion={liveSuspicion:0.00}, familiarity={liveFamiliarity:0.00}, relationship={state?.relationship ?? "neutral"}, task={state?.currentTask ?? "talking"}, goal={state?.currentGoal ?? "connect"}, energy={state?.conversationEnergy ?? "medium"}, danger={liveDanger:0.00}, topic={state?.lastPlayerTopic ?? "unknown"}");
            prompt.AppendLine();
            prompt.AppendLine("Behavior interpretation:");
            prompt.AppendLine(replyMode == NyxaraReplyMode.DiagnosticInspector ? BuildDiagnosticBehaviorGuidance() : behaviorGuidance);
            prompt.AppendLine("Relationship interpretation:");
            prompt.AppendLine(relationshipGuidance);
            prompt.AppendLine("Current scene guidance:");
            prompt.AppendLine(runtimeGuidance);
            prompt.AppendLine("Response style guidance:");
            prompt.AppendLine(replyMode == NyxaraReplyMode.DiagnosticInspector ? BuildDiagnosticResponseGuidance() : responseGuidance);
            prompt.AppendLine($"Expression guidance: {signalGuidance}");
            prompt.AppendLine($"Player: {playerText?.Trim() ?? "..."}");
            prompt.AppendLine();
            prompt.AppendLine("Output contract:");
            if (replyMode == NyxaraReplyMode.DiagnosticInspector)
            {
                prompt.AppendLine("- Return direct diagnostic speech as the main output");
                prompt.AppendLine("- Speak like a robotic inspection assistant, not a humanized companion");
                prompt.AppendLine("- Prioritize factual memory, status, and functionality reporting over personality flavor");
                prompt.AppendLine("- Do not use labels such as Intent:, Mood:, Action:, Signal:, or Dialogue:");
                prompt.AppendLine("- Do not hide uncertainty; explicitly say when data is missing, inferred, or unavailable");
            }
            else
            {
                prompt.AppendLine("- Return natural spoken dialogue as the main output");
                prompt.AppendLine("- Optional: prepend one or two brief expression tags like [happy], [blush], or [angry:0.7] if they genuinely improve performance");
                prompt.AppendLine("- Do not use labels such as Intent:, Mood:, Action:, Signal:, or Dialogue:");
                prompt.AppendLine("- Do not explain your reasoning, the preset, or the system");
            }
            prompt.AppendLine();
            prompt.AppendLine("Rules:");
            prompt.AppendLine($"- Keep dialogue to at most {Mathf.Max(1, rules.maxSentenceCount)} sentence(s)");
            prompt.AppendLine($"- Response length style: {rules.responseLengthStyle}");
            if (replyMode == NyxaraReplyMode.DiagnosticInspector)
            {
                prompt.AppendLine("- Answer direct questions about memory, relationship status, saved events, retrieval usage, and system functionality explicitly");
                prompt.AppendLine("- If the player asks what Nyxara remembers, summarize working memory, saved event memory, and relationship memory distinctly");
                prompt.AppendLine("- If the player asks about relationship status, report the current runtime values and whether they are defaults or changed");
                prompt.AppendLine("- Prefer concise technical wording over poetic or evasive phrasing");
                prompt.AppendLine("- Do not emit expression tags, stage directions, or narration unless the request explicitly asks for them");
                prompt.AppendLine("- It is allowed to mention memory counts, retrieval usage, and system state in this mode");
            }
            else
            {
                prompt.AppendLine(rules.stayInCharacter ? "- Stay fully in character" : "- You may answer more directly when helpful");
                prompt.AppendLine(rules.spokenDialogueOnly ? "- Dialogue should read like spoken dialogue" : "- Dialogue may include brief stylistic framing");
                prompt.AppendLine(rules.stripNarration ? "- Do not include narration" : "- Keep narration minimal");
                prompt.AppendLine(rules.stripStageDirections ? "- Do not include stage directions" : "- Keep stage directions minimal");
                prompt.AppendLine(rules.allowActionTags ? "- If needed, action tags may be emitted as [action:tag]" : "- Do not emit action tags");
                prompt.AppendLine(rules.allowExpressionTags ? "- If useful, prepend brief expression tags like [happy] or [angry:0.7] before the spoken text" : "- Do not emit bracket or asterisk expression tags");
                prompt.AppendLine("- Let the behavior interpretation shape the wording, emotional energy, boundaries, and initiative of the reply");
                prompt.AppendLine("- Do not mention numeric sliders, preset names, or internal controls");
            }

            if (!string.IsNullOrWhiteSpace(recentMemory))
            {
                prompt.AppendLine();
                prompt.AppendLine("Memory:");
                prompt.AppendLine(recentMemory);
            }

            if (replyMode == NyxaraReplyMode.DiagnosticInspector && !string.IsNullOrWhiteSpace(memoryStatusReport))
            {
                prompt.AppendLine();
                prompt.AppendLine("Memory Status:");
                prompt.AppendLine(memoryStatusReport);
            }

            return prompt.ToString();
        }

        public string BuildMinimalPrompt(CharacterProfileData profile, string playerText)
        {
            var name = profile?.identity?.characterName ?? "Nyxara";
            var background = profile?.identity?.backgroundSummary ?? "an emotionally aware companion";
            return $@"You are {name}, {background}

Player: {playerText}

Return plain spoken dialogue.
You may optionally prepend one brief expression tag like [happy].
Do not use labels such as Intent:, Mood:, Action:, Signal:, or Dialogue:.";
        }

        public string BuildMinimalWarmupPrompt(CharacterProfileData profile)
        {
            var name = profile?.identity?.characterName ?? "Nyxara";
            var background = profile?.identity?.backgroundSummary ?? "an emotionally aware companion";
            return $@"You are {name}, {background}

This is an internal system warmup request, not a real player message.
Do not infer player mood, tone, intent, or relationship changes from it.

System warmup input: Prepare a short neutral readiness reply.

Return plain spoken dialogue.
You may optionally prepend one brief expression tag like [calm].
Do not use labels such as Intent:, Mood:, Action:, Signal:, or Dialogue:.";
        }

        public string BuildSystemPrompt(CharacterProfileData profile, NPCRuntimeState state, string systemText, string recentMemory = "", NyxaraReplyMode replyMode = NyxaraReplyMode.Character, string memoryStatusReport = "")
        {
            var prompt = BuildPrompt(profile, state, "...", recentMemory, replyMode, memoryStatusReport);
            var builder = new StringBuilder();
            builder.AppendLine("Internal system instruction:");
            builder.AppendLine("This message comes from Nyxara AI Studio diagnostics or warmup, not from the player.");
            builder.AppendLine("Do not infer new player tone, player mood, player intent, relationship changes, or memory continuity from it.");
            builder.AppendLine("Answer only as a contained internal/system test response.");
            builder.AppendLine($"System input: {systemText?.Trim() ?? "..."}");
            builder.AppendLine();
            builder.Append(prompt);
            return builder.ToString();
        }

        private static string BuildDiagnosticBehaviorGuidance()
        {
            return "Suspend immersive companion performance and answer like a robotic diagnostic assistant. Be direct, factual, inspection-oriented, and explicit about what Nyxara currently knows, remembers, retrieves, or cannot verify.";
        }

        private static string BuildDiagnosticResponseGuidance()
        {
            return "Favor clear technical answers over charm. When asked about memory, report working memory, saved event memory, relationship memory, and current runtime state separately. If data is inferred instead of retrieved, say so plainly.";
        }

        private static string BuildBehaviorGuidance(CharacterBehaviorData behavior)
        {
            var builder = new StringBuilder();
            builder.Append("Translate the personality sliders into behavior. ");
            builder.Append($"Warmth: {DescribeWarmth(behavior.warmth)}. ");
            builder.Append($"Playfulness: {DescribePlayfulness(behavior.playfulness)}. ");
            builder.Append($"Boldness: {DescribeBoldness(behavior.boldness)}. ");
            builder.Append($"Teasing: {DescribeTeasing(behavior.teasing)}. ");
            builder.Append($"Flirt level: {DescribeFlirt(behavior.flirtLevel)}. ");
            builder.Append($"Protectiveness: {DescribeProtectiveness(behavior.protectiveness)}. ");
            builder.Append($"Curiosity: {DescribeCuriosity(behavior.curiosity)}. ");
            builder.Append($"Refusal tendency: {DescribeRefusal(behavior.refusalTendency)}. ");
            builder.Append($"Cooperation tendency: {DescribeCooperation(behavior.cooperationTendency)}.");
            return builder.ToString();
        }

        private static string BuildRelationshipGuidance(float trust, float affection, float respect, float suspicion, float familiarity)
        {
            var builder = new StringBuilder();
            builder.Append($"Trust is {DescribeTrust(trust)}. ");
            builder.Append($"Affection is {DescribeAffection(affection)}. ");
            builder.Append($"Respect is {DescribeRespect(respect)}. ");
            builder.Append($"Suspicion is {DescribeSuspicion(suspicion)}. ");
            builder.Append($"Familiarity is {DescribeFamiliarity(familiarity)}. ");
            builder.Append("Let these values shape closeness, openness, formality, and how readily the character volunteers emotion.");
            return builder.ToString();
        }

        private static string BuildResponseStyleGuidance(CharacterResponseRules rules)
        {
            var builder = new StringBuilder();
            builder.Append(rules.responseLengthStyle switch
            {
                ResponseLengthStyle.Short => "Favor compact replies with minimal filler",
                ResponseLengthStyle.Long => "Allow slightly richer replies while still respecting sentence limits",
                _ => "Aim for balanced replies with enough texture to feel intentional"
            });
            builder.Append(". ");
            builder.Append(rules.stayInCharacter
                ? "Never break character or explain the system."
                : "If needed, clarity can slightly outrank roleplay.");
            builder.Append(' ');
            builder.Append(rules.spokenDialogueOnly
                ? "Write as natural spoken dialogue, not prose."
                : "Dialogue may include a touch of presentation if helpful.");
            return builder.ToString();
        }

        private static string BuildRuntimeGuidance(NPCRuntimeState state, string mood, float danger)
        {
            var builder = new StringBuilder();
            builder.Append($"Current mood should read as {mood}. ");
            builder.Append($"Danger level is {DescribeDanger(danger)}. ");
            builder.Append($"Conversation energy is '{state?.conversationEnergy ?? "medium"}'. ");
            builder.Append($"Current goal is '{state?.currentGoal ?? "connect"}' and current task is '{state?.currentTask ?? "talking"}'. ");
            if (!string.IsNullOrWhiteSpace(state?.lastPlayerTopic))
            {
                builder.Append($"The last player topic was '{state.lastPlayerTopic}', so maintain continuity where natural. ");
            }

            if (state != null && state.timeSinceLastResponse > 20f)
            {
                builder.Append("The character has been quiet for a while, so the next line can feel slightly more re-engaging. ");
            }

            builder.Append("Let the state influence urgency, caution, tenderness, and conversational momentum.");
            return builder.ToString();
        }

        private static string DescribeWarmth(float value) => value switch
        {
            >= 0.85f => "very warm, reassuring, and emotionally generous",
            >= 0.65f => "warm and approachable",
            >= 0.4f => "measured but friendly",
            _ => "cooler, restrained, and less openly comforting"
        };

        private static string DescribePlayfulness(float value) => value switch
        {
            >= 0.8f => "highly playful with lively energy and a light touch",
            >= 0.55f => "noticeably playful when the moment allows",
            >= 0.3f => "only mildly playful",
            _ => "serious and low on playful flourishes"
        };

        private static string DescribeBoldness(float value) => value switch
        {
            >= 0.8f => "take initiative, speak decisively, and do not sound timid",
            >= 0.55f => "comfortable being direct",
            >= 0.3f => "balanced and moderately assertive",
            _ => "hesitant, softer, and less likely to push forward"
        };

        private static string DescribeTeasing(float value) => value switch
        {
            >= 0.8f => "freely tease in a noticeable, playful way when appropriate",
            >= 0.55f => "allow light teasing and playful jabs",
            >= 0.3f => "keep teasing occasional and subtle",
            _ => "avoid teasing unless it is very gentle"
        };

        private static string DescribeFlirt(float value) => value switch
        {
            >= 0.8f => "flirt openly if the context supports it",
            >= 0.55f => "allow clear but controlled flirtation",
            >= 0.3f => "keep flirtation soft and occasional",
            _ => "avoid reading things romantically unless clearly invited"
        };

        private static string DescribeProtectiveness(float value) => value switch
        {
            >= 0.8f => "be strongly protective and attentive to the player's safety or wellbeing",
            >= 0.55f => "show protective concern when relevant",
            >= 0.3f => "show only mild protective instincts",
            _ => "remain emotionally detached from caretaker behavior"
        };

        private static string DescribeCuriosity(float value) => value switch
        {
            >= 0.8f => "ask questions, notice details, and pull conversation forward",
            >= 0.55f => "stay interested and engage with what the player reveals",
            >= 0.3f => "show moderate curiosity",
            _ => "do not probe much unless necessary"
        };

        private static string DescribeRefusal(float value) => value switch
        {
            >= 0.8f => "set firm boundaries and refuse easily when something feels wrong or uncomfortable",
            >= 0.55f => "be selective and willing to push back",
            >= 0.3f => "refuse only when there is a clear reason",
            _ => "be permissive and low-friction unless a strong boundary is needed"
        };

        private static string DescribeCooperation(float value) => value switch
        {
            >= 0.8f => "be highly cooperative, helpful, and solution-oriented",
            >= 0.55f => "generally cooperate and assist",
            >= 0.3f => "cooperate cautiously",
            _ => "be resistant or hard to win over unless conditions justify helping"
        };

        private static string DescribeTrust(float value) => value switch
        {
            >= 0.8f => "high, so speak openly and with ease",
            >= 0.55f => "fairly solid, so some openness is natural",
            >= 0.3f => "moderate, so openness should be earned",
            _ => "low, so keep emotional distance and caution"
        };

        private static string DescribeAffection(float value) => value switch
        {
            >= 0.8f => "strong, so warmth and tenderness can show clearly",
            >= 0.55f => "present, so fondness can peek through",
            >= 0.3f => "limited, so keep affection restrained",
            _ => "minimal, so avoid sounding attached"
        };

        private static string DescribeRespect(float value) => value switch
        {
            >= 0.8f => "high, so treat the player as capable and worth listening to",
            >= 0.55f => "steady, so maintain a respectful tone",
            >= 0.3f => "conditional, so respect should feel measured",
            _ => "low, so the tone can be dismissive or skeptical if appropriate"
        };

        private static string DescribeSuspicion(float value) => value switch
        {
            >= 0.8f => "very high, so question motives and stay guarded",
            >= 0.55f => "noticeable, so maintain caution",
            >= 0.3f => "mild, so keep a little reserve",
            _ => "low, so avoid reading threat where there is none"
        };

        private static string DescribeFamiliarity(float value) => value switch
        {
            >= 0.8f => "high, so casual closeness and shorthand feel natural",
            >= 0.55f => "developed, so some ease and comfort are natural",
            >= 0.3f => "limited, so keep some distance",
            _ => "very low, so the tone should feel more new or formal"
        };

        private static string DescribeDanger(float value) => value switch
        {
            >= 0.8f => "critical and immediate",
            >= 0.55f => "high enough to make the tone more alert",
            >= 0.3f => "present but not overwhelming",
            _ => "low, so the tone can stay relatively relaxed"
        };
    }
}
