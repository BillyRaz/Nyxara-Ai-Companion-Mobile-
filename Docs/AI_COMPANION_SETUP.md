# Nyxara AI Companion Stack

This Unity project is scaffolded around a local-first companion pipeline:

- `LLMUnity` for local GGUF inference inside Unity
- `whisper.unity` for local speech-to-text
- `Piper` for local text-to-speech
- Custom Nyxara runtime/editor tooling for profile authoring, memory, expression routing, diagnostics, and runtime UI

## Local repositories

These upstream repositories are currently present under `External/Repos`:

- `LLMUnity`
- `llama.cpp`
- `whisper.unity`
- `whisper.cpp`
- `piper`
- `sherpa-onnx`
- `arfoundation-samples`

## Unity package wiring

`Packages/manifest.json` points at local package sources:

- `ai.undream.llm` -> `External/Repos/LLMUnity`
- `com.whisper.unity` -> `External/Repos/whisper.unity/Packages/com.whisper.unity`

That keeps the project reproducible without depending on live Git package fetches during refresh.

## Current Studio workflow

The main path is now:

1. Open `Nyxara AI > Studio > Setup Wizard`
2. Import the local model/runtime assets you already downloaded
3. Open `Nyxara AI > Studio`
4. Assign a compatible source character
5. Build the studio root
6. Finalize the companion prefab
7. Configure profile, memory, expression routing, and runtime defaults
8. Enter Play Mode and test microphone, lip sync, replies, and overlay behavior

## Setup Wizard responsibilities

The wizard can currently:

- copy a `.gguf` model into `Assets/StreamingAssets/Models`
- import Whisper Unity from a downloaded folder or package
- copy a Whisper model into `Assets/StreamingAssets/Speech`
- copy a Piper runtime folder into `Assets/StreamingAssets/Speech/PiperRuntime`
- copy Piper voice files into `Assets/StreamingAssets/Speech/PiperVoices`
- update the generated studio config asset so the project uses the imported paths

## Runtime components in the current build

- `NyxaraCompanionBrain`
  - Coordinates prompt building, reply generation, parsing, runtime state, and memory recording.
- `RecentMemoryController`
  - Tracks working memory plus saved event and relationship memory.
- `JsonMemoryEventStore`
  - Persists saved event and relationship memories as JSON under Unity persistent data.
- `WhisperMicrophoneInput`
  - Records microphone audio, resolves mic routing, normalizes transcripts, and reports rejection/debug state.
- `PiperTtsService`
  - Generates and plays local speech output when voice is enabled.
- `ArkItBlendshapeDriver`
  - Drives facial blendshapes and now works better with multi-renderer face setups.
- `ExpressionTriggerPlayer`
  - Plays expression triggers routed from parsed response tags.
- `RuntimeConversationOverlay`
  - Exposes hold-to-talk, typed prompts, and reply/status feedback in play mode.

## Current configuration expectations

### LLM model

Provide a GGUF model in the project-local model folder, typically:

- `Assets/StreamingAssets/Models`

### Whisper model

Provide a Whisper model under:

- `Assets/StreamingAssets/Speech`

### Piper runtime and voice

Provide:

- a Piper runtime folder under `Assets/StreamingAssets/Speech/PiperRuntime`
- at least one Piper voice `.onnx` model under `Assets/StreamingAssets/Speech/PiperVoices`

## Studio tabs that matter right now

- `Profile`
  - Structured character authoring, presets, expression routing, and live runtime preview
- `Memory`
  - Memory previews plus reset controls for session, saved event, and relationship memory
- `Lips & Expression`
  - Lip sync tests, microphone tests, and reply inspection
- `Diagnostics`
  - Runtime health, STT diagnostics, face coverage, and integration findings

## Suggested validation pass inside Unity

1. Let Package Manager finish importing local packages.
2. Run the Setup Wizard for any missing external assets.
3. Open the Studio window and verify `Status` and `Diagnostics`.
4. Build or rebuild the studio root and finalize the prefab.
5. Open `Profile` and confirm identity, relationship defaults, and response rules.
6. Open `Memory` and verify the reset controls and saved-memory previews.
7. Enter Play Mode and test the runtime overlay plus mic flow.
