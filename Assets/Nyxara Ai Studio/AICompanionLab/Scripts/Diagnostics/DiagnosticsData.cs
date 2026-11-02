// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
namespace Nyxara.AICompanion.Diagnostics
{
    [Serializable]
    public class SystemDiagnosticsReport
    {
        public string timestamp;
        public float durationMs;
        public ComponentStatus llmStatus = new();
        public ComponentStatus sttStatus = new();
        public ComponentStatus ttsStatus = new();
        public ComponentStatus faceStatus = new();
        public ComponentStatus lipSyncStatus = new();
        public ComponentStatus expressionStatus = new();
        public PerformanceMetrics performance = new();
        public List<ConfigIssue> configIssues = new();
        public List<PathValidation> pathValidations = new();
        public RuntimeSnapshot runtimeSnapshot = new();
        public List<DiagnosticFinding> findings = new();

        public bool IsHealthy =>
            (!llmStatus.affectsOverallHealth || llmStatus.isOperational) &&
            (!sttStatus.affectsOverallHealth || sttStatus.isOperational) &&
            (!ttsStatus.affectsOverallHealth || ttsStatus.isOperational) &&
            configIssues.TrueForAll(issue => issue.severity != IssueSeverity.Critical);
    }

    [Serializable]
    public class ComponentStatus
    {
        public string name;
        public bool isPresent;
        public bool isOperational;
        public string statusMessage;
        public float lastResponseTimeMs;
        public int errorCount;
        public string lastError;
        public bool affectsOverallHealth = true;
        public string stateLabel;
        public string recommendedAction;
        public List<string> details = new();
    }

    [Serializable]
    public class PerformanceMetrics
    {
        public float averageLLMLatencyMs;
        public float averageSTTLatencyMs;
        public float averageTTSLatencyMs;
        public long memoryUsageMB;
        public float cpuUsagePercent;
        public int activeThreads;
        public int queueLength;
    }

    [Serializable]
    public class ConfigIssue
    {
        public IssueSeverity severity;
        public string component;
        public string issue;
        public string suggestion;
    }

    [Serializable]
    public class PathValidation
    {
        public string name;
        public string path;
        public bool exists;
        public long fileSizeMB;
        public string lastModified;
    }

    [Serializable]
    public class RuntimeSnapshot
    {
        public string currentMood;
        public float trust;
        public float affection;
        public float suspicion;
        public string currentTask;
        public bool isSpeaking;
        public bool isThinking;
        public string lastIntent;
        public string lastAction;
        public string lastSignal;
        public string lastDialogue;
        public float timeSinceLastResponse;
        public int memoryCount;
        public int savedEventMemoryCount;
        public int savedRelationshipMemoryCount;
        public string memoryStorageSummary;
    }

    [Serializable]
    public class DiagnosticFinding
    {
        public DiagnosticSeverity severity;
        public string title;
        public string message;
    }

    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum IssueSeverity
    {
        Critical,
        Warning,
        Info
    }
}
