// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nyxara.AICompanion.Expressions
{
    /// <summary>
    /// Code-defined Nyxara expression library.
    ///
    /// Every shape name below was verified against the imported face rig
    /// (SK_MechanicGirl_03.fbx). The primary head mesh exposes all 52 ARKit-style
    /// shapes used here, so the catalog never references an inventented control.
    /// Persisted <see cref="ExpressionPreset"/> assets remain authoritative: this
    /// catalog only fills presets that are missing from the project.
    /// </summary>
    public static class NyxaraExpressionCatalog
    {
        public sealed class Definition
        {
            public string id;
            public string displayName;
            public string description;
            public ExpressionCategory category;
            public bool preserveIndependentChannels;
            public float transitionTimeInSeconds;
            public float holdDuration;
            public IReadOnlyList<BlendshapeWeight> weights;
        }

        private static readonly List<Definition> Definitions = BuildDefinitions();
        private static readonly List<ExpressionPreset> CachedPresets = new();

        /// <summary>Stable identifiers for the code-defined presets.</summary>
        public static IReadOnlyList<Definition> GetDefinitions()
        {
            return Definitions;
        }

        /// <summary>
        /// Returns the shared in-memory preset instances. They are never written to
        /// disk by this class; use NyxaraExpressionLibraryBuilder to materialise assets.
        /// </summary>
        public static IReadOnlyList<ExpressionPreset> GetPresets()
        {
            if (CachedPresets.Count == 0)
            {
                foreach (var definition in Definitions)
                {
                    CachedPresets.Add(CreatePreset(definition));
                }
            }

            for (var i = 0; i < CachedPresets.Count; i++)
            {
                if (CachedPresets[i] == null)
                {
                    CachedPresets[i] = CreatePreset(Definitions[i]);
                }
            }

            return CachedPresets;
        }

        public static ExpressionPreset GetPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return null;
            }

            return GetPresets().FirstOrDefault(preset =>
                string.Equals(preset.presetId, presetId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Drops the cached in-memory instances so the next request rebuilds them.</summary>
        public static void InvalidateCache()
        {
            CachedPresets.Clear();
        }

        public static ExpressionPreset CreatePreset(Definition definition)
        {
            if (definition == null)
            {
                return null;
            }

            var preset = ScriptableObject.CreateInstance<ExpressionPreset>();
            preset.name = definition.displayName;
            preset.hideFlags = HideFlags.DontSave;
            preset.presetId = definition.id;
            preset.displayName = definition.displayName;
            preset.description = definition.description;
            preset.category = definition.category;
            preset.transitionTimeInSeconds = definition.transitionTimeInSeconds;
            preset.holdDuration = definition.holdDuration;
            preset.autoReturnToNeutral = false;
            preset.preserveIndependentChannels = definition.preserveIndependentChannels;
            preset.blendshapeWeights = definition.weights
                .Where(weight => weight != null && weight.weight > 0.01f)
                .Select(weight => new BlendshapeWeight
                {
                    blendshapeName = weight.blendshapeName,
                    weight = weight.weight
                })
                .ToList();
            preset.isBuiltIn = true;
            return preset;
        }

        private static Definition Define(
            string id,
            string displayName,
            ExpressionCategory category,
            string description,
            bool preserveIndependentChannels,
            float transitionTimeInSeconds,
            float holdDuration,
            params (string shape, float weight)[] weights)
        {
            var list = new List<BlendshapeWeight>(weights?.Length ?? 0);
            if (weights != null)
            {
                foreach (var (shape, weight) in weights)
                {
                    list.Add(new BlendshapeWeight
                    {
                        blendshapeName = shape,
                        weight = Mathf.Clamp(weight, 0f, 100f)
                    });
                }
            }

            return new Definition
            {
                id = id,
                displayName = displayName,
                description = description,
                category = category,
                preserveIndependentChannels = preserveIndependentChannels,
                transitionTimeInSeconds = transitionTimeInSeconds,
                holdDuration = holdDuration,
                weights = list
            };
        }

        private static List<Definition> BuildDefinitions()
        {
            const ExpressionCategory emotion = ExpressionCategory.Emotion;
            const ExpressionCategory personality = ExpressionCategory.Personality;
            const ExpressionCategory eye = ExpressionCategory.EyeAction;
            const ExpressionCategory mouth = ExpressionCategory.MouthAction;
            const ExpressionCategory speech = ExpressionCategory.Speech;

            var definitions = new List<Definition>
            {
                // ------------------------------------------------------------
                // Core emotions
                // ------------------------------------------------------------
                Define("nyxara.emotion.neutral", "Neutral", emotion,
                    "Relaxed resting face. Everything returns to zero.",
                    true, 0.20f, 1.5f),

                Define("nyxara.emotion.joy", "Joy", emotion,
                    "Open warm smile with lifted cheeks and softly narrowed eyes.",
                    true, 0.24f, 2.5f,
                    ("mouthSmileLeft", 68f), ("mouthSmileRight", 68f),
                    ("mouthDimpleLeft", 12f), ("mouthDimpleRight", 12f),
                    ("cheekSquintLeft", 35f), ("cheekSquintRight", 35f),
                    ("eyeSquintLeft", 25f), ("eyeSquintRight", 25f),
                    ("browOuterUpLeft", 8f), ("browOuterUpRight", 8f)),

                Define("nyxara.emotion.soft_smile", "Soft Smile", emotion,
                    "Gentle, reserved smile with a calm brow.",
                    true, 0.22f, 2.2f,
                    ("mouthSmileLeft", 34f), ("mouthSmileRight", 34f),
                    ("mouthDimpleLeft", 10f), ("mouthDimpleRight", 10f),
                    ("cheekSquintLeft", 16f), ("cheekSquintRight", 16f),
                    ("browInnerUp", 6f)),

                Define("nyxara.emotion.amusement", "Amusement", emotion,
                    "Held-back laugh: bright smile, lifted cheeks, slightly parted jaw.",
                    true, 0.20f, 2.0f,
                    ("mouthSmileLeft", 70f), ("mouthSmileRight", 70f),
                    ("mouthDimpleLeft", 18f), ("mouthDimpleRight", 18f),
                    ("cheekSquintLeft", 40f), ("cheekSquintRight", 40f),
                    ("eyeSquintLeft", 35f), ("eyeSquintRight", 35f),
                    ("jawOpen", 10f), ("browOuterUpLeft", 12f), ("browOuterUpRight", 12f)),

                Define("nyxara.emotion.excitement", "Excitement", emotion,
                    "Wide-eyed, open-mouthed delight.",
                    true, 0.18f, 2.0f,
                    ("mouthSmileLeft", 72f), ("mouthSmileRight", 72f),
                    ("cheekSquintLeft", 42f), ("cheekSquintRight", 42f),
                    ("eyeWideLeft", 20f), ("eyeWideRight", 20f),
                    ("jawOpen", 14f),
                    ("browInnerUp", 18f),
                    ("browOuterUpLeft", 28f), ("browOuterUpRight", 28f)),

                Define("nyxara.emotion.affection", "Affection", emotion,
                    "Tender expression with a warm smile and softened eyes.",
                    true, 0.26f, 2.5f,
                    ("mouthSmileLeft", 46f), ("mouthSmileRight", 46f),
                    ("mouthDimpleLeft", 12f), ("mouthDimpleRight", 12f),
                    ("cheekSquintLeft", 28f), ("cheekSquintRight", 28f),
                    ("eyeSquintLeft", 30f), ("eyeSquintRight", 30f),
                    ("browInnerUp", 14f)),

                Define("nyxara.emotion.sadness", "Sadness", emotion,
                    "Drooping brow, down-turned mouth, and heavy lower eyelids.",
                    true, 0.28f, 2.8f,
                    ("browInnerUp", 48f),
                    ("browDownLeft", 18f), ("browDownRight", 18f),
                    ("mouthFrownLeft", 46f), ("mouthFrownRight", 46f),
                    ("mouthLowerDownLeft", 20f), ("mouthLowerDownRight", 20f),
                    ("mouthPressLeft", 12f), ("mouthPressRight", 12f),
                    ("mouthShrugLower", 10f),
                    ("eyeSquintLeft", 18f), ("eyeSquintRight", 18f)),

                Define("nyxara.emotion.grief", "Grief", emotion,
                    "Deep sorrow with raised inner brow and a trembling mouth.",
                    true, 0.30f, 3.0f,
                    ("browInnerUp", 62f),
                    ("browDownLeft", 28f), ("browDownRight", 28f),
                    ("mouthFrownLeft", 58f), ("mouthFrownRight", 58f),
                    ("mouthLowerDownLeft", 30f), ("mouthLowerDownRight", 30f),
                    ("mouthStretchLeft", 10f), ("mouthStretchRight", 10f),
                    ("eyeSquintLeft", 25f), ("eyeSquintRight", 25f)),

                Define("nyxara.emotion.anger", "Anger", emotion,
                    "Hard brow, narrowed eyes, sneering nose, and pressed lips.",
                    true, 0.18f, 2.0f,
                    ("browDownLeft", 62f), ("browDownRight", 62f),
                    ("browInnerUp", 10f),
                    ("eyeSquintLeft", 22f), ("eyeSquintRight", 22f),
                    ("cheekSquintLeft", 20f), ("cheekSquintRight", 20f),
                    ("noseSneerLeft", 22f), ("noseSneerRight", 22f),
                    ("mouthFrownLeft", 28f), ("mouthFrownRight", 28f),
                    ("mouthPressLeft", 38f), ("mouthPressRight", 38f),
                    ("jawForward", 8f)),

                Define("nyxara.emotion.frustration", "Frustration", emotion,
                    "Tense brow with a tight, unhappy mouth.",
                    true, 0.20f, 2.2f,
                    ("browDownLeft", 48f), ("browDownRight", 48f),
                    ("browInnerUp", 18f),
                    ("eyeSquintLeft", 18f), ("eyeSquintRight", 18f),
                    ("mouthFrownLeft", 20f), ("mouthFrownRight", 20f),
                    ("mouthPressLeft", 30f), ("mouthPressRight", 30f),
                    ("noseSneerLeft", 10f), ("noseSneerRight", 10f),
                    ("jawForward", 6f)),

                Define("nyxara.emotion.surprise", "Surprise", emotion,
                    "Raised brows, wide eyes, and an open jaw.",
                    true, 0.16f, 1.6f,
                    ("browInnerUp", 55f),
                    ("browOuterUpLeft", 52f), ("browOuterUpRight", 52f),
                    ("eyeWideLeft", 55f), ("eyeWideRight", 55f),
                    ("jawOpen", 32f), ("mouthFunnel", 14f)),

                Define("nyxara.emotion.awe", "Awe", emotion,
                    "Softly raised brows, wide eyes, and a quiet open mouth.",
                    true, 0.30f, 2.5f,
                    ("browInnerUp", 45f),
                    ("browOuterUpLeft", 32f), ("browOuterUpRight", 32f),
                    ("eyeWideLeft", 35f), ("eyeWideRight", 35f),
                    ("jawOpen", 18f), ("mouthSmileLeft", 18f), ("mouthSmileRight", 18f)),

                Define("nyxara.emotion.fear", "Fear", emotion,
                    "Raised inner brow, wide eyes, stretched mouth, and parted jaw.",
                    true, 0.18f, 2.0f,
                    ("browInnerUp", 60f),
                    ("browOuterUpLeft", 28f), ("browOuterUpRight", 28f),
                    ("eyeWideLeft", 48f), ("eyeWideRight", 48f),
                    ("mouthStretchLeft", 34f), ("mouthStretchRight", 34f),
                    ("mouthFrownLeft", 18f), ("mouthFrownRight", 18f),
                    ("jawOpen", 16f)),

                Define("nyxara.emotion.disgust", "Disgust", emotion,
                    "Wrinkled nose, raised upper lip, and a lowered brow.",
                    true, 0.20f, 2.0f,
                    ("noseSneerLeft", 55f), ("noseSneerRight", 55f),
                    ("browDownLeft", 36f), ("browDownRight", 36f),
                    ("mouthUpperUpLeft", 30f), ("mouthUpperUpRight", 30f),
                    ("mouthFrownLeft", 26f), ("mouthFrownRight", 26f),
                    ("mouthPressLeft", 16f), ("mouthPressRight", 16f),
                    ("cheekSquintLeft", 12f), ("cheekSquintRight", 12f)),

                Define("nyxara.emotion.contempt", "Contempt", emotion,
                    "One-sided smirk with a lowered brow and lifted nose.",
                    true, 0.22f, 2.2f,
                    ("mouthSmileLeft", 50f), ("mouthDimpleLeft", 18f),
                    ("browDownLeft", 18f), ("noseSneerLeft", 22f),
                    ("eyeSquintRight", 12f), ("jawLeft", 5f)),

                Define("nyxara.emotion.confusion", "Confusion", emotion,
                    "Asymmetric brow, squinted eye, and a crooked mouth.",
                    true, 0.24f, 2.2f,
                    ("browDownLeft", 34f), ("browOuterUpRight", 42f),
                    ("browInnerUp", 20f),
                    ("eyeSquintRight", 14f),
                    ("mouthFrownLeft", 20f), ("mouthPressRight", 14f),
                    ("jawLeft", 8f)),

                Define("nyxara.emotion.thinking", "Thinking", emotion,
                    "Focused brow, pursed lips, and a subtle jaw shift.",
                    true, 0.24f, 2.4f,
                    ("browDownLeft", 30f), ("browInnerUp", 24f),
                    ("eyeSquintRight", 14f),
                    ("mouthPressLeft", 18f), ("mouthLeft", 8f)),

                Define("nyxara.emotion.determination", "Determination", emotion,
                    "Grounded brow, narrowed eyes, and a firm closed mouth.",
                    true, 0.18f, 2.2f,
                    ("browDownLeft", 42f), ("browDownRight", 42f),
                    ("browInnerUp", 10f),
                    ("eyeSquintLeft", 18f), ("eyeSquintRight", 18f),
                    ("cheekSquintLeft", 12f), ("cheekSquintRight", 12f),
                    ("mouthClose", 10f),
                    ("mouthPressLeft", 26f), ("mouthPressRight", 26f),
                    ("jawForward", 5f)),

                Define("nyxara.emotion.calm", "Calm", emotion,
                    "Quiet, content expression with soft eyes.",
                    true, 0.30f, 2.5f,
                    ("browInnerUp", 8f),
                    ("eyeSquintLeft", 8f), ("eyeSquintRight", 8f),
                    ("mouthClose", 4f),
                    ("mouthSmileLeft", 12f), ("mouthSmileRight", 12f)),

                Define("nyxara.emotion.relief", "Relief", emotion,
                    "Released tension: soft smile, lifted inner brow, relaxed jaw.",
                    true, 0.30f, 2.5f,
                    ("browInnerUp", 22f),
                    ("cheekSquintLeft", 14f), ("cheekSquintRight", 14f),
                    ("eyeSquintLeft", 24f), ("eyeSquintRight", 24f),
                    ("mouthSmileLeft", 30f), ("mouthSmileRight", 30f),
                    ("jawOpen", 5f), ("mouthClose", 3f)),

                // ------------------------------------------------------------
                // Personality expressions
                // ------------------------------------------------------------
                Define("nyxara.personality.playful", "Playful", personality,
                    "Bright, cheeky smile with lifted cheeks.",
                    true, 0.20f, 2.0f,
                    ("mouthSmileLeft", 55f), ("mouthSmileRight", 55f),
                    ("mouthDimpleLeft", 18f), ("mouthDimpleRight", 18f),
                    ("cheekSquintLeft", 30f), ("cheekSquintRight", 30f),
                    ("eyeSquintLeft", 16f), ("eyeSquintRight", 16f),
                    ("browOuterUpLeft", 14f), ("browOuterUpRight", 14f),
                    ("jawOpen", 4f)),

                Define("nyxara.personality.mischievous", "Mischievous", personality,
                    "Lopsided smirk, raised outer brow, and narrowed eye.",
                    true, 0.20f, 2.0f,
                    ("mouthSmileLeft", 55f), ("mouthSmileRight", 34f),
                    ("mouthDimpleLeft", 16f),
                    ("browDownLeft", 20f), ("browOuterUpRight", 32f),
                    ("eyeSquintRight", 20f), ("noseSneerRight", 14f)),

                Define("nyxara.personality.shy", "Shy", personality,
                    "Small smile, lifted inner brow, and softly squinted eyes.",
                    true, 0.26f, 2.2f,
                    ("browInnerUp", 30f),
                    ("mouthSmileLeft", 24f), ("mouthSmileRight", 24f),
                    ("mouthDimpleLeft", 8f), ("mouthDimpleRight", 8f),
                    ("mouthPressLeft", 10f),
                    ("cheekSquintLeft", 20f), ("cheekSquintRight", 20f),
                    ("eyeSquintLeft", 22f), ("eyeSquintRight", 22f)),

                Define("nyxara.personality.confident", "Confident", personality,
                    "Steady brow, easy smile, and grounded jaw.",
                    true, 0.22f, 2.2f,
                    ("mouthSmileLeft", 36f), ("mouthSmileRight", 36f),
                    ("browDownLeft", 10f), ("browDownRight", 10f),
                    ("browOuterUpLeft", 16f), ("browOuterUpRight", 16f),
                    ("eyeSquintLeft", 12f), ("eyeSquintRight", 12f),
                    ("mouthClose", 6f), ("jawForward", 4f)),

                Define("nyxara.personality.sassy", "Sassy", personality,
                    "One raised brow, asymmetric smile, and a tilted nose sneer.",
                    true, 0.18f, 2.0f,
                    ("mouthSmileLeft", 46f), ("mouthDimpleLeft", 20f),
                    ("browOuterUpLeft", 34f), ("browDownRight", 20f),
                    ("eyeSquintRight", 15f), ("noseSneerLeft", 15f)),

                Define("nyxara.personality.flirty", "Flirty", personality,
                    "Warm smile with softened, narrowed eyes.",
                    true, 0.24f, 2.2f,
                    ("mouthSmileLeft", 42f), ("mouthSmileRight", 42f),
                    ("mouthDimpleLeft", 10f), ("mouthDimpleRight", 10f),
                    ("cheekSquintLeft", 26f), ("cheekSquintRight", 26f),
                    ("eyeSquintLeft", 26f), ("eyeSquintRight", 26f),
                    ("browOuterUpLeft", 10f), ("browOuterUpRight", 10f)),

                Define("nyxara.personality.serious", "Serious", personality,
                    "Level brow, firm lips, and a subtle forward jaw.",
                    true, 0.18f, 2.2f,
                    ("browDownLeft", 30f), ("browDownRight", 30f),
                    ("browInnerUp", 8f),
                    ("eyeSquintLeft", 8f), ("eyeSquintRight", 8f),
                    ("mouthClose", 8f),
                    ("mouthPressLeft", 20f), ("mouthPressRight", 20f),
                    ("jawForward", 3f)),

                Define("nyxara.personality.gentle", "Gentle", personality,
                    "Kind, open expression with soft eyes and a small smile.",
                    true, 0.28f, 2.4f,
                    ("browInnerUp", 15f),
                    ("mouthSmileLeft", 20f), ("mouthSmileRight", 20f),
                    ("cheekSquintLeft", 15f), ("cheekSquintRight", 15f),
                    ("eyeSquintLeft", 15f), ("eyeSquintRight", 15f)),

                Define("nyxara.personality.cheerful", "Cheerful", personality,
                    "Big friendly smile with lifted cheeks and bright brows.",
                    true, 0.18f, 2.2f,
                    ("mouthSmileLeft", 62f), ("mouthSmileRight", 62f),
                    ("cheekSquintLeft", 36f), ("cheekSquintRight", 36f),
                    ("eyeSquintLeft", 20f), ("eyeSquintRight", 20f),
                    ("browOuterUpLeft", 16f), ("browOuterUpRight", 16f),
                    ("jawOpen", 6f)),

                Define("nyxara.personality.smug", "Smug", personality,
                    "Self-satisfied half smile with a lifted brow.",
                    true, 0.20f, 2.0f,
                    ("mouthSmileLeft", 52f), ("mouthDimpleLeft", 15f),
                    ("browOuterUpLeft", 26f), ("browDownRight", 16f),
                    ("eyeSquintLeft", 15f), ("noseSneerLeft", 18f)),

                Define("nyxara.personality.worried", "Worried", personality,
                    "Raised inner brow with a tight, uncertain mouth.",
                    true, 0.28f, 2.5f,
                    ("browInnerUp", 46f),
                    ("browDownLeft", 14f), ("browDownRight", 14f),
                    ("eyeSquintLeft", 10f), ("eyeSquintRight", 10f),
                    ("mouthFrownLeft", 26f), ("mouthFrownRight", 26f),
                    ("mouthPressLeft", 10f), ("mouthPressRight", 10f),
                    ("mouthStretchLeft", 12f), ("mouthStretchRight", 12f)),

                Define("nyxara.personality.bored", "Bored", personality,
                    "Heavy lids, flat mouth, and a lowered lower lip.",
                    true, 0.28f, 2.5f,
                    ("browDownLeft", 12f), ("browDownRight", 12f),
                    ("eyeSquintLeft", 12f), ("eyeSquintRight", 12f),
                    ("mouthFrownLeft", 10f), ("mouthFrownRight", 10f),
                    ("mouthPressLeft", 14f), ("mouthPressRight", 14f),
                    ("mouthShrugLower", 16f)),

                Define("nyxara.personality.eager", "Eager", personality,
                    "Bright brows, wide eyes, and an open expectant smile.",
                    true, 0.18f, 2.0f,
                    ("browInnerUp", 20f),
                    ("browOuterUpLeft", 30f), ("browOuterUpRight", 30f),
                    ("eyeWideLeft", 24f), ("eyeWideRight", 24f),
                    ("mouthSmileLeft", 36f), ("mouthSmileRight", 36f),
                    ("jawOpen", 10f)),

                Define("nyxara.personality.dramatic", "Dramatic", personality,
                    "Theatrical brows, wide eyes, and a rounded open mouth.",
                    true, 0.18f, 2.0f,
                    ("browOuterUpLeft", 40f), ("browOuterUpRight", 40f),
                    ("eyeWideLeft", 30f), ("eyeWideRight", 30f),
                    ("mouthFunnel", 16f), ("jawOpen", 20f),
                    ("cheekPuff", 8f)),

                Define("nyxara.personality.friendly", "Friendly", personality,
                    "Approachable smile with warm cheeks.",
                    true, 0.22f, 2.2f,
                    ("mouthSmileLeft", 45f), ("mouthSmileRight", 45f),
                    ("cheekSquintLeft", 25f), ("cheekSquintRight", 25f),
                    ("browInnerUp", 10f),
                    ("eyeSquintLeft", 10f), ("eyeSquintRight", 10f)),

                Define("nyxara.personality.sleepy", "Sleepy", personality,
                    "Drooping lids, relaxed brow, and a slack lower lip.",
                    true, 0.32f, 2.8f,
                    ("eyeSquintLeft", 36f), ("eyeSquintRight", 36f),
                    ("browInnerUp", 8f),
                    ("mouthRollLower", 16f), ("jawOpen", 4f),
                    ("mouthFrownLeft", 6f), ("mouthFrownRight", 6f)),

                // ------------------------------------------------------------
                // Eye expressions and actions (designed to be used as overlays)
                // ------------------------------------------------------------
                Define("nyxara.eye.blink_both", "Blink Both", eye,
                    "Full blink on both eyes.",
                    false, 0.06f, 0.35f,
                    ("eyeBlinkLeft", 100f), ("eyeBlinkRight", 100f)),

                Define("nyxara.eye.blink_left", "Blink Left", eye,
                    "Full blink on the left eye only.",
                    false, 0.06f, 0.35f,
                    ("eyeBlinkLeft", 100f)),

                Define("nyxara.eye.blink_right", "Blink Right", eye,
                    "Full blink on the right eye only.",
                    false, 0.06f, 0.35f,
                    ("eyeBlinkRight", 100f)),

                Define("nyxara.eye.wink_left", "Wink Left", eye,
                    "Left-eye wink with a small right-eye squint.",
                    false, 0.08f, 0.6f,
                    ("eyeBlinkLeft", 90f), ("eyeSquintRight", 22f)),

                Define("nyxara.eye.wink_right", "Wink Right", eye,
                    "Right-eye wink with a small left-eye squint.",
                    false, 0.08f, 0.6f,
                    ("eyeBlinkRight", 90f), ("eyeSquintLeft", 22f)),

                Define("nyxara.eye.look_left", "Look Left", eye,
                    "Gaze to the character's left.",
                    false, 0.10f, 1.0f,
                    ("eyeLookOutLeft", 70f), ("eyeLookInRight", 70f)),

                Define("nyxara.eye.look_right", "Look Right", eye,
                    "Gaze to the character's right.",
                    false, 0.10f, 1.0f,
                    ("eyeLookInLeft", 70f), ("eyeLookOutRight", 70f)),

                Define("nyxara.eye.look_up", "Look Up", eye,
                    "Gaze upward.",
                    false, 0.10f, 1.0f,
                    ("eyeLookUpLeft", 68f), ("eyeLookUpRight", 68f)),

                Define("nyxara.eye.look_down", "Look Down", eye,
                    "Gaze downward.",
                    false, 0.10f, 1.0f,
                    ("eyeLookDownLeft", 68f), ("eyeLookDownRight", 68f)),

                Define("nyxara.eye.look_up_left", "Look Up Left", eye,
                    "Diagonal gaze upward and to the character's left.",
                    false, 0.10f, 1.0f,
                    ("eyeLookUpLeft", 60f), ("eyeLookOutLeft", 40f),
                    ("eyeLookInRight", 40f), ("eyeLookUpRight", 40f)),

                Define("nyxara.eye.look_up_right", "Look Up Right", eye,
                    "Diagonal gaze upward and to the character's right.",
                    false, 0.10f, 1.0f,
                    ("eyeLookUpRight", 60f), ("eyeLookOutRight", 40f),
                    ("eyeLookInLeft", 40f), ("eyeLookUpLeft", 40f)),

                Define("nyxara.eye.wide", "Eye Wide", eye,
                    "Surprised, fully open eyes with a lifted inner brow.",
                    false, 0.10f, 1.0f,
                    ("eyeWideLeft", 82f), ("eyeWideRight", 82f),
                    ("browInnerUp", 10f)),

                Define("nyxara.eye.squint", "Eye Squint", eye,
                    "Narrowed, smiling eyes.",
                    false, 0.10f, 1.0f,
                    ("eyeSquintLeft", 72f), ("eyeSquintRight", 72f),
                    ("cheekSquintLeft", 20f), ("cheekSquintRight", 20f)),

                Define("nyxara.eye.soft_gaze", "Soft Gaze", eye,
                    "Relaxed, slightly lowered gaze.",
                    false, 0.14f, 1.2f,
                    ("eyeSquintLeft", 22f), ("eyeSquintRight", 22f),
                    ("eyeLookDownLeft", 10f), ("eyeLookDownRight", 10f)),

                // ------------------------------------------------------------
                // Mouth and jaw expressions (designed to be used as overlays)
                // ------------------------------------------------------------
                Define("nyxara.mouth.smile", "Mouth Smile", mouth,
                    "Mouth-only smile without changing the upper face.",
                    false, 0.10f, 1.0f,
                    ("mouthSmileLeft", 60f), ("mouthSmileRight", 60f),
                    ("mouthDimpleLeft", 14f), ("mouthDimpleRight", 14f)),

                Define("nyxara.mouth.grin", "Grin", mouth,
                    "Wide toothy grin with a slightly open jaw.",
                    false, 0.10f, 1.0f,
                    ("mouthSmileLeft", 76f), ("mouthSmileRight", 76f),
                    ("mouthDimpleLeft", 24f), ("mouthDimpleRight", 24f),
                    ("mouthUpperUpLeft", 14f), ("mouthUpperUpRight", 14f),
                    ("jawOpen", 10f)),

                Define("nyxara.mouth.frown", "Mouth Frown", mouth,
                    "Down-turned mouth with a heavy lower lip.",
                    false, 0.12f, 1.0f,
                    ("mouthFrownLeft", 52f), ("mouthFrownRight", 52f),
                    ("mouthLowerDownLeft", 15f), ("mouthLowerDownRight", 15f),
                    ("mouthPressLeft", 10f), ("mouthPressRight", 10f)),

                Define("nyxara.mouth.pout", "Pout", mouth,
                    "Protruding, slightly open pout.",
                    false, 0.10f, 1.0f,
                    ("mouthPucker", 56f), ("mouthFunnel", 26f),
                    ("mouthLowerDownLeft", 16f), ("mouthLowerDownRight", 16f),
                    ("mouthPressLeft", 10f), ("mouthPressRight", 10f),
                    ("jawOpen", 4f)),

                Define("nyxara.mouth.kiss", "Kiss", mouth,
                    "Tight, rounded kiss shape.",
                    false, 0.08f, 0.8f,
                    ("mouthPucker", 72f), ("mouthFunnel", 20f),
                    ("mouthClose", 14f)),

                Define("nyxara.mouth.jaw_open", "Jaw Open", mouth,
                    "Open jaw with a relaxed mouth.",
                    false, 0.10f, 1.0f,
                    ("jawOpen", 52f)),

                Define("nyxara.mouth.jaw_clench", "Jaw Clench", mouth,
                    "Clenched jaw with pressed lips.",
                    false, 0.10f, 1.0f,
                    ("mouthPressLeft", 46f), ("mouthPressRight", 46f),
                    ("mouthClose", 20f), ("jawForward", 15f),
                    ("jawOpen", 2f)),

                Define("nyxara.mouth.left", "Mouth Left", mouth,
                    "Mouth pulled to the character's left.",
                    false, 0.10f, 1.0f,
                    ("mouthLeft", 52f), ("jawLeft", 20f)),

                Define("nyxara.mouth.right", "Mouth Right", mouth,
                    "Mouth pulled to the character's right.",
                    false, 0.10f, 1.0f,
                    ("mouthRight", 52f), ("jawRight", 20f)),

                Define("nyxara.mouth.tongue_out", "Tongue Out", mouth,
                    "Tongue out with a slightly open jaw.",
                    false, 0.08f, 1.0f,
                    ("tongueOut", 62f), ("jawOpen", 20f)),

                Define("nyxara.mouth.lip_bite", "Lip Bite", mouth,
                    "Lower lip rolled in and gently bitten.",
                    false, 0.08f, 1.0f,
                    ("mouthRollLower", 52f), ("mouthPressLeft", 20f),
                    ("mouthPressRight", 20f), ("jawOpen", 5f)),

                Define("nyxara.mouth.upper_lip_roll", "Upper Lip Roll", mouth,
                    "Upper lip curled inward with the jaw nearly closed.",
                    false, 0.08f, 1.0f,
                    ("mouthRollUpper", 58f), ("mouthPressLeft", 12f),
                    ("mouthPressRight", 12f), ("jawOpen", 3f)),

                Define("nyxara.mouth.upper_lip_shrug", "Upper Lip Shrug", mouth,
                    "Upper lip lifted into a tight, skeptical line.",
                    false, 0.08f, 1.0f,
                    ("mouthShrugUpper", 56f), ("noseSneerLeft", 12f),
                    ("noseSneerRight", 12f)),

                Define("nyxara.mouth.lip_press", "Lip Press", mouth,
                    "Firmly pressed lips.",
                    false, 0.08f, 1.0f,
                    ("mouthPressLeft", 56f), ("mouthPressRight", 56f),
                    ("mouthClose", 24f)),

                Define("nyxara.mouth.stretch", "Mouth Stretch", mouth,
                    "Wide horizontal mouth stretch.",
                    false, 0.08f, 1.0f,
                    ("mouthStretchLeft", 56f), ("mouthStretchRight", 56f),
                    ("jawOpen", 8f)),

                Define("nyxara.mouth.funnel", "Mouth Funnel", mouth,
                    "Rounded funnel mouth.",
                    false, 0.08f, 1.0f,
                    ("mouthFunnel", 62f), ("jawOpen", 10f)),

                Define("nyxara.mouth.pucker", "Mouth Pucker", mouth,
                    "Small rounded pucker.",
                    false, 0.08f, 1.0f,
                    ("mouthPucker", 66f), ("jawOpen", 5f)),

                Define("nyxara.mouth.cheek_puff", "Cheek Puff", mouth,
                    "Puffed cheeks with a sealed mouth.",
                    false, 0.12f, 1.2f,
                    ("cheekPuff", 72f)),

                Define("nyxara.mouth.nose_sneer", "Nose Sneer", mouth,
                    "Raised nose with a lifted upper lip.",
                    false, 0.10f, 1.0f,
                    ("noseSneerLeft", 56f), ("noseSneerRight", 56f),
                    ("mouthUpperUpLeft", 20f), ("mouthUpperUpRight", 20f)),

                // ------------------------------------------------------------
                // Speech and visemes (approximated with the ARKit shape set)
                // ------------------------------------------------------------
                Define("nyxara.speech.sil", "Viseme: Sil", speech,
                    "Resting mouth for silence.",
                    false, 0.06f, 0.4f,
                    ("mouthClose", 6f)),

                Define("nyxara.speech.aa", "Viseme: AA", speech,
                    "Open 'ah' vowel.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 56f), ("mouthLowerDownLeft", 24f),
                    ("mouthLowerDownRight", 24f), ("mouthUpperUpLeft", 10f),
                    ("mouthUpperUpRight", 10f)),

                Define("nyxara.speech.ah", "Viseme: AH", speech,
                    "Short open vowel.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 44f), ("mouthLowerDownLeft", 18f),
                    ("mouthLowerDownRight", 18f)),

                Define("nyxara.speech.eh", "Viseme: EH", speech,
                    "Mid-open 'eh' vowel.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 24f), ("mouthStretchLeft", 30f),
                    ("mouthStretchRight", 30f)),

                Define("nyxara.speech.iy", "Viseme: IY", speech,
                    "Wide 'ee' vowel.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 14f), ("mouthSmileLeft", 28f),
                    ("mouthSmileRight", 28f), ("mouthStretchLeft", 36f),
                    ("mouthStretchRight", 36f)),

                Define("nyxara.speech.ih", "Viseme: IH", speech,
                    "Short 'ih' vowel.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 18f), ("mouthSmileLeft", 14f),
                    ("mouthSmileRight", 14f), ("mouthStretchLeft", 26f),
                    ("mouthStretchRight", 26f)),

                Define("nyxara.speech.ow", "Viseme: OW", speech,
                    "Rounded 'oh' vowel.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 30f), ("mouthFunnel", 56f)),

                Define("nyxara.speech.ao", "Viseme: AO", speech,
                    "Open rounded 'aw' vowel.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 36f), ("mouthFunnel", 40f),
                    ("mouthPucker", 20f)),

                Define("nyxara.speech.uh", "Viseme: UH", speech,
                    "Short rounded 'uh' vowel.",
                    false, 0.06f, 0.4f,
                    ("mouthPucker", 60f), ("jawOpen", 10f)),

                Define("nyxara.speech.uw", "Viseme: UW", speech,
                    "Tight rounded 'oo' vowel.",
                    false, 0.06f, 0.4f,
                    ("mouthPucker", 70f), ("jawOpen", 8f)),

                Define("nyxara.speech.w", "Viseme: W", speech,
                    "Gliding 'w' shape.",
                    false, 0.06f, 0.4f,
                    ("mouthPucker", 55f), ("mouthFunnel", 15f),
                    ("jawOpen", 5f)),

                Define("nyxara.speech.fv", "Viseme: FV", speech,
                    "Labiodental 'f' and 'v'.",
                    false, 0.06f, 0.4f,
                    ("mouthClose", 36f), ("mouthLowerDownLeft", 8f),
                    ("mouthLowerDownRight", 8f), ("mouthUpperUpLeft", 5f),
                    ("mouthUpperUpRight", 5f)),

                Define("nyxara.speech.mbp", "Viseme: MBP", speech,
                    "Closed bilabial 'm', 'b', and 'p'.",
                    false, 0.06f, 0.4f,
                    ("mouthClose", 72f), ("mouthPressLeft", 10f),
                    ("mouthPressRight", 10f)),

                Define("nyxara.speech.th", "Viseme: TH", speech,
                    "Tongue between the teeth.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 20f), ("tongueOut", 8f),
                    ("mouthStretchLeft", 10f), ("mouthStretchRight", 10f)),

                Define("nyxara.speech.l", "Viseme: L", speech,
                    "Tongue-tip 'l'.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 18f), ("tongueOut", 12f)),

                Define("nyxara.speech.r", "Viseme: R", speech,
                    "Rounded 'r' with a lowered lower lip.",
                    false, 0.06f, 0.4f,
                    ("mouthPucker", 30f), ("jawOpen", 15f),
                    ("mouthShrugLower", 8f)),

                Define("nyxara.speech.sh", "Viseme: SH", speech,
                    "Rounded hushing shape.",
                    false, 0.06f, 0.4f,
                    ("mouthPucker", 40f), ("mouthFunnel", 30f),
                    ("jawOpen", 8f)),

                Define("nyxara.speech.sz", "Viseme: SZ", speech,
                    "Narrow sibilant shape.",
                    false, 0.06f, 0.4f,
                    ("mouthStretchLeft", 36f), ("mouthStretchRight", 36f),
                    ("mouthClose", 10f)),

                Define("nyxara.speech.hh", "Viseme: HH", speech,
                    "Open breath shape.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 26f), ("mouthFunnel", 5f)),

                Define("nyxara.speech.ng", "Viseme: NG", speech,
                    "Closed nasal shape.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 15f), ("mouthClose", 20f)),

                Define("nyxara.speech.dt", "Viseme: DT", speech,
                    "Alveolar 'd' and 't'.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 8f), ("mouthClose", 26f)),

                Define("nyxara.speech.gk", "Viseme: GK", speech,
                    "Velar 'g' and 'k'.",
                    false, 0.06f, 0.4f,
                    ("jawOpen", 25f), ("mouthClose", 10f))
            };

            return definitions;
        }
    }
}
