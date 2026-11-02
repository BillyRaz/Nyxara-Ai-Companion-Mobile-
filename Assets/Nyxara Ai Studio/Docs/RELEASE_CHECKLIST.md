# Release Checklist

## Cleaned

- public editor menu/window naming updated to `Nyxara AI` / `Nyxara AI Studio`
- duplicate Studio-window toolbar navigation removed
- legacy bootstrap shortcut removed from the main Studio window
- default generated output paths updated to `Assets/NyxaraAIStudio`

## Renamed Or Repositioned

- main Studio menu path moved to `Nyxara AI/Studio`
- diagnostics menu moved to `Nyxara AI/Diagnostics`
- expression editor menu moved to `Nyxara AI/Studio/Expression Editor`
- lip sync editor menu moved to `Nyxara AI/Studio/Lip Sync Editor`
- legacy bootstrap entry moved to `Nyxara AI/Legacy/Create Bootstrap Objects`

## Exclude Before Shipping

- `External/`
- `Assets/StreamingAssets/Models/`
- `Assets/StreamingAssets/Speech/PiperVoices/`
- `Assets/StreamingAssets/Speech/ggml-tiny.bin`
- `Assets/AICompanionStudio/Generated/AICompanionStudioConfig.asset`
- any generated prefabs or companions linked to non-redistributable characters
- any old backups, experimental assets, or abandoned local content
- `Assets/AICompanion/Mesh/Nyxaraold currupted but stable and working right now/`
- `Assets/AICompanion/Mesh/Realisticgirl_Fbx/`
- `Assets/AICompanionStudio/Companions/BuisnessGirl_CompanionRoot.prefab`
- `Assets/AICompanionStudio/Prefabs/BuisnessGirl_StudioRoot.prefab`

## Manual Rights Review

- `Assets/AICompanion/Mesh/Realisticgirl_Fbx/`
- `Assets/AICompanionStudio/Companions/BuisnessGirl_CompanionRoot.prefab`
- `Assets/AICompanionStudio/Prefabs/BuisnessGirl_StudioRoot.prefab`

## Confirmed Safe To Ship

- `Assets/AICompanion/Mesh/NyxaraNew/`
- Nyxara-named assets you created
- `Assets/AICompanion/Audio/Testing Audio/Nyxara Testing Ai Voice.wav`
- Nyxara companion prefabs only if they do not reference excluded third-party source assets

## Final Manual Review Before Shipping

- verify redistribution rights for every demo asset that remains
- verify there are no machine-specific file paths in shippable assets
- verify the package works without the excluded local model/voice files
- verify the Studio build/setup/finalize flow still works in a clean import
- verify Unity `6.0.3f1` import behavior and note the known package signature warning
- verify only release-intended demo content remains
