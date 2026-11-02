# Dependencies For Release

# Nyxara AI Studio

This file lists the external and package dependencies currently used by the project, grouped for release preparation.

## Required External Dependencies

These are part of the working local-first pipeline, but should usually be installed or provided separately rather than blindly bundled into the public V1 package.

- `LLMUnity`
  Unity package id: `ai.undream.llm`
  Current source: `External/Repos/LLMUnity`
  Purpose: local GGUF LLM inference inside Unity

- `whisper.unity`
  Unity package id: `com.whisper.unity`
  Current source: `External/Repos/whisper.unity/Packages/com.whisper.unity`
  Purpose: local speech-to-text integration

- `Piper`
  Current related repos/runtime:
  - `External/Repos/piper`
  - `External/PiperRuntime`
  Purpose: local text-to-speech generation

- local GGUF model file
  Current project examples are under:
  - `Assets/StreamingAssets/Models/`
  Purpose: local LLM model runtime

- Whisper model file
  Current expected path:
  - `Assets/StreamingAssets/Speech/ggml-tiny.bin`
  Purpose: local STT runtime

- Piper voice model
  Current example path:
  - `Assets/StreamingAssets/Speech/PiperVoices/`
  Purpose: local TTS runtime

## Supporting External Repos Present In This Project

These exist in the current workspace but are not all meant to be redistributed with the V1 release package.

- `External/Repos/LLMUnity`
- `External/Repos/llama.cpp`
- `External/Repos/whisper.unity`
- `External/Repos/whisper.cpp`
- `External/Repos/piper`
- `External/Repos/sherpa-onnx`
- `External/Repos/arfoundation-samples`

## Unity Package Dependencies Used By The Project

These are declared in `Packages/manifest.json`.

- `ai.undream.llm`
- `com.whisper.unity`
- `com.unity.inputsystem`
- `com.unity.render-pipelines.universal`
- `com.unity.ai.navigation`
- `com.unity.ugui`
- `com.unity.timeline`

Other standard Unity packages and built-in modules are also present in `Packages/manifest.json`, but the list above is the most relevant for Nyxara AI Studio functionality.

## Optional Dependencies / Not Core To V1 Packaging

- `sherpa-onnx`
  Present in external repos, but not part of the public-facing V1 workflow

- `arfoundation-samples`
  Sample content repository, not part of Nyxara AI Studio release packaging

- `External/PiperRuntime`
  May be useful for local setup, but should be reviewed before bundling in a public package

## Do Not Ship By Default

Unless you intentionally prepare and license-review them, these should stay out of the public V1 package:

- `External/`
- `External/Repos/LLMUnity`
- `External/Repos/llama.cpp`
- `External/Repos/whisper.unity`
- `External/Repos/whisper.cpp`
- `External/Repos/piper`
- `External/Repos/sherpa-onnx`
- `External/Repos/arfoundation-samples`
- `Assets/StreamingAssets/Models/`
- `Assets/StreamingAssets/Speech/ggml-tiny.bin`
- `Assets/StreamingAssets/Speech/PiperVoices/`
- `Assets/StreamingAssets/LlamaLib-v2.0.5/`
- machine-specific executable paths
- machine-specific model paths

## Release Recommendation

For V1, the safest public release approach is:

1. Ship Nyxara AI Studio editor/runtime code.
2. Ship only confirmed-safe Nyxara-owned demo content.
3. Document the required external dependencies separately.
4. Let users install or configure local models, Whisper, and Piper on their own.

## Unity Version Note

Developed and tested on Unity `6.0.3f1`.

This Unity version has a known package signature warning.
It does not affect the functionality of Nyxara AI Studio.

You may use newer Unity versions at your own discretion.

For best stability, use the tested version above for V1.
