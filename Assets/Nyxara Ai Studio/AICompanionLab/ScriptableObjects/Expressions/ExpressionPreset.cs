// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nyxara.AICompanion.Expressions
{
    [CreateAssetMenu(fileName = "NewExpressionPreset", menuName = "AI Companion/Expression Preset")]
    public class ExpressionPreset : ScriptableObject
    {
        [Header("Identity")]
        public string presetId;
        public string displayName;
        public string description;

        [Header("Expression Category")]
        public ExpressionCategory category = ExpressionCategory.Emotion;

        [Header("Blendshape Weights")]
        public List<BlendshapeWeight> blendshapeWeights = new();

        [Header("Animation Settings")]
        public float transitionTimeInSeconds = 0.15f;
        public float holdDuration = 1.5f;
        public bool autoReturnToNeutral;

        [Header("Thumbnail")]
        public Texture2D thumbnail;

        public void ApplyToSkinnedMeshRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return;
            }

            foreach (var weight in blendshapeWeights)
            {
                var index = renderer.sharedMesh.GetBlendShapeIndex(weight.blendshapeName);
                if (index >= 0)
                {
                    renderer.SetBlendShapeWeight(index, weight.weight);
                }
            }
        }

        public void ApplyToSkinnedMeshRenderers(IEnumerable<SkinnedMeshRenderer> renderers)
        {
            if (renderers == null)
            {
                return;
            }

            foreach (var renderer in renderers.Where(r => r != null))
            {
                ApplyToSkinnedMeshRenderer(renderer);
            }
        }

        public void ResetToNeutral(SkinnedMeshRenderer renderer, List<string> allBlendshapes)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return;
            }

            foreach (var blendshapeName in allBlendshapes)
            {
                var index = renderer.sharedMesh.GetBlendShapeIndex(blendshapeName);
                if (index >= 0)
                {
                    renderer.SetBlendShapeWeight(index, 0f);
                }
            }
        }

        public void ResetToNeutral(IEnumerable<SkinnedMeshRenderer> renderers, List<string> allBlendshapes)
        {
            if (renderers == null || allBlendshapes == null)
            {
                return;
            }

            foreach (var renderer in renderers.Where(r => r != null))
            {
                ResetToNeutral(renderer, allBlendshapes);
            }
        }

        public Dictionary<string, float> ToDictionary()
        {
            var dict = new Dictionary<string, float>();
            foreach (var weight in blendshapeWeights)
            {
                dict[weight.blendshapeName] = weight.weight;
            }

            return dict;
        }
    }

    [Serializable]
    public class BlendshapeWeight
    {
        public string blendshapeName;
        [Range(0f, 100f)] public float weight;
    }

    public enum ExpressionCategory
    {
        Emotion,
        Signal,
        Mood,
        Phoneme,
        Custom
    }
}
