// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nyxara.AICompanion.Face;
using UnityEngine;

namespace Nyxara.AICompanion.LipSync
{
    public class VisemeLipSyncController : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer faceRenderer;
        [SerializeField] private List<SkinnedMeshRenderer> additionalFaceRenderers = new();
        [SerializeField] private LipSyncData lipSyncData;
        [SerializeField] private PiperTTSPhonemeExtractor phonemeExtractor;
        [SerializeField] private AudioSource audioSource;

        [Header("Runtime Settings")]
        [SerializeField] private bool enableLipSync = true;
        [SerializeField] private float mouthOpenAmount = 0.45f;
        [SerializeField] private bool expressionModeActive;
        [SerializeField] private float visemeIntensityScale = 0.6f;
        [SerializeField] private float lowerLipDropAmount = 0.18f;
        [SerializeField] private float upperLipRaiseAmount = 0.08f;
        [SerializeField] private float mouthStretchAmount = 0.06f;
        [SerializeField] private float releaseDuration = 0.08f;

        private Coroutine _lipSyncCoroutine;
        private readonly Dictionary<string, float> _appliedBlendshapeWeights = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _trackedBlendshapeNames = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] JawHelperBlendshapeNames =
        {
            "jawOpen",
            "mouthLowerDownLeft",
            "mouthLowerDownRight",
            "mouthUpperUpLeft",
            "mouthUpperUpRight",
            "mouthStretchLeft",
            "mouthStretchRight"
        };

        public bool IsSpeaking { get; private set; }

        private void Awake()
        {
            CacheBlendshapeIndices();
        }

        private void CacheBlendshapeIndices()
        {
            _trackedBlendshapeNames.Clear();
            if (lipSyncData == null)
            {
                return;
            }

            foreach (var mapping in lipSyncData.visemeMappings)
            {
                if (mapping == null)
                {
                    continue;
                }

                foreach (var blendshapeName in mapping.EnumerateBlendshapeNames())
                {
                    _trackedBlendshapeNames.Add(blendshapeName);
                }
            }

            foreach (var blendshapeName in JawHelperBlendshapeNames)
            {
                _trackedBlendshapeNames.Add(blendshapeName);
            }
        }

        public async Task SpeakWithLipSync(string text, float clipDuration = -1f)
        {
            CacheBlendshapeIndices();

            if (expressionModeActive)
            {
                StopLipSync();
                return;
            }

            if (!enableLipSync || GetAllRenderers().Count == 0 || lipSyncData == null || phonemeExtractor == null)
            {
                if (_lipSyncCoroutine != null)
                {
                    StopCoroutine(_lipSyncCoroutine);
                }

                _lipSyncCoroutine = StartCoroutine(SimpleJawMovement());
                return;
            }

            if (_lipSyncCoroutine != null)
            {
                StopCoroutine(_lipSyncCoroutine);
            }

            IsSpeaking = true;
            var phonemeTimeline = await phonemeExtractor.ExtractPhonemesFromText(text);
            phonemeTimeline = RetimeTimelineToClipDuration(phonemeTimeline, clipDuration);
            _lipSyncCoroutine = StartCoroutine(ProcessLipSyncTimeline(phonemeTimeline));
        }

        private IEnumerator ProcessLipSyncTimeline(List<PiperTTSPhonemeExtractor.VisemeFrame> timeline)
        {
            if (timeline == null || timeline.Count == 0)
            {
                yield return ReleaseToSilence();
                _lipSyncCoroutine = null;
                IsSpeaking = false;
                yield break;
            }

            for (var i = 0; i < timeline.Count; i++)
            {
                var currentFrame = timeline[i];
                var nextFrame = i + 1 < timeline.Count ? timeline[i + 1] : new PiperTTSPhonemeExtractor.VisemeFrame
                {
                    viseme = Viseme.sil,
                    duration = releaseDuration
                };

                var duration = Mathf.Max(0.02f, currentFrame.duration);
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    var t = Mathf.Clamp01(elapsed / duration);
                    ApplyFrameBlend(currentFrame.viseme, nextFrame.viseme, Mathf.SmoothStep(0f, 1f, t), Time.deltaTime);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            yield return ReleaseToSilence();
            _lipSyncCoroutine = null;
            IsSpeaking = false;
        }

        private void ApplyViseme(Viseme viseme)
        {
            if (GetAllRenderers().Count == 0 || lipSyncData == null)
            {
                return;
            }

            ApplyImmediateTargets(BuildFrameTargets(viseme, viseme, 1f));
        }

        private void ApplyFrameBlend(Viseme currentViseme, Viseme nextViseme, float t, float deltaTime)
        {
            if (GetAllRenderers().Count == 0 || lipSyncData == null)
            {
                return;
            }

            ApplySmoothedTargets(BuildFrameTargets(currentViseme, nextViseme, t), deltaTime);
        }

        private Dictionary<string, float> BuildFrameTargets(Viseme currentViseme, Viseme nextViseme, float blend)
        {
            var targets = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (lipSyncData == null)
            {
                return targets;
            }

            var currentMapping = ResolveMapping(currentViseme);
            var nextMapping = ResolveMapping(nextViseme);

            if (currentMapping != null)
            {
                var currentWeight = Mathf.Lerp(currentMapping.intensity * visemeIntensityScale, 0f, blend);
                AddMappingTargets(targets, currentMapping, currentWeight);
            }

            if (nextMapping != null)
            {
                var nextWeight = nextMapping == currentMapping
                    ? Mathf.Lerp(nextMapping.intensity * visemeIntensityScale, nextMapping.intensity * visemeIntensityScale, blend)
                    : Mathf.Lerp(0f, nextMapping.intensity * visemeIntensityScale, blend);
                AddMappingTargets(targets, nextMapping, nextWeight);
            }

            var currentJaw = currentMapping != null ? currentMapping.jawOpenContribution : 0f;
            var nextJaw = nextMapping != null ? nextMapping.jawOpenContribution : 0f;
            AddJawTargets(targets, Mathf.Lerp(currentJaw, nextJaw, blend) * mouthOpenAmount);
            return targets;
        }

        private void AddMappingTargets(IDictionary<string, float> targets, VisemeMapping mapping, float rawWeight)
        {
            if (mapping == null)
            {
                return;
            }

            var shapedWeight = ShapeTargetWeight(rawWeight);
            foreach (var blendshapeName in mapping.EnumerateBlendshapeNames())
            {
                targets[blendshapeName] = shapedWeight;
            }
        }

        private void AddJawTargets(IDictionary<string, float> targets, float amount)
        {
            if (lipSyncData == null)
            {
                return;
            }

            var rawJawWeight = amount * lipSyncData.jawOpenMultiplier * 100f;
            var jawWeight = ShapeTargetWeight(rawJawWeight);
            targets["jawOpen"] = jawWeight;

            // These helpers expose the teeth/opening more naturally during speech.
            var lowerLipWeight = ShapeTargetWeight(rawJawWeight * lowerLipDropAmount);
            var upperLipWeight = ShapeTargetWeight(rawJawWeight * upperLipRaiseAmount);
            var stretchWeight = ShapeTargetWeight(rawJawWeight * mouthStretchAmount);
            targets["mouthLowerDownLeft"] = lowerLipWeight;
            targets["mouthLowerDownRight"] = lowerLipWeight;
            targets["mouthUpperUpLeft"] = upperLipWeight;
            targets["mouthUpperUpRight"] = upperLipWeight;
            targets["mouthStretchLeft"] = stretchWeight;
            targets["mouthStretchRight"] = stretchWeight;
        }

        private IEnumerator SimpleJawMovement()
        {
            IsSpeaking = true;
            var timer = 0f;
            const float duration = 1f;
            while (timer < duration)
            {
                var jawWeight = Mathf.PingPong(timer * 8f, 28f);
                ApplySmoothedTargets(BuildJawOnlyTargets(jawWeight / 100f), Time.deltaTime);
                timer += Time.deltaTime;
                yield return null;
            }

            yield return ReleaseToSilence();
            IsSpeaking = false;
        }

        public void StopLipSync()
        {
            if (_lipSyncCoroutine != null)
            {
                StopCoroutine(_lipSyncCoroutine);
                _lipSyncCoroutine = null;
            }

            ApplyViseme(Viseme.sil);
            IsSpeaking = false;
        }

        public void SetExpressionMode(bool active)
        {
            expressionModeActive = active;
            if (expressionModeActive)
            {
                StopLipSync();
            }
        }

        private void OnDestroy()
        {
            StopLipSync();
        }

        private List<PiperTTSPhonemeExtractor.VisemeFrame> RetimeTimelineToClipDuration(List<PiperTTSPhonemeExtractor.VisemeFrame> timeline, float clipDuration)
        {
            if (timeline == null || timeline.Count == 0 || clipDuration <= 0f)
            {
                return timeline ?? new List<PiperTTSPhonemeExtractor.VisemeFrame>();
            }

            var sourceDuration = timeline.Sum(frame => Mathf.Max(0f, frame.duration));
            if (sourceDuration <= 0.001f)
            {
                return timeline;
            }

            var scale = clipDuration / sourceDuration;
            var retimed = new List<PiperTTSPhonemeExtractor.VisemeFrame>(timeline.Count);
            var timestamp = 0f;
            foreach (var frame in timeline)
            {
                var duration = Mathf.Max(0.02f, frame.duration * scale);
                retimed.Add(new PiperTTSPhonemeExtractor.VisemeFrame
                {
                    viseme = frame.viseme,
                    timestamp = timestamp,
                    duration = duration
                });
                timestamp += duration;
            }

            return retimed;
        }

        private IEnumerator ReleaseToSilence()
        {
            var elapsed = 0f;
            while (elapsed < releaseDuration)
            {
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, releaseDuration));
                ApplyFrameBlend(Viseme.sil, Viseme.sil, t, Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            ApplyImmediateTargets(new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase));
        }

        private VisemeMapping ResolveMapping(Viseme viseme)
        {
            return lipSyncData != null ? lipSyncData.visemeMappings.Find(m => m.viseme == viseme) : null;
        }

        private void ResetMappedBlendshapes()
        {
            ApplyImmediateTargets(new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase));
        }

        private Dictionary<string, float> BuildJawOnlyTargets(float amount)
        {
            var targets = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            AddJawTargets(targets, amount);
            return targets;
        }

        private void ApplySmoothedTargets(IReadOnlyDictionary<string, float> targets, float deltaTime)
        {
            foreach (var blendshapeName in _trackedBlendshapeNames)
            {
                var targetWeight = targets != null && targets.TryGetValue(blendshapeName, out var target) ? target : 0f;
                var currentWeight = _appliedBlendshapeWeights.TryGetValue(blendshapeName, out var current)
                    ? current
                    : GetBlendshapeWeight(blendshapeName);
                var smoothing = 1f - Mathf.Exp(-Mathf.Max(1f, lipSyncData.responseSmoothing) * Mathf.Max(0.001f, deltaTime));
                var nextWeight = Mathf.Lerp(currentWeight, targetWeight, smoothing);
                SetBlendshapeWeight(blendshapeName, nextWeight);
                _appliedBlendshapeWeights[blendshapeName] = nextWeight;
            }
        }

        private void ApplyImmediateTargets(IReadOnlyDictionary<string, float> targets)
        {
            foreach (var blendshapeName in _trackedBlendshapeNames)
            {
                var targetWeight = targets != null && targets.TryGetValue(blendshapeName, out var target) ? target : 0f;
                SetBlendshapeWeight(blendshapeName, targetWeight);
                _appliedBlendshapeWeights[blendshapeName] = targetWeight;
            }
        }

        private float ShapeTargetWeight(float rawWeight)
        {
            var normalized = Mathf.Clamp01(rawWeight / 100f);
            var responseEnd = Mathf.Max(lipSyncData.responseStart + 0.001f, lipSyncData.responseEnd);
            var mapped = Mathf.InverseLerp(lipSyncData.responseStart, responseEnd, normalized);
            var shaped = Mathf.Pow(Mathf.Clamp01(mapped), Mathf.Max(0.01f, lipSyncData.responseFalloff));
            return shaped * 100f;
        }

        private float GetBlendshapeWeight(string blendshapeName)
        {
            if (string.IsNullOrWhiteSpace(blendshapeName))
            {
                return 0f;
            }

            foreach (var renderer in GetAllRenderers())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                foreach (var candidate in ArkItBlendshapeDriver.ResolveBlendshapeCandidates(blendshapeName))
                {
                    var index = renderer.sharedMesh.GetBlendShapeIndex(candidate);
                    if (index >= 0)
                    {
                        return renderer.GetBlendShapeWeight(index);
                    }
                }
            }

            return 0f;
        }

        private void SetBlendshapeWeight(string blendshapeName, float weight)
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

                foreach (var candidate in ArkItBlendshapeDriver.ResolveBlendshapeCandidates(blendshapeName))
                {
                    var index = renderer.sharedMesh.GetBlendShapeIndex(candidate);
                    if (index >= 0)
                    {
                        renderer.SetBlendShapeWeight(index, weight);
                    }
                }
            }
        }

        private List<SkinnedMeshRenderer> GetAllRenderers()
        {
            var renderers = new List<SkinnedMeshRenderer>();
            if (faceRenderer != null)
            {
                renderers.Add(faceRenderer);
            }

            foreach (var renderer in additionalFaceRenderers)
            {
                if (renderer != null && !renderers.Contains(renderer))
                {
                    renderers.Add(renderer);
                }
            }

            return renderers;
        }
    }
}
