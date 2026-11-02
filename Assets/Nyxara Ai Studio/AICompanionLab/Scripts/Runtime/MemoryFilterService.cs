// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Nyxara.AICompanion.Runtime
{
    public enum MemorySaveDecision
    {
        Discard,
        WorkingOnly,
        SaveEvent,
        SaveRelationship,
        MergeExisting
    }

    public readonly struct MemoryFilterResult
    {
        public MemoryFilterResult(MemorySaveDecision decision, string reason, string normalizedKey)
        {
            Decision = decision;
            Reason = reason ?? string.Empty;
            NormalizedKey = normalizedKey ?? string.Empty;
        }

        public MemorySaveDecision Decision { get; }
        public string Reason { get; }
        public string NormalizedKey { get; }
    }

    public class MemoryFilterService
    {
        private static readonly Regex NonAlphaNumericRegex = new(@"[^a-z0-9\s]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly string[] GreetingNoise =
        {
            "hello",
            "hi",
            "hey",
            "testing",
            "test",
            "can you hear me",
            "say anything",
            "check anything"
        };

        public MemoryFilterResult Evaluate(
            MemoryEventRecord candidate,
            IReadOnlyList<MemoryEventRecord> eventMemories,
            IReadOnlyList<MemoryEventRecord> relationshipMemories)
        {
            if (candidate == null)
            {
                return new MemoryFilterResult(MemorySaveDecision.Discard, "No memory candidate was provided.", string.Empty);
            }

            var normalizedKey = NormalizeKey(candidate.playerInput, candidate.parsedVisibleReply);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return new MemoryFilterResult(MemorySaveDecision.Discard, "Candidate was empty after normalization.", normalizedKey);
            }

            if (LooksLikeNoise(candidate.playerInput))
            {
                return new MemoryFilterResult(MemorySaveDecision.WorkingOnly, "Low-value greeting/test chatter should stay in working memory only.", normalizedKey);
            }

            if (IsDuplicate(normalizedKey, eventMemories) || IsDuplicate(normalizedKey, relationshipMemories))
            {
                return new MemoryFilterResult(MemorySaveDecision.MergeExisting, "Duplicate memory candidate matched an existing saved record.", normalizedKey);
            }

            if (candidate.relationshipEffect != null && candidate.relationshipEffect.HasMeaningfulChange(0.12f))
            {
                return new MemoryFilterResult(MemorySaveDecision.SaveRelationship, "Meaningful relationship change detected.", normalizedKey);
            }

            if (candidate.importance >= 0.6f)
            {
                return new MemoryFilterResult(MemorySaveDecision.SaveEvent, "Candidate crossed the event-memory importance threshold.", normalizedKey);
            }

            return new MemoryFilterResult(MemorySaveDecision.WorkingOnly, "Candidate was useful for the current conversation but not important enough for long-term storage.", normalizedKey);
        }

        public float EstimateImportance(MemoryEventRecord candidate)
        {
            if (candidate == null)
            {
                return 0f;
            }

            var score = 0.35f;
            if (!string.IsNullOrWhiteSpace(candidate.topic))
            {
                score += 0.1f;
            }

            if (!string.IsNullOrWhiteSpace(candidate.intent) &&
                !string.Equals(candidate.intent, "neutral", StringComparison.OrdinalIgnoreCase))
            {
                score += 0.12f;
            }

            if (candidate.detectedExpressionTags != null && candidate.detectedExpressionTags.Count > 0)
            {
                score += 0.08f;
            }

            if (!string.IsNullOrWhiteSpace(candidate.playerInput) && candidate.playerInput.Trim().Length >= 24)
            {
                score += 0.08f;
            }

            if (candidate.relationshipEffect != null && candidate.relationshipEffect.HasMeaningfulChange())
            {
                score += 0.35f;
            }

            return Mathf.Clamp01(score);
        }

        private static bool IsDuplicate(string normalizedKey, IReadOnlyList<MemoryEventRecord> records)
        {
            if (records == null || records.Count == 0 || string.IsNullOrWhiteSpace(normalizedKey))
            {
                return false;
            }

            for (var i = records.Count - 1; i >= 0; i--)
            {
                var record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (string.Equals(record.normalizedKey, normalizedKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeNoise(string playerInput)
        {
            if (string.IsNullOrWhiteSpace(playerInput))
            {
                return true;
            }

            var normalized = NormalizeText(playerInput);
            foreach (var value in GreetingNoise)
            {
                if (normalized == value || normalized.StartsWith(value + " ", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return normalized.Length <= 4;
        }

        private static string NormalizeKey(string playerInput, string npcReply)
        {
            var normalizedPlayer = NormalizeText(playerInput);
            var normalizedReply = NormalizeText(npcReply);
            return $"{normalizedPlayer}|{normalizedReply}".Trim('|');
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Trim().ToLowerInvariant());
            var compact = NonAlphaNumericRegex.Replace(builder.ToString(), " ");
            compact = MultiSpaceRegex.Replace(compact, " ").Trim();
            return compact;
        }
    }
}
