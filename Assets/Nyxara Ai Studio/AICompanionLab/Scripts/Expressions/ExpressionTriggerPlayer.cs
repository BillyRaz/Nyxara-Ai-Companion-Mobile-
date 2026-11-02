// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nyxara.AICompanion.Data;
using Nyxara.AICompanion.Face;
using UnityEngine;

namespace Nyxara.AICompanion.Expressions
{
    public class ExpressionTriggerPlayer : MonoBehaviour
    {
        [SerializeField] private CharacterProfileData characterProfile;
        [SerializeField] private ArkItBlendshapeDriver faceDriver;
        [SerializeField] private ExpressionLibraryManager expressionLibrary;

        private readonly Dictionary<string, float> _perTriggerLastPlayedAt = new(System.StringComparer.OrdinalIgnoreCase);
        private Coroutine _playbackRoutine;
        private string _lastTriggerKey = string.Empty;
        private float _lastTriggerIntensity = 1f;
        private float _lastGlobalTriggerTime = -999f;
        private float _lastDuplicateTriggerTime = -999f;

        public CharacterProfileData CharacterProfile => characterProfile;

        private void Awake()
        {
            AutoResolveDependencies();
        }

        private void OnValidate()
        {
            AutoResolveDependencies();
        }

        public void SetProfile(CharacterProfileData profile)
        {
            characterProfile = profile;
        }

        public bool TryPlayTrigger(string triggerKey, float intensity = 1f)
        {
            AutoResolveDependencies();
            if (!Application.isPlaying || faceDriver == null || characterProfile?.expressionRouting == null)
            {
                return false;
            }

            var routing = characterProfile.expressionRouting;
            if (!routing.expressionTagSupport)
            {
                return false;
            }

            var mapping = characterProfile.ResolveExpressionTrigger(triggerKey);
            if (mapping == null)
            {
                return false;
            }

            var now = Time.time;
            var normalizedIntensity = Mathf.Clamp01(intensity);
            if (routing.useGlobalCooldown && now - _lastGlobalTriggerTime < routing.globalCooldown)
            {
                return false;
            }

            if (_perTriggerLastPlayedAt.TryGetValue(mapping.triggerKey, out var perTriggerTime) &&
                now - perTriggerTime < mapping.cooldown)
            {
                return false;
            }

            if (routing.ignoreDuplicateTriggers &&
                string.Equals(_lastTriggerKey, mapping.triggerKey, System.StringComparison.OrdinalIgnoreCase) &&
                now - _lastDuplicateTriggerTime < routing.duplicateMemoryWindow &&
                Mathf.Abs(_lastTriggerIntensity - normalizedIntensity) < routing.duplicateIntensityThreshold)
            {
                return false;
            }

            if (_playbackRoutine != null)
            {
                StopCoroutine(_playbackRoutine);
            }

            _lastGlobalTriggerTime = now;
            _lastDuplicateTriggerTime = now;
            _lastTriggerKey = mapping.triggerKey;
            _lastTriggerIntensity = normalizedIntensity;
            _perTriggerLastPlayedAt[mapping.triggerKey] = now;
            _playbackRoutine = StartCoroutine(PlayMappingRoutine(mapping, normalizedIntensity, routing));
            return true;
        }

        public Dictionary<string, float> ResolveTargetWeights(string triggerKey, float intensity = 1f)
        {
            var mapping = characterProfile?.ResolveExpressionTrigger(triggerKey);
            return mapping?.BuildTargetWeights(intensity) ?? new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerator PlayMappingRoutine(
            CharacterExpressionTriggerMapping mapping,
            float intensity,
            ExpressionRoutingSettings routing)
        {
            var targets = mapping.BuildTargetWeights(intensity);
            if (targets.Count == 0)
            {
                yield break;
            }

            var baseline = CaptureCurrentWeights(targets.Keys);
            var mouthKeys = targets.Keys.Where(ExpressionBuilderHelper.IsMouthRelatedBlendshape).ToList();
            var nonMouthKeys = targets.Keys.Except(mouthKeys).ToList();

            yield return BlendWeights(baseline, targets, mapping.blendSpeed, includeMouth: true);

            var mouthReleaseAt = Time.time + Mathf.Min(mapping.holdDuration, routing.useLipSafePlayback ? routing.mouthHitDuration : mapping.holdDuration);
            var holdEndAt = Time.time + mapping.holdDuration;
            while (Time.time < holdEndAt)
            {
                foreach (var key in nonMouthKeys)
                {
                    faceDriver.TrySetBlendshapeWeight(key, targets[key]);
                }

                if (routing.useLipSafePlayback)
                {
                    foreach (var key in mouthKeys)
                    {
                        var value = Time.time < mouthReleaseAt
                            ? targets[key]
                            : baseline.TryGetValue(key, out var baselineWeight) ? baselineWeight : 0f;
                        faceDriver.TrySetBlendshapeWeight(key, value);
                    }
                }
                else
                {
                    foreach (var key in mouthKeys)
                    {
                        faceDriver.TrySetBlendshapeWeight(key, targets[key]);
                    }
                }

                yield return null;
            }

            yield return BlendWeights(CaptureCurrentWeights(targets.Keys), baseline, mapping.returnSpeed, includeMouth: true);
            _playbackRoutine = null;
        }

        private IEnumerator BlendWeights(
            IReadOnlyDictionary<string, float> from,
            IReadOnlyDictionary<string, float> to,
            float speed,
            bool includeMouth)
        {
            var activeKeys = to.Keys.ToList();
            var settled = false;
            while (!settled)
            {
                settled = true;
                foreach (var key in activeKeys)
                {
                    if (!includeMouth && ExpressionBuilderHelper.IsMouthRelatedBlendshape(key))
                    {
                        continue;
                    }

                    var current = faceDriver.GetBlendshapeWeight(key);
                    var target = to.TryGetValue(key, out var value) ? value : 0f;
                    var next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0.01f, speed) * Time.deltaTime));
                    faceDriver.TrySetBlendshapeWeight(key, next);
                    if (Mathf.Abs(next - target) > 0.25f)
                    {
                        settled = false;
                    }
                }

                yield return null;
            }

            foreach (var key in activeKeys)
            {
                faceDriver.TrySetBlendshapeWeight(key, to.TryGetValue(key, out var target) ? target : 0f);
            }
        }

        private Dictionary<string, float> CaptureCurrentWeights(IEnumerable<string> keys)
        {
            var captured = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                captured[key] = faceDriver != null ? faceDriver.GetBlendshapeWeight(key) : 0f;
            }

            return captured;
        }

        private void AutoResolveDependencies()
        {
            if (faceDriver == null)
            {
                faceDriver = GetComponent<ArkItBlendshapeDriver>() ?? FindFirstObjectByType<ArkItBlendshapeDriver>();
            }

            if (expressionLibrary == null)
            {
                expressionLibrary = GetComponent<ExpressionLibraryManager>() ?? FindFirstObjectByType<ExpressionLibraryManager>();
            }
        }
    }
}
