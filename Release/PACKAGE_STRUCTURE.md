# Recommended Release Structure

This is the intended public-facing package structure for V1 packaging, without requiring risky moves in the working project.

## Core Tooling

- `Assets/AICompanion/Runtime`
- `Assets/AICompanion/Editor`
- `Assets/AICompanionLab/Editor`
- `Assets/AICompanionLab/ScriptableObjects`
- `Assets/AICompanionLab/Scripts`

## Nyxara AI Studio Data

- `Assets/NyxaraAIStudio/Generated`
- `Assets/NyxaraAIStudio/Expressions`
- `Assets/NyxaraAIStudio/Profiles`
- `Assets/NyxaraAIStudio/Prefabs`
- `Assets/NyxaraAIStudio/Companions`

## Safe Demo Content

- `Assets/AICompanion/Mesh/NyxaraNew/`
- `Assets/AICompanion/Audio/Testing Audio/Nyxara Testing Ai Voice.wav`
- Nyxara-owned demo prefabs and Nyxara-owned supporting assets only

## Documentation

- root release docs
- `Release/` packaging docs

## Keep Out Of Public Package

- `External/`
- local models
- Piper voices and external runtime downloads
- third-party character content
- old internal fallback assets
- machine-specific generated config
