// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nyxara.AICompanion.Runtime
{
    public class JsonMemoryEventStore
    {
        [Serializable]
        private class MemoryEventCollection
        {
            public List<MemoryEventRecord> records = new();
        }

        private readonly string _eventsPath;
        private readonly string _relationshipPath;
        private readonly int _maxSavedEventEntries;
        private readonly int _maxRelationshipEntries;
        private readonly MemoryEventCollection _eventCache;
        private readonly MemoryEventCollection _relationshipCache;

        public JsonMemoryEventStore(string characterId, int maxSavedEventEntries, int maxRelationshipEntries)
        {
            var safeCharacterId = SanitizeFileName(characterId);
            var root = Path.Combine(Application.persistentDataPath, "NyxaraMemory");
            Directory.CreateDirectory(root);

            _eventsPath = Path.Combine(root, $"{safeCharacterId}_events.json");
            _relationshipPath = Path.Combine(root, $"{safeCharacterId}_relationships.json");
            _maxSavedEventEntries = Mathf.Max(16, maxSavedEventEntries);
            _maxRelationshipEntries = Mathf.Max(8, maxRelationshipEntries);
            _eventCache = LoadCollection(_eventsPath);
            _relationshipCache = LoadCollection(_relationshipPath);
        }

        public IReadOnlyList<MemoryEventRecord> GetEventMemories()
        {
            return _eventCache.records;
        }

        public IReadOnlyList<MemoryEventRecord> GetRelationshipMemories()
        {
            return _relationshipCache.records;
        }

        public string EventsPath => _eventsPath;
        public string RelationshipPath => _relationshipPath;

        public void Save(MemoryEventRecord record, MemorySaveDecision decision)
        {
            if (record == null || decision == MemorySaveDecision.Discard || decision == MemorySaveDecision.WorkingOnly)
            {
                return;
            }

            if (decision == MemorySaveDecision.SaveRelationship)
            {
                record.isRelationshipMemory = true;
                AppendOrMerge(_relationshipCache.records, record);
                PruneAndConsolidate(_relationshipCache.records, _maxRelationshipEntries);
                WriteCollection(_relationshipPath, _relationshipCache);
                return;
            }

            if (decision == MemorySaveDecision.MergeExisting)
            {
                if (MergeIntoExistingCollection(_relationshipCache.records, record))
                {
                    PruneAndConsolidate(_relationshipCache.records, _maxRelationshipEntries);
                    WriteCollection(_relationshipPath, _relationshipCache);
                    return;
                }

                AppendOrMerge(_eventCache.records, record);
                PruneAndConsolidate(_eventCache.records, _maxSavedEventEntries);
                WriteCollection(_eventsPath, _eventCache);
                return;
            }

            AppendOrMerge(_eventCache.records, record);
            PruneAndConsolidate(_eventCache.records, _maxSavedEventEntries);
            WriteCollection(_eventsPath, _eventCache);
        }

        public void ClearAll()
        {
            _eventCache.records.Clear();
            _relationshipCache.records.Clear();
            DeleteIfExists(_eventsPath);
            DeleteIfExists(_relationshipPath);
        }

        public void ClearEvents()
        {
            _eventCache.records.Clear();
            DeleteIfExists(_eventsPath);
        }

        public void ClearRelationships()
        {
            _relationshipCache.records.Clear();
            DeleteIfExists(_relationshipPath);
        }

        private static MemoryEventCollection LoadCollection(string path)
        {
            if (!File.Exists(path))
            {
                return new MemoryEventCollection();
            }

            try
            {
                var json = File.ReadAllText(path);
                var collection = JsonUtility.FromJson<MemoryEventCollection>(json);
                return collection ?? new MemoryEventCollection();
            }
            catch
            {
                return new MemoryEventCollection();
            }
        }

        private static void WriteCollection(string path, MemoryEventCollection collection)
        {
            var json = JsonUtility.ToJson(collection, true);
            File.WriteAllText(path, json);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void AppendOrMerge(List<MemoryEventRecord> records, MemoryEventRecord record)
        {
            if (records == null || record == null)
            {
                return;
            }

            for (var i = records.Count - 1; i >= 0; i--)
            {
                var existing = records[i];
                if (existing == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(record.normalizedKey) &&
                    string.Equals(existing.normalizedKey, record.normalizedKey, StringComparison.Ordinal))
                {
                    existing.duplicateCount = Mathf.Max(1, existing.duplicateCount) + 1;
                    existing.timestampUtc = record.timestampUtc;
                    existing.importance = Mathf.Max(existing.importance, record.importance);
                    existing.filterDecisionReason = record.filterDecisionReason;
                    existing.consolidated = true;
                    if (record.relationshipEffect != null && record.relationshipEffect.HasMeaningfulChange())
                    {
                        existing.relationshipEffect = record.relationshipEffect;
                    }
                    return;
                }
            }

            records.Add(record);
        }

        private static bool MergeIntoExistingCollection(List<MemoryEventRecord> records, MemoryEventRecord record)
        {
            if (records == null || record == null)
            {
                return false;
            }

            for (var i = records.Count - 1; i >= 0; i--)
            {
                var existing = records[i];
                if (existing == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(record.normalizedKey) &&
                    string.Equals(existing.normalizedKey, record.normalizedKey, StringComparison.Ordinal))
                {
                    existing.duplicateCount = Mathf.Max(1, existing.duplicateCount) + 1;
                    existing.timestampUtc = record.timestampUtc;
                    existing.importance = Mathf.Max(existing.importance, record.importance);
                    existing.filterDecisionReason = record.filterDecisionReason;
                    existing.consolidated = true;
                    if (record.relationshipEffect != null && record.relationshipEffect.HasMeaningfulChange())
                    {
                        existing.relationshipEffect = record.relationshipEffect;
                    }

                    return true;
                }
            }

            return false;
        }

        private static void PruneAndConsolidate(List<MemoryEventRecord> records, int maxCount)
        {
            if (records == null)
            {
                return;
            }

            records.RemoveAll(record =>
                record == null ||
                (record.duplicateCount > 3 && record.importance < 0.55f) ||
                (record.importance < 0.45f && record.duplicateCount > 1));

            while (records.Count > maxCount)
            {
                records.RemoveAt(0);
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Nyxara";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Trim();
        }
    }
}
