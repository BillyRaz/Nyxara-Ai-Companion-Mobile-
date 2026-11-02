# Changelog

## Unreleased

- Added a structured `Profile` authoring workflow with identity, behavior, relationship defaults, response rules, and expression-routing controls
- Added a `Memory` studio tab with working-memory preview, saved event memory preview, relationship memory preview, and reset tools
- Added JSON-backed saved event and relationship memory storage for runtime conversations
- Added `NyxaraReplyMode` support so the brain can switch between normal character replies and a diagnostic inspector flow
- Added expression trigger playback support driven from parsed response tags
- Expanded the runtime conversation overlay with live reply output and smarter voice-capture configuration hooks
- Expanded Whisper microphone diagnostics with capture mode, transcript normalization, rejection reasons, forwarding decisions, and explicit microphone routing support
- Expanded diagnostics reporting for runtime memory and speech-to-text troubleshooting
- Added VRM-aware lip-sync mapping support and broader facial compatibility updates
- Trimmed the extra debug/helper microphone buttons from the Studio test panel to keep the core controls cleaner

## V1 Release

- Added the Nyxara AI Studio one-click setup workflow for AI companion authoring in Unity
- Added local LLM, local STT, and local TTS workflow support
- Added ARKit-compatible character workflow support
- Added Studio build, apply-rig, and finalize-prefab flow
- Added expression editing and expression library tools
- Added lip sync editing and testing tools
- Added status, diagnostics, and testing panels
- Added demo/support content intended for release-safe packaging review
- Updated public-facing editor labels to Nyxara AI / Nyxara AI Studio
- Cleaned duplicate Studio toolbar navigation and removed the legacy bootstrap shortcut from the main Studio window
