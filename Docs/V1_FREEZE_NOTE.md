# V1 Freeze Note

## What V1 Includes

- Unity studio builder for creating a companion root from a source character or existing root
- Local LLM wiring through LLMUnity
- Local STT wiring through Whisper
- Local TTS wiring through Piper
- Expression library authoring and preset save/load/delete flow
- ARKit-style expression builder inside the main studio window
- Multi-renderer face support for split meshes such as head, lashes, eyes, and mouth
- Runtime face driver, signal router, and lip sync integration
- Expression Mode for separating full-face authoring from runtime lip-sync control
- System scan and diagnostics tooling
- Status Panel for quick connection checks
- Memory tab with saved-event and relationship-memory previews plus reset controls
- Testing tab with:
  - imported voice playback test
  - lip sync test
  - full system test
  - live lip mixer
- Profile tab with:
  - structured identity editing
  - behavior, relationship, and response-rule editing
  - expression trigger routing helpers
  - preset helpers and runtime preview tools
- Runtime in-scene conversation overlay with:
  - hold-to-talk microphone flow
  - release-to-send behavior
  - typed prompt popup
  - status display
  - last reply visibility
- JSON-backed saved event and relationship memory persistence
- Diagnostic reply mode for inspecting prompt and memory behavior
- Expression trigger playback from parsed response tags

## What Is Intentionally Excluded

- Final facial rig perfection
- Advanced viseme authoring tools beyond current editor flow
- Fully automated STT streaming conversation loop without hold/release input
- Rich in-game UI styling for runtime conversation controls
- Mobile-specific input handling
- Full input rebinding UI
- Production-grade persistence for all runtime tuning controls
- Full timeline-style memory visualization tools
- Emotion planner / behavior tree level AI orchestration
- Multiplayer or networked companion support
- Cloud model fallback support
- Formal save/load game integration

## Known Polish Items

- Companion response speed still needs optimization
- LLM startup and first-response latency are still high
- Lip sync timing can still need tuning per character rig
- Rig quality strongly affects teeth, jaw, and tongue realism
- Runtime overlay is functional but visually basic
- Microphone flow should be tested more heavily against different microphones and input backends
- Some runtime/editor settings still require rebuild or reapply steps after changes
- Testing and diagnostics output can still be condensed into clearer summaries
- Character-specific expression/lip presets may still need manual tuning after rebuild

## Future Expansion List

- Faster response pipeline and reduced first-token latency
- True push-to-talk / voice-chat mode with optional auto-listen loop
- Better in-scene runtime UI for conversation and debugging
- Server-backed LLM support as an optional alternative to local runtime inference
- Server-backed STT and TTS support as optional linked services
- A dedicated `Server` tab for service login, host/IP, API keys, model IDs, and other provider-specific connection settings
- Status-tab controls for replacing local LLM/STT/TTS model paths directly from the editor
- Status-tab controls for switching between local file paths and remote model/service IDs
- Input System-native rebinding UI
- Character rig validation wizard
- Guided lipsync calibration workflow
- Richer launch-ready runtime UI styling and UX polish
- More advanced action execution and behavior planning
- Safer automatic fallback handling when LLM, STT, or TTS are missing
- One-click deployment presets for desktop builds
- Per-character tuning profiles for facial weights, lip sync, and response style
