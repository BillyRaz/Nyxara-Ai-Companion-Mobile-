// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using Nyxara.AICompanion.Expressions;
using UnityEngine;

namespace Nyxara.AICompanion.Face
{
    public class ExpressionSignalRouter : MonoBehaviour
    {
        [System.Serializable]
        public class SignalMapping
        {
            public string signalName;
            public string blendshapeName;
            [Range(0f, 100f)]
            public float weight = 50f;
            public float transitionSpeed = 0.1f;
        }

        [Header("Mood to Base Expression")]
        public string moodToBaseBlendshape = "mouthSmile";
        public AnimationCurve moodToWeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 100f);

        [Header("Signal Mappings")]
        public List<SignalMapping> signalMappings = new();

        [Header("Mood Blendshape Mapping")]
        public List<MoodBlendshapeMapping> moodMappings = new();

        [Header("Runtime")]
        [SerializeField] private SkinnedMeshRenderer targetRenderer;
        [SerializeField] private bool expressionModeActive;

        private Dictionary<string, SignalMapping> _signalMap;
        private Dictionary<string, MoodBlendshapeMapping> _moodMap;
        private ArkItBlendshapeDriver _driver;
        private string _currentSignal = "none";
        private string _currentMood = "calm";
        private Dictionary<string, float> _originalWeights = new();

        [Serializable]
        public class MoodBlendshapeMapping
        {
            public string moodName;
            public string blendshapeName;
            [Range(0f, 100f)] public float weight = 30f;
        }

        private void Awake()
        {
            _driver = GetComponent<ArkItBlendshapeDriver>();
            if (_driver == null)
            {
                _driver = FindFirstObjectByType<ArkItBlendshapeDriver>();
            }

            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SkinnedMeshRenderer>();
            }

            BuildSignalMap();
            BuildMoodMap();
            SaveOriginalWeights();
        }

        private void BuildSignalMap()
        {
            _signalMap = new Dictionary<string, SignalMapping>();
            foreach (var mapping in signalMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.signalName) && !_signalMap.ContainsKey(mapping.signalName))
                {
                    _signalMap[mapping.signalName] = mapping;
                }
            }
        }

        private void BuildMoodMap()
        {
            _moodMap = new Dictionary<string, MoodBlendshapeMapping>();
            foreach (var mapping in moodMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.moodName) && !_moodMap.ContainsKey(mapping.moodName))
                {
                    _moodMap[mapping.moodName] = mapping;
                }
            }
        }

        private void SaveOriginalWeights()
        {
            if (targetRenderer == null || targetRenderer.sharedMesh == null)
            {
                return;
            }

            _originalWeights.Clear();
            var mesh = targetRenderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                _originalWeights[mesh.GetBlendShapeName(i)] = targetRenderer.GetBlendShapeWeight(i);
            }
        }

        public void ApplySignal(string signal, string mood = null)
        {
            if (!string.IsNullOrEmpty(mood))
            {
                _currentMood = mood;
            }

            _currentSignal = string.IsNullOrWhiteSpace(signal) ? "none" : signal;

            if (_signalMap.TryGetValue(_currentSignal, out var mapping))
            {
                SetBlendshapeWeight(mapping.blendshapeName, mapping.weight);
            }

            ApplyMoodInfluence();
        }

        private void ApplyMoodInfluence()
        {
            if (_moodMap.TryGetValue(_currentMood, out var moodMapping))
            {
                SetBlendshapeWeight(moodMapping.blendshapeName, moodMapping.weight);
            }

            if (!string.IsNullOrEmpty(moodToBaseBlendshape))
            {
                SetBlendshapeWeight(moodToBaseBlendshape, GetMoodWeight(_currentMood));
            }
        }

        private float GetMoodWeight(string mood)
        {
            var normalized = mood switch
            {
                "playful" => 0.6f,
                "warm" => 0.8f,
                "guarded" => 0.2f,
                "tense" => 0.1f,
                "confident" => 0.7f,
                "curious" => 0.5f,
                _ => 0.4f
            };

            return moodToWeightCurve != null ? moodToWeightCurve.Evaluate(normalized) : normalized * 100f;
        }

        private void SetBlendshapeWeight(string blendshapeName, float weight)
        {
            if (!expressionModeActive && ExpressionBuilderHelper.IsMouthRelatedBlendshape(blendshapeName))
            {
                return;
            }

            if (_driver != null)
            {
                _driver.SetBlendshapeWeight(blendshapeName, weight);
                return;
            }

            if (targetRenderer != null && targetRenderer.sharedMesh != null)
            {
                var index = targetRenderer.sharedMesh.GetBlendShapeIndex(blendshapeName);
                if (index >= 0)
                {
                    targetRenderer.SetBlendShapeWeight(index, weight);
                }
            }
        }

        public void ClearSignal()
        {
            if (_signalMap.TryGetValue(_currentSignal, out var mapping))
            {
                SetBlendshapeWeight(mapping.blendshapeName, 0f);
            }

            _currentSignal = "none";
        }

        public void AddSignalMapping(string signalName, string blendshapeName, float weight)
        {
            signalMappings.Add(new SignalMapping
            {
                signalName = signalName,
                blendshapeName = blendshapeName,
                weight = weight
            });
            BuildSignalMap();
        }

        public void SetExpressionMode(bool active)
        {
            expressionModeActive = active;
        }

#if UNITY_EDITOR
        public void RefreshMappings()
        {
            BuildSignalMap();
            BuildMoodMap();
        }
#endif
    }
}
