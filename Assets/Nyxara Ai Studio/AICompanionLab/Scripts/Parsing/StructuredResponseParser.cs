// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Nyxara.AICompanion.Data;
using UnityEngine;

namespace Nyxara.AICompanion.Parsing
{
    public static class StructuredResponseParser
    {
        private static readonly Regex IntentRegex = new(@"Intent:\s*(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MoodRegex = new(@"Mood:\s*(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ActionRegex = new(@"Action:\s*(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SignalRegex = new(@"Signal:\s*(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DialogueRegex = new(@"Dialogue:\s*(.+?)(?=\n\w+:|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex BracketTagRegex = new(@"\[(?<content>[^\[\]\r\n]+)\]", RegexOptions.Compiled);
        private static readonly Regex AsteriskTagRegex = new(@"(?<!\S)\*(?<content>[^*\r\n]+)\*(?!\S)", RegexOptions.Compiled);

        private static readonly string[] ValidIntents = { "neutral", "question", "tease", "flirt", "reassure", "warn", "suspicious", "friendly", "follow_accept", "follow_reject" };
        private static readonly string[] ValidMoods = { "calm", "playful", "warm", "guarded", "tense", "confident", "curious" };
        private static readonly string[] ValidActions = { "none", "follow", "wait", "stop", "focus_player", "warn" };
        private static readonly string[] ValidSignals = { "none", "smile", "eyebrow_raise", "head_tilt", "suspicious_look", "shy_smile", "amused_smirk", "concerned", "bold_stare" };

        public static NPCResponseData Parse(string rawResponse, NPCRuntimeState currentState, CharacterProfileData profile = null)
        {
            var result = new NPCResponseData();
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                result.dialogue = "...";
                return result;
            }

            result.intent = ParseField(IntentRegex, rawResponse, "neutral", ValidIntents);
            result.mood = ParseField(MoodRegex, rawResponse, currentState?.mood ?? "calm", ValidMoods);
            result.action = ParseField(ActionRegex, rawResponse, "none", ValidActions);
            result.signal = ParseField(SignalRegex, rawResponse, "none", ValidSignals);
            result.rawDialogue = ParseDialogue(rawResponse);

            var tagContext = ParseTriggerTags(result.rawDialogue, profile);
            result.expressionTriggers = tagContext.Expressions;
            result.actionTriggers = tagContext.Actions;
            result.dialogue = tagContext.CleanDialogue;

            if (string.IsNullOrWhiteSpace(result.dialogue))
            {
                var fallbackDialogue = DialogueSanitizer.Sanitize(ParseDialogue(rawResponse));
                result.dialogue = string.IsNullOrWhiteSpace(fallbackDialogue)
                    ? "I'm not sure what to say right now."
                    : fallbackDialogue;
            }

            return result;
        }

        public static TriggerParseResult ParseTriggerTags(string rawDialogue, CharacterProfileData profile = null)
        {
            var expressions = new List<ResponseTriggerTag>();
            var actions = new List<ResponseTriggerTag>();
            if (string.IsNullOrWhiteSpace(rawDialogue))
            {
                return new TriggerParseResult(expressions, actions, string.Empty);
            }

            var cleaned = rawDialogue;
            var allowBracketTags = profile?.expressionRouting == null || profile.expressionRouting.acceptBracketTags;
            var allowAsteriskTags = profile?.expressionRouting == null || profile.expressionRouting.acceptAsteriskTags;

            if (allowBracketTags)
            {
                cleaned = BracketTagRegex.Replace(cleaned, match =>
                {
                    ParseTrigger(match.Groups["content"].Value, "brackets", expressions, actions);
                    return " ";
                });
            }

            if (allowAsteriskTags)
            {
                cleaned = AsteriskTagRegex.Replace(cleaned, match =>
                {
                    ParseTrigger(match.Groups["content"].Value, "asterisks", expressions, actions);
                    return " ";
                });
            }

            cleaned = DialogueSanitizer.Sanitize(cleaned);
            return new TriggerParseResult(expressions, actions, cleaned);
        }

        private static string ParseField(Regex regex, string input, string defaultValue, string[] validValues)
        {
            var match = regex.Match(input);
            if (!match.Success)
            {
                return defaultValue;
            }

            var value = match.Groups[1].Value.Trim().ToLowerInvariant();
            foreach (var valid in validValues)
            {
                if (string.Equals(value, valid, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return defaultValue;
        }

        private static string ParseDialogue(string input)
        {
            var match = DialogueRegex.Match(input);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            var trimmedInput = input.Trim();
            if (string.IsNullOrWhiteSpace(trimmedInput))
            {
                return string.Empty;
            }

            var containsStructuredLabels =
                IntentRegex.IsMatch(trimmedInput) ||
                MoodRegex.IsMatch(trimmedInput) ||
                ActionRegex.IsMatch(trimmedInput) ||
                SignalRegex.IsMatch(trimmedInput) ||
                DialogueRegex.IsMatch(trimmedInput);

            if (!containsStructuredLabels)
            {
                return trimmedInput;
            }

            var lines = input.Split('\n');
            var dialogueLines = new List<string>();
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (!string.IsNullOrWhiteSpace(line) &&
                    !line.StartsWith("Intent:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Mood:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Action:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Signal:", StringComparison.OrdinalIgnoreCase))
                {
                    dialogueLines.Insert(0, line);
                }
            }

            return dialogueLines.Count > 0
                ? string.Join(" ", dialogueLines)
                : trimmedInput;
        }

        private static void ParseTrigger(
            string rawTag,
            string sourceFormat,
            ICollection<ResponseTriggerTag> expressions,
            ICollection<ResponseTriggerTag> actions)
        {
            if (string.IsNullOrWhiteSpace(rawTag))
            {
                return;
            }

            var parts = rawTag
                .Split(':')
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
            if (parts.Length == 0)
            {
                return;
            }

            var tagType = "expression";
            var key = parts[0];
            var intensityIndex = 1;

            if (parts.Length >= 2 &&
                (string.Equals(parts[0], "action", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[0], "act", StringComparison.OrdinalIgnoreCase)))
            {
                tagType = "action";
                key = parts[1];
                intensityIndex = 2;
            }
            else if (parts.Length >= 2 &&
                     (string.Equals(parts[0], "expression", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(parts[0], "expr", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(parts[0], "emotion", StringComparison.OrdinalIgnoreCase)))
            {
                key = parts[1];
                intensityIndex = 2;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var intensity = 1f;
            if (parts.Length > intensityIndex &&
                float.TryParse(parts[intensityIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedIntensity))
            {
                intensity = Mathf.Clamp01(parsedIntensity);
            }

            var trigger = new ResponseTriggerTag
            {
                key = key.ToLowerInvariant(),
                intensity = intensity,
                sourceFormat = sourceFormat
            };

            if (string.Equals(tagType, "action", StringComparison.Ordinal))
            {
                actions.Add(trigger);
            }
            else
            {
                expressions.Add(trigger);
            }
        }

        public readonly struct TriggerParseResult
        {
            public TriggerParseResult(List<ResponseTriggerTag> expressions, List<ResponseTriggerTag> actions, string cleanDialogue)
            {
                Expressions = expressions ?? new List<ResponseTriggerTag>();
                Actions = actions ?? new List<ResponseTriggerTag>();
                CleanDialogue = cleanDialogue ?? string.Empty;
            }

            public List<ResponseTriggerTag> Expressions { get; }
            public List<ResponseTriggerTag> Actions { get; }
            public string CleanDialogue { get; }
        }
    }
}
