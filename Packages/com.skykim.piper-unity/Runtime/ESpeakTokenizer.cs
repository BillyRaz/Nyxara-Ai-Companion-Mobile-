using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;

public class AudioConfig
{
    public int sample_rate { get; set; }
    public string quality { get; set; }
}

public class ESpeakConfig
{
    public string voice { get; set; }
}

public class InferenceConfig
{
    public float noise_scale { get; set; }
    public float length_scale { get; set; }
    public float noise_w { get; set; }
}

public class PiperConfig
{
    public AudioConfig audio { get; set; }
    public ESpeakConfig espeak { get; set; }
    public InferenceConfig inference { get; set; }
    public string phoneme_type { get; set; }

    [JsonProperty("phoneme_id_map")]
    public Dictionary<string, int[]> PhonemeIdMap { get; set; }
}

public class ESpeakTokenizer : MonoBehaviour
{
    public TextAsset jsonFile;

    public int SampleRate { get; private set; }
    public string Quality { get; private set; }
    public string Voice { get; private set; }
    public string PhonemeType { get; private set; }

    /// <summary>True when the JSON config has been parsed and the tokenizer can be used.</summary>
    public bool IsReady => isInitialized;

    private PiperConfig config;
    private float[] inferenceParams;
    private bool isInitialized;
    private TextAsset initializedJsonFile;

    void Awake()
    {
        // When added at runtime the caller may assign jsonFile after AddComponent. In that case the
        // tokenizer initializes lazily on the first Tokenize/GetInferenceParams/Initialize call.
        if (jsonFile != null)
        {
            Initialize();
        }
    }

    /// <summary>
    /// Parses the voice JSON config. Safe to call repeatedly; re-parses only when the assigned file changes
    /// or a previous attempt failed.
    /// </summary>
    /// <returns>True when the tokenizer is ready.</returns>
    public bool Initialize()
    {
        if (isInitialized && initializedJsonFile == jsonFile)
        {
            return true;
        }

        if (jsonFile == null)
        {
            Debug.LogError("[ESpeakTokenizer] JSON file is not assigned. Please assign the voice .onnx.json file.");
            return false;
        }

        config = null;
        isInitialized = false;
        initializedJsonFile = null;

        try
        {
            config = JsonConvert.DeserializeObject<PiperConfig>(jsonFile.text);
            if (config == null || config.audio == null || config.espeak == null || config.inference == null || config.PhonemeIdMap == null)
            {
                Debug.LogError("[ESpeakTokenizer] JSON data is missing required fields or is invalid.");
                config = null;
                return false;
            }
        }
        catch (JsonException ex)
        {
            Debug.LogError($"[ESpeakTokenizer] Failed to parse JSON file. Error: {ex.Message}");
            config = null;
            return false;
        }

        inferenceParams = new float[3]
        {
            config.inference.noise_scale,
            config.inference.length_scale,
            config.inference.noise_w
        };

        SampleRate = config.audio.sample_rate;
        Quality = config.audio.quality;
        Voice = config.espeak.voice;
        PhonemeType = config.phoneme_type;

        initializedJsonFile = jsonFile;
        isInitialized = true;
        Debug.Log($"[ESpeakTokenizer] Config loaded. SampleRate={SampleRate}, Voice='{Voice}', PhonemeType='{PhonemeType}'.");
        return true;
    }

    /// <summary>Initializes on demand. Used by callers that configure the component at runtime.</summary>
    public bool EnsureInitialized()
    {
        return isInitialized && initializedJsonFile == jsonFile ? true : Initialize();
    }

    public int[] Tokenize(string[] phonemes)
    {
        if (!EnsureInitialized())
        {
            Debug.LogError("[ESpeakTokenizer] Tokenizer is not initialized.");
            return null;
        }

        int estimatedCapacity = (phonemes != null ? phonemes.Length * 2 : 0) + 3;
        var tokenizedList = new List<int>(estimatedCapacity) { 1, 0 };

        if (phonemes != null && phonemes.Length > 0)
        {
            foreach (string phoneme in phonemes)
            {
                if (config.PhonemeIdMap.TryGetValue(phoneme, out int[] ids) && ids.Length > 0)
                {
                    tokenizedList.Add(ids[0]);
                    tokenizedList.Add(0);
                }
                else
                {
                    Debug.LogWarning($"Token not found for phoneme: '{phoneme}'. It will be skipped.");
                }
            }
        }

        tokenizedList.Add(2);

        return tokenizedList.ToArray();
    }

    public float[] GetInferenceParams()
    {
        if (!EnsureInitialized())
        {
            Debug.LogError("[ESpeakTokenizer] Tokenizer is not initialized. Cannot get inference parameters.");
            return null;
        }

        return (float[])inferenceParams.Clone();
    }
}
