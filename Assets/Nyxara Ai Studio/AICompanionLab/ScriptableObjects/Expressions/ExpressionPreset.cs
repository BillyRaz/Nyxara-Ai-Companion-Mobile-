// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using Nyxara.AICompanion.Face;
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

        [Header("Channel Layering")]
        [Tooltip("When used as a base expression, do not write eye blink or eye look shapes so blinking and gaze stay independent. Built-in emotions enable this; legacy presets keep their previous behaviour.")]
        public bool preserveIndependentChannels;

        [Header("Thumbnail")]
        public Texture2D thumbnail;

        [NonSerialized] public bool isBuiltIn;

        public void ApplyToSkinnedMeshRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                return;
            }

            foreach (var weight in blendshapeWeights)
            {
                var index = ArkItBlendshapeDriver.FindBlendshapeIndex(renderer.sharedMesh, weight.blendshapeName);
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
                var index = ArkItBlendshapeDriver.FindBlendshapeIndex(renderer.sharedMesh, blendshapeName);
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
        // Values 0-4 are the original serialized categories and must not be renumbered.
        Emotion = 0,       // Core emotions
        Signal = 1,
        Mood = 2,
        Phoneme = 3,
        Custom = 4,

        // Nyxara Expression Library categories.
        Personality = 5,
        EyeAction = 6,
        MouthAction = 7,
        Speech = 8
    }

    public static class ExpressionCategoryUtility
    {
        public static string GetDisplayName(ExpressionCategory category)
        {
            return category switch
            {
                ExpressionCategory.Emotion => "Core Emotions",
                ExpressionCategory.Personality => "Personality",
                ExpressionCategory.EyeAction => "Eye Actions",
                ExpressionCategory.MouthAction => "Mouth & Jaw",
                ExpressionCategory.Speech => "Speech & Visemes",
                ExpressionCategory.Signal => "Signals (Legacy)",
                ExpressionCategory.Mood => "Moods (Legacy)",
                ExpressionCategory.Phoneme => "Phonemes (Legacy)",
                _ => "Custom"
            };
        }

        public static int GetSortOrder(ExpressionCategory category)
        {
            return category switch
            {
                ExpressionCategory.Emotion => 0,
                ExpressionCategory.Personality => 1,
                ExpressionCategory.EyeAction => 2,
                ExpressionCategory.MouthAction => 3,
                ExpressionCategory.Speech => 4,
                ExpressionCategory.Signal => 5,
                ExpressionCategory.Mood => 6,
                ExpressionCategory.Phoneme => 7,
                _ => 8
            };
        }

        public static readonly ExpressionCategory[] LibraryOrder =
        {
            ExpressionCategory.Emotion,
            ExpressionCategory.Personality,
            ExpressionCategory.EyeAction,
            ExpressionCategory.MouthAction,
            ExpressionCategory.Speech
        };
    }
}
