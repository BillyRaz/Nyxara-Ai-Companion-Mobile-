# Piper Unity (UPM package)

Local **Unity Package Manager** package for [`skykim/piper-unity`](https://github.com/skykim/piper-unity)
— on-device neural text-to-speech with Piper voice models and espeak-ng phonemization,
running on **Unity Inference Engine** (CPU) on Windows, macOS and Android.

> This package exists because `https://github.com/skykim/piper-unity.git` is **not** a UPM package.
> That repository is a complete Unity demo project: its runtime code lives in `Assets/Scripts` and
> there is no `package.json` at the repository root, so Unity Package Manager cannot install it from
> a Git URL. Here the upstream runtime scripts and native espeak-ng libraries are repackaged in the
> standard UPM layout so they can be installed as `"com.skykim.piper-unity": "file:com.skykim.piper-unity"`.

## Contents

| Path | Description |
| --- | --- |
| `Runtime/ESpeakNG.cs` | P/Invoke bindings for `espeak-ng` |
| `Runtime/ESpeakTokenizer.cs` | Reads a Piper voice `.onnx.json` config and maps phonemes to model tokens |
| `Runtime/PiperManager.cs` | `MonoBehaviour` that phonemizes text, runs the ONNX model and plays the generated `AudioClip` |
| `Runtime/FrequencyBandAnalyser.cs` | Optional demo audio visualizer |
| `Runtime/Plugins/Windows/x64/espeak-ng.dll` | espeak-ng 1.5.2 for Windows x64 |
| `Runtime/Plugins/macOS/x64/libespeak-ng.dylib` | espeak-ng for macOS |
| `Runtime/Plugins/Android/libs/arm64-v8a/libespeak-ng.so` | espeak-ng for Android arm64 |

All native plugins have explicit `.meta` platform settings, so each library is only shipped to its
own platform.

## Requirements

- Unity 6 (`6000.x`) or newer with `com.unity.ai.inference` **2.2.1+** (this project uses 2.6.1)
- `com.unity.nuget.newtonsoft-json` (declared as a package dependency)
- A Piper voice model: `<voice>.onnx` plus its `<voice>.onnx.json` config
- The `espeak-ng-data` folder (shipped in the upstream `StreamingAssets.zip`)

## Setup in this project

1. **Voice model**: copy `<voice>.onnx` somewhere under `Assets/` (for example
   `Assets/Speech/Piper/`) so Unity's Inference Engine imports it as a `ModelAsset`.
   Files inside `StreamingAssets` are *not* imported as model assets.
   In this project the importable copy is `Assets/Speech/Piper/clean100.onnx`.
2. **Voice config**: copy `<voice>.onnx.json` under `Assets/` as well. Unity imports it as a
   `TextAsset`; the tokenizer needs it for `phoneme_id_map`, sample rate and voice name.
   This project uses `Assets/Speech/Piper/clean100.onnx.json`.
3. **espeak data**: `espeak-ng-data` must be reachable from `StreamingAssets`. This project already
   ships a copy at `Assets/StreamingAssets/Speech/PiperRuntime/piper/espeak-ng-data`.
   Always use **forward slashes** in the component field.
4. **Scene wiring**: add an `AudioSource` and a `PiperManager` component to a GameObject, then set:
   - `Model Asset` → `clean100.onnx`
   - `Tokenizer` → an `ESpeakTokenizer` component whose `Json File` is `clean100.onnx.json`
   - `Voice Name Text` → optional `UnityEngine.UI.Text` label (can stay empty)
   - `Espeak Data Directory` → `Speech/PiperRuntime/piper/espeak-ng-data` for this project
     (or `espeak-ng-data` if you copy the folder to the root of `StreamingAssets`)
   - `Speaker Id` → `0` for the `clean100` voice
5. Call `Initialize()` (or leave `Initialize On Start` enabled) and wait until `IsInitialized` is
   `true`, then call `SynthesizeAndPlay(text)`.

### Runtime API

| Member | Purpose |
| --- | --- |
| `Initialize()` | Starts asynchronous initialization. Safe to call repeatedly. |
| `IsInitialized` / `IsInitializing` | Reliable initialization status. |
| `InitializationError` | Human-readable reason for a failed initialization. |
| `InitializationCompleted` | `Action<bool>` raised once per attempt. |
| `Synthesize(text, onClip, onError)` | Synthesizes into one `AudioClip`; only one request runs at a time. |
| `CancelSynthesis()` | Stops the active request and reports cancellation through `onError`. |
| `SynthesizeAndPlay(text)` | Convenience wrapper that plays through the component's own `AudioSource`. |

The `clean100` voice is a 245-speaker model. `PiperManager` sends the configured `Speaker Id`
(default `0`, which maps to `speaker_id_map["6209"]`).

### Android

On Android the `espeak-ng-data` folder is not read directly from the APK. Zip it as
`<configured folder>.zip` (for example `Speech/PiperRuntime/piper/espeak-ng-data.zip`) and place it
next to the folder inside `StreamingAssets`; `PiperManager` copies and extracts it to
`Application.persistentDataPath` on first run. This project already contains that zip, and the
extracted directory is reused on later launches.

## Notes

- Only 64-bit desktop and arm64 Android native libraries are included, matching upstream.
- `PiperManager` remains usable as a standalone demo component. The Nyxara `PiperTtsService`
  creates a hidden child `PiperManager` at runtime so the companion keeps using its existing
  `AudioSource` and lip-sync pipeline.
- Upstream license: MIT, see `LICENSE.md`. Piper and espeak-ng keep their own licenses.
