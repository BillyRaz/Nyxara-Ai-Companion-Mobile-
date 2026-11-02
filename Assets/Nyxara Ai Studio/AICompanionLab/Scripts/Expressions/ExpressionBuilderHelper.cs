// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nyxara.AICompanion.Expressions
{
    public static class ExpressionBuilderHelper
    {
        [Serializable]
        public sealed class ControlDefinition
        {
            public string key;
            public string displayName;
            public string[] candidateBlendshapeNames;
        }

        private static readonly List<ControlDefinition> DefaultControls = new()
        {
            Create("browInnerUp", "Brow Inner Up", "browInnerUp", "BrowInnerUp", "Brow_Raise_Inner_L", "Brow_Raise_Inner_R"),
            Create("browDownLeft", "Brow Down Left", "browDownLeft", "BrowDownLeft", "Brow_Drop_L"),
            Create("browDownRight", "Brow Down Right", "browDownRight", "BrowDownRight", "Brow_Drop_R"),
            Create("eyeBlinkLeft", "Eye Blink Left", "eyeBlinkLeft", "EyeBlinkLeft", "Eye_Blink_L", "Blink_L", "Fcl_EYE_Close_L", "Fcl_EYE_Close"),
            Create("eyeBlinkRight", "Eye Blink Right", "eyeBlinkRight", "EyeBlinkRight", "Eye_Blink_R", "Blink_R", "Fcl_EYE_Close_R", "Fcl_EYE_Close"),
            Create("eyeSquintLeft", "Eye Squint Left", "eyeSquintLeft", "EyeSquintLeft", "Eye_Squint_L", "Fcl_EYE_Joy_L", "Fcl_EYE_Joy"),
            Create("eyeSquintRight", "Eye Squint Right", "eyeSquintRight", "EyeSquintRight", "Eye_Squint_R", "Fcl_EYE_Joy_R", "Fcl_EYE_Joy"),
            Create("eyeWideLeft", "Eye Wide Left", "eyeWideLeft", "EyeWideLeft", "Eye_Wide_L", "Fcl_EYE_Spread", "Fcl_EYE_Surprised"),
            Create("eyeWideRight", "Eye Wide Right", "eyeWideRight", "EyeWideRight", "Eye_Wide_R", "Fcl_EYE_Spread", "Fcl_EYE_Surprised"),
            Create("cheekPuff", "Cheek Puff", "cheekPuff", "CheekPuff", "Cheek_Puff_L", "Cheek_Puff_R"),
            Create("cheekSquintLeft", "Cheek Squint Left", "cheekSquintLeft", "CheekSquintLeft", "Cheek_Raise_L"),
            Create("cheekSquintRight", "Cheek Squint Right", "cheekSquintRight", "CheekSquintRight", "Cheek_Raise_R"),
            Create("noseSneerLeft", "Nose Sneer Left", "noseSneerLeft", "NoseSneerLeft", "Nose_Sneer_L"),
            Create("noseSneerRight", "Nose Sneer Right", "noseSneerRight", "NoseSneerRight", "Nose_Sneer_R"),
            Create("jawOpen", "Jaw Open", "jawOpen", "JawOpen", "mouthOpen", "Jaw_Open", "V_Open", "viseme_aa", "viseme_AA", "A", "Fcl_MTH_A"),
            Create("jawForward", "Jaw Forward", "jawForward", "JawForward", "Jaw_Forward"),
            Create("jawLeft", "Jaw Left", "jawLeft", "JawLeft", "Jaw_L"),
            Create("jawRight", "Jaw Right", "jawRight", "JawRight", "Jaw_R"),
            Create("mouthClose", "Mouth Close", "mouthClose", "MouthClose", "Mouth_Close", "viseme_sil", "viseme_PP", "viseme_pp", "Fcl_MTH_Close"),
            Create("mouthFunnel", "Mouth Funnel", "mouthFunnel", "MouthFunnel", "Mouth_Funnel_Up_L", "Mouth_Funnel_Up_R", "Mouth_Funnel_Down_L", "Mouth_Funnel_Down_R", "O", "viseme_O", "Fcl_MTH_O", "Fcl_MTH_U"),
            Create("mouthPucker", "Mouth Pucker", "mouthPucker", "MouthPucker", "Mouth_Pucker_Up_L", "Mouth_Pucker_Up_R", "Mouth_Pucker_Down_L", "Mouth_Pucker_Down_R", "U", "viseme_U", "viseme_ou", "Fcl_MTH_U", "Fcl_MTH_O"),
            Create("mouthLeft", "Mouth Left", "mouthLeft", "MouthLeft", "Mouth_L"),
            Create("mouthRight", "Mouth Right", "mouthRight", "MouthRight", "Mouth_R"),
            Create("mouthSmileLeft", "Smile Left", "mouthSmileLeft", "mouthSmile_L", "SmileLeft", "Mouth_Smile_L", "Mouth_Smile_Sharp_L", "Fcl_MTH_E", "Fcl_MTH_I", "Fcl_MTH_A"),
            Create("mouthSmileRight", "Smile Right", "mouthSmileRight", "mouthSmile_R", "SmileRight", "Mouth_Smile_R", "Mouth_Smile_Sharp_R", "Fcl_MTH_E", "Fcl_MTH_I", "Fcl_MTH_A"),
            Create("mouthFrownLeft", "Frown Left", "mouthFrownLeft", "mouthFrown_L", "FrownLeft", "Mouth_Frown_L"),
            Create("mouthFrownRight", "Frown Right", "mouthFrownRight", "mouthFrown_R", "FrownRight", "Mouth_Frown_R"),
            Create("mouthDimpleLeft", "Dimple Left", "mouthDimpleLeft", "mouthDimple_L", "DimpleLeft", "Mouth_Dimple_L"),
            Create("mouthDimpleRight", "Dimple Right", "mouthDimpleRight", "mouthDimple_R", "DimpleRight", "Mouth_Dimple_R"),
            Create("mouthStretchLeft", "Mouth Stretch Left", "mouthStretchLeft", "mouthStretch_L", "StretchLeft", "Mouth_Stretch_L", "viseme_I", "viseme_E", "Fcl_MTH_I", "Fcl_MTH_E"),
            Create("mouthStretchRight", "Mouth Stretch Right", "mouthStretchRight", "mouthStretch_R", "StretchRight", "Mouth_Stretch_R", "viseme_I", "viseme_E", "Fcl_MTH_I", "Fcl_MTH_E"),
            Create("mouthPressLeft", "Mouth Press Left", "mouthPressLeft", "mouthPress_L", "MouthPressLeft", "Mouth_Press_L"),
            Create("mouthPressRight", "Mouth Press Right", "mouthPressRight", "mouthPress_R", "MouthPressRight", "Mouth_Press_R"),
            Create("mouthLowerDownLeft", "Lower Down Left", "mouthLowerDownLeft", "mouthLowerDown_L", "MouthLowerDownLeft", "Mouth_Down_Lower_L", "Fcl_MTH_A"),
            Create("mouthLowerDownRight", "Lower Down Right", "mouthLowerDownRight", "mouthLowerDown_R", "MouthLowerDownRight", "Mouth_Down_Lower_R", "Fcl_MTH_A"),
            Create("mouthUpperUpLeft", "Upper Up Left", "mouthUpperUpLeft", "mouthUpperUp_L", "MouthUpperUpLeft", "Mouth_Up_Upper_L", "Fcl_MTH_A"),
            Create("mouthUpperUpRight", "Upper Up Right", "mouthUpperUpRight", "mouthUpperUp_R", "MouthUpperUpRight", "Mouth_Up_Upper_R", "Fcl_MTH_A"),
            Create("mouthRollUpper", "Roll Upper Lip", "mouthRollUpper", "MouthRollUpper", "Mouth_Roll_In_Upper_L", "Mouth_Roll_In_Upper_R", "Mouth_Roll_Out_Upper_L", "Mouth_Roll_Out_Upper_R"),
            Create("mouthRollLower", "Roll Lower Lip", "mouthRollLower", "MouthRollLower", "Mouth_Roll_In_Lower_L", "Mouth_Roll_In_Lower_R", "Mouth_Roll_Out_Lower_L", "Mouth_Roll_Out_Lower_R"),
            Create("mouthShrugUpper", "Shrug Upper Lip", "mouthShrugUpper", "MouthShrugUpper", "Mouth_Shrug_Upper"),
            Create("mouthShrugLower", "Shrug Lower Lip", "mouthShrugLower", "MouthShrugLower", "Mouth_Shrug_Lower"),
            Create("tongueOut", "Tongue Out", "tongueOut", "TongueOut", "Tongue_Out", "V_Tongue_Out")
        };

        public static IReadOnlyList<ControlDefinition> GetDefaultControls()
        {
            return DefaultControls;
        }

        public static IReadOnlyList<string> DetectCompatibilityProfiles(IEnumerable<string> blendshapeNames)
        {
            var normalized = new HashSet<string>((blendshapeNames ?? Array.Empty<string>()).Select(Normalize), StringComparer.OrdinalIgnoreCase);
            var profiles = new List<string>();

            if (normalized.Contains("jawopen") && normalized.Contains("mouthsmileleft") && normalized.Contains("eyeblinkleft"))
            {
                profiles.Add("ARKit");
            }

            if ((normalized.Contains("fcleyeclose") || (normalized.Contains("fcleyeclosel") && normalized.Contains("fcleyecloser"))) &&
                (normalized.Contains("fclmtha") || normalized.Contains("fclmtho") || normalized.Contains("fclmthu") || normalized.Contains("fclmthclose")))
            {
                profiles.Add("VRM");
            }

            if (normalized.Contains("jawopen") && normalized.Contains("mouthsmilel") && normalized.Contains("mouthclose"))
            {
                profiles.Add("CC/Reallusion");
            }

            if (normalized.Contains("vopen") || normalized.Contains("visemeaa") || normalized.Contains("visemeo") || normalized.Contains("visemeu"))
            {
                profiles.Add("Viseme/VTuber");
            }

            if (normalized.Contains("metahuman") || (normalized.Contains("jawopen") && normalized.Contains("browraiseouterl") && normalized.Contains("eyeblinkleft")))
            {
                profiles.Add("Unreal/MetaHuman-like");
            }

            if (profiles.Count == 0)
            {
                profiles.Add("Custom/Unknown");
            }

            return profiles;
        }

        public static bool LooksLikeRecognizedControlName(string blendshapeName)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return false;
            }

            var normalizedName = Normalize(blendshapeName);
            foreach (var control in DefaultControls)
            {
                foreach (var candidate in control.candidateBlendshapeNames ?? Array.Empty<string>())
                {
                    if (string.Equals(normalizedName, Normalize(candidate), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static List<string> GetBlendshapeNames(SkinnedMeshRenderer renderer)
        {
            var names = new List<string>();
            if (renderer == null || renderer.sharedMesh == null)
            {
                return names;
            }

            var mesh = renderer.sharedMesh;
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                names.Add(mesh.GetBlendShapeName(i));
            }

            return names;
        }

        public static Dictionary<string, string> AutoDetectBlendshapes(SkinnedMeshRenderer renderer)
        {
            var blendshapeNames = GetBlendshapeNames(renderer);
            return AutoDetectBlendshapes(blendshapeNames);
        }

        public static Dictionary<string, string> AutoDetectBlendshapes(IEnumerable<SkinnedMeshRenderer> renderers)
        {
            var blendshapeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (renderers != null)
            {
                foreach (var renderer in renderers)
                {
                    foreach (var name in GetBlendshapeNames(renderer))
                    {
                        blendshapeNames.Add(name);
                    }
                }
            }

            return AutoDetectBlendshapes(blendshapeNames);
        }

        public static List<string> GetBlendshapeNames(IEnumerable<SkinnedMeshRenderer> renderers)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (renderers != null)
            {
                foreach (var renderer in renderers)
                {
                    foreach (var name in GetBlendshapeNames(renderer))
                    {
                        names.Add(name);
                    }
                }
            }

            return names.ToList();
        }

        public static bool IsMouthRelatedBlendshape(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.ToLowerInvariant();
            return normalized.Contains("mouth") ||
                   normalized.Contains("jaw") ||
                   normalized.Contains("lip") ||
                   normalized.Contains("smile") ||
                   normalized.Contains("frown") ||
                   normalized.Contains("funnel") ||
                   normalized.Contains("pucker") ||
                   normalized.Contains("shrug") ||
                   normalized.Contains("roll") ||
                   normalized.Contains("press") ||
                   normalized.Contains("tongue") ||
                   normalized.Contains("viseme") ||
                   normalized.Contains("aa") ||
                   normalized.Contains("oh") ||
                   normalized.Contains("ou") ||
                   normalized.Contains("ee") ||
                   normalized.Contains("ih");
        }

        public static bool IsEyeRelatedBlendshape(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.ToLowerInvariant();
            return normalized.Contains("eye") ||
                   normalized.Contains("brow") ||
                   normalized.Contains("cheek") ||
                   normalized.Contains("nose");
        }

        public static bool IsJawRelatedBlendshape(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.ToLowerInvariant();
            return normalized.Contains("jaw");
        }

        public static bool IsTongueOrTeethRelatedBlendshape(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.ToLowerInvariant();
            return normalized.Contains("tongue") ||
                   normalized.Contains("teeth") ||
                   normalized.Contains("tooth");
        }

        private static Dictionary<string, string> AutoDetectBlendshapes(IEnumerable<string> blendshapeNames)
        {
            var blendshapeNameList = blendshapeNames?.ToList() ?? new List<string>();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var control in DefaultControls)
            {
                var detected = FindBestMatch(blendshapeNameList, control.candidateBlendshapeNames);
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    result[control.key] = detected;
                }
            }

            return result;
        }

        public static Dictionary<string, float> BuildBlendshapeWeights(
            IReadOnlyDictionary<string, string> controlToBlendshapeMap,
            IReadOnlyDictionary<string, float> controlWeights)
        {
            return BuildBlendshapeWeights(controlToBlendshapeMap, controlWeights, null);
        }

        public static Dictionary<string, float> BuildBlendshapeWeights(
            IReadOnlyDictionary<string, string> controlToBlendshapeMap,
            IReadOnlyDictionary<string, float> controlWeights,
            IEnumerable<string> availableBlendshapeNames)
        {
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (controlToBlendshapeMap == null || controlWeights == null)
            {
                return result;
            }

            var availableNames = new HashSet<string>(availableBlendshapeNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var control in DefaultControls)
            {
                if (!controlToBlendshapeMap.TryGetValue(control.key, out var blendshapeName) || string.IsNullOrWhiteSpace(blendshapeName))
                {
                    continue;
                }

                if (!controlWeights.TryGetValue(control.key, out var weight) || weight <= 0.01f)
                {
                    continue;
                }

                result[blendshapeName] = weight;

                foreach (var alias in control.candidateBlendshapeNames)
                {
                    if (string.IsNullOrWhiteSpace(alias) || string.Equals(alias, blendshapeName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (availableNames.Count == 0 || availableNames.Contains(alias))
                    {
                        result[alias] = weight;
                    }
                }
            }

            return result;
        }

        public static Dictionary<string, float> LoadControlWeightsFromPreset(
            ExpressionPreset preset,
            IReadOnlyDictionary<string, string> controlToBlendshapeMap)
        {
            var result = DefaultControls.ToDictionary(control => control.key, _ => 0f, StringComparer.OrdinalIgnoreCase);
            if (preset == null || controlToBlendshapeMap == null)
            {
                return result;
            }

            var presetLookup = preset.blendshapeWeights.ToDictionary(weight => weight.blendshapeName, weight => weight.weight, StringComparer.OrdinalIgnoreCase);
            foreach (var control in DefaultControls)
            {
                if (!controlToBlendshapeMap.TryGetValue(control.key, out var blendshapeName) ||
                    string.IsNullOrWhiteSpace(blendshapeName))
                {
                    continue;
                }

                if (TryResolvePresetWeight(presetLookup, blendshapeName, control.candidateBlendshapeNames, out var weight))
                {
                    result[control.key] = weight;
                }
            }

            return result;
        }

        private static ControlDefinition Create(string key, string displayName, params string[] candidates)
        {
            return new ControlDefinition
            {
                key = key,
                displayName = displayName,
                candidateBlendshapeNames = candidates ?? Array.Empty<string>()
            };
        }

        private static string FindBestMatch(IReadOnlyList<string> blendshapeNames, IReadOnlyList<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                var exact = blendshapeNames.FirstOrDefault(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(exact))
                {
                    return exact;
                }
            }

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = Normalize(candidate);
                var contains = blendshapeNames.FirstOrDefault(name =>
                    Normalize(name).IndexOf(normalizedCandidate, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrWhiteSpace(contains))
                {
                    return contains;
                }
            }

            return string.Empty;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
        }

        private static bool TryResolvePresetWeight(
            IReadOnlyDictionary<string, float> presetLookup,
            string selectedBlendshapeName,
            IReadOnlyList<string> candidateBlendshapeNames,
            out float weight)
        {
            weight = 0f;
            if (presetLookup == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(selectedBlendshapeName) &&
                presetLookup.TryGetValue(selectedBlendshapeName, out weight))
            {
                return true;
            }

            foreach (var candidate in candidateBlendshapeNames ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    presetLookup.TryGetValue(candidate, out weight))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
