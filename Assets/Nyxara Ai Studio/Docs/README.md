# Nyxara AI Studio

Fully local AI companion system for Unity — no API or cloud required.

## ⚡ Quick Start

1. Import the package  
2. Download any optional external dependencies you want to use  
3. Open `Nyxara AI > Studio > Setup Wizard`
4. Browse to your downloaded files and click install  
5. Open `Nyxara AI > Studio`
6. Assign character  
7. Click `Build Studio`  
8. Click `Finalize Companion Root Prefab`  
9. Press Play and test

## Important

Nyxara AI Studio supports external AI integrations (LLMUnity, Whisper, Piper).

The package compiles and the demo scene loads in a clean Unity project without these dependencies installed.

These are NOT required for importing the package.

Nyxara Setup Wizard does not download anything or connect to external services. It only copies files, folders, or supported Whisper ZIP files that the user manually selects inside Unity into the correct project locations.

If not installed:
- AI generation is disabled
- Speech-to-text is disabled
- Text-to-speech is disabled

The project will still import and run without errors.

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
- Expression authoring and expression library tools
- Lip sync editing and testing tools
- Status, diagnostics, and runtime testing panels
- Demo-safe sample expression/profile content

---


## External Dependencies (LLM and STT required for full functionality, TTS optional)

Nyxara AI Studio relies on external tools for AI, speech, and runtime processing.

These are NOT included in the package and must be installed separately.

### Core Systems

- **LLMUnity** (Local LLM inside Unity)  
  [https://github.com/undreamai/LLMUnity](https://github.com/undreamai/LLMUnity)

- **whisper.unity** (Speech-to-text in Unity)  
  [https://github.com/Macoron/whisper.unity](https://github.com/Macoron/whisper.unity)
If the package does not install correctly via Package Manager, you may download and import it manually from the repository.

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

## ⚙️ External Setup (Inside Unity)

After downloading the optional external tools, use Nyxara Setup Wizard to place them into the correct locations automatically.

1. Open:
   Nyxara AI → Studio → Setup Wizard

2. Browse and install what you want to use:

   - LLM `.gguf`
   - Whisper Unity package folder or ZIP
   - Whisper model
   - Piper runtime folder
   - Piper voice `.onnx`

   The Whisper source field accepts either:
   - an extracted folder containing a supported Whisper package structure
   - a manually downloaded `.zip` containing a supported Whisper package structure

   Supported Whisper package structures:
   - `.unitypackage` inside the selected folder or ZIP
   - `Assets/...`
   - embedded `package.json` package content, including `Packages/.../package.json`

3. Open:
   Nyxara AI → Studio

4. Go to the **Status** tab and verify the paths and status fields:
   - LLM Model Status → Found
   - Whisper Model Status → Found
   - Voice Output Status → Ready (if configured)

Voice output is optional.  
If Piper is not configured, Nyxara AI Studio will still work using text-based responses.

The wizard only works with files, folders, or supported Whisper ZIP files the user manually selects. It does not download anything or connect to external services automatically.

You can also use `Validate & Bind Installed Integrations` in Setup Wizard to detect existing LLMUnity, whisper.unity, and Piper configuration in the project and repair Nyxara bindings without forcing a reinstall.

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

Any example paths shown are for reference only.

You must supply your own models and runtime files.

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

For complete documentation, including setup, dependencies, and usage instructions, visit:
https://github.com/BillyRaz/Nyxara-AI-Studio
