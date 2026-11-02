# AI Companion System Spec

## Purpose

This project is a Unity-based local AI companion pipeline for building a character that can:

- listen to the player with local speech-to-text
- think and reply with a local LLM
- speak with local text-to-speech
- animate facial expressions and lip sync
- save reusable expression presets
- route expression triggers from parsed responses
- remember meaningful conversation events and relationship changes
- be assembled and diagnosed from one editor workflow

The system is designed so a single character asset can be turned into a reusable "studio root" and then into a runtime companion prefab.

## Main Goals

- Build a repeatable authoring workflow inside Unity.
- Keep the full AI stack local where possible.
- Support portrait-style studio setup for testing and iteration.
- Support split facial meshes such as head, eyelashes, eyes, and mouth.
- Let expressions and lip sync coexist without fighting over the same blendshapes.
- Add structured profile authoring instead of relying only on raw JSON edits.
- Keep short-term and saved conversation memory available to the runtime prompt flow.
- Provide diagnostics that expose missing paths, missing wiring, and rig limitations.

## Current Architecture

### Editor Layer

- `NyxaraCompanionStudioWindow`
  - Main control panel for Studio, Status, Expression, Profile, Lips & Expression, Memory, and Diagnostics tabs.
  - Central place for builder actions, profile authoring, expression authoring, memory inspection, and system scans.
- `NyxaraCompanionStudioBuilder`
  - Builds the studio root.
  - Wires runtime components together.
  - Creates studio camera, lights, folders, and prefabs.
  - Auto-collects related face renderers for split-mesh facial rigs.
- `NyxaraCompanionStudioWindow.Profile`
  - Structured profile editor for identity, behavior, relationship defaults, response rules, routing, and preset helpers.
- `ExpressionEditorWindow`
  - Detailed editor window for manual blendshape sculpting.
- `LipSyncEditorWindow`
  - Detailed lip-sync editing workflow.

### Runtime Layer

- `NyxaraCompanionBrain`
  - Main runtime orchestrator.
  - Connects dialogue generation, prompt building, memory, face signals, speech, and actions.
- `RecentMemoryController`
  - Maintains working memory, saved event memory, and saved relationship memory.
- `JsonMemoryEventStore`
  - Persists saved event and relationship memory to JSON in Unity persistent data.
- `MemoryFilterService`
  - Decides whether a conversation event should be discarded, kept only in working memory, saved, or merged.
- `ArkItBlendshapeDriver`
  - Runtime blendshape driver for speaking/thinking states.
  - Now supports multiple face renderers.
- `ExpressionSignalRouter`
  - Applies mood and signal-based blendshape responses.
  - Respects expression mode rules.
- `ExpressionLibraryManager`
  - Loads, applies, saves, deletes, and previews expression presets.
  - Supports multi-renderer expression application.
- `ExpressionTriggerPlayer`
  - Plays routed expression triggers with cooldown and duplicate suppression.
- `VisemeLipSyncController`
  - Applies viseme and jaw-driven speech motion.
  - Respects expression mode rules.
- `PiperTTSPhonemeExtractor`
  - Extracts phoneme/viseme timing from Piper-compatible speech flow.
- `PiperTtsService`
  - Local text-to-speech playback entry point.

## AI Stack

### LLM

- Uses `LLMUnity` with a local model.
- Current setup is aimed at a local Qwen model.
- `LLMAgent` is attached and auto-wired into the brain.

### Speech To Text

- Uses Whisper integration.
- `WhisperMicrophoneInput` and `WhisperManager` are used for player speech.
- The microphone path now reports capture mode, normalized transcript state, rejection reasons, forwarding decisions, and explicit mic routing details.

### Text To Speech

- Uses Piper.
- `PiperTtsService` handles generation and playback.
- Face and lip-sync hooks are connected from TTS into facial motion.

## Facial System

### Expression Authoring

- ARKit-style expression builder is available in the main Expression tab.
- Supports auto-detection of ARKit-like blendshape names.
- Supports direct save into the expression library with `Build Expression`.
- Presets can be loaded, replaced, and deleted from the library workflow.

### Expression Presets

- Stored as `ExpressionPreset` assets.
- Each preset stores blendshape weights, identity fields, category, and timing.
- The expression library manager loads these from the configured expression folder.

### Expression Trigger Routing

- Parsed response tags can now trigger routed facial expressions.
- Trigger playback uses profile-defined routing plus cooldown and duplicate suppression rules.

### Multi-Renderer Face Support

The system previously assumed one face mesh. It now supports multiple linked face renderers, including:

- head mesh
- eyelashes mesh
- eye meshes
- separate mouth mesh

This is handled in:

- `ArkItBlendshapeDriver`
- `ExpressionLibraryManager`
- `VisemeLipSyncController`
- studio builder auto-wiring

### Expression Mode

The Expression tab now has an `Expression Mode` toggle.

- `Expression Mode ON`
  - Expression tools own the full face.
  - Mouth, jaw, and eye-related runtime automation are suppressed.
  - Best used for authoring and previewing expression presets.
- `Expression Mode OFF`
  - Lip sync owns mouth-related speech behavior.
  - Expression systems still drive non-mouth facial behavior.
  - Best used for runtime speaking behavior.

This separation exists to stop lip sync and expression tools from fighting over the same facial controls.

## Diagnostics

The system scan in `NyxaraCompanionStudioWindow` currently checks:

- LLM wiring and model path presence
- Whisper/STT wiring
- Piper/TTS wiring and configured paths
- face driver presence
- lip sync presence
- expression library presence
- runtime memory state
- source character path
- prefab output path

Speech-to-text diagnostics now also report:

- visible microphone count
- resolved mic route and selected device
- natural versus assisted capture mode
- last raw transcript and normalized transcript
- rejection reason and whether debug bypass was used
- forwarding decision and likely speech issue summary

It also now reports facial rig structure, including:

- number of detected face renderers
- which renderers were found
- eye-related blendshape coverage
- mouth-related blendshape coverage
- jaw-related blendshape coverage
- tongue/teeth-related blendshape coverage

## What Is Working Now

- Studio root build workflow
- prefab generation
- apply-rig workflow for an existing root
- local model path wiring
- local Whisper path wiring
- local Piper path wiring
- expression library loading and saving
- ARKit-style expression authoring in the main editor tab
- expression preset deletion and replacement
- split face mesh support
- diagnostics for face mesh coverage
- expression mode ownership split
- structured profile authoring in Studio
- saved event memory and relationship memory persistence
- runtime memory reset tools and previews
- expression trigger routing from parsed response tags
- richer STT diagnostics and runtime overlay reply visibility

## Current Known Limitations

### Rig Quality Still Matters

The Unity-side system is much stronger now, but it cannot create rig behavior that does not exist in the source asset.

Examples:

- If teeth are not separated correctly during jaw motion, that usually means jaw/teeth rigging or weighting needs work.
- If upper teeth should stay stable while lower teeth move with jaw opening, that typically requires proper jaw bone rigging or teeth-specific deformation support.
- If tongue behavior is not correct, the rig must expose appropriate tongue motion or weighting.

### Humanoid Transfer Problems

You already identified another real issue: humanoid animation transfer is still affected by weighting problems in the character rig.

Likely sources:

- jaw weighting
- facial part weighting
- eye weighting
- bone orientation consistency
- humanoid avatar mapping cleanup

### Diagnostics Are Richer, But Still Verbose

The scan now reports much more detail for memory and STT, but it can still be condensed into clearer grouped summaries for launch.

## Progress Summary

### Before This Iteration

- Facial workflow assumed a single face mesh.
- Expression saving was more manual.
- Diagnostics did not clearly expose split-face rig structure.
- Profile editing relied more heavily on raw JSON and scattered fields.
- Runtime memory persistence was not part of the main authoring workflow.
- Mouth/jaw issues were hard to distinguish from wiring problems.

### After This Iteration

- Split face mesh support is implemented.
- Expression builder is integrated into the main studio workflow.
- Expression mode gives clear ownership separation.
- Diagnostics now expose renderer coverage and jaw/eye/tongue data.
- Profile authoring is now a structured workflow inside Studio.
- Runtime memory persistence and reset tooling are now part of the companion loop.
- Parsed responses can trigger routed expressions directly.
- The remaining major issues are mostly rig-related rather than editor-wiring-related.

## Recommended Next Steps

### Rig Pass

- Re-rig jaw and teeth behavior.
- Fix humanoid transfer weighting issues.
- Review tongue setup.
- Confirm upper teeth remain attached appropriately while lower jaw opens.

### Editor Improvements

- Condense diagnostics into clearer grouped health summaries.
- Expose lip-sync helper tuning values in the editor.
- Add a dedicated rig validation section for jaw, teeth, tongue, and eye behavior.
- Keep trimming debug-only controls from launch-facing editor surfaces where they are no longer needed.

### Runtime Improvements

- Add layered facial blending rules instead of simple ownership handoff.
- Add configurable priority between lip sync, mood, and expression presets.
- Add optional jaw-only runtime debug overlays or sliders.
- Add an optional server-backed AI stack alongside the current local-first setup.
- Add a dedicated server configuration workflow for credentials, endpoints, IP/host, API keys, and remote model IDs.
- Expand the Status tab so local LLM/STT/TTS model paths can be swapped directly there without leaving the main studio workflow.
- Support hybrid setups where local and remote services can be mixed per subsystem, such as local TTS with remote LLM.

## Files Most Central To The System

- `Assets/AICompanionLab/Scripts/Editor/NyxaraCompanionStudioWindow.cs`
- `Assets/AICompanionLab/Scripts/Editor/NyxaraCompanionStudioWindow.Profile.cs`
- `Assets/AICompanionLab/Scripts/Editor/NyxaraCompanionStudioBuilder.cs`
- `Assets/AICompanionLab/Scripts/Core/NyxaraCompanionBrain.cs`
- `Assets/AICompanionLab/Scripts/Runtime/RecentMemoryController.cs`
- `Assets/AICompanionLab/Scripts/Runtime/JsonMemoryEventStore.cs`
- `Assets/AICompanion/Runtime/Face/ArkItBlendshapeDriver.cs`
- `Assets/AICompanionLab/Scripts/Face/ExpressionSignalRouter.cs`
- `Assets/AICompanionLab/Scripts/Expressions/ExpressionLibraryManager.cs`
- `Assets/AICompanionLab/Scripts/Expressions/ExpressionTriggerPlayer.cs`
- `Assets/AICompanionLab/Scripts/LipSync/VisemeLipSyncController.cs`
- `Assets/AICompanion/Runtime/Speech/PiperTtsService.cs`
- `Assets/AICompanionLab/ScriptableObjects/Expressions/ExpressionPreset.cs`
- `Assets/AICompanionLab/Scripts/Expressions/ExpressionBuilderHelper.cs`

## Bottom Line

The project has moved from a fragile prototype into a much more structured local AI companion pipeline.

The core editor/runtime architecture is now good enough that the biggest remaining blockers are no longer "does the system support this?" but "does the rig provide correct deformation and weighting for it?".

That is real progress.
