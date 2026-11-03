// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nyxara.AICompanion.Face
{
    public class ArkItBlendshapeDriver : MonoBehaviour
    {
        private static readonly Dictionary<string, string[]> BlendshapeAliases = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["jawOpen"] = new[] { "jawOpen", "JawOpen", "Jaw_Open", "V_Open", "V_Lip_Open", "V_Dental_Lip", "Mouth_Drop_Lower", "Mouth_Drop_Upper", "viseme_aa", "viseme_AA", "A", "Fcl_MTH_A" },
            ["jawForward"] = new[] { "jawForward", "JawForward", "Jaw_Forward" },
            ["jawLeft"] = new[] { "jawLeft", "JawLeft", "Jaw_L" },
            ["jawRight"] = new[] { "jawRight", "JawRight", "Jaw_R" },
            ["mouthClose"] = new[] { "mouthClose", "MouthClose", "Mouth_Close", "viseme_sil", "viseme_PP", "viseme_pp", "Fcl_MTH_Close" },
            ["mouthFunnel"] = new[] { "mouthFunnel", "MouthFunnel", "Mouth_Funnel_Up_L", "Mouth_Funnel_Up_R", "Mouth_Funnel_Down_L", "Mouth_Funnel_Down_R", "O", "viseme_O", "Fcl_MTH_O", "Fcl_MTH_U" },
            ["mouthPucker"] = new[] { "mouthPucker", "MouthPucker", "Mouth_Pucker_Up_L", "Mouth_Pucker_Up_R", "Mouth_Pucker_Down_L", "Mouth_Pucker_Down_R", "U", "viseme_U", "viseme_ou", "Fcl_MTH_U", "Fcl_MTH_O" },
            ["mouthLeft"] = new[] { "mouthLeft", "MouthLeft", "Mouth_L" },
            ["mouthRight"] = new[] { "mouthRight", "MouthRight", "Mouth_R" },
            ["mouthSmileLeft"] = new[] { "mouthSmileLeft", "mouthSmile_L", "SmileLeft", "Mouth_Smile_L", "Mouth_Smile_Sharp_L", "Fcl_MTH_E", "Fcl_MTH_I", "Fcl_MTH_A" },
            ["mouthSmileRight"] = new[] { "mouthSmileRight", "mouthSmile_R", "SmileRight", "Mouth_Smile_R", "Mouth_Smile_Sharp_R", "Fcl_MTH_E", "Fcl_MTH_I", "Fcl_MTH_A" },
            ["mouthFrownLeft"] = new[] { "mouthFrownLeft", "mouthFrown_L", "FrownLeft", "Mouth_Frown_L" },
            ["mouthFrownRight"] = new[] { "mouthFrownRight", "mouthFrown_R", "FrownRight", "Mouth_Frown_R" },
            ["mouthStretchLeft"] = new[] { "mouthStretchLeft", "mouthStretch_L", "StretchLeft", "Mouth_Stretch_L", "viseme_I", "viseme_E", "Fcl_MTH_I", "Fcl_MTH_E" },
            ["mouthStretchRight"] = new[] { "mouthStretchRight", "mouthStretch_R", "StretchRight", "Mouth_Stretch_R", "viseme_I", "viseme_E", "Fcl_MTH_I", "Fcl_MTH_E" },
            ["mouthPressLeft"] = new[] { "mouthPressLeft", "mouthPress_L", "MouthPressLeft", "Mouth_Press_L" },
            ["mouthPressRight"] = new[] { "mouthPressRight", "mouthPress_R", "MouthPressRight", "Mouth_Press_R" },
            ["mouthDimpleLeft"] = new[] { "mouthDimpleLeft", "mouthDimple_L", "DimpleLeft", "Mouth_Dimple_L" },
            ["mouthDimpleRight"] = new[] { "mouthDimpleRight", "mouthDimple_R", "DimpleRight", "Mouth_Dimple_R" },
            ["mouthLowerDownLeft"] = new[] { "mouthLowerDownLeft", "mouthLowerDown_L", "MouthLowerDownLeft", "Mouth_Down_Lower_L", "Mouth_Drop_Lower", "V_Lip_Open", "Fcl_MTH_A" },
            ["mouthLowerDownRight"] = new[] { "mouthLowerDownRight", "mouthLowerDown_R", "MouthLowerDownRight", "Mouth_Down_Lower_R", "Mouth_Drop_Lower", "V_Lip_Open", "Fcl_MTH_A" },
            ["mouthUpperUpLeft"] = new[] { "mouthUpperUpLeft", "mouthUpperUp_L", "MouthUpperUpLeft", "Mouth_Up_Upper_L", "Mouth_Drop_Upper", "V_Dental_Lip", "Fcl_MTH_A" },
            ["mouthUpperUpRight"] = new[] { "mouthUpperUpRight", "mouthUpperUp_R", "MouthUpperUpRight", "Mouth_Up_Upper_R", "Mouth_Drop_Upper", "V_Dental_Lip", "Fcl_MTH_A" },
            ["mouthRollUpper"] = new[] { "mouthRollUpper", "MouthRollUpper", "Mouth_Roll_In_Upper_L", "Mouth_Roll_In_Upper_R", "Mouth_Roll_Out_Upper_L", "Mouth_Roll_Out_Upper_R" },
            ["mouthRollLower"] = new[] { "mouthRollLower", "MouthRollLower", "Mouth_Roll_In_Lower_L", "Mouth_Roll_In_Lower_R", "Mouth_Roll_Out_Lower_L", "Mouth_Roll_Out_Lower_R" },
            ["mouthShrugUpper"] = new[] { "mouthShrugUpper", "MouthShrugUpper", "Mouth_Shrug_Upper" },
            ["mouthShrugLower"] = new[] { "mouthShrugLower", "MouthShrugLower", "Mouth_Shrug_Lower" },
            ["tongueOut"] = new[] { "tongueOut", "TongueOut", "Tongue_Out", "V_Tongue_Out" },
            ["eyeBlinkLeft"] = new[] { "eyeBlinkLeft", "EyeBlinkLeft", "Eye_Blink_L", "Blink_L", "Fcl_EYE_Close_L", "Fcl_EYE_Close" },
            ["eyeBlinkRight"] = new[] { "eyeBlinkRight", "EyeBlinkRight", "Eye_Blink_R", "Blink_R", "Fcl_EYE_Close_R", "Fcl_EYE_Close" },
            ["eyeSquintLeft"] = new[] { "eyeSquintLeft", "EyeSquintLeft", "Eye_Squint_L", "Fcl_EYE_Joy_L", "Fcl_EYE_Joy" },
            ["eyeSquintRight"] = new[] { "eyeSquintRight", "EyeSquintRight", "Eye_Squint_R", "Fcl_EYE_Joy_R", "Fcl_EYE_Joy" },
            ["eyeWideLeft"] = new[] { "eyeWideLeft", "EyeWideLeft", "Eye_Wide_L", "Fcl_EYE_Spread", "Fcl_EYE_Surprised" },
            ["eyeWideRight"] = new[] { "eyeWideRight", "EyeWideRight", "Eye_Wide_R", "Fcl_EYE_Spread", "Fcl_EYE_Surprised" }
        };

        [SerializeField] private SkinnedMeshRenderer targetRenderer;
        [SerializeField] private List<SkinnedMeshRenderer> additionalRenderers = new();
        [SerializeField] private string jawOpenBlendshape = "jawOpen";
        [SerializeField] private string mouthFunnelBlendshape = "mouthFunnel";
        [SerializeField] private string browInnerUpBlendshape = "browInnerUp";
        [SerializeField] private float speakingWeight = 65f;
        [SerializeField] private float thinkingWeight = 25f;
        [SerializeField] private bool expressionModeActive;

        public SkinnedMeshRenderer TargetRenderer => targetRenderer;
        public IReadOnlyList<SkinnedMeshRenderer> AdditionalRenderers => additionalRenderers;
        public IReadOnlyList<SkinnedMeshRenderer> TargetRenderers => GetAllRenderers();

        public void SetSpeaking(bool active)
        {
            if (expressionModeActive)
            {
                return;
            }

            SetBlendshapeWeight(jawOpenBlendshape, active ? speakingWeight : 0f);
            SetBlendshapeWeight(mouthFunnelBlendshape, active ? speakingWeight * 0.6f : 0f);
        }

        public void SetThinking(bool active)
        {
            if (expressionModeActive)
            {
                return;
            }

            SetBlendshapeWeight(browInnerUpBlendshape, active ? thinkingWeight : 0f);
        }

        public void SetBlendshapeWeight(string blendshapeName, float weight)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return;
            }

            foreach (var renderer in GetAllRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                foreach (var candidate in GetBlendshapeCandidates(blendshapeName))
                {
                    var index = FindBlendshapeIndex(renderer.sharedMesh, candidate);
                    if (index >= 0)
                    {
                        renderer.SetBlendShapeWeight(index, weight);
                    }
                }
            }
        }

        public bool TrySetBlendshapeWeight(string blendshapeName, float weight)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return false;
            }

            var applied = false;
            foreach (var renderer in GetAllRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                foreach (var candidate in GetBlendshapeCandidates(blendshapeName))
                {
                    var index = FindBlendshapeIndex(renderer.sharedMesh, candidate);
                    if (index < 0)
                    {
                        continue;
                    }

                    renderer.SetBlendShapeWeight(index, weight);
                    applied = true;
                }
            }

            return applied;
        }

        public float GetBlendshapeWeight(string blendshapeName)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return 0f;
            }

            var weight = 0f;
            foreach (var renderer in GetAllRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                foreach (var candidate in GetBlendshapeCandidates(blendshapeName))
                {
                    var index = FindBlendshapeIndex(renderer.sharedMesh, candidate);
                    if (index >= 0)
                    {
                        weight = Mathf.Max(weight, renderer.GetBlendShapeWeight(index));
                    }
                }
            }

            return weight;
        }

        public bool HasBlendshape(string blendshapeName)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return false;
            }

            return GetAllRenderers()
                .Any(renderer => renderer != null &&
                                 renderer.sharedMesh != null &&
                                 GetBlendshapeCandidates(blendshapeName).Any(candidate => FindBlendshapeIndex(renderer.sharedMesh, candidate) >= 0));
        }

        public List<string> GetBlendshapeNames()
        {
            var names = new HashSet<string>();
            foreach (var renderer in GetAllRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                var mesh = renderer.sharedMesh;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    names.Add(mesh.GetBlendShapeName(i));
                }
            }

            return names.ToList();
        }

        public IEnumerator ReleaseSpeakingWhenSourceStops(AudioSource source)
        {
            if (source == null)
            {
                yield break;
            }

            while (source.isPlaying)
            {
                yield return null;
            }

            SetSpeaking(false);
        }

        public void SetExpressionMode(bool active)
        {
            expressionModeActive = active;
            if (expressionModeActive)
            {
                SetBlendshapeWeight(jawOpenBlendshape, 0f);
                SetBlendshapeWeight(mouthFunnelBlendshape, 0f);
                SetBlendshapeWeight(browInnerUpBlendshape, 0f);
            }
        }

        private List<SkinnedMeshRenderer> GetAllRenderers()
        {
            var renderers = new List<SkinnedMeshRenderer>();
            if (targetRenderer != null)
            {
                renderers.Add(targetRenderer);
            }

            foreach (var renderer in additionalRenderers)
            {
                if (renderer != null && !renderers.Contains(renderer))
                {
                    renderers.Add(renderer);
                }
            }

            return renderers;
        }

        public static IReadOnlyList<string> ResolveBlendshapeCandidates(string blendshapeName)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return System.Array.Empty<string>();
            }

            var canonicalName = GetCanonicalBlendshapeName(blendshapeName);
            if (BlendshapeAliases.TryGetValue(canonicalName, out var aliases) && aliases != null && aliases.Length > 0)
            {
                if (string.Equals(canonicalName, blendshapeName, StringComparison.OrdinalIgnoreCase))
                {
                    return aliases;
                }

                var expanded = new string[aliases.Length + 1];
                expanded[0] = blendshapeName;
                Array.Copy(aliases, 0, expanded, 1, aliases.Length);
                return expanded;
            }

            return new[] { blendshapeName };
        }

        /// <summary>
        /// Imported FBX meshes usually prefix each blendshape with the mesh name
        /// (for example "MECHANICGIRL_HEAD.jawOpen"). Return the portable shape name
        /// so the same expression can be matched against every face mesh.
        /// </summary>
        public static string GetCanonicalBlendshapeName(string blendshapeName)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return string.Empty;
            }

            var value = blendshapeName.Trim();
            var separator = value.LastIndexOf('.');
            return separator >= 0 && separator < value.Length - 1
                ? value.Substring(separator + 1)
                : value;
        }

        /// <summary>
        /// Resolves a blendshape by exact name first, then by canonical name. This lets a
        /// head-authored expression name such as "MECHANICGIRL_HEAD.jawOpen" or the bare
        /// "jawOpen" drive "pasted__CCG_TEETH1.jawOpen" on the teeth mesh.
        /// </summary>
        public static int FindBlendshapeIndex(Mesh mesh, string blendshapeName)
        {
            if (mesh == null || string.IsNullOrWhiteSpace(blendshapeName))
            {
                return -1;
            }

            var index = mesh.GetBlendShapeIndex(blendshapeName);
            if (index >= 0)
            {
                return index;
            }

            var canonicalName = GetCanonicalBlendshapeName(blendshapeName);
            if (!string.Equals(canonicalName, blendshapeName, StringComparison.Ordinal))
            {
                index = mesh.GetBlendShapeIndex(canonicalName);
                if (index >= 0)
                {
                    return index;
                }
            }

            var canonicalSpan = canonicalName.AsSpan();
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var meshBlendshapeName = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(meshBlendshapeName))
                {
                    continue;
                }

                if (string.Equals(meshBlendshapeName, canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }

                var separator = meshBlendshapeName.LastIndexOf('.');
                var meshCanonicalSpan = separator >= 0
                    ? meshBlendshapeName.AsSpan(separator + 1)
                    : meshBlendshapeName.AsSpan();
                if (meshCanonicalSpan.Equals(canonicalSpan, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static IEnumerable<string> GetBlendshapeCandidates(string blendshapeName)
        {
            return ResolveBlendshapeCandidates(blendshapeName);
        }
    }
}
