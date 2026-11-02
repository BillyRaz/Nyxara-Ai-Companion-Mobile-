# What Is Included

## Included In The Release-Safe V1 Package

- Core editor tooling under the Nyxara AI Studio workflow
- Core runtime scripts required for companion behavior
- Studio builder, status, diagnostics, testing, expression, and lip sync tooling
- ScriptableObjects and editor/runtime support code required by the workflow
- Release documentation
- Nyxara-owned demo assets that are confirmed safe to redistribute

## Intended Shippable Content

- `Assets/AICompanion/Runtime`
- `Assets/AICompanionLab/Editor`
- `Assets/AICompanionLab/ScriptableObjects`
- `Assets/AICompanionLab/Scripts`
- release-safe assets created under `Assets/NyxaraAIStudio`
- `Assets/AICompanion/Mesh/NyxaraNew/`
- `Assets/AICompanion/Audio/Testing Audio/Nyxara Testing Ai Voice.wav`
- release docs in the project root

## Exclude From The Shippable Package

- `External/`
- `Library/`
- `Logs/`
- `Temp/`
- `UserSettings/`
- local-only generated config with machine-specific paths
- local model files and local voice executables
- third-party or uncertain character/demo assets
- old backups, experiments, and unused sample content

## Manual Review Before Shipping

- `Assets/AICompanion/Mesh/Nyxaraold currupted but stable and working right now/`
- `Assets/AICompanion/Mesh/Realisticgirl_Fbx/`
- `Assets/AICompanionStudio/Companions/BuisnessGirl_CompanionRoot.prefab`
- `Assets/AICompanionStudio/Prefabs/BuisnessGirl_StudioRoot.prefab`
- `Assets/AICompanionStudio/Generated/`
- `Assets/StreamingAssets/Models/`
- `Assets/StreamingAssets/Speech/PiperVoices/`
- `Assets/StreamingAssets/Speech/ggml-tiny.bin`

Confirmed safe from your review:

- Nyxara-named assets you created
- `Assets/AICompanion/Mesh/NyxaraNew/`
- `Assets/AICompanion/Audio/Testing Audio/Nyxara Testing Ai Voice.wav`

Keep excluded from the public package:

- `BuisnessGirl` assets
- the old broken Nyxara import folder kept only as internal reference/fallback
