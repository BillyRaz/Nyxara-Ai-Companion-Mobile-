using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.IO.Compression;
#endif
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(AudioSource))]
public class PiperManager : MonoBehaviour
{
    private const int MaxChunkCharacters = 400;
    private const string EspeakExtractionMarker = ".piper-extracted";

    [Header("Model")]
    [Tooltip("Piper voice model imported by Unity's Inference Engine (must be a .onnx asset inside Assets/).")]
    public ModelAsset modelAsset;

    [Tooltip("Tokenizer that owns the matching .onnx.json voice config.")]
    public ESpeakTokenizer tokenizer;

    public Text voiceNameText;

    [Tooltip("Folder containing the espeak-ng-data directory. Relative paths are resolved against StreamingAssets.")]
    public string espeakDataDirectory = "espeak-ng-data";

    [Tooltip("Speaker index used by multi-speaker voices. clean100 maps \"6209\" to 0.")]
    public int speakerId = 0;

    [Tooltip("Initialize automatically from Start(). Set false when the component is configured at runtime.")]
    public bool initializeOnStart = true;

    [Header("Punctuation pauses")]
    [Range(0.0f, 1.0f)] public float commaDelay = 0.1f;
    [Range(0.0f, 1.0f)] public float periodDelay = 0.5f;
    [Range(0.0f, 1.0f)] public float questionExclamationDelay = 0.6f;

    /// <summary>True when the model, tokenizer and eSpeak data are ready for inference.</summary>
    public bool IsInitialized => isInitialized;

    /// <summary>True while the initialization coroutine is running.</summary>
    public bool IsInitializing => isInitializing;

    /// <summary>True while a synthesis request owns the inference worker.</summary>
    public bool IsSynthesizing => synthesisCoroutine != null;

    /// <summary>Human readable reason for the last initialization failure.</summary>
    public string InitializationError => initializationError;

    /// <summary>Raised once per initialization attempt. Argument is true on success.</summary>
    public event Action<bool> InitializationCompleted;

    private Worker engine;
    private AudioSource audioSource;
    private bool isInitialized;
    private bool isInitializing;
    private bool hasSidKey;
    private string initializationError;

    private Coroutine initializationCoroutine;
    private Coroutine synthesisCoroutine;
    private Action<AudioClip> activeOnComplete;
    private Action<string> activeOnError;

    private static bool espeakInitialized;
    private static string espeakInitializedDataPath;

    private struct SpeechChunk
    {
        public string Text;
        public float PauseAfter;
    }

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.LogWarning("[PiperManager] AudioSource component not found. One will be added automatically.");
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    void Start()
    {
        if (initializeOnStart && !isInitialized && !isInitializing)
        {
            Initialize();
        }
    }

    void OnDestroy()
    {
        CancelSynthesis();
        if (initializationCoroutine != null)
        {
            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }

        isInitializing = false;
        isInitialized = false;
        engine?.Dispose();
        engine = null;
    }

    /// <summary>
    /// Starts initialization if it has not already started. Safe to call multiple times.
    /// Use <see cref="IsInitialized"/>, <see cref="IsInitializing"/> and
    /// <see cref="InitializationCompleted"/> to observe progress.
    /// </summary>
    public void Initialize()
    {
        if (isInitialized || isInitializing)
        {
            return;
        }

        if (!isActiveAndEnabled)
        {
            SetInitializationFailed("PiperManager must be active and enabled before it can initialize.");
            return;
        }

        isInitializing = true;
        initializationError = null;
        initializationCoroutine = StartCoroutine(InitializePiper());
    }

    private IEnumerator InitializePiper()
    {
        // Give the caller one frame to finish assigning fields when the component was added at runtime.
        yield return null;

        if (modelAsset == null)
        {
            FailInitialization("No voice model assigned. Assign a Piper .onnx ModelAsset to 'Model Asset'.");
            yield break;
        }

        if (tokenizer == null)
        {
            FailInitialization("No tokenizer assigned. Assign an ESpeakTokenizer with the voice .onnx.json config.");
            yield break;
        }

        if (!tokenizer.IsReady && !tokenizer.Initialize())
        {
            FailInitialization("Tokenizer could not be initialized. Assign the voice .onnx.json file to the ESpeakTokenizer.");
            yield break;
        }

        string espeakDataPath = ResolveEspeakDataDirectory();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!IsEspeakDataReady(espeakDataPath))
        {
            yield return StartCoroutine(ExtractEspeakDataOnAndroid(espeakDataPath));
            if (!IsEspeakDataReady(espeakDataPath))
            {
                FailInitialization($"eSpeak data was not found at '{espeakDataPath}'. Ship '{(string.IsNullOrWhiteSpace(espeakDataDirectory) ? "espeak-ng-data" : espeakDataDirectory)}.zip' in StreamingAssets for Android.");
                yield break;
            }
        }
#else
        if (!Directory.Exists(espeakDataPath))
        {
            FailInitialization($"eSpeak data directory was not found at '{espeakDataPath}'. Expected a copy of 'espeak-ng-data' under StreamingAssets.");
            yield break;
        }

        yield return null;
#endif

        try
        {
            if (!InitializeESpeak(espeakDataPath))
            {
                FailInitialization($"eSpeak-ng could not be initialized with data at '{espeakDataPath}'.");
                yield break;
            }

            var model = ModelLoader.Load(modelAsset);
            engine?.Dispose();
            engine = null;
            engine = new Worker(model, BackendType.CPU);

            hasSidKey = model.inputs.Any(input => string.Equals(input.name, "sid", StringComparison.Ordinal));

            if (voiceNameText != null)
            {
                voiceNameText.text = $"Model: {modelAsset.name}";
            }
        }
        catch (Exception ex)
        {
            engine?.Dispose();
            engine = null;
            FailInitialization($"Failed to load the voice model: {ex.Message}");
            yield break;
        }

        // Warm up before reporting readiness so the first spoken line is not delayed.
        string warmupError;
        if (!TrySynthesizeChunk("hello", out _, out warmupError))
        {
            FailInitialization($"Model warmup failed: {warmupError}");
            yield break;
        }

        initializationCoroutine = null;
        isInitializing = false;
        isInitialized = true;
        Debug.Log($"[PiperManager] Initialized voice '{modelAsset.name}' (speaker {speakerId}).");
        InitializationCompleted?.Invoke(true);
    }

    private bool InitializeESpeak(string dataPath)
    {
        // eSpeak-ng keeps process-wide state. Re-initializing it while other components use it can crash native code,
        // so only re-run the native init when the data directory actually changes.
        if (!espeakInitialized || !string.Equals(espeakInitializedDataPath, dataPath, StringComparison.Ordinal))
        {
            int initResult;
            try
            {
                initResult = ESpeakNG.espeak_Initialize(0, 0, dataPath, 0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PiperManager] eSpeak-ng native call failed: {ex}");
                return false;
            }

            if (initResult <= 0)
            {
                Debug.LogError($"[PiperManager] eSpeak-ng initialization FAILED. Error code: {initResult}");
                return false;
            }

            espeakInitialized = true;
            espeakInitializedDataPath = dataPath;
            Debug.Log($"[PiperManager] eSpeak-ng initialized. Data path: {dataPath}");
        }

        if (tokenizer == null || string.IsNullOrEmpty(tokenizer.Voice))
        {
            Debug.LogError("[PiperManager] Tokenizer is not assigned or has no voice name.");
            return false;
        }

        string voiceName = tokenizer.Voice;
        int voiceResult = ESpeakNG.espeak_SetVoiceByName(voiceName);
        if (voiceResult != 0)
        {
            Debug.LogError($"[PiperManager] Set voice to '{voiceName}' FAILED. Error code: {voiceResult}");
            return false;
        }

        return true;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static bool IsEspeakDataReady(string dataPath)
    {
        return Directory.Exists(dataPath) &&
               File.Exists(Path.Combine(dataPath, EspeakExtractionMarker));
    }

    private IEnumerator ExtractEspeakDataOnAndroid(string targetDirectory)
    {
        string folderName = string.IsNullOrWhiteSpace(espeakDataDirectory)
            ? "espeak-ng-data"
            : Path.GetFileName(espeakDataDirectory.Replace('\\', '/').TrimEnd('/'));

        string zipSourcePath = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" +
            espeakDataDirectory.Replace('\\', '/').Trim('/') + ".zip";
        string zipDestPath = Path.Combine(Application.persistentDataPath, folderName + ".zip");
        string tempDirectory = targetDirectory + ".tmp";

        Debug.Log($"[PiperManager] Android: extracting eSpeak data from '{zipSourcePath}'.");

        using (var request = UnityEngine.Networking.UnityWebRequest.Get(zipSourcePath))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[PiperManager] Failed to read eSpeak data zip: {request.error}");
                yield break;
            }

            File.WriteAllBytes(zipDestPath, request.downloadHandler.data);
        }

        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }

            Directory.CreateDirectory(tempDirectory);

            using (var archive = new ZipArchive(File.OpenRead(zipDestPath), ZipArchiveMode.Read))
            {
                string tempRoot = Path.GetFullPath(tempDirectory) + Path.DirectorySeparatorChar;

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // Directory entry.
                        continue;
                    }

                    string relativeName = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (relativeName.StartsWith(folderName + "/", StringComparison.Ordinal))
                    {
                        relativeName = relativeName.Substring(folderName.Length + 1);
                    }

                    if (string.IsNullOrEmpty(relativeName))
                    {
                        continue;
                    }

                    string destinationPath = Path.GetFullPath(Path.Combine(tempDirectory, relativeName));
                    if (!destinationPath.StartsWith(tempRoot, StringComparison.Ordinal))
                    {
                        Debug.LogWarning($"[PiperManager] Skipping unsafe zip entry '{entry.FullName}'.");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    using (var source = entry.Open())
                    using (var destination = File.Create(destinationPath))
                    {
                        source.CopyTo(destination);
                    }
                }
            }

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }

            Directory.Move(tempDirectory, targetDirectory);
            File.WriteAllText(Path.Combine(targetDirectory, EspeakExtractionMarker), "ok");
            Debug.Log("[PiperManager] eSpeak data extracted to persistentDataPath.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PiperManager] Failed to extract eSpeak data: {ex.Message}");

            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
            catch (Exception cleanupEx)
            {
                Debug.LogWarning($"[PiperManager] Could not clean up the temporary eSpeak folder: {cleanupEx.Message}");
            }
        }
        finally
        {
            if (File.Exists(zipDestPath))
            {
                File.Delete(zipDestPath);
            }
        }
    }
#endif

    private string ResolveEspeakDataDirectory()
    {
        string configured = string.IsNullOrWhiteSpace(espeakDataDirectory) ? "espeak-ng-data" : espeakDataDirectory;
        string normalized = configured.Replace('\\', '/').TrimEnd('/');

#if UNITY_ANDROID && !UNITY_EDITOR
        string folderName = System.IO.Path.GetFileName(normalized);
        return System.IO.Path.Combine(Application.persistentDataPath, folderName);
#else
        if (System.IO.Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        return System.IO.Path.Combine(
            Application.streamingAssetsPath,
            normalized.Replace('/', System.IO.Path.DirectorySeparatorChar));
#endif
    }

    private void FailInitialization(string message)
    {
        engine?.Dispose();
        engine = null;
        SetInitializationFailed(message);
        InitializationCompleted?.Invoke(false);
    }

    private void SetInitializationFailed(string message)
    {
        initializationError = message;
        initializationCoroutine = null;
        isInitializing = false;
        isInitialized = false;
        Debug.LogError($"[PiperManager] {message}");
    }

    // ---------------------------------------------------------------------
    // Synthesis
    // ---------------------------------------------------------------------

    /// <summary>
    /// Synthesizes <paramref name="text"/> into a single AudioClip on the main thread.
    /// Only one request may run at a time; concurrent calls fail through <paramref name="onError"/>.
    /// </summary>
    /// <returns>True when the request was accepted.</returns>
    public bool Synthesize(string text, Action<AudioClip> onComplete, Action<string> onError)
    {
        if (!isInitialized || engine == null)
        {
            onError?.Invoke("Piper is not initialized.");
            return false;
        }

        if (synthesisCoroutine != null)
        {
            onError?.Invoke("A synthesis request is already running.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            onComplete?.Invoke(null);
            return true;
        }

        synthesisCoroutine = StartCoroutine(SynthesizeRoutine(text, onComplete, onError));
        return true;
    }

    /// <summary>Stops the active synthesis request, if any, and reports cancellation.</summary>
    public void CancelSynthesis()
    {
        if (synthesisCoroutine == null)
        {
            return;
        }

        StopCoroutine(synthesisCoroutine);

        var onError = activeOnError;
        activeOnComplete = null;
        activeOnError = null;
        synthesisCoroutine = null;
        onError?.Invoke("Synthesis was cancelled.");
    }

    public void OnSubmitText(Text textField)
    {
        if (textField == null || string.IsNullOrEmpty(textField.text))
        {
            Debug.LogError("Input text is empty. Please enter some text.");
            return;
        }

        SynthesizeAndPlay(textField.text);
    }

    public void SynthesizeAndPlay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Synthesize(
            text,
            clip =>
            {
                if (clip == null || audioSource == null)
                {
                    return;
                }

                audioSource.PlayOneShot(clip);
                StartCoroutine(DestroyClipWhenFinished(clip));
            },
            error => Debug.LogError($"[PiperManager] {error}"));
    }

    private IEnumerator DestroyClipWhenFinished(AudioClip clip)
    {
        while (audioSource != null && audioSource.isPlaying)
        {
            yield return null;
        }

        if (clip != null)
        {
            Destroy(clip);
        }
    }

    private IEnumerator SynthesizeRoutine(string text, Action<AudioClip> onComplete, Action<string> onError)
    {
        activeOnComplete = onComplete;
        activeOnError = onError;

        AudioClip clip = null;
        string error = null;

        yield return BuildClipRoutine(text, c => clip = c, e => error = e);

        activeOnComplete = null;
        activeOnError = null;
        synthesisCoroutine = null;

        if (!string.IsNullOrEmpty(error))
        {
            onError?.Invoke(error);
        }
        else
        {
            onComplete?.Invoke(clip);
        }
    }

    private IEnumerator BuildClipRoutine(string text, Action<AudioClip> onComplete, Action<string> onError)
    {
        var chunks = BuildChunks(text);
        if (chunks.Count == 0)
        {
            onError?.Invoke($"No speakable text found in \"{text}\".");
            yield break;
        }

        int sampleRate = tokenizer != null && tokenizer.SampleRate > 0 ? tokenizer.SampleRate : 22050;
        var samples = new List<float>();

        foreach (var chunk in chunks)
        {
            if (!TrySynthesizeChunk(chunk.Text, out var chunkSamples, out var chunkError))
            {
                onError?.Invoke(chunkError);
                yield break;
            }

            samples.AddRange(chunkSamples);

            int pauseSamples = Mathf.RoundToInt(Mathf.Max(0f, chunk.PauseAfter) * sampleRate);
            if (pauseSamples > 0)
            {
                samples.AddRange(new float[pauseSamples]);
            }

            // Keep the main thread responsive between sentences/phrases.
            yield return null;
        }

        if (samples.Count == 0)
        {
            onError?.Invoke("Synthesis produced no audio data.");
            yield break;
        }

        AudioClip clip = null;
        try
        {
            clip = AudioClip.Create("PiperSpeech", samples.Count, 1, sampleRate, false);
            clip.SetData(samples.ToArray(), 0);
        }
        catch (Exception ex)
        {
            if (clip != null)
            {
                Destroy(clip);
            }

            onError?.Invoke($"Failed to build the audio clip: {ex.Message}");
            yield break;
        }

        onComplete?.Invoke(clip);
    }

    private bool TrySynthesizeChunk(string textChunk, out float[] audioData, out string error)
    {
        audioData = null;
        error = null;

        if (engine == null)
        {
            error = "The inference worker is not available.";
            return false;
        }

        if (tokenizer == null || !tokenizer.IsReady)
        {
            error = "The tokenizer is not ready.";
            return false;
        }

        string phonemeStr = Phonemize(textChunk);
        if (string.IsNullOrEmpty(phonemeStr))
        {
            error = $"Phoneme conversion failed for \"{textChunk}\".";
            return false;
        }

        string[] phonemeArray = phonemeStr.Trim().Select(c => c.ToString()).ToArray();
        int[] phonemeTokens = tokenizer.Tokenize(phonemeArray);
        if (phonemeTokens == null || phonemeTokens.Length == 0)
        {
            error = $"Tokenization failed for \"{textChunk}\".";
            return false;
        }

        float[] scales = tokenizer.GetInferenceParams();
        if (scales == null || scales.Length < 3)
        {
            error = "Inference parameters are not available.";
            return false;
        }

        try
        {
            using var phonemesTensor = new Tensor<int>(new TensorShape(1, phonemeTokens.Length), phonemeTokens);
            using var lengthTensor = new Tensor<int>(new TensorShape(1), new[] { phonemeTokens.Length });
            using var scalesTensor = new Tensor<float>(new TensorShape(3), scales);
            using var sidTensor = hasSidKey
                ? new Tensor<int>(new TensorShape(1), new[] { Mathf.Max(0, speakerId) })
                : null;

            engine.SetInput("input", phonemesTensor);
            engine.SetInput("input_lengths", lengthTensor);
            engine.SetInput("scales", scalesTensor);
            if (hasSidKey && sidTensor != null)
            {
                engine.SetInput("sid", sidTensor);
            }

            engine.Schedule();

            var outputTensor = engine.PeekOutput() as Tensor<float>;
            if (outputTensor == null)
            {
                error = "The model produced no float output tensor.";
                return false;
            }

            using var readback = outputTensor.ReadbackAndClone();
            audioData = readback.DownloadToArray();
        }
        catch (Exception ex)
        {
            error = $"Inference failed: {ex.Message}";
            return false;
        }

        if (audioData == null || audioData.Length == 0)
        {
            error = "The model returned empty audio data.";
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------
    // Text preparation
    // ---------------------------------------------------------------------

    private List<SpeechChunk> BuildChunks(string text)
    {
        var chunks = new List<SpeechChunk>();
        var pending = new StringBuilder();

        string[] parts = Regex.Split(text, "([,.;:?!])");
        foreach (string rawPart in parts)
        {
            if (string.IsNullOrEmpty(rawPart))
            {
                continue;
            }

            string part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (part.Length == 1 && ",.;:?!".IndexOf(part[0]) >= 0)
            {
                float pause = GetPauseForPunctuation(part[0]);
                if (pending.Length > 0)
                {
                    AddChunks(chunks, pending.ToString(), pause);
                    pending.Length = 0;
                }
                else if (chunks.Count > 0)
                {
                    var last = chunks[chunks.Count - 1];
                    last.PauseAfter = Mathf.Max(last.PauseAfter, pause);
                    chunks[chunks.Count - 1] = last;
                }

                continue;
            }

            pending.Append(part).Append(' ');
        }

        if (pending.Length > 0)
        {
            AddChunks(chunks, pending.ToString(), 0f);
        }

        return chunks;
    }

    private float GetPauseForPunctuation(char punctuation)
    {
        switch (punctuation)
        {
            case ',':
            case ';':
            case ':':
                return commaDelay;
            case '.':
                return periodDelay;
            case '?':
            case '!':
                return questionExclamationDelay;
            default:
                return 0f;
        }
    }

    private void AddChunks(List<SpeechChunk> chunks, string text, float pauseAfter)
    {
        string cleaned = CleanText(text);
        if (cleaned.Length == 0)
        {
            return;
        }

        string[] words = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();

        foreach (string word in words)
        {
            if (builder.Length > 0 && builder.Length + 1 + word.Length > MaxChunkCharacters)
            {
                chunks.Add(new SpeechChunk { Text = builder.ToString(), PauseAfter = 0f });
                builder.Length = 0;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(word);
        }

        if (builder.Length > 0)
        {
            chunks.Add(new SpeechChunk { Text = builder.ToString(), PauseAfter = pauseAfter });
        }
    }

    private static string CleanText(string text)
    {
        // Keep letters, digits, whitespace and apostrophes (contractions). Replace everything else with spaces.
        string cleaned = Regex.Replace(text, @"[^\p{L}\p{N}\s']", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim();
    }

    private string Phonemize(string text)
    {
        IntPtr textPtr = IntPtr.Zero;
        try
        {
            byte[] textBytes = Encoding.UTF8.GetBytes(text + "\0");
            textPtr = Marshal.AllocHGlobal(textBytes.Length);
            Marshal.Copy(textBytes, 0, textPtr, textBytes.Length);

            IntPtr pointerToText = textPtr;
            const int textMode = 0;    // espeakCHARS_AUTO
            const int phonemeMode = 2; // IPA output as UTF-8

            // espeak_TextToPhonemes converts one clause at a time and advances the input pointer.
            // Keep calling until the whole buffer has been consumed, otherwise text after the first
            // punctuation mark would be silently dropped.
            var builder = new StringBuilder();
            while (pointerToText != IntPtr.Zero && Marshal.ReadByte(pointerToText) != 0)
            {
                IntPtr previousPointer = pointerToText;
                IntPtr resultPtr = ESpeakNG.espeak_TextToPhonemes(ref pointerToText, textMode, phonemeMode);
                if (resultPtr == IntPtr.Zero)
                {
                    break;
                }

                string clause = PtrToUtf8String(resultPtr).Trim();
                if (clause.Length > 0)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(clause);
                }

                if (pointerToText == previousPointer)
                {
                    // Defensive: never spin forever if the native call does not advance.
                    break;
                }
            }

            return builder.Length > 0 ? builder.ToString() : null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PiperManager] Phonemize failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (textPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(textPtr);
            }
        }
    }

    private static string PtrToUtf8String(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        var bytes = new List<byte>();
        for (int offset = 0; ; offset++)
        {
            byte value = Marshal.ReadByte(ptr, offset);
            if (value == 0)
            {
                break;
            }

            bytes.Add(value);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
