// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Text;
using Nyxara.AICompanion.Data;
using UnityEngine;

namespace Nyxara.AICompanion.Runtime
{
    public class RecentMemoryController : MonoBehaviour
    {
        [SerializeField] private int maxMemoryEntries = 3;
        [SerializeField] private bool enableStructuredEventMemory = true;
        [SerializeField] private int maxSavedEventEntries = 128;
        [SerializeField] private int maxRelationshipEntries = 64;
        [SerializeField] private bool logMemoryDecisions;

        private readonly Queue<MemoryEntry> _memories = new();
        private readonly MemoryFilterService _memoryFilter = new();
        private JsonMemoryEventStore _memoryStore;
        private string _sessionId;
        private string _storeCharacterId;

        [System.Serializable]
        public class MemoryEntry
        {
            public string content;
            public float importance;
            public float timestamp;

            public MemoryEntry(string content, float importance)
            {
                this.content = content;
                this.importance = importance;
                timestamp = Time.time;
            }
        }

        private void Awake()
        {
            _sessionId = Guid.NewGuid().ToString("N");
        }

        public void AddMemory(string content, float importance = 0.5f)
        {
            var entry = new MemoryEntry(content, importance);
            _memories.Enqueue(entry);

            while (_memories.Count > maxMemoryEntries)
            {
                _memories.Dequeue();
            }
        }

        public void AddPlayerMessage(string message)
        {
            AddMemory($"Player said: {message}", 0.7f);
        }

        public void AddNPCResponse(string response, string intent)
        {
            AddMemory($"I responded ({intent}): {response}", 0.6f);
        }

        public void AddEvent(string eventDescription, float importance = 0.8f)
        {
            AddMemory($"Event: {eventDescription}", importance);
        }

        public string GetMemoryString()
        {
            if (_memories.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            var index = 1;
            foreach (var memory in _memories)
            {
                sb.AppendLine($"{index}. {memory.content}");
                index++;
            }

            return sb.ToString();
        }

        public int GetMemoryCount()
        {
            return _memories.Count;
        }

        public int GetSavedEventCount()
        {
            return _memoryStore != null ? _memoryStore.GetEventMemories().Count : 0;
        }

        public int GetSavedRelationshipEventCount()
        {
            return _memoryStore != null ? _memoryStore.GetRelationshipMemories().Count : 0;
        }

        public string GetMemoryStorageSummary()
        {
            return $"Working={GetMemoryCount()}, Event={GetSavedEventCount()}, Relationship={GetSavedRelationshipEventCount()}, Session={_sessionId}";
        }

        public string GetEventsFilePath(string characterId)
        {
            EnsureStoreInitialized(characterId);
            return _memoryStore?.EventsPath ?? string.Empty;
        }

        public string GetRelationshipFilePath(string characterId)
        {
            EnsureStoreInitialized(characterId);
            return _memoryStore?.RelationshipPath ?? string.Empty;
        }

        public string BuildSavedEventPreview(string characterId, int maxEntries = 6)
        {
            EnsureStoreInitialized(characterId);
            return BuildDetailedEventPreview(_memoryStore?.GetEventMemories(), maxEntries, "No saved event memories yet.");
        }

        public string BuildRelationshipPreview(string characterId, int maxEntries = 4)
        {
            EnsureStoreInitialized(characterId);
            return BuildDetailedRelationshipPreview(_memoryStore?.GetRelationshipMemories(), maxEntries, "No saved relationship memories yet.");
        }

        public string BuildPromptMemoryBlock(string characterId, bool includeSavedMemories, bool diagnosticMode)
        {
            var sections = new List<string>();
            var workingMemory = GetMemoryString().Trim();
            if (!string.IsNullOrWhiteSpace(workingMemory))
            {
                sections.Add($"Working Memory:\n{workingMemory}");
            }

            if (!enableStructuredEventMemory || !includeSavedMemories)
            {
                return string.Join("\n\n", sections);
            }

            EnsureStoreInitialized(characterId);
            if (_memoryStore == null)
            {
                return string.Join("\n\n", sections);
            }

            var savedEventSummary = BuildSavedEventSummary(_memoryStore.GetEventMemories(), diagnosticMode);
            if (!string.IsNullOrWhiteSpace(savedEventSummary))
            {
                sections.Add(savedEventSummary);
            }

            var relationshipSummary = BuildRelationshipSummary(_memoryStore.GetRelationshipMemories(), diagnosticMode);
            if (!string.IsNullOrWhiteSpace(relationshipSummary))
            {
                sections.Add(relationshipSummary);
            }

            return string.Join("\n\n", sections);
        }

        public string BuildMemoryStatusReport(string characterId)
        {
            EnsureStoreInitialized(characterId);
            var report = new StringBuilder();
            report.AppendLine($"Working memory entries: {GetMemoryCount()}");
            report.AppendLine($"Saved event memories: {GetSavedEventCount()}");
            report.AppendLine($"Saved relationship memories: {GetSavedRelationshipEventCount()}");
            report.AppendLine($"Storage summary: {GetMemoryStorageSummary()}");

            if (_memoryStore != null)
            {
                var lastEvent = GetLatestRecord(_memoryStore.GetEventMemories());
                var lastRelationship = GetLatestRecord(_memoryStore.GetRelationshipMemories());
                report.AppendLine($"Last saved event: {FormatRecordForStatus(lastEvent)}");
                report.AppendLine($"Last relationship memory: {FormatRecordForStatus(lastRelationship)}");
            }

            return report.ToString().TrimEnd();
        }

        public void RecordConversationEvent(
            string characterId,
            string playerInput,
            string rawNpcReply,
            string parsedVisibleReply,
            string topic,
            string mood,
            string intent,
            List<ResponseTriggerTag> expressionTriggers,
            MemoryStateSnapshot beforeState,
            MemoryStateSnapshot afterState)
        {
            if (!enableStructuredEventMemory)
            {
                return;
            }

            EnsureStoreInitialized(characterId);

            var candidate = new MemoryEventRecord
            {
                eventType = "conversation",
                playerInput = playerInput ?? string.Empty,
                npcReply = parsedVisibleReply ?? string.Empty,
                rawNpcReply = rawNpcReply ?? string.Empty,
                parsedVisibleReply = parsedVisibleReply ?? string.Empty,
                topic = topic ?? string.Empty,
                mood = string.IsNullOrWhiteSpace(mood) ? "calm" : mood,
                intent = string.IsNullOrWhiteSpace(intent) ? "neutral" : intent,
                sourceSessionId = _sessionId,
                sourceCharacterId = string.IsNullOrWhiteSpace(characterId) ? "Nyxara" : characterId,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                beforeState = beforeState ?? new MemoryStateSnapshot(),
                afterState = afterState ?? new MemoryStateSnapshot()
            };

            candidate.relationshipEffect = MemoryRelationshipDelta.FromSnapshots(candidate.beforeState, candidate.afterState);

            if (expressionTriggers != null)
            {
                foreach (var trigger in expressionTriggers)
                {
                    if (trigger != null && !string.IsNullOrWhiteSpace(trigger.key))
                    {
                        candidate.detectedExpressionTags.Add(trigger.ToString());
                    }
                }
            }

            candidate.importance = _memoryFilter.EstimateImportance(candidate);
            var filter = _memoryFilter.Evaluate(candidate, _memoryStore.GetEventMemories(), _memoryStore.GetRelationshipMemories());
            candidate.normalizedKey = filter.NormalizedKey;
            candidate.filterDecisionReason = filter.Reason;

            if (logMemoryDecisions)
            {
                Debug.Log($"[Nyxara Memory] Decision={filter.Decision} | Reason={filter.Reason} | Topic={candidate.topic} | Intent={candidate.intent}");
            }

            _memoryStore.Save(candidate, filter.Decision);
        }

        public void Clear()
        {
            _memories.Clear();
        }

        public void ResetSessionMemory()
        {
            _memories.Clear();
            _sessionId = Guid.NewGuid().ToString("N");
        }

        public void ResetAllMemory(string characterId)
        {
            EnsureStoreInitialized(characterId);
            _memories.Clear();
            _memoryStore?.ClearAll();
            _sessionId = Guid.NewGuid().ToString("N");
        }

        public void ResetRelationshipMemory(string characterId)
        {
            EnsureStoreInitialized(characterId);
            _memoryStore?.ClearRelationships();
        }

        public void ResetSavedEventMemory(string characterId)
        {
            EnsureStoreInitialized(characterId);
            _memoryStore?.ClearEvents();
        }

        private void EnsureStoreInitialized(string characterId)
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
            {
                _sessionId = Guid.NewGuid().ToString("N");
            }

            var resolvedCharacterId = string.IsNullOrWhiteSpace(characterId) ? "Nyxara" : characterId.Trim();
            if (_memoryStore != null && string.Equals(_storeCharacterId, resolvedCharacterId, StringComparison.Ordinal))
            {
                return;
            }

            _storeCharacterId = resolvedCharacterId;
            _memoryStore = new JsonMemoryEventStore(_storeCharacterId, maxSavedEventEntries, maxRelationshipEntries);
        }

        private static string BuildSavedEventSummary(IReadOnlyList<MemoryEventRecord> records, bool diagnosticMode)
        {
            if (records == null || records.Count == 0)
            {
                return diagnosticMode ? "Saved Event Memory:\n- none saved yet" : string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Saved Event Memory:");
            var count = 0;
            for (var i = records.Count - 1; i >= 0 && count < 3; i--)
            {
                var record = records[i];
                if (record == null)
                {
                    continue;
                }

                builder.AppendLine($"- Topic: {Fallback(record.topic, "unknown")} | Player: {Fallback(record.playerInput, "n/a")} | Reply: {Fallback(record.parsedVisibleReply, "n/a")} | Importance: {record.importance:0.00}");
                count++;
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildDetailedEventPreview(IReadOnlyList<MemoryEventRecord> records, int maxEntries, string emptyMessage)
        {
            if (records == null || records.Count == 0)
            {
                return emptyMessage;
            }

            var builder = new StringBuilder();
            var count = 0;
            for (var i = records.Count - 1; i >= 0 && count < Mathf.Max(1, maxEntries); i--)
            {
                var record = records[i];
                if (record == null)
                {
                    continue;
                }

                count++;
                builder.AppendLine($"{count}. Topic: {Fallback(record.topic, "unknown")}");
                builder.AppendLine($"   Player: {Fallback(record.playerInput, "n/a")}");
                builder.AppendLine($"   Reply: {Fallback(record.parsedVisibleReply, "n/a")}");
                builder.AppendLine($"   Importance: {record.importance:0.00} | Duplicates: {record.duplicateCount} | Time: {Fallback(record.timestampUtc, "n/a")}");
                builder.AppendLine($"   Decision: {Fallback(record.filterDecisionReason, "n/a")}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildRelationshipSummary(IReadOnlyList<MemoryEventRecord> records, bool diagnosticMode)
        {
            if (records == null || records.Count == 0)
            {
                return diagnosticMode ? "Relationship Memory:\n- no meaningful relationship changes saved yet" : string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Relationship Memory:");
            var count = 0;
            for (var i = records.Count - 1; i >= 0 && count < 2; i--)
            {
                var record = records[i];
                if (record == null || record.relationshipEffect == null)
                {
                    continue;
                }

                builder.AppendLine(
                    $"- Topic: {Fallback(record.topic, "unknown")} | Trust {record.relationshipEffect.trustDelta:+0.00;-0.00;0.00}, " +
                    $"Affection {record.relationshipEffect.affectionDelta:+0.00;-0.00;0.00}, Respect {record.relationshipEffect.respectDelta:+0.00;-0.00;0.00}, " +
                    $"Suspicion {record.relationshipEffect.suspicionDelta:+0.00;-0.00;0.00}, Familiarity {record.relationshipEffect.familiarityDelta:+0.00;-0.00;0.00}");
                count++;
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildDetailedRelationshipPreview(IReadOnlyList<MemoryEventRecord> records, int maxEntries, string emptyMessage)
        {
            if (records == null || records.Count == 0)
            {
                return emptyMessage;
            }

            var builder = new StringBuilder();
            var count = 0;
            for (var i = records.Count - 1; i >= 0 && count < Mathf.Max(1, maxEntries); i--)
            {
                var record = records[i];
                if (record == null || record.relationshipEffect == null)
                {
                    continue;
                }

                count++;
                builder.AppendLine($"{count}. Topic: {Fallback(record.topic, "unknown")}");
                builder.AppendLine($"   Player: {Fallback(record.playerInput, "n/a")}");
                builder.AppendLine(
                    $"   Delta: Trust {record.relationshipEffect.trustDelta:+0.00;-0.00;0.00}, " +
                    $"Affection {record.relationshipEffect.affectionDelta:+0.00;-0.00;0.00}, " +
                    $"Respect {record.relationshipEffect.respectDelta:+0.00;-0.00;0.00}, " +
                    $"Suspicion {record.relationshipEffect.suspicionDelta:+0.00;-0.00;0.00}, " +
                    $"Familiarity {record.relationshipEffect.familiarityDelta:+0.00;-0.00;0.00}");
                builder.AppendLine($"   Time: {Fallback(record.timestampUtc, "n/a")} | Duplicates: {record.duplicateCount}");
            }

            return builder.ToString().TrimEnd();
        }

        private static MemoryEventRecord GetLatestRecord(IReadOnlyList<MemoryEventRecord> records)
        {
            if (records == null || records.Count == 0)
            {
                return null;
            }

            for (var i = records.Count - 1; i >= 0; i--)
            {
                if (records[i] != null)
                {
                    return records[i];
                }
            }

            return null;
        }

        private static string FormatRecordForStatus(MemoryEventRecord record)
        {
            if (record == null)
            {
                return "none";
            }

            return $"topic={Fallback(record.topic, "unknown")}, player='{Fallback(record.playerInput, "n/a")}', importance={record.importance:0.00}, duplicates={record.duplicateCount}";
        }

        private static string Fallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
