# Nyxara AI Studio

Nyxara AI Studio is a Unity editor and runtime tool for turning ARKit-compatible 3D characters into AI companions with a one-click setup flow.

It is built for creators who want a practical local-first workflow inside Unity: set up a character, build a studio root, finalize a companion prefab, and test speech, lip sync, and status tools from one place.

---

## Who It Is For

- Unity creators building AI-driven character experiences
- Developers working with ARKit-style facial blendshapes
- Teams that want a practical V1 workflow before broader compatibility polish

---

## V1 Core Capabilities

- One-click studio/root setup for ARKit-compatible characters
- Local LLM, local STT, and local TTS wiring inside Unity
- Studio build, apply-rig, and finalize-prefab workflow
- Structured profile authoring with identity, behavior, response rules, relationship defaults, and expression routing
- Persistent memory flow with working memory, saved event memory, and saved relationship memory
- Expression authoring, trigger routing, and expression library tools
- Lip sync editing, testing tools, and improved multi-renderer facial support
- Status, diagnostics, memory, and runtime testing panels
- Runtime conversation overlay with hold-to-talk, typed prompts, and live reply visibility
- Demo-safe sample expression/profile content

---

## Basic Workflow

1. Import Nyxara AI Studio
2. Download any optional external dependencies you want to use (see below)
3. Open `Nyxara AI > Studio > Setup Wizard`
4. Browse to the files or folders you downloaded and click install
5. Open `Nyxara AI > Studio`
6. Assign a compatible source character
7. Build the studio root
8. Finalize the companion prefab
9. Tune profile, memory, and expression routing in Studio
10. Enter Play Mode and test the local stack

---

## Important

The package compiles and the demo scene loads in a clean Unity project without these dependencies installed.

If dependencies are not installed, Nyxara AI Studio will still import and run, but AI, speech-to-text, and voice features will remain disabled until configured.

Nyxara Setup Wizard does not download anything or connect to external services. It only copies files or folders that the user manually selects inside Unity into the correct project locations.

---

## External Dependencies (Required For Full Functionality)

Nyxara AI Studio relies on external tools for AI, speech, and runtime processing.

These are NOT included in the package and must be installed separately.

### Core Systems

- **LLMUnity** (Local LLM inside Unity)  
  [https://github.com/undreamai/LLMUnity](https://github.com/undreamai/LLMUnity)

- **whisper.unity** (Speech-to-text in Unity)  
  [https://github.com/Macoron/whisper.unity](https://github.com/Macoron/whisper.unity)

- **Piper TTS** (Local text-to-speech)  
  [https://github.com/rhasspy/piper/releases](https://github.com/rhasspy/piper/releases)

- **sherpa-onnx** (Optional advanced speech tools)  
  [https://github.com/k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)

Recommended model:
Gemma 3 4B IT (Q4_K_M GGUF)

Download:
https://huggingface.co/Aldaris/gemma-3-4b-it-Q4_K_M-GGUF

## 🔊 Piper Voices (TTS)

Download voices from:

https://huggingface.co/rhasspy/piper-voices
---

## Unity Packages Used

These Unity packages are expected in your project:

- `com.unity.inputsystem`
- `com.unity.render-pipelines.universal`
- `com.unity.ai.navigation`
- `com.unity.ugui`
- `com.unity.timeline`

---

## External Content Not Included

The following are intentionally NOT included in the package:

- Local LLM model files (`.gguf`)
- Whisper model files
- Piper voice models
- Native runtime binaries
- External repositories (`LLMUnity`, `Piper`, `Whisper`, etc.)
- Any third-party characters, meshes, or textures

Typical paths are for reference only.

You must supply your own models and runtime files.

---

## Setup Wizard

Nyxara AI Studio includes `Nyxara Setup Wizard` under `Nyxara AI > Studio > Setup Wizard`.

Use it after you manually download your optional dependencies.

The wizard can:

- copy a `.gguf` LLM model into `StreamingAssets/Models`
- import the Whisper Unity integration from a downloaded folder when it contains a `.unitypackage`, an `Assets` folder, or an embedded `package.json` package
- copy a Whisper model into `StreamingAssets/Speech`
- copy a Piper runtime folder into `StreamingAssets/Speech/PiperRuntime`
- copy a Piper voice model into `StreamingAssets/Speech/PiperVoices`
- update the Studio Config automatically so the rest of Nyxara uses the installed paths

The current Studio Config also exposes `Smart Voice Capture` so the runtime overlay can switch between a lighter hold-to-talk path and the more protective capture flow used for speech testing.

What it does not do:

- it does not download dependencies
- it does not search your computer automatically
- it does not connect to the internet
- it does not modify files outside the Unity project

---

## Studio Tabs

The main Studio window now covers:

- `Studio`: build, rebuild, apply-rig, finalize-prefab, and dependency wiring
- `Status`: quick connection and readiness checks
- `Expression`: facial authoring, trigger routing, and preset workflows
- `Profile`: structured character authoring, response rules, preset helpers, and runtime preview tools
- `Lips & Expression`: lip sync testing, microphone testing, and live reply inspection
- `Memory`: working memory preview, saved event memory preview, relationship memory preview, and reset controls
- `Diagnostics`: runtime health, STT detail, face coverage, and integration findings

---

## Runtime Features

Current runtime behavior includes:

- Character reply mode and diagnostic inspector mode for prompt and memory debugging
- Memory-aware prompt building using current session context plus saved memory summaries
- JSON-backed saved event memory and relationship memory under Unity persistent data
- Relationship-state resets that can either preserve live affinity state or restore profile defaults
- Expression trigger playback routed from parsed response tags
- Improved microphone diagnostics including capture mode, transcript normalization, rejection reasons, and forwarding decisions
- Runtime overlay support for hold-to-talk, typed prompts, and last reply visibility

---

## License Note (Important)

External dependencies are subject to their own licenses.

Nyxara AI Studio does NOT redistribute:

- AI models
- voice models
- external runtimes
- third-party repositories

You are responsible for complying with all third-party licenses.

---

## Unity Version

Developed and tested on Unity `6.0.3f1`.

This Unity version has a known package signature warning.  
It does not affect the functionality of Nyxara AI Studio.

You may use newer Unity versions at your own discretion.

For best stability, use the tested version above for V1.

---

## Not Included In V1

- Character packs or avatar libraries
- Bundled AI models or voice assets
- Preconfigured external toolchains
- Guaranteed compatibility with every facial rig
- Advanced realism polish
- Cloud/server features

---

## Release Packaging Notes

- Keep the core Nyxara AI Studio editor/runtime code
- Exclude external repos and local-only data
- Exclude third-party assets unless redistribution-safe
- Review:
  [WHAT_IS_INCLUDED.md](./WHAT_IS_INCLUDED.md)
  [RELEASE_CHECKLIST.md](./RELEASE_CHECKLIST.md)

---

## Final Note

Demo characters shown in videos are NOT included.

Nyxara AI Studio is designed to work with any ARKit-compatible character.

---
