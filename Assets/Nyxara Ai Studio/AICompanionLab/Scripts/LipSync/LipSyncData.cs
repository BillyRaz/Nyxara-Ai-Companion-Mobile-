// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nyxara.AICompanion.LipSync
{
    [CreateAssetMenu(fileName = "NewLipSyncData", menuName = "AI Companion/Lip Sync Data")]
    public class LipSyncData : ScriptableObject
    {
        [Header("Viseme to Blendshape Mapping")]
        public List<VisemeMapping> visemeMappings = new();

        [Header("Animation Settings")]
        public float smoothTime = 0.05f;
        public float jawOpenMultiplier = 0.7f;
        [Range(0f, 0.95f)] public float responseStart = 0f;
        [Range(0.05f, 1f)] public float responseEnd = 1f;
        [Range(0.25f, 3f)] public float responseFalloff = 1.35f;
        [Range(1f, 25f)] public float responseSmoothing = 12f;

        [Header("Debug")]
        public bool showDebugInfo;
    }

    [Serializable]
    public class VisemeMapping
    {
        public Viseme viseme;
        public string blendshapeName;
        [Range(0f, 100f)] public float intensity = 100f;
        public float jawOpenContribution;

        public IEnumerable<string> EnumerateBlendshapeNames()
        {
            return string.IsNullOrWhiteSpace(blendshapeName)
                ? Enumerable.Empty<string>()
                : blendshapeName
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name));
        }
    }

    public enum Viseme
    {
        sil,
        AA,
        IY,
        UH,
        OW,
        EH,
        IH,
        AH,
        AW,
        EY,
        ER,
        AO,
        OY,
        TH,
        DH,
        FV,
        SZ,
        SH,
        HH,
        M,
        N,
        NG,
        L,
        R,
        Y,
        W,
        BPM,
        DT,
        GK
    }
}
