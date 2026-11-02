// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Nyxara.AICompanion.LipSync
{
    public class PiperTTSPhonemeExtractor : MonoBehaviour
    {
        [SerializeField] private string piperExecutablePath;
        [SerializeField] private string voiceModelPath;

        private static readonly Dictionary<string, Viseme> PhonemeToViseme = new()
        {
            ["AA"] = Viseme.AA, ["AA0"] = Viseme.AA, ["AA1"] = Viseme.AA, ["AA2"] = Viseme.AA,
            ["IY"] = Viseme.IY, ["IY0"] = Viseme.IY, ["IY1"] = Viseme.IY, ["IY2"] = Viseme.IY,
            ["UH"] = Viseme.UH, ["UH0"] = Viseme.UH, ["UH1"] = Viseme.UH, ["UH2"] = Viseme.UH,
            ["OW"] = Viseme.OW, ["OW0"] = Viseme.OW, ["OW1"] = Viseme.OW, ["OW2"] = Viseme.OW,
            ["EH"] = Viseme.EH, ["EH0"] = Viseme.EH, ["EH1"] = Viseme.EH, ["EH2"] = Viseme.EH,
            ["IH"] = Viseme.IH, ["IH0"] = Viseme.IH, ["IH1"] = Viseme.IH, ["IH2"] = Viseme.IH,
            ["AH"] = Viseme.AH, ["AH0"] = Viseme.AH, ["AH1"] = Viseme.AH, ["AH2"] = Viseme.AH,
            ["AW"] = Viseme.AW, ["AW0"] = Viseme.AW, ["AW1"] = Viseme.AW, ["AW2"] = Viseme.AW,
            ["EY"] = Viseme.EY, ["EY0"] = Viseme.EY, ["EY1"] = Viseme.EY, ["EY2"] = Viseme.EY,
            ["ER"] = Viseme.ER, ["ER0"] = Viseme.ER, ["ER1"] = Viseme.ER, ["ER2"] = Viseme.ER,
            ["AO"] = Viseme.AO, ["AO0"] = Viseme.AO, ["AO1"] = Viseme.AO, ["AO2"] = Viseme.AO,
            ["OY"] = Viseme.OY, ["OY0"] = Viseme.OY, ["OY1"] = Viseme.OY, ["OY2"] = Viseme.OY,
            ["TH"] = Viseme.TH, ["DH"] = Viseme.DH,
            ["F"] = Viseme.FV, ["V"] = Viseme.FV,
            ["S"] = Viseme.SZ, ["Z"] = Viseme.SZ,
            ["SH"] = Viseme.SH, ["ZH"] = Viseme.SH,
            ["HH"] = Viseme.HH,
            ["M"] = Viseme.M,
            ["N"] = Viseme.N, ["NG"] = Viseme.NG,
            ["L"] = Viseme.L,
            ["R"] = Viseme.R,
            ["Y"] = Viseme.Y,
            ["W"] = Viseme.W,
            ["B"] = Viseme.BPM, ["P"] = Viseme.BPM,
            ["D"] = Viseme.DT, ["T"] = Viseme.DT,
            ["G"] = Viseme.GK, ["K"] = Viseme.GK,
            ["CH"] = Viseme.SH, ["JH"] = Viseme.SH,
            ["SIL"] = Viseme.sil
        };

        [Serializable]
        public struct VisemeFrame
        {
            public Viseme viseme;
            public float timestamp;
            public float duration;
        }

        public async Task<List<VisemeFrame>> ExtractPhonemesFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<VisemeFrame>();
            }

            await Task.Yield();
            var phonemes = EstimatePhonemeTiming(text);
            return ConvertToVisemeTimeline(phonemes);
        }

        private List<(string phoneme, float start, float end)> EstimatePhonemeTiming(string text)
        {
            var result = new List<(string, float, float)>();
            var phonemes = ConvertTextToPhonemes(text);
            var currentTime = 0f;
            const float durationPerPhoneme = 0.08f;

            foreach (var phoneme in phonemes)
            {
                result.Add((phoneme, currentTime, currentTime + durationPerPhoneme));
                currentTime += durationPerPhoneme;
            }

            return result;
        }

        private List<string> ConvertTextToPhonemes(string text)
        {
            var phonemes = new List<string>();
            var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                foreach (var c in word)
                {
                    var phoneme = CharToPhoneme(c);
                    if (!string.IsNullOrEmpty(phoneme))
                    {
                        phonemes.Add(phoneme);
                    }
                }

                phonemes.Add("SIL");
            }

            return phonemes;
        }

        private static string CharToPhoneme(char c)
        {
            return c switch
            {
                'a' => "AA",
                'e' => "EH",
                'i' => "IH",
                'o' => "OW",
                'u' => "UH",
                'b' or 'p' => "B",
                'd' or 't' => "D",
                'g' or 'k' => "G",
                'f' or 'v' => "F",
                's' or 'z' => "S",
                'm' => "M",
                'n' => "N",
                'l' => "L",
                'r' => "R",
                'h' => "HH",
                'w' => "W",
                'y' => "Y",
                _ => "SIL"
            };
        }

        private List<VisemeFrame> ConvertToVisemeTimeline(List<(string phoneme, float start, float end)> phonemeTimeline)
        {
            var result = new List<VisemeFrame>();
            foreach (var (phoneme, start, end) in phonemeTimeline)
            {
                result.Add(new VisemeFrame
                {
                    viseme = PhonemeToViseme.GetValueOrDefault(phoneme, Viseme.sil),
                    timestamp = start,
                    duration = end - start
                });
            }

            return result;
        }
    }
}
